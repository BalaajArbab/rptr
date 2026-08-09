using System.ComponentModel.DataAnnotations;

namespace rptr.Services.Playlists;

public sealed record PlaylistTagDto(
    [param: Required]
    string Name,
    [param: Required, RegularExpression("^#[0-9A-Fa-f]{6}$")]
    string Colour);

public sealed record UpdatePlaylistTagsRequest(
    [param: MaxLength(10)]
    IReadOnlyList<PlaylistTagDto> Tags);

public sealed record PlaylistAccessResponse(
    string SpotifyPlaylistId,
    DateTime LastAccessed,
    bool Created);
