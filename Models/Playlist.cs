namespace rptr.Models;

public class Playlist
{
    public int Id { get; set; }
    public string SpotifyPlaylistId { get; set; } = string.Empty;
    public int OwnerUserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastAccessed { get; set; }
    public List<PlaylistTag> Tags { get; set; } = [];
    public User OwnerUser { get; set; } = null!;
    public List<PlaylistSong> Songs { get; set; } = [];
}

public class PlaylistTag
{
    public string Name { get; set; } = string.Empty;
    public string Colour { get; set; } = string.Empty;
}
