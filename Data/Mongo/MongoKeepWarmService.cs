using MongoDB.Bson;
using MongoDB.Driver;

namespace rptr.Data.Mongo;

public sealed class MongoKeepWarmService
{
    private readonly IMongoCollection<BsonDocument> _guestCredentials;
    private readonly IMongoCollection<BsonDocument> _guestSessionCounters;

    public MongoKeepWarmService(MongoDbContext mongoDbContext)
    {
        _guestCredentials = mongoDbContext.Database
            .GetCollection<BsonDocument>("guest_credentials");
        _guestSessionCounters = mongoDbContext.Database
            .GetCollection<BsonDocument>("guest_session_counters");
    }

    public Task TouchAsync(CancellationToken cancellationToken = default) =>
        Task.WhenAll(
            ReadDocumentAsync(
                _guestCredentials,
                "spotify",
                cancellationToken),
            ReadDocumentAsync(
                _guestSessionCounters,
                "guest-session",
                cancellationToken));

    private static async Task ReadDocumentAsync(
        IMongoCollection<BsonDocument> collection,
        string documentId,
        CancellationToken cancellationToken)
    {
        _ = await collection
            .Find(Builders<BsonDocument>.Filter.Eq("_id", documentId))
            .Limit(1)
            .FirstOrDefaultAsync(cancellationToken);
    }
}
