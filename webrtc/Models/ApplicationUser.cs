using Microsoft.AspNetCore.Identity;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace webrtc.Models
{
    public enum Gender { Male, Female }
    public class ApplicationUser : IdentityUser
    {
        public string ConnectionId { get; set; }
        public bool InCall { get; set; }
        public Gender Gender { get; set; }
    }

}
