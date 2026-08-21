using System.Net;
using System.Net.Http.Json;
using dotnetrpg_again.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using rptr.Endpoints;
using rptr.IntegrationTests.Infrastructure;
using rptr.Services.PlaylistSongs;
using rptr.Services.Snippets;

namespace rptr.IntegrationTests;

[Collection(ApiIntegrationFixture.CollectionName)]
public sealed class ApiEndpointTests(ApiIntegrationFixture fixture)
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task ProtectedEndpoint_WithoutJwt_ReturnsUnauthorized()
    {
        using var client = fixture.CreateAnonymousClient();

        var response = await client.GetAsync(
            $"/api/playlist/{ApiIntegrationFixture.PlaylistId}/songs");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task GetPlaylistSongs_WithOwnerJwt_ReturnsSeededAnnotations()
    {
        using var client = fixture.CreateOwnerClient();

        var response = await client.GetAsync(
            $"/api/playlist/{ApiIntegrationFixture.PlaylistId}/songs");

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<PlaylistSongsResponse>();
        Assert.NotNull(result);
        Assert.True(result.CanEdit);
        Assert.True(result.CanDelete);

        var song = Assert.Single(result.Songs);
        Assert.Equal(ApiIntegrationFixture.SeedSongId, song.SongId);
        Assert.Equal("\u96e8\u306e\u30c6\u30b9\u30c8", song.SongName);
        Assert.Equal("Focus", Assert.Single(song.Tags!));
        Assert.Equal("Seed snippet", song.Snippet?.Name);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CreateSnippet_WithOwnerJwt_PersistsToSqlServer()
    {
        using var client = fixture.CreateOwnerClient();
        var request = new CreateSnippetRequest(
            "  Created through HTTP  ",
            ApiIntegrationFixture.PlaylistId,
            "created-song",
            "Created Song",
            "Created Artist",
            200_000,
            [new SnippetSegmentDto(2_000, 10_000)]);

        var response = await client.PostAsJsonAsync("/api/snippet", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<SnippetResponse>();
        Assert.NotNull(result);
        Assert.Equal("Created through HTTP", result.Name);

        using var scope = fixture.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<DataContext>();
        var persisted = await context.Snippets
            .AsNoTracking()
            .Include(value => value.PlaylistSong)
            .SingleAsync(value => value.Id == result.Id);

        Assert.Equal("created-song", persisted.PlaylistSong.SpotifySongId);
        Assert.Equal("Created through HTTP", persisted.Name);
        Assert.Equal(2_000, Assert.Single(persisted.Segments).StartMs);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CreateFeedback_WithNormalJwt_PersistsActorAndMessageToMongo()
    {
        using var client = fixture.CreateOwnerClient();

        var response = await client.PostAsJsonAsync(
            "/feedback",
            new CreateFeedbackRequest("  Integration feedback  "));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<CreateFeedbackResponse>();
        Assert.NotNull(result);

        var document = await fixture.MongoDatabase
            .GetCollection<BsonDocument>("feedback")
            .Find(Builders<BsonDocument>.Filter.Eq("_id", result.Id))
            .SingleAsync();

        Assert.Equal("normal", document["ActorType"].AsString);
        Assert.Equal(fixture.OwnerUserId, document["NormalUserId"].AsInt32);
        Assert.Equal("Integration feedback", document["Message"].AsString);
    }
}
