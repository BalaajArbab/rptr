using System.Net;
using System.Net.Http.Json;
using dotnetrpg_again.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using rptr.IntegrationTests.Infrastructure;
using rptr.Services.Activity;

namespace rptr.IntegrationTests;

public sealed class IntegrationHostConfigurationTests
{
    private const string SqlConnectionString =
        "Server=127.0.0.1,1;Database=rptr_configuration_test;User Id=sa;" +
        "Password=Integration-tests-only-password!1;TrustServerCertificate=True";

    [Fact]
    [Trait("Category", "Integration")]
    public void DataContext_UsesConnectionStringInjectedByTestHost()
    {
        using var factory = CreateFactory();
        using var scope = factory.Services.CreateScope();
        using var context = scope.ServiceProvider.GetRequiredService<DataContext>();

        var expected = new SqlConnectionStringBuilder(SqlConnectionString);
        var actual = new SqlConnectionStringBuilder(
            context.Database.GetDbConnection().ConnectionString);

        Assert.Equal(expected.DataSource, actual.DataSource);
        Assert.Equal(expected.InitialCatalog, actual.InitialCatalog);
        Assert.Equal(expected.UserID, actual.UserID);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task DemoVideoInteraction_WithValidMilestone_ReturnsAccepted()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var request = new DemoVideoInteractionActivityEventRequest
        {
            PlaybackSessionId = Guid.NewGuid(),
            Action = "milestone",
            PositionMs = 120_000,
            DurationMs = 480_000,
            Percentage = 25
        };

        var response = await client.PostAsJsonAsync("/api/hello/video", request);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task DemoVideoInteraction_WithInvalidMilestone_ReturnsBadRequest()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        var request = new DemoVideoInteractionActivityEventRequest
        {
            PlaybackSessionId = Guid.NewGuid(),
            Action = "milestone",
            PositionMs = 120_000,
            DurationMs = 480_000,
            Percentage = 30
        };

        var response = await client.PostAsJsonAsync("/api/hello/video", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static IntegrationApiFactory CreateFactory() => new(
        SqlConnectionString,
        "mongodb://127.0.0.1:1",
        "rptr_configuration_test",
        "rptr-integration-tests-only-signing-key-which-is-long-enough-for-hmac-sha512");
}
