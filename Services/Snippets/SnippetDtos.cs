using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace rptr.Services.Snippets;

public sealed record SnippetSegmentDto(int StartMs, int EndMs);

public sealed record CreateSnippetRequest(
    string Name,
    string PlaylistId,
    string SongId,
    string SongName,
    string PrimaryArtistName,
    [param: Range(1, int.MaxValue)]
    int OriginalSongLengthMs,
    [param: MinLength(1), MaxLength(4)]
    IReadOnlyList<SnippetSegmentDto> Segments);

public sealed record UpdateSnippetRequest(
    string Name,
    [param: MinLength(1), MaxLength(4)]
    IReadOnlyList<SnippetSegmentDto> Segments);

public sealed record SnippetResponse(
    [property: JsonNumberHandling(JsonNumberHandling.Strict)]
    int Id,
    string PlaylistId,
    string SongId,
    string Name,
    string SongName,
    string PrimaryArtistName,
    int OriginalSongLengthMs,
    DateTime CreatedAt,
    IReadOnlyList<SnippetSegmentDto> Segments);

public sealed record PlaylistSnippetsResponse(
    bool CanEdit,
    bool CanDelete,
    IReadOnlyList<SnippetResponse> Snippets);
