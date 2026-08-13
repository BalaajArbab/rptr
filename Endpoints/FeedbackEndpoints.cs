using System.Globalization;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.RateLimiting;
using rptr.Services.Authorization;
using rptr.Services.Feedback;
using rptr.Services.GuestSessions;

namespace rptr.Endpoints;

public static class FeedbackEndpoints
{
    private const int MaximumMessageLength = 2000;

    public static IEndpointRouteBuilder MapFeedbackEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/feedback", CreateFeedbackAsync)
            .RequireAuthorization()
            .WithName("CreateFeedback")
            .WithTags("Feedback")
            .WithSummary("Submit feedback")
            .WithDescription("Stores a feedback message for the authenticated normal or guest user.")
            .Accepts<CreateFeedbackRequest>("application/json")
            .Produces<CreateFeedbackResponse>(StatusCodes.Status201Created, "application/json")
            .ProducesValidationProblem(StatusCodes.Status400BadRequest, "application/problem+json")
            .ProducesProblem(StatusCodes.Status401Unauthorized, "application/problem+json");

        endpoints.MapPost("/api/feedback/guest", CreateGuestFeedbackAsync)
            .RequireAuthorization(policy =>
            {
                policy.AuthenticationSchemes.Add(
                    GuestFeedbackTokenService.AuthenticationScheme);
                policy.RequireAuthenticatedUser();
                policy.RequireClaim(
                    ClaimsPrincipalExtensions.SessionTypeClaim,
                    GuestFeedbackTokenService.SessionType);
            })
            .RequireRateLimiting("GuestFeedback")
            .WithName("CreateGuestFeedback")
            .WithTags("Feedback")
            .WithSummary("Submit feedback after a guest session")
            .WithDescription(
                "Stores feedback using the feedback-only bearer token returned when the guest session began. " +
                "The token remains valid briefly after the guest session's absolute expiry and cannot authorize other API operations.")
            .Accepts<CreateFeedbackRequest>("application/json")
            .Produces<CreateFeedbackResponse>(StatusCodes.Status201Created, "application/json")
            .ProducesValidationProblem(StatusCodes.Status400BadRequest, "application/problem+json")
            .ProducesProblem(StatusCodes.Status401Unauthorized, "application/problem+json")
            .ProducesProblem(StatusCodes.Status429TooManyRequests, "application/problem+json")
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable, "application/problem+json");

        return endpoints;
    }

    private static async Task<IResult> CreateFeedbackAsync(
        CreateFeedbackRequest request,
        ClaimsPrincipal principal,
        HttpContext httpContext,
        IFeedbackStore feedbackStore,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (!principal.TryGetUserId(out var userId))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Authentication failed",
                detail: "The authenticated session does not contain a valid user identifier.");
        }

        var (message, validationProblem) = ValidateMessage(request.Message);
        if (validationProblem is not null)
        {
            return validationProblem;
        }

        FeedbackActor actor;
        if (principal.TryGetGuestSessionNumber(out var guestSessionNumber))
        {
            actor = new FeedbackActor("guest", null, guestSessionNumber);
        }
        else
        {
            actor = new FeedbackActor("normal", userId, null);
        }

        return await StoreFeedbackAsync(
            actor,
            message,
            httpContext,
            feedbackStore,
            loggerFactory.CreateLogger("Feedback"),
            cancellationToken);
    }

    private static async Task<IResult> CreateGuestFeedbackAsync(
        CreateFeedbackRequest request,
        ClaimsPrincipal principal,
        HttpContext httpContext,
        IFeedbackStore feedbackStore,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        if (!principal.TryGetGuestFeedbackSessionNumber(out var sessionNumber))
        {
            return Results.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                title: "Guest feedback authentication failed.");
        }

        var (message, validationProblem) = ValidateMessage(request.Message);
        if (validationProblem is not null)
        {
            return validationProblem;
        }

        var logger = loggerFactory.CreateLogger("Feedback");
        try
        {
            return await StoreFeedbackAsync(
                new FeedbackActor("guest", null, sessionNumber),
                message,
                httpContext,
                feedbackStore,
                logger,
                cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "Guest feedback could not be stored for session {GuestSessionNumber}",
                sessionNumber);
            return Results.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Feedback is temporarily unavailable.");
        }
    }

    private static async Task<IResult> StoreFeedbackAsync(
        FeedbackActor actor,
        string message,
        HttpContext httpContext,
        IFeedbackStore feedbackStore,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var feedback = await feedbackStore.CreateAsync(
            actor,
            message,
            GetRemoteIpAddress(httpContext),
            cancellationToken);

        logger.LogInformation(
            "Feedback {FeedbackId} received from {ActorType} actor {ActorId}",
            feedback.Id,
            actor.Type,
            actor.GuestSessionNumber?.ToString(CultureInfo.InvariantCulture)
                ?? actor.NormalUserId?.ToString(CultureInfo.InvariantCulture));

        return Results.Json(
            new CreateFeedbackResponse(feedback.Id, feedback.CreatedAt),
            statusCode: StatusCodes.Status201Created);
    }

    private static (string Message, IResult? ValidationProblem) ValidateMessage(
        string? requestedMessage)
    {
        var message = requestedMessage?.Trim() ?? string.Empty;
        var messageLength = new StringInfo(message).LengthInTextElements;

        if (messageLength == 0)
        {
            return (message, Results.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["message"] = ["A feedback message is required."]
                }));
        }

        if (messageLength > MaximumMessageLength)
        {
            return (message, Results.ValidationProblem(
                new Dictionary<string, string[]>
                {
                    ["message"] = [$"Feedback messages can have at most {MaximumMessageLength} characters."]
                }));
        }

        return (message, null);
    }

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

public sealed record CreateFeedbackRequest(
    [param: Required, MaxLength(2000)] string Message);

public sealed record CreateFeedbackResponse(int Id, DateTimeOffset CreatedAt);
