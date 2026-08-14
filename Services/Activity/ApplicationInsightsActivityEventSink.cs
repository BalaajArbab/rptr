using System.Collections.Concurrent;
using System.Reflection;

namespace rptr.Services.Activity;

public sealed class ApplicationInsightsActivityEventSink(
    ILogger<ApplicationInsightsActivityEventSink> logger)
    : IActivityEventSink
{
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> EventProperties = new();

    public void Track(
        ActivityEventRequest activityEvent,
        ActivityActor actor,
        string? clientIpAddress)
    {
        var eventType = activityEvent.GetType();
        var properties = new Dictionary<string, object?>
        {
            ["ActorType"] = actor.Type,
            ["UserId"] = actor.UserId,
            ["GuestSessionNumber"] = actor.GuestSessionNumber,
            ["client.address"] = clientIpAddress
        };

        foreach (var property in GetEventProperties(eventType))
        {
            properties[property.Name] = property.GetValue(activityEvent);
        }

        using var scope = logger.BeginScope(properties);
        logger.LogInformation(
            "{microsoft.custom_event.name}",
            ActivityEventMetadata.GetDiscriminator(eventType));
    }

    private static PropertyInfo[] GetEventProperties(Type eventType) =>
        EventProperties.GetOrAdd(
            eventType,
            static type => type
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(property => property.CanRead
                    && property.GetIndexParameters().Length == 0)
                .ToArray());
}
