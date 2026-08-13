using System.Globalization;

namespace rptr.Services.Snippets;

public sealed class SnippetService : ISnippetService
{
    private const int MaximumNameLength = 45;
    private const int MinimumSegmentLengthMs = 6000;

    private readonly DataContext _dbContext;
    private readonly ILogger<SnippetService> _logger;

    public SnippetService(
        DataContext dbContext,
        ILogger<SnippetService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<ServiceResult<SnippetResponse>> CreateAsync(
        int userId,
        CreateSnippetRequest request,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateCreateRequest(request);
        if (validation.Errors is not null)
        {
            return ServiceResult<SnippetResponse>.Validation(validation.Errors);
        }

        var playlist = await _dbContext.Playlists
            .SingleOrDefaultAsync(
                p => p.SpotifyPlaylistId == request.PlaylistId,
                cancellationToken);

        if (playlist is null)
        {
            return ServiceResult<SnippetResponse>.Failure(ServiceResultStatus.NotFound);
        }

        if (playlist.OwnerUserId != userId)
        {
            return ServiceResult<SnippetResponse>.Failure(ServiceResultStatus.Forbidden);
        }

        var playlistSong = await _dbContext.PlaylistSongs
            .Include(s => s.Snippet)
            .SingleOrDefaultAsync(
                s => s.PlaylistId == playlist.Id && s.SpotifySongId == request.SongId,
                cancellationToken);

        if (playlistSong?.Snippet is not null)
        {
            return ServiceResult<SnippetResponse>.Failure(ServiceResultStatus.Conflict);
        }

        if (playlistSong is null)
        {
            playlistSong = new PlaylistSong
            {
                Playlist = playlist,
                SpotifySongId = request.SongId,
                Tags = null,
                Excluded = false
            };

            _dbContext.PlaylistSongs.Add(playlistSong);
        }

        playlistSong.SongName = request.SongName.Trim();
        playlistSong.PrimaryArtistName = request.PrimaryArtistName.Trim();
        playlistSong.OriginalSongLengthMs = request.OriginalSongLengthMs;

        var snippet = new Snippet
        {
            PlaylistSong = playlistSong,
            Name = validation.Name!,
            CreatedAt = DateTime.UtcNow,
            Segments = validation.Segments!
        };

        _dbContext.Snippets.Add(snippet);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (DatabaseErrorClassifier.IsUniqueConstraintViolation(ex))
        {
            return ServiceResult<SnippetResponse>.Failure(ServiceResultStatus.Conflict);
        }

        _logger.LogInformation(
            "Created snippet {SnippetId} for playlist {SpotifyPlaylistId}, song {SongId}, and owner {UserId}.",
            snippet.Id,
            playlist.SpotifyPlaylistId,
            playlistSong.SpotifySongId,
            userId);

        return ServiceResult<SnippetResponse>.Success(MapSnippet(snippet));
    }

    public async Task<ServiceResult<SnippetResponse>> UpdateAsync(
        int userId,
        int snippetId,
        UpdateSnippetRequest request,
        CancellationToken cancellationToken = default)
    {
        var snippet = await _dbContext.Snippets
            .Include(s => s.PlaylistSong)
            .ThenInclude(s => s.Playlist)
            .SingleOrDefaultAsync(s => s.Id == snippetId, cancellationToken);

        if (snippet is null)
        {
            return ServiceResult<SnippetResponse>.Failure(ServiceResultStatus.NotFound);
        }

        if (snippet.PlaylistSong.Playlist.OwnerUserId != userId)
        {
            return ServiceResult<SnippetResponse>.Failure(ServiceResultStatus.Forbidden);
        }

        var validation = ValidateNameAndSegments(
            request.Name,
            request.Segments,
            snippet.PlaylistSong.OriginalSongLengthMs!.Value);

        if (validation.Errors is not null)
        {
            return ServiceResult<SnippetResponse>.Validation(validation.Errors);
        }

        snippet.Name = validation.Name!;
        snippet.Segments = validation.Segments!;
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Updated snippet {SnippetId} by owner {UserId}.", snippetId, userId);
        return ServiceResult<SnippetResponse>.Success(MapSnippet(snippet));
    }

    public async Task<ServiceResult<bool>> DeleteAsync(
        int userId,
        int snippetId,
        CancellationToken cancellationToken = default)
    {
        var snippet = await _dbContext.Snippets
            .Include(s => s.PlaylistSong)
            .ThenInclude(s => s.Playlist)
            .SingleOrDefaultAsync(s => s.Id == snippetId, cancellationToken);

        if (snippet is null)
        {
            return ServiceResult<bool>.Failure(ServiceResultStatus.NotFound);
        }

        if (snippet.PlaylistSong.Playlist.OwnerUserId != userId)
        {
            return ServiceResult<bool>.Failure(ServiceResultStatus.Forbidden);
        }

        var playlistSong = snippet.PlaylistSong;
        if (playlistSong.Tags is null && !playlistSong.Excluded)
        {
            _dbContext.PlaylistSongs.Remove(playlistSong);
        }
        else
        {
            _dbContext.Snippets.Remove(snippet);
        }
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Deleted snippet {SnippetId} by owner {UserId}.", snippetId, userId);
        return ServiceResult<bool>.Success(true);
    }

    private static CreateValidationResult ValidateCreateRequest(CreateSnippetRequest request)
    {
        var validation = ValidateNameAndSegments(
            request.Name,
            request.Segments,
            request.OriginalSongLengthMs);
        var errors = validation.Errors is null
            ? new Dictionary<string, string[]>()
            : new Dictionary<string, string[]>(validation.Errors);

        AddRequiredError(errors, "playlistId", request.PlaylistId, "The Spotify playlist id is required.");
        AddRequiredError(errors, "songId", request.SongId, "The Spotify song id is required.");
        AddRequiredError(errors, "songName", request.SongName, "The song name is required.");
        AddRequiredError(
            errors,
            "primaryArtistName",
            request.PrimaryArtistName,
            "The primary artist name is required.");

        if (request.OriginalSongLengthMs <= 0)
        {
            errors["originalSongLengthMs"] = ["The original song length must be positive."];
        }

        return errors.Count == 0
            ? new CreateValidationResult(validation.Name, validation.Segments, null)
            : new CreateValidationResult(null, null, errors);
    }

    private static NameAndSegmentsValidationResult ValidateNameAndSegments(
        string? name,
        IReadOnlyList<SnippetSegmentDto>? segments,
        int originalSongLengthMs)
    {
        var errors = new Dictionary<string, string[]>();
        var trimmedName = name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmedName))
        {
            errors["name"] = ["The snippet name cannot be blank."];
        }

