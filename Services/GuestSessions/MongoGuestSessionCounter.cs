using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using rptr.Data.Mongo;

namespace rptr.Services.GuestSessions;

public sealed class MongoGuestSessionCounter : IGuestSessionCounter
{
    private const string SessionCounterId = "guest-session";

    private readonly IMongoCollection<GuestSessionCounterDocument> _counters;

    public MongoGuestSessionCounter(MongoDbContext mongoDbContext)
    {
        _counters = mongoDbContext.Database.GetCollection<GuestSessionCounterDocument>(
            "guest_session_counters");
    }

    public async Task<long> GetNextAsync(CancellationToken cancellationToken = default)
    {
        var counter = await _counters.FindOneAndUpdateAsync(
            Builders<GuestSessionCounterDocument>.Filter.Eq(
                value => value.Id,
                SessionCounterId),
            Builders<GuestSessionCounterDocument>.Update.Inc(value => value.Value, 1),
            new FindOneAndUpdateOptions<GuestSessionCounterDocument>
            {
                IsUpsert = true,
                ReturnDocument = ReturnDocument.After
            },
            cancellationToken);

        return counter.Value;
    }

    private sealed class GuestSessionCounterDocument
    {
        [BsonId]
        public string Id { get; init; } = string.Empty;

        public long Value { get; set; }
    }
}
