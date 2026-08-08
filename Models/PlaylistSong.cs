namespace rptr.Models;

public class PlaylistSong
{
    public int Id { get; set; }
    public int PlaylistId { get; set; }
    public string SpotifySongId { get; set; } = string.Empty;
    public string SongName { get; set; } = string.Empty;
    public string PrimaryArtistName { get; set; } = string.Empty;
    public int? OriginalSongLengthMs { get; set; }
    public bool Excluded { get; set; }
    public List<string>? Tags { get; set; }
    public Playlist Playlist { get; set; } = null!;
    public Snippet? Snippet { get; set; }
}
