namespace rptr.Services.Spotify;

public interface ISpotifyHttpClient
{
    Task<HttpResponseMessage> SendAsync(
        Func<HttpRequestMessage> requestFactory,
        CancellationToken cancellationToken = default);
}
