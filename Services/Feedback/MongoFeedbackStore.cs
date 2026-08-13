using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using rptr.Data.Mongo;

namespace rptr.Services.Feedback;

public sealed class MongoFeedbackStore : IFeedbackStore
{
    private const string CounterId = "feedback";
    private readonly IMongoCollection<FeedbackDocument> _feedback;
    private readonly IMongoCollection<FeedbackCounter> _counters;
    private readonly TimeProvider _timeProvider;

    public MongoFeedbackStore(MongoDbContext mongo, TimeProvider timeProvider)
    {
        _feedback = mongo.Database.GetCollection<FeedbackDocument>("feedback");
        _counters = mongo.Database.GetCollection<FeedbackCounter>("feedback_counters");
        _timeProvider = timeProvider;
    }

    public async Task<StoredFeedback> CreateAsync(
        FeedbackActor actor,
        string message,
        string? ipAddress,
        CancellationToken cancellationToken = default)
    {
        var counter = await _counters.FindOneAndUpdateAsync(
            value => value.Id == CounterId,
            Builders<FeedbackCounter>.Update.Inc(value => value.Value, 1),
            new FindOneAndUpdateOptions<FeedbackCounter>
            {
                IsUpsert = true,
                ReturnDocument = ReturnDocument.After
            },
            cancellationToken);

        if (counter.Value > int.MaxValue)
        {
            throw new InvalidOperationException("The feedback identifier range is exhausted.");
        }

        var createdAt = _timeProvider.GetUtcNow();
        var document = new FeedbackDocument
        {
            Id = (int)counter.Value,
            ActorType = actor.Type,
            NormalUserId = actor.NormalUserId,
            GuestSessionNumber = actor.GuestSessionNumber,
            Message = message,
            IpAddress = ipAddress,
            CreatedAt = createdAt.UtcDateTime
        };

        await _feedback.InsertOneAsync(document, cancellationToken: cancellationToken);
        return new StoredFeedback(document.Id, createdAt);
    }

    private sealed class FeedbackCounter
    {
        [BsonId]
        public string Id { get; set; } = string.Empty;

        public long Value { get; set; }
    }

    private sealed class FeedbackDocument
    {
        [BsonId]
        public int Id { get; set; }

        public string ActorType { get; set; } = string.Empty;
        public int? NormalUserId { get; set; }
        public long? GuestSessionNumber { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? IpAddress { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
