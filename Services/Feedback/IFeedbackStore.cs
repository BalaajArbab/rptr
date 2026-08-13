namespace rptr.Services.Feedback;

public sealed record FeedbackActor(
    string Type,
    int? NormalUserId,
    long? GuestSessionNumber);

public sealed record StoredFeedback(int Id, DateTimeOffset CreatedAt);

public interface IFeedbackStore
{
    Task<StoredFeedback> CreateAsync(
        FeedbackActor actor,
        string message,
        string? ipAddress,
        CancellationToken cancellationToken = default);
}
