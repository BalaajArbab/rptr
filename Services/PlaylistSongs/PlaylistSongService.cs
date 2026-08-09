using System.Globalization;
using rptr.Services.Snippets;

namespace rptr.Services.PlaylistSongs;

public sealed class PlaylistSongService : IPlaylistSongService
{
    private const int MaximumTagCount = 2;
    private const int MaximumTagLength = 10;

    private readonly DataContext _dbContext;
    private readonly ILogger<PlaylistSongService> _logger;

    public PlaylistSongService(
        DataContext dbContext,
        ILogger<PlaylistSongService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<ServiceResult<PlaylistSongsResponse>> GetForPlaylistAsync(
        int userId,
        string spotifyPlaylistId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(spotifyPlaylistId))
        {
            return ServiceResult<PlaylistSongsResponse>.Validation(PlaylistIdRequiredError());
        }

        var playlist = await _dbContext.Playlists
            .AsNoTracking()
            .Include(p => p.Songs)
            .ThenInclude(s => s.Snippet)
            .SingleOrDefaultAsync(
                p => p.SpotifyPlaylistId == spotifyPlaylistId,
                cancellationToken);

        if (playlist is null)
        {
            return ServiceResult<PlaylistSongsResponse>.Success(
                new PlaylistSongsResponse(false, false, []));
        }

        var canMutate = playlist.OwnerUserId == userId;
        var songs = playlist.Songs
            .Where(HasAnnotations)
            .OrderBy(s => s.Id)
            .Select(MapSong)
            .ToArray();

        return ServiceResult<PlaylistSongsResponse>.Success(
            new PlaylistSongsResponse(canMutate, canMutate, songs));
    }

    public async Task<ServiceResult<SongTagsResponse?>> PutTagsAsync(
        int userId,
        string spotifyPlaylistId,
        string spotifySongId,
        PutSongTagsRequest request,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateTagsRequest(spotifyPlaylistId, spotifySongId, request);
        if (validation.Errors is not null)
        {
            return ServiceResult<SongTagsResponse?>.Validation(validation.Errors);
        }

        var playlistResult = await GetOwnedPlaylistAsync(
            userId,
            spotifyPlaylistId,
            cancellationToken);

        if (playlistResult.Status != ServiceResultStatus.Success)
        {
            return ServiceResult<SongTagsResponse?>.Failure(playlistResult.Status);
        }

        var playlist = playlistResult.Value!;
        var song = await _dbContext.PlaylistSongs
            .Include(s => s.Snippet)
            .SingleOrDefaultAsync(
                s => s.PlaylistId == playlist.Id && s.SpotifySongId == spotifySongId,
                cancellationToken);

        var normalizedTags = validation.Tags;
        if (song is null && normalizedTags is null)
        {
            return ServiceResult<SongTagsResponse?>.Success(null);
        }

        if (song is null)
        {
            song = new PlaylistSong
            {
                Playlist = playlist,
                SpotifySongId = spotifySongId
            };

            _dbContext.PlaylistSongs.Add(song);
        }

        song.SongName = validation.SongName!;
        song.PrimaryArtistName = validation.PrimaryArtistName!;
        song.Tags = normalizedTags;

        if (!HasAnnotations(song))
        {
            _dbContext.PlaylistSongs.Remove(song);
        }

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (DatabaseErrorClassifier.IsUniqueConstraintViolation(ex))
        {
            return ServiceResult<SongTagsResponse?>.Failure(ServiceResultStatus.Conflict);
        }

        _logger.LogInformation(
            "Stored {TagCount} tags for playlist {SpotifyPlaylistId}, song {SpotifySongId}, and owner {UserId}.",
            normalizedTags?.Count ?? 0,
            spotifyPlaylistId,
            spotifySongId,
            userId);

        return normalizedTags is null
            ? ServiceResult<SongTagsResponse?>.Success(null)
            : ServiceResult<SongTagsResponse?>.Success(MapTags(song, spotifyPlaylistId));
    }

    public async Task<ServiceResult<SongExcludedResponse>> PutExcludedAsync(
        int userId,
        string spotifyPlaylistId,
        string spotifySongId,
        PutSongExcludedRequest request,
        CancellationToken cancellationToken = default)
    {
        var metadata = ValidateMetadata(
            spotifyPlaylistId,
            spotifySongId,
            request.SongName,
            request.PrimaryArtistName);

        if (metadata.Errors is not null)
        {
            return ServiceResult<SongExcludedResponse>.Validation(metadata.Errors);
        }

        var playlistResult = await GetOwnedPlaylistAsync(
            userId,
            spotifyPlaylistId,
            cancellationToken);

        if (playlistResult.Status != ServiceResultStatus.Success)
        {
            return ServiceResult<SongExcludedResponse>.Failure(playlistResult.Status);
        }

        var playlist = playlistResult.Value!;
        var song = await _dbContext.PlaylistSongs
            .Include(s => s.Snippet)
            .SingleOrDefaultAsync(
                s => s.PlaylistId == playlist.Id && s.SpotifySongId == spotifySongId,
                cancellationToken);

        if (song is null && !request.Excluded)
        {
            return ServiceResult<SongExcludedResponse>.Success(
                new SongExcludedResponse(spotifySongId, false));
        }

        if (song is null)
        {
            song = new PlaylistSong
            {
                Playlist = playlist,
                SpotifySongId = spotifySongId
            };

            _dbContext.PlaylistSongs.Add(song);
        }

        song.SongName = metadata.SongName!;
        song.PrimaryArtistName = metadata.PrimaryArtistName!;
        song.Excluded = request.Excluded;

        if (!HasAnnotations(song))
        {
            _dbContext.PlaylistSongs.Remove(song);
        }

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (DatabaseErrorClassifier.IsUniqueConstraintViolation(ex))
        {
            return ServiceResult<SongExcludedResponse>.Failure(ServiceResultStatus.Conflict);
        }

        _logger.LogInformation(
            "Set excluded to {Excluded} for playlist {SpotifyPlaylistId}, song {SpotifySongId}, and owner {UserId}.",
            request.Excluded,
            spotifyPlaylistId,
            spotifySongId,
            userId);

        return ServiceResult<SongExcludedResponse>.Success(
            new SongExcludedResponse(spotifySongId, request.Excluded));
    }

