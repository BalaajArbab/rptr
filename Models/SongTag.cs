namespace rptr.Models;

public class SongTag
{
    public int Id { get; set; }
    public int PlaylistId { get; set; }
    public string SongId { get; set; } = string.Empty;
    public string SongName { get; set; } = string.Empty;
    public string PrimaryArtistName { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = [];
    public Playlist Playlist { get; set; } = null!;
}
