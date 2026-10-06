using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace rptr.IntegrationTests.Infrastructure;

public sealed class IntegrationApiFactory(
    string sqlConnectionString,
    string mongoConnectionString,
    string mongoDatabaseName,
    string jwtSigningKey) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("IntegrationTests");
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AppSettings:Token"] = jwtSigningKey,
                ["Database:ConnectionName"] = "IntegrationConnection",
                ["ConnectionStrings:IntegrationConnection"] = sqlConnectionString,
                ["MongoDb:ConnectionString"] = mongoConnectionString,
                ["MongoDb:DatabaseName"] = mongoDatabaseName,
                ["KeepWarmDispatch:Enabled"] = "false",
                ["GuestSessions:EntryCode"] = "integration-entry-code",
                ["GuestSessions:CredentialEncryptionKey"] =
                    "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY="
            });
        });
    }
}
