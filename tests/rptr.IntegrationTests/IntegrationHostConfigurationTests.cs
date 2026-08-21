using dotnetrpg_again.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using rptr.IntegrationTests.Infrastructure;

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
        using var factory = new IntegrationApiFactory(
            SqlConnectionString,
            "mongodb://127.0.0.1:1",
            "rptr_configuration_test",
            "rptr-integration-tests-only-signing-key-which-is-long-enough-for-hmac-sha512");
        using var scope = factory.Services.CreateScope();
        using var context = scope.ServiceProvider.GetRequiredService<DataContext>();

        var expected = new SqlConnectionStringBuilder(SqlConnectionString);
        var actual = new SqlConnectionStringBuilder(
            context.Database.GetDbConnection().ConnectionString);

        Assert.Equal(expected.DataSource, actual.DataSource);
        Assert.Equal(expected.InitialCatalog, actual.InitialCatalog);
        Assert.Equal(expected.UserID, actual.UserID);
    }
}
