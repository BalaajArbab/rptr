namespace rptr.Models;

public class Snippet
{
    public int Id { get; set; }
    public int PlaylistId { get; set; }
    public string SongId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string SongName { get; set; } = string.Empty;
    public string PrimaryArtistName { get; set; } = string.Empty;
    public int OriginalSongLengthMs { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<SnippetSegment> Segments { get; set; } = [];
    public Playlist Playlist { get; set; } = null!;
}

public class SnippetSegment
{
    public int StartMs { get; set; }
    public int EndMs { get; set; }
}
