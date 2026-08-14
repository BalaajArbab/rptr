namespace rptr.Services.Activity;

public sealed record ActivityActor(
    string Type,
    int? UserId,
    long? GuestSessionNumber);

public interface IActivityEventSink
{
    void Track(
        ActivityEventRequest activityEvent,
        ActivityActor actor,
        string? clientIpAddress);

    void Track(
        string eventName,
        object activityEvent,
        ActivityActor actor,
        string? clientIpAddress);
}