    private async Task<ServiceResult<Playlist>> GetOwnedPlaylistAsync(
        int userId,
        string spotifyPlaylistId,
        CancellationToken cancellationToken)
    {
        var playlist = await _dbContext.Playlists.SingleOrDefaultAsync(
            p => p.SpotifyPlaylistId == spotifyPlaylistId,
            cancellationToken);

        if (playlist is null)
        {
            return ServiceResult<Playlist>.Failure(ServiceResultStatus.NotFound);
        }

        return playlist.OwnerUserId == userId
            ? ServiceResult<Playlist>.Success(playlist)
            : ServiceResult<Playlist>.Failure(ServiceResultStatus.Forbidden);
    }

    private static TagsValidationResult ValidateTagsRequest(
        string spotifyPlaylistId,
        string spotifySongId,
        PutSongTagsRequest request)
    {
        var metadata = ValidateMetadata(
            spotifyPlaylistId,
            spotifySongId,
            request.SongName,
            request.PrimaryArtistName);
        var errors = metadata.Errors is null
            ? new Dictionary<string, string[]>()
            : new Dictionary<string, string[]>(metadata.Errors);

        if (request.Tags is null)
        {
            errors["tags"] = ["Tags are required. Use an empty array to clear the stored song tags."];
            return new TagsValidationResult(null, null, null, errors);
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

        if (errors.Count > 0)
        {
            return new TagsValidationResult(null, null, null, errors);
        }

        return new TagsValidationResult(
            metadata.SongName,
            metadata.PrimaryArtistName,
            normalizedTags.Count == 0 ? null : normalizedTags,
            null);
    }

    private static MetadataValidationResult ValidateMetadata(
        string spotifyPlaylistId,
        string spotifySongId,
        string? songName,
        string? primaryArtistName)
    {
        var errors = new Dictionary<string, string[]>();
        var normalizedSongName = songName?.Trim() ?? string.Empty;
        var normalizedArtistName = primaryArtistName?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(spotifyPlaylistId))
        {
            errors["spotifyPlaylistId"] = ["The Spotify playlist id is required."];
        }

        if (string.IsNullOrWhiteSpace(spotifySongId))
        {
            errors["songId"] = ["The Spotify song id is required."];
        }

        if (string.IsNullOrWhiteSpace(normalizedSongName))
        {
            errors["songName"] = ["The song name is required."];
        }

        if (string.IsNullOrWhiteSpace(normalizedArtistName))
        {
            errors["primaryArtistName"] = ["The primary artist name is required."];
        }

        return errors.Count == 0
            ? new MetadataValidationResult(normalizedSongName, normalizedArtistName, null)
            : new MetadataValidationResult(null, null, errors);
    }

    private static bool HasAnnotations(PlaylistSong song) =>
        song.Snippet is not null || song.Tags is not null || song.Excluded;

    private static PlaylistSongResponse MapSong(PlaylistSong song) => new(
        song.SpotifySongId,
        song.SongName,
        song.PrimaryArtistName,
        song.OriginalSongLengthMs,
        song.Excluded,
        song.Tags?.ToArray(),
        song.Snippet is null
            ? null
            : new PlaylistSongSnippetResponse(
                song.Snippet.Id,
                song.Snippet.Name,
                song.Snippet.CreatedAt,
                song.Snippet.Segments
                    .Select(s => new SnippetSegmentDto(s.StartMs, s.EndMs))
                    .ToArray()));

    private static SongTagsResponse MapTags(PlaylistSong song, string spotifyPlaylistId) => new(
        song.Id,
        spotifyPlaylistId,
        song.SpotifySongId,
        song.SongName,
        song.PrimaryArtistName,
        song.Tags!.ToArray());

    private static IReadOnlyDictionary<string, string[]> PlaylistIdRequiredError() =>
        new Dictionary<string, string[]>
        {
            ["spotifyPlaylistId"] = ["The Spotify playlist id is required."]
        };

    private sealed record TagsValidationResult(
        string? SongName,
        string? PrimaryArtistName,
        List<string>? Tags,
        IReadOnlyDictionary<string, string[]>? Errors);

    private sealed record MetadataValidationResult(
        string? SongName,
        string? PrimaryArtistName,
        IReadOnlyDictionary<string, string[]>? Errors);
}
