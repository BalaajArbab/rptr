using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace rptr.Models;

public class User
{
    public int Id { get; set; }
    public string SpotifyAccountId { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string SpotifyProfileUrl { get; set; } = string.Empty;
    public string SpotifyImageUrl { get; set; } = string.Empty;
    public string SubscriptionLevel { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public SpotifyToken? SpotifyToken { get; set; }

}
