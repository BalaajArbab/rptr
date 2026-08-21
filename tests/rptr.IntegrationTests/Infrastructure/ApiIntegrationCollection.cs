namespace rptr.IntegrationTests.Infrastructure;

[CollectionDefinition(ApiIntegrationFixture.CollectionName, DisableParallelization = true)]
public sealed class ApiIntegrationCollection : ICollectionFixture<ApiIntegrationFixture>;
