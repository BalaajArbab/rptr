using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;

namespace rptr.Services.Activity;

public sealed class ApplicationInsightsActivityEventSink(
    ILogger<ApplicationInsightsActivityEventSink> logger)
    : IActivityEventSink
{
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> EventProperties = new();

    public void Track(
        ActivityEventRequest activityEvent,
        ActivityActor actor,
        string? clientIpAddress) =>
        Track(
            ActivityEventMetadata.GetDiscriminator(activityEvent.GetType()),
            activityEvent,
            actor,
            clientIpAddress);

    public void Track(
        string eventName,
        object activityEvent,
        ActivityActor actor,
        string? clientIpAddress)
    {
        var eventType = activityEvent.GetType();
        var properties = new Dictionary<string, object?>
        {
            ["ActorType"] = actor.Type,
            ["UserId"] = actor.UserId,
            ["GuestSessionNumber"] = actor.GuestSessionNumber,
            ["ClientIpAddress"] = clientIpAddress
        };

        foreach (var property in GetEventProperties(eventType))
        {
            properties[property.Name] = ConvertPropertyValue(
                property.GetValue(activityEvent));
        }

        using var scope = logger.BeginScope(properties);
        logger.LogInformation(
            "{microsoft.custom_event.name} {microsoft.client.ip}",
            eventName,
            clientIpAddress);
    }

    private static PropertyInfo[] GetEventProperties(Type eventType) =>
        EventProperties.GetOrAdd(
            eventType,
            static type => type
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(property => property.CanRead
                    && property.GetIndexParameters().Length == 0)
                .ToArray());

    private static object? ConvertPropertyValue(object? value) => value switch
    {
        IEnumerable<string> values => string.Join(", ", values),
        IEnumerable values => JsonSerializer.Serialize(value, value.GetType()),
        Enum enumValue => enumValue.ToString(),
        _ => value
    };
}
