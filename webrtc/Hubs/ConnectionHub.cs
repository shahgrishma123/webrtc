using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNet.SignalR.Hubs;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR;
using webrtc.Models;

namespace SignalRCoreWebRTC.Hubs
{
    [Route("/ConnectionHub")]
    public class ConnectionHub : Hub<IConnectionHub>
    {
        private readonly List<ApplicationUser> _Users;
        private readonly List<ApplicationUserCall> _UserCalls;
        private readonly List<ApplicationCallOffer> _CallOffers;

        public ConnectionHub(List<ApplicationUser> users, List<ApplicationUserCall> userCalls, List<ApplicationCallOffer> callOffers)
        {
            _Users = users;
            _UserCalls = userCalls;
            _CallOffers = callOffers;
        }

        public async Task Join(string username)
        {
            // Add the new user
            _Users.Add(new ApplicationUser
            {
                UserName = username,
                ConnectionId = Context.ConnectionId
            });

            // Send down the new list to all clients
            await SendUserListUpdate();
        }

        public override async Task OnDisconnectedAsync(Exception exception)
        {
            // Hang up any calls the user is in
            await HangUp(); // Gets the user from "Context" which is available in the whole hub

            // Remove the user
            _Users.RemoveAll(u => u.ConnectionId == Context.ConnectionId);

            // Send down the new user list to all clients
            await SendUserListUpdate();

            await base.OnDisconnectedAsync(exception);
        }

        public async Task CallUser(ApplicationUser targetConnectionId)
        {
            var callingUser = _Users.SingleOrDefault(u => u.ConnectionId == Context.ConnectionId);
            var targetUser = _Users.SingleOrDefault(u => u.ConnectionId == targetConnectionId.ConnectionId);

            // Make sure the person we are trying to call is still here
            if (targetUser == null)
            {
                // If not, let the caller know
                await Clients.Caller.CallDeclined(targetConnectionId, "The user you called has left.");
                return;
            }

            // And that they aren't already in a call
            if (GetUserCall(targetUser.ConnectionId) != null)
            {
                await Clients.Caller.CallDeclined(targetConnectionId, string.Format("{0} is already in a call.", targetUser.UserName));
                return;
            }

            // They are here, so tell them someone wants to talk
            await Clients.Client(targetConnectionId.ConnectionId).IncomingCall(callingUser);

            // Create an offer
            _CallOffers.Add(new ApplicationCallOffer
            {
                ApplicationCaller = callingUser,
                ApplicationCallee = targetUser
            });
        }

        public async Task AnswerCall(bool acceptCall, ApplicationUser targetConnectionId)
        {
            var callingUser = _Users.SingleOrDefault(u => u.ConnectionId == Context.ConnectionId);
            var targetUser = _Users.SingleOrDefault(u => u.ConnectionId == targetConnectionId.ConnectionId);

            // This can only happen if the server-side came down and clients were cleared, while the user
            // still held their browser session.
            if (callingUser == null)
            {
                return;
            }

            // Make sure the original caller has not left the page yet
            if (targetUser == null)
            {
                await Clients.Caller.CallEnded(targetConnectionId, "The other user in your call has left.");
                return;
            }

            // Send a decline message if the callee said no
            if (acceptCall == false)
            {
                await Clients.Client(targetConnectionId.ConnectionId).CallDeclined(callingUser, string.Format("{0} did not accept your call.", callingUser.UserName));
                return;
            }

            // Make sure there is still an active offer.  If there isn't, then the other use hung up before the Callee answered.
            var offerCount = _CallOffers.RemoveAll(c => c.ApplicationCallee.ConnectionId == callingUser.ConnectionId
                                                  && c.ApplicationCaller.ConnectionId == targetUser.ConnectionId);
            if (offerCount < 1)
            {
                await Clients.Caller.CallEnded(targetConnectionId, string.Format("{0} has already hung up.", targetUser.UserName));
                return;
            }

            // And finally... make sure the user hasn't accepted another call already
            if (GetUserCall(targetUser.ConnectionId) != null)
            {
                // And that they aren't already in a call
                await Clients.Caller.CallDeclined(targetConnectionId, string.Format("{0} chose to accept someone elses call instead of yours :(", targetUser.UserName));
                return;
            }

            // Remove all the other offers for the call initiator, in case they have multiple calls out
            _CallOffers.RemoveAll(c => c.ApplicationCaller.ConnectionId == targetUser.ConnectionId);

            // Create a new call to match these folks up
            _UserCalls.Add(new ApplicationUserCall
            {
                ApplicationUsers = new List<ApplicationUser> { callingUser, targetUser }
            });

            // Tell the original caller that the call was accepted
            await Clients.Client(targetConnectionId.ConnectionId).CallAccepted(callingUser);

            // Update the user list, since thes two are now in a call
            await SendUserListUpdate();
        }

