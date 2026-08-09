using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.WebUtilities;
using rptr.Services.Authorization;
using rptr.Services.Spotify;

namespace rptr.Services.Playlists;

public sealed class PlaylistService : IPlaylistService
{
    private const int MaximumTagCount = 10;

    private static readonly Regex HexColourPattern = new(
        "^#[0-9A-Fa-f]{6}$",
        RegexOptions.CultureInvariant);

    private readonly DataContext _dbContext;
    private readonly IAuthService _authService;
    private readonly ISpotifyHttpClient _spotifyHttpClient;
    private readonly ILogger<PlaylistService> _logger;

    public PlaylistService(
        DataContext dbContext,
        IAuthService authService,
        ISpotifyHttpClient spotifyHttpClient,
        ILogger<PlaylistService> logger)
    {
        _dbContext = dbContext;
        _authService = authService;
        _spotifyHttpClient = spotifyHttpClient;
        _logger = logger;
    }

    public async Task<ServiceResult<PlaylistAccessResponse>> AccessAsync(
        int userId,
        string spotifyPlaylistId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(spotifyPlaylistId))
        {
            return ServiceResult<PlaylistAccessResponse>.Validation(new Dictionary<string, string[]>
            {
                ["spotifyPlaylistId"] = ["The Spotify playlist id is required."]
            });
        }

        var playlist = await _dbContext.Playlists
            .SingleOrDefaultAsync(
                p => p.SpotifyPlaylistId == spotifyPlaylistId,
                cancellationToken);

        if (playlist is not null)
        {
            playlist.LastAccessed = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Updated last access for playlist {SpotifyPlaylistId} by user {UserId}.",
                spotifyPlaylistId,
                userId);

