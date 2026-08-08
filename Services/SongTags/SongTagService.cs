using System.Globalization;

namespace rptr.Services.SongTags;

public sealed class SongTagService : ISongTagService
{
    private const int MaximumTagCount = 2;
    private const int MaximumTagLength = 9;

    private readonly DataContext _dbContext;
    private readonly ILogger<SongTagService> _logger;

    public SongTagService(
        DataContext dbContext,
        ILogger<SongTagService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<ServiceResult<IReadOnlyList<SongTagsResponse>>> GetForPlaylistAsync(
        string spotifyPlaylistId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(spotifyPlaylistId))
        {
            return ServiceResult<IReadOnlyList<SongTagsResponse>>.Validation(
                new Dictionary<string, string[]>
                {
                    ["spotifyPlaylistId"] = ["The Spotify playlist id is required."]
                });
        }

        var songTags = await _dbContext.SongTags
            .AsNoTracking()
            .Where(s => s.Playlist.SpotifyPlaylistId == spotifyPlaylistId)
            .OrderBy(s => s.Id)
            .ToArrayAsync(cancellationToken);

        return ServiceResult<IReadOnlyList<SongTagsResponse>>.Success(
            songTags.Select(s => Map(s, spotifyPlaylistId)).ToArray());
    }

    public async Task<ServiceResult<SongTagsResponse?>> PutAsync(
        int userId,
        string spotifyPlaylistId,
        string songId,
        PutSongTagsRequest request,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateRequest(spotifyPlaylistId, songId, request);
        if (validation.Errors is not null)
        {
            return ServiceResult<SongTagsResponse?>.Validation(validation.Errors);
        }

        var playlist = await _dbContext.Playlists
            .SingleOrDefaultAsync(
                p => p.SpotifyPlaylistId == spotifyPlaylistId,
                cancellationToken);

        if (playlist is null)
        {
            return ServiceResult<SongTagsResponse?>.Failure(ServiceResultStatus.NotFound);
        }

        if (playlist.OwnerUserId != userId)
        {
            return ServiceResult<SongTagsResponse?>.Failure(ServiceResultStatus.Forbidden);
        }

        var songTag = await _dbContext.SongTags
            .SingleOrDefaultAsync(
                s => s.PlaylistId == playlist.Id && s.SongId == songId,
                cancellationToken);

        if (validation.Tags!.Count == 0)
        {
            if (songTag is not null)
            {
                _dbContext.SongTags.Remove(songTag);
                await _dbContext.SaveChangesAsync(cancellationToken);

                _logger.LogInformation(
                    "Deleted song tags for playlist {SpotifyPlaylistId}, song {SongId}, and owner {UserId}.",
                    spotifyPlaylistId,
                    songId,
                    userId);
            }

            return ServiceResult<SongTagsResponse?>.Success(null);
        }

        if (songTag is null)
        {
            songTag = new SongTag
            {
                Playlist = playlist,
                SongId = songId
            };

            _dbContext.SongTags.Add(songTag);
        }

        songTag.SongName = validation.SongName!;
        songTag.PrimaryArtistName = validation.PrimaryArtistName!;
        songTag.Tags = validation.Tags;

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (DatabaseErrorClassifier.IsUniqueConstraintViolation(ex))
        {
            return ServiceResult<SongTagsResponse?>.Failure(ServiceResultStatus.Conflict);
        }

        _logger.LogInformation(
            "Upserted {TagCount} song tags for playlist {SpotifyPlaylistId}, song {SongId}, and owner {UserId}.",
            songTag.Tags.Count,
            spotifyPlaylistId,
            songId,
            userId);

        return ServiceResult<SongTagsResponse?>.Success(Map(songTag, spotifyPlaylistId));
    }

    private static RequestValidationResult ValidateRequest(
        string spotifyPlaylistId,
        string songId,
        PutSongTagsRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(spotifyPlaylistId))
        {
            errors["spotifyPlaylistId"] = ["The Spotify playlist id is required."];
        }

        if (string.IsNullOrWhiteSpace(songId))
        {
            errors["songId"] = ["The Spotify song id is required."];
        }

        if (request.Tags is null)
        {
            errors["tags"] = ["Tags are required. Use an empty array to delete the stored song tags."];
            return new RequestValidationResult(null, null, null, errors);
        }

        if (request.Tags.Count > MaximumTagCount)
        {
            errors["tags"] = [$"A song can have at most {MaximumTagCount} tags."];
        }

        var normalizedTags = new List<string>(Math.Min(request.Tags.Count, MaximumTagCount));
        var uniqueTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (var index = 0; index < request.Tags.Count; index++)
        {
            var tag = request.Tags[index]?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(tag))
            {
                errors[$"tags[{index}]"] = ["Song tags cannot be blank."];
            }
            else if (new StringInfo(tag).LengthInTextElements > MaximumTagLength)
            {
                errors[$"tags[{index}]"] = [$"Song tags can have at most {MaximumTagLength} characters."];
            }
            else if (!uniqueTags.Add(tag))
            {
                errors[$"tags[{index}]"] = ["Song tags must be unique for a song."];
            }

            normalizedTags.Add(tag);
        }

        var songName = request.SongName?.Trim() ?? string.Empty;
        var primaryArtistName = request.PrimaryArtistName?.Trim() ?? string.Empty;

        if (normalizedTags.Count > 0)
        {
            if (string.IsNullOrWhiteSpace(songName))
            {
                errors["songName"] = ["The song name is required when storing tags."];
            }

            if (string.IsNullOrWhiteSpace(primaryArtistName))
            {
                errors["primaryArtistName"] = ["The primary artist name is required when storing tags."];
            }
        }

        return errors.Count == 0
            ? new RequestValidationResult(songName, primaryArtistName, normalizedTags, null)
            : new RequestValidationResult(null, null, null, errors);
    }

    private static SongTagsResponse Map(SongTag songTag, string spotifyPlaylistId) => new(
        songTag.Id,
        spotifyPlaylistId,
        songTag.SongId,
        songTag.SongName,
        songTag.PrimaryArtistName,
        songTag.Tags.ToArray());

    private sealed record RequestValidationResult(
        string? SongName,
        string? PrimaryArtistName,
        List<string>? Tags,
        IReadOnlyDictionary<string, string[]>? Errors);
}
