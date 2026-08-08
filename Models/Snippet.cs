namespace rptr.Models;

public class Snippet
{
    public int Id { get; set; }
    public int PlaylistSongId { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public List<SnippetSegment> Segments { get; set; } = [];
    public PlaylistSong PlaylistSong { get; set; } = null!;
}

public class SnippetSegment
{
    public int StartMs { get; set; }
    public int EndMs { get; set; }
}