            return ServiceResult<PlaylistAccessResponse>.Success(
                new PlaylistAccessResponse(spotifyPlaylistId, playlist.LastAccessed, false));
        }

        var user = await _dbContext.Users
            .SingleOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user is null)
        {
            return ServiceResult<PlaylistAccessResponse>.Failure(ServiceResultStatus.Unauthorized);
        }

        SpotifyAccessTokenResponse? token;
        try
        {
            token = await _authService.GetSpotifyAccessTokenAsync(userId, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Could not obtain a current Spotify access token for playlist registration by user {UserId}.",
                userId);
            return ServiceResult<PlaylistAccessResponse>.Failure(ServiceResultStatus.UpstreamFailure);
        }

        if (token is null)
        {
            return ServiceResult<PlaylistAccessResponse>.Failure(ServiceResultStatus.Unauthorized);
        }

        SpotifyPlaylistResponse spotifyPlaylist;
        try
        {
            spotifyPlaylist = await GetSpotifyPlaylistAsync(
                spotifyPlaylistId,
                token.SpotifyAccessToken,
                cancellationToken);
        }
        catch (SpotifyApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogWarning(
                "Spotify playlist registration failed because playlist {SpotifyPlaylistId} was not found.",
                spotifyPlaylistId);
            return ServiceResult<PlaylistAccessResponse>.Failure(ServiceResultStatus.NotFound);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Spotify playlist verification failed for playlist {SpotifyPlaylistId} and user {UserId}.",
                spotifyPlaylistId,
                userId);
            return ServiceResult<PlaylistAccessResponse>.Failure(ServiceResultStatus.UpstreamFailure);
        }

        if (spotifyPlaylist.IsPublic is not true)
        {
            return ServiceResult<PlaylistAccessResponse>.Validation(new Dictionary<string, string[]>
            {
                ["spotifyPlaylistId"] = ["Only public Spotify playlists are supported."]
            });
        }

        if (!string.Equals(
                spotifyPlaylist.Owner?.Id,
                user.AccountName,
                StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "Playlist {SpotifyPlaylistId} owner {SpotifyOwnerId} did not match Spotify user id {SpotifyUserId} for user {UserId}.",
                spotifyPlaylistId,
                spotifyPlaylist.Owner?.Id,
                user.AccountName,
                userId);

            return ServiceResult<PlaylistAccessResponse>.Failure(ServiceResultStatus.Forbidden);
        }

        if (!string.Equals(spotifyPlaylist.Id, spotifyPlaylistId, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "Spotify playlist verification returned an unexpected id for requested playlist {SpotifyPlaylistId}.",
                spotifyPlaylistId);
            return ServiceResult<PlaylistAccessResponse>.Failure(ServiceResultStatus.UpstreamFailure);
        }

        var accessedAt = DateTime.UtcNow;
        playlist = new Playlist
        {
            SpotifyPlaylistId = spotifyPlaylistId,
            OwnerUserId = userId,
            CreatedAt = accessedAt,
            LastAccessed = accessedAt
        };

        _dbContext.Playlists.Add(playlist);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (DatabaseErrorClassifier.IsUniqueConstraintViolation(ex))
        {
            return ServiceResult<PlaylistAccessResponse>.Failure(ServiceResultStatus.Conflict);
        }

        _logger.LogInformation(
            "Verified and registered public Spotify playlist {SpotifyPlaylistId} for owner {UserId}.",
            spotifyPlaylistId,
            userId);

        return ServiceResult<PlaylistAccessResponse>.Success(
            new PlaylistAccessResponse(spotifyPlaylistId, accessedAt, true));
    }

    public async Task<IReadOnlyList<PlaylistTagDto>> GetTagsAsync(
        string spotifyPlaylistId,
        CancellationToken cancellationToken = default)
    {
        var playlist = await _dbContext.Playlists
            .AsNoTracking()
            .SingleOrDefaultAsync(
                p => p.SpotifyPlaylistId == spotifyPlaylistId,
                cancellationToken);

        return playlist?.Tags
            .Select(MapTag)
            .ToArray() ?? [];
    }

    public async Task<ServiceResult<IReadOnlyList<PlaylistTagDto>>> UpdateTagsAsync(
        int userId,
        string spotifyPlaylistId,
        IReadOnlyList<PlaylistTagDto>? tags,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateTags(tags);
        if (validation.Errors is not null)
        {
            return ServiceResult<IReadOnlyList<PlaylistTagDto>>.Validation(validation.Errors);
        }

        if (string.IsNullOrWhiteSpace(spotifyPlaylistId))
        {
            return ServiceResult<IReadOnlyList<PlaylistTagDto>>.Validation(
                new Dictionary<string, string[]>
                {
                    ["spotifyPlaylistId"] = ["The Spotify playlist id is required."]
                });
        }

        var playlist = await _dbContext.Playlists
            .SingleOrDefaultAsync(
                p => p.SpotifyPlaylistId == spotifyPlaylistId,
                cancellationToken);

        if (playlist is null)
        {
            return ServiceResult<IReadOnlyList<PlaylistTagDto>>.Failure(ServiceResultStatus.NotFound);
        }

        if (playlist.OwnerUserId != userId)
        {
            return ServiceResult<IReadOnlyList<PlaylistTagDto>>.Failure(ServiceResultStatus.Forbidden);
        }

        var normalizedTags = validation.Tags!;
        playlist.Tags = normalizedTags;
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Updated {TagCount} tags for playlist {SpotifyPlaylistId} by owner {UserId}.",
            normalizedTags.Count,
            spotifyPlaylistId,
            userId);

        return ServiceResult<IReadOnlyList<PlaylistTagDto>>.Success(
            normalizedTags.Select(MapTag).ToArray());
    }

    private async Task<SpotifyPlaylistResponse> GetSpotifyPlaylistAsync(
        string spotifyPlaylistId,
        string accessToken,
        CancellationToken cancellationToken)
    {
        var endpoint = QueryHelpers.AddQueryString(
            $"https://api.spotify.com/v1/playlists/{Uri.EscapeDataString(spotifyPlaylistId)}",
            "fields",
            "id,owner(id),public");

        using var response = await _spotifyHttpClient.SendAsync(
            () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
                return request;
            },
            cancellationToken);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var playlist = await JsonSerializer.DeserializeAsync<SpotifyPlaylistResponse>(
            stream,
            JsonSerializerOptions.Web,
            cancellationToken);

        if (playlist is null
            || string.IsNullOrWhiteSpace(playlist.Id)
            || string.IsNullOrWhiteSpace(playlist.Owner?.Id))
        {
            throw new InvalidOperationException("Spotify playlist response did not include its id and owner.");
        }

        return playlist;
    }

    private static TagValidationResult ValidateTags(IReadOnlyList<PlaylistTagDto>? tags)
    {
        var errors = new Dictionary<string, string[]>();
        if (tags is null)
        {
            errors["tags"] = ["Tags are required. Use an empty array to clear all tags."];
            return new TagValidationResult(null, errors);
        }

        if (tags.Count > MaximumTagCount)
        {
            errors["tags"] = [$"A playlist can have at most {MaximumTagCount} tags."];
        }

        var normalized = new List<PlaylistTag>(Math.Min(tags.Count, MaximumTagCount));
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < tags.Count; index++)
        {
            var name = tags[index].Name?.Trim() ?? string.Empty;
            var colour = tags[index].Colour?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(name))
            {
                errors[$"tags[{index}].name"] = ["Tag names cannot be blank."];
            }
            else if (!names.Add(name))
            {
                errors[$"tags[{index}].name"] = ["Tag names must be unique within a playlist."];
            }

            if (!HexColourPattern.IsMatch(colour))
            {
                errors[$"tags[{index}].colour"] = ["Tag colours must use #RRGGBB format."];
            }

            normalized.Add(new PlaylistTag
            {
                Name = name,
                Colour = colour.ToUpperInvariant()
            });
        }

        return errors.Count == 0
            ? new TagValidationResult(normalized, null)
            : new TagValidationResult(null, errors);
    }

    private static PlaylistTagDto MapTag(PlaylistTag tag) => new(tag.Name, tag.Colour);

    private sealed record TagValidationResult(
        List<PlaylistTag>? Tags,
        IReadOnlyDictionary<string, string[]>? Errors);

    private sealed class SpotifyPlaylistResponse
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("public")]
        public bool? IsPublic { get; set; }

        [JsonPropertyName("owner")]
        public SpotifyPlaylistOwner? Owner { get; set; }
    }

    private sealed class SpotifyPlaylistOwner
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;
    }
}
