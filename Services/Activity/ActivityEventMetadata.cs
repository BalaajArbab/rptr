using System.Collections.Frozen;
using System.Reflection;
using System.Text.Json.Serialization;

namespace rptr.Services.Activity;

public sealed record ActivityEventType(Type RequestType, string Discriminator);

public static class ActivityEventMetadata
{
    private static readonly ActivityEventType[] RegisteredEventTypes =
        typeof(ActivityEventRequest)
            .GetCustomAttributes<JsonDerivedTypeAttribute>(inherit: false)
            .Select(attribute => new ActivityEventType(
                attribute.DerivedType,
                attribute.TypeDiscriminator as string
                    ?? throw new InvalidOperationException(
                        $"Activity event '{attribute.DerivedType.Name}' must use a string discriminator.")))
            .ToArray();

    private static readonly FrozenDictionary<Type, string> DiscriminatorsByType =
        RegisteredEventTypes.ToFrozenDictionary(
            eventType => eventType.RequestType,
            eventType => eventType.Discriminator);

    public static IReadOnlyList<ActivityEventType> EventTypes => RegisteredEventTypes;

    public static string GetDiscriminator(Type requestType) =>
        DiscriminatorsByType.TryGetValue(requestType, out var discriminator)
            ? discriminator
            : throw new InvalidOperationException(
                $"Activity event type '{requestType.Name}' is not registered with JsonDerivedType.");
}
