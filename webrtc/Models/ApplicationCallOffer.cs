using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace webrtc.Models
{
    public class ApplicationCallOffer
    {
        public ApplicationUser ApplicationCaller { get; set; }
        public ApplicationUser ApplicationCallee { get; set; }
    }
}
