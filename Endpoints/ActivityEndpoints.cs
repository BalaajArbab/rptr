using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using rptr.Services.Activity;
using rptr.Services.Authorization;
using rptr.Services.GuestSessions;

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
                StatusCodes.Status403Forbidden,
                "application/problem+json")
            .ProducesProblem(
                StatusCodes.Status429TooManyRequests,
                "application/problem+json");

        endpoints.MapPost(
                "/api/feedback/guest/session-ended",
                RecordGuestSessionEnded)
            .RequireAuthorization(policy =>
            {
                policy.AuthenticationSchemes.Add(
                    GuestFeedbackTokenService.AuthenticationScheme);
                policy.RequireAuthenticatedUser();
                policy.RequireClaim(
                    ClaimsPrincipalExtensions.SessionTypeClaim,
                    GuestFeedbackTokenService.SessionType);
            })
            .RequireRateLimiting("Activity")
            .WithName("RecordGuestSessionEnded")
            .WithTags("Activity")
            .WithSummary("Record the end of a guest session")
            .WithDescription(
                "Records a guest-session ending using the restricted token returned when the guest session began. " +
                "The session identity and duration are derived by the backend.")
            .Accepts<GuestSessionEndedActivityEventRequest>("application/json")
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

        endpoints.MapPost("/api/hello", RecordFrontPageView)
            .AllowAnonymous()
            .RequireRateLimiting("PageView")
            .WithName("RecordFrontPageView")
            .WithTags("Activity")
            .WithSummary("Record a front-page view")
            .WithDescription(
                "Records an anonymous front-page view. Device type and client IP are derived by the backend.")
            .Produces(StatusCodes.Status202Accepted)
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
        catch (Exception exception)
            when (exception is JsonException or NotSupportedException)
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

        var isGuest = principal.TryGetGuestSessionNumber(out var guestSessionNumber);
        if (activityEvent is GuestSessionStartedActivityEventRequest && !isGuest)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Guest authentication required",
                detail: "Only a guest session can record a guest-session-started event.");
        }

        var actor = isGuest
            ? new ActivityActor("guest", null, guestSessionNumber)
            : new ActivityActor("normal", userId, null);

        eventSink.Track(
            activityEvent,
            actor,
            GetRemoteIpAddress(httpContext));

        return Results.Accepted();
    }

    private static IResult RecordGuestSessionEnded(
        GuestSessionEndedActivityEventRequest request,
        ClaimsPrincipal principal,
        HttpContext httpContext,
        IActivityEventSink eventSink,
        TimeProvider timeProvider)
    {
        if (!principal.TryGetGuestFeedbackSessionNumber(out var sessionNumber)
            || !principal.TryGetGuestFeedbackIssuedAt(out var issuedAt))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Guest session event authentication failed.");
        }

        var requestErrors = ValidateObject(request);
        if (requestErrors.Count > 0)
        {
            return Results.ValidationProblem(requestErrors);
        }

        var duration = timeProvider.GetUtcNow() - issuedAt;
        var durationSeconds = (int)Math.Clamp(
            Math.Floor(duration.TotalSeconds),
            0,
            int.MaxValue);

        eventSink.Track(
            GuestSessionEndedActivityEventRequest.EventType,
            new GuestSessionEndedActivityEvent(
                request.EndReason,
                durationSeconds),
            new ActivityActor("guest", null, sessionNumber),
            GetRemoteIpAddress(httpContext));

        return Results.Accepted();
    }

    private static IResult RecordFrontPageView(
        HttpContext httpContext,
        IActivityEventSink eventSink)
    {
        eventSink.Track(
            "front_page_viewed",
            new FrontPageViewedActivityEvent(
                GetDeviceType(httpContext.Request),
                GetPlatform(httpContext.Request)),
            new ActivityActor("anonymous", null, null),
            GetRemoteIpAddress(httpContext));

        return Results.Accepted();
    }

    private static JsonSerializerOptions CreateEventJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            AllowOutOfOrderMetadataProperties = true,
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

    private static string GetDeviceType(HttpRequest request)
    {
        if (string.Equals(
                request.Headers["Sec-CH-UA-Mobile"].ToString(),
                "?1",
                StringComparison.Ordinal))
        {
            return "phone";
        }

        var userAgent = request.Headers.UserAgent.ToString();
        return userAgent.Contains("Mobile", StringComparison.OrdinalIgnoreCase)
            || userAgent.Contains("iPhone", StringComparison.OrdinalIgnoreCase)
            || userAgent.Contains("Windows Phone", StringComparison.OrdinalIgnoreCase)
            ? "phone"
            : "desktop";
    }

    private static string GetPlatform(HttpRequest request)
    {
        var platformHint = request.Headers["Sec-CH-UA-Platform"]
            .ToString()
            .Trim()
            .Trim('"');
        if (!string.IsNullOrWhiteSpace(platformHint))
        {
            return platformHint;
        }

        var userAgent = request.Headers.UserAgent.ToString();
        if (userAgent.Contains("Android", StringComparison.OrdinalIgnoreCase))
        {
            return "Android";
        }

        if (userAgent.Contains("iPhone", StringComparison.OrdinalIgnoreCase)
            || userAgent.Contains("iPad", StringComparison.OrdinalIgnoreCase))
        {
            return "iOS";
        }

        if (userAgent.Contains("Windows", StringComparison.OrdinalIgnoreCase))
        {
            return "Windows";
        }

        if (userAgent.Contains("CrOS", StringComparison.OrdinalIgnoreCase))
        {
            return "Chrome OS";
        }

        if (userAgent.Contains("Mac OS", StringComparison.OrdinalIgnoreCase)
            || userAgent.Contains("Macintosh", StringComparison.OrdinalIgnoreCase))
        {
            return "macOS";
        }

        return userAgent.Contains("Linux", StringComparison.OrdinalIgnoreCase)
            ? "Linux"
            : "Unknown";
    }

    private sealed record GuestSessionEndedActivityEvent(
        string EndReason,
        int SessionDurationSeconds);

    private sealed record FrontPageViewedActivityEvent(
        string DeviceType,
        string Platform);
}
