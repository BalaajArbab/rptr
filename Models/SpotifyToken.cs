using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace rptr.Models
{
    public class SpotifyToken
    {
        
        public string AccessToken { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
        public DateTime ExpiresAt { get; set; }
        public DateTime RefreshTokenExpiresAt { get; set; }
        public string Scope { get; set; } = string.Empty;

        public int UserId { get; set; }
        public User User { get; set; } = null!;
    }
}