using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using rptr.Services.Snippets;

namespace rptr.Services.PlaylistSongs;

public sealed record PlaylistSongSnippetResponse(
    [property: JsonNumberHandling(JsonNumberHandling.Strict)]
    int Id,
    string Name,
    DateTime CreatedAt,
    IReadOnlyList<SnippetSegmentDto> Segments);

public sealed record PlaylistSongResponse(
    string SongId,
    string SongName,
    string PrimaryArtistName,
    int? OriginalSongLengthMs,
    bool Excluded,
    IReadOnlyList<string>? Tags,
    PlaylistSongSnippetResponse? Snippet);

public sealed record PlaylistSongsResponse(
    bool CanEdit,
    bool CanDelete,
    IReadOnlyList<PlaylistSongResponse> Songs);

public sealed record PutSongTagsRequest(
    string SongName,
    string PrimaryArtistName,
    [param: MaxLength(2)]
    IReadOnlyList<string> Tags);

public sealed record SongTagsResponse(
    [property: JsonNumberHandling(JsonNumberHandling.Strict)]
    int Id,
    string PlaylistId,
    string SongId,
    string SongName,
    string PrimaryArtistName,
    IReadOnlyList<string> Tags);

public sealed record PutSongExcludedRequest(
    string SongName,
    string PrimaryArtistName,
    bool Excluded);

public sealed record SongExcludedResponse(
    string SongId,
    bool Excluded);
