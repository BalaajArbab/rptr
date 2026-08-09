using System.Globalization;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using rptr.Services.Authorization;

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
            .WithDescription("Stores a feedback message for the authenticated user.")
            .Accepts<CreateFeedbackRequest>("application/json")
            .Produces<CreateFeedbackResponse>(StatusCodes.Status201Created, "application/json")
            .ProducesValidationProblem(StatusCodes.Status400BadRequest, "application/problem+json")
            .ProducesProblem(StatusCodes.Status401Unauthorized, "application/problem+json");

        return endpoints;
    }

    private static async Task<IResult> CreateFeedbackAsync(
        CreateFeedbackRequest request,
        ClaimsPrincipal principal,
        HttpContext httpContext,
        DataContext db,
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

        var message = request.Message?.Trim() ?? string.Empty;
        var messageLength = new StringInfo(message).LengthInTextElements;

        if (messageLength == 0)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["message"] = ["A feedback message is required."]
            });
        }

        if (messageLength > MaximumMessageLength)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["message"] = [$"Feedback messages can have at most {MaximumMessageLength} characters."]
            });
        }

        var remoteIpAddress = httpContext.Connection.RemoteIpAddress;
        if (remoteIpAddress?.IsIPv4MappedToIPv6 == true)
        {
            remoteIpAddress = remoteIpAddress.MapToIPv4();
        }

        var feedback = new Feedback
        {
            UserId = userId,
            Message = message,
            IpAddress = remoteIpAddress?.ToString(),
            CreatedAt = DateTimeOffset.UtcNow
        };

        db.Feedback.Add(feedback);
        await db.SaveChangesAsync(cancellationToken);

        loggerFactory
            .CreateLogger("Feedback")
            .LogInformation(
                "Feedback {FeedbackId} received from user {UserId}",
                feedback.Id,
                userId);

        return Results.Json(
            new CreateFeedbackResponse(feedback.Id, feedback.CreatedAt),
            statusCode: StatusCodes.Status201Created);
    }
}

public sealed record CreateFeedbackRequest(
    [param: Required, MaxLength(2000)] string? Message);

public sealed record CreateFeedbackResponse(int Id, DateTimeOffset CreatedAt);