        if (segments is null || segments.Count is < 1 or > 4)
        {
            errors["segments"] = ["A snippet must contain between 1 and 4 segments."];
        }

        var normalizedSegments = new List<SnippetSegment>();
        if (segments is not null)
        {
            var previousEndMs = -1;
            for (var index = 0; index < segments.Count; index++)
            {
                var segment = segments[index];
                var segmentErrors = new List<string>();

                if (segment.StartMs < 0)
                {
                    segmentErrors.Add("The start timestamp cannot be negative.");
                }

                if (segment.EndMs > originalSongLengthMs)
                {
                    segmentErrors.Add("The end timestamp cannot exceed the original song length.");
                }

                if ((long)segment.EndMs - segment.StartMs < MinimumSegmentLengthMs)
                {
                    segmentErrors.Add(
                        $"Each segment must be at least {MinimumSegmentLengthMs} milliseconds long.");
                }

                if (index > 0 && segment.StartMs < previousEndMs)
                {
                    segmentErrors.Add("Segments must be ordered and cannot overlap.");
                }

                if (segmentErrors.Count > 0)
                {
                    errors[$"segments[{index}]"] = segmentErrors.ToArray();
                }

                previousEndMs = segment.EndMs;
                normalizedSegments.Add(new SnippetSegment
                {
                    StartMs = segment.StartMs,
                    EndMs = segment.EndMs
                });
            }
        }

        return errors.Count == 0
            ? new NameAndSegmentsValidationResult(
                TruncateTextElements(trimmedName, MaximumNameLength),
                normalizedSegments,
                null)
            : new NameAndSegmentsValidationResult(null, null, errors);
    }

    private static void AddRequiredError(
        IDictionary<string, string[]> errors,
        string key,
        string? value,
        string message)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors[key] = [message];
        }
    }

    private static string TruncateTextElements(string value, int maximumLength)
    {
        var indexes = StringInfo.ParseCombiningCharacters(value);
        return indexes.Length <= maximumLength
            ? value
            : value[..indexes[maximumLength]];
    }

    private static SnippetResponse MapSnippet(Snippet snippet) => new(
        snippet.Id,
        snippet.PlaylistSong.Playlist.SpotifyPlaylistId,
        snippet.PlaylistSong.SpotifySongId,
        snippet.Name,
        snippet.PlaylistSong.SongName,
        snippet.PlaylistSong.PrimaryArtistName,
        snippet.PlaylistSong.OriginalSongLengthMs!.Value,
        snippet.CreatedAt,
        snippet.Segments.Select(s => new SnippetSegmentDto(s.StartMs, s.EndMs)).ToArray());

    private sealed record NameAndSegmentsValidationResult(
        string? Name,
        List<SnippetSegment>? Segments,
        IReadOnlyDictionary<string, string[]>? Errors);

    private sealed record CreateValidationResult(
        string? Name,
        List<SnippetSegment>? Segments,
        IReadOnlyDictionary<string, string[]>? Errors);
}
