using rptr.Data.Mongo;

namespace rptr.Endpoints;

public static class KeepWarmEndpoints
{
    public static IEndpointRouteBuilder MapKeepWarmEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/touch", TouchAsync)
            .AllowAnonymous()
            .RequireRateLimiting("KeepWarm")
            .WithName("TouchMongo")
            .WithTags("Health")
            .WithSummary("Keep the API and MongoDB active")
            .WithDescription(
                "Reads the guest credential and guest-session counter documents without modifying or returning their contents.")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(
                StatusCodes.Status429TooManyRequests,
                "application/problem+json")
            .ProducesProblem(
                StatusCodes.Status503ServiceUnavailable,
                "application/problem+json");

        return endpoints;
    }

    private static async Task<IResult> TouchAsync(
        MongoKeepWarmService keepWarmService,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        try
        {
            await keepWarmService.TouchAsync(cancellationToken);
            return Results.NoContent();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            loggerFactory
                .CreateLogger("KeepWarm")
                .LogWarning(exception, "The MongoDB keep-warm request failed.");

            return Results.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "MongoDB is temporarily unavailable.");
        }
    }
}
