using Microsoft.Extensions.Diagnostics.HealthChecks;
using MongoDB.Bson;

namespace rptr.Data.Mongo;

public sealed class MongoDbHealthCheck(MongoDbContext mongoDbContext) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await mongoDbContext.Database.RunCommandAsync<BsonDocument>(
                new BsonDocument("ping", 1),
                cancellationToken: cancellationToken);

            return HealthCheckResult.Healthy();
        }
        catch (Exception exception)
        {
            return HealthCheckResult.Unhealthy(
                "MongoDB did not respond to a ping command.",
                exception);
        }
    }
}
