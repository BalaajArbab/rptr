using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace rptr.Services.SongTags;

public sealed record PutSongTagsRequest(
    string? SongName,
    string? PrimaryArtistName,
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
