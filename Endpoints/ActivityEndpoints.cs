using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using rptr.Services.Activity;
using rptr.Services.Authorization;

namespace rptr.Endpoints;

public static class ActivityEndpoints
{
    private const int MaximumDecodedPayloadLength = 4096;
    private static readonly JsonSerializerOptions EventJsonOptions =
        CreateEventJsonOptions();

    public static IEndpointRouteBuilder MapActivityEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/token", RecordActivity)
            .RequireAuthorization()
            .RequireRateLimiting("Activity")
            .WithName("RecordActivity")
            .WithTags("Activity")
            .WithSummary("Record a client activity")
            .WithDescription(
                "Accepts Base64-encoded UTF-8 JSON containing a supported client activity event. " +
                "Actor identity, server time, and client IP are derived by the backend.")
            .Accepts<EncodedActivityRequest>("application/json")
            .Produces(StatusCodes.Status202Accepted)
            .ProducesValidationProblem(
                StatusCodes.Status400BadRequest,
                "application/problem+json")
            .ProducesProblem(
                StatusCodes.Status401Unauthorized,
                "application/problem+json")
            .ProducesProblem(
                StatusCodes.Status429TooManyRequests,
                "application/problem+json");

        return endpoints;
    }

    private static IResult RecordActivity(
        EncodedActivityRequest request,
        ClaimsPrincipal principal,
        HttpContext httpContext,
        IActivityEventSink eventSink)
    {
        if (!principal.TryGetUserId(out var userId))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Authentication failed",
                detail: "The authenticated session does not contain a valid user identifier.");
        }

        var envelopeErrors = ValidateObject(request);
        if (envelopeErrors.Count > 0)
        {
            return Results.ValidationProblem(envelopeErrors);
        }

        byte[] decodedPayload;
        try
        {
            decodedPayload = Convert.FromBase64String(request.Payload);
        }
        catch (FormatException)
        {
            return InvalidPayload("The payload must be valid Base64.");
        }

        if (decodedPayload.Length > MaximumDecodedPayloadLength)
        {
            return InvalidPayload(
                $"The decoded activity payload cannot exceed {MaximumDecodedPayloadLength} bytes.");
        }

        ActivityEventRequest? activityEvent;
        try
        {
            activityEvent = JsonSerializer.Deserialize<ActivityEventRequest>(
                decodedPayload,
                EventJsonOptions);
        }
        catch (JsonException)
        {
            return InvalidPayload(
                "The decoded payload is not a supported activity event.");
        }

        if (activityEvent is null)
        {
            return InvalidPayload("The decoded payload is required.");
        }

        var eventErrors = ValidateObject(activityEvent);
        if (eventErrors.Count > 0)
        {
            return Results.ValidationProblem(eventErrors);
        }

        var actor = principal.TryGetGuestSessionNumber(out var guestSessionNumber)
            ? new ActivityActor("guest", null, guestSessionNumber)
            : new ActivityActor("normal", userId, null);

        eventSink.Track(
            activityEvent,
            actor,
            GetRemoteIpAddress(httpContext));

        return Results.Accepted();
    }

    private static JsonSerializerOptions CreateEventJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        };
        options.Converters.Add(new TrimStringJsonConverter());
        return options;
    }

    private static Dictionary<string, string[]> ValidateObject(object value)
    {
        var validationResults = new List<ValidationResult>();
        Validator.TryValidateObject(
            value,
            new ValidationContext(value),
            validationResults,
            validateAllProperties: true);

        return validationResults
            .SelectMany(result => result.MemberNames.DefaultIfEmpty("payload")
                .Select(memberName => new
                {
                    MemberName = string.IsNullOrEmpty(memberName)
                        ? "payload"
                        : JsonNamingPolicy.CamelCase.ConvertName(memberName),
                    Message = result.ErrorMessage ?? "The value is invalid."
                }))
            .GroupBy(result => result.MemberName, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(result => result.Message).ToArray(),
                StringComparer.Ordinal);
    }

    private static IResult InvalidPayload(string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["payload"] = [message]
        });

    private static string? GetRemoteIpAddress(HttpContext httpContext)
    {
        var remoteIpAddress = httpContext.Connection.RemoteIpAddress;
        if (remoteIpAddress?.IsIPv4MappedToIPv6 == true)
        {
            remoteIpAddress = remoteIpAddress.MapToIPv4();
        }

        return remoteIpAddress?.ToString();
    }
}