        public async Task HangUp()
        {
            var callingUser = _Users.SingleOrDefault(u => u.ConnectionId == Context.ConnectionId);

            if (callingUser == null)
            {
                return;
            }

            var currentCall = GetUserCall(callingUser.ConnectionId);

            // Send a hang up message to each user in the call, if there is one
            if (currentCall != null)
            {
                foreach (var user in currentCall.ApplicationUsers.Where(u => u.ConnectionId != callingUser.ConnectionId))
                {
                    await Clients.Client(user.ConnectionId).CallEnded(callingUser, string.Format("{0} has hung up.", callingUser.UserName));
                }

                // Remove the call from the list if there is only one (or none) person left.  This should
                // always trigger now, but will be useful when we implement conferencing.
                currentCall.ApplicationUsers.RemoveAll(u => u.ConnectionId == callingUser.ConnectionId);
                if (currentCall.ApplicationUsers.Count < 2)
                {
                    _UserCalls.Remove(currentCall);
                }
            }

            // Remove all offers initiating from the caller
            _CallOffers.RemoveAll(c => c.ApplicationCaller.ConnectionId == callingUser.ConnectionId);

            await SendUserListUpdate();
        }

        // WebRTC Signal Handler
        public async Task SendSignal(string signal, string targetConnectionId)
        {
            var callingUser = _Users.SingleOrDefault(u => u.ConnectionId == Context.ConnectionId);
            var targetUser = _Users.SingleOrDefault(u => u.ConnectionId == targetConnectionId);

            // Make sure both users are valid
            if (callingUser == null || targetUser == null)
            {
                return;
            }

            // Make sure that the person sending the signal is in a call
            var userCall = GetUserCall(callingUser.ConnectionId);

            // ...and that the target is the one they are in a call with
            if (userCall != null && userCall.ApplicationUsers.Exists(u => u.ConnectionId == targetUser.ConnectionId))
            {
                // These folks are in a call together, let's let em talk WebRTC
                await Clients.Client(targetConnectionId).ReceiveSignal(callingUser, signal);
            }
        }

        #region Private Helpers

        private async Task SendUserListUpdate()
        {
            _Users.ForEach(u => u.InCall = (GetUserCall(u.ConnectionId) != null));
            await Clients.All.UpdateUserList(_Users);
        }

        private ApplicationUserCall GetUserCall(string connectionId)
        {
            var matchingCall =
                _UserCalls.SingleOrDefault(uc => uc.ApplicationUsers.SingleOrDefault(u => u.ConnectionId == connectionId) != null);
            return matchingCall;
        }

        #endregion
    }
    public interface IConnectionHub
    {
        Task UpdateUserList(List<ApplicationUser> userList);
        Task CallAccepted(ApplicationUser acceptingUser);
        Task CallDeclined(ApplicationUser decliningUser, string reason);
        Task IncomingCall(ApplicationUser callingUser);
        Task ReceiveSignal(ApplicationUser signalingUser, string signal);
        Task CallEnded(ApplicationUser signalingUser, string signal);
    }
}
