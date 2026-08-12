using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace rptr.Data.Mongo;

public sealed class MongoDbContext
{
    public MongoDbContext(IOptions<MongoDbSettings> settings)
    {
        var value = settings.Value;

        if (string.IsNullOrWhiteSpace(value.ConnectionString))
        {
            throw new InvalidOperationException(
                $"{MongoDbSettings.SectionName}:ConnectionString is not configured.");
        }

        if (string.IsNullOrWhiteSpace(value.DatabaseName))
        {
            throw new InvalidOperationException(
                $"{MongoDbSettings.SectionName}:DatabaseName is not configured.");
        }

        Client = new MongoClient(value.ConnectionString);
        Database = Client.GetDatabase(value.DatabaseName);
    }

    public IMongoClient Client { get; }
    public IMongoDatabase Database { get; }
}
