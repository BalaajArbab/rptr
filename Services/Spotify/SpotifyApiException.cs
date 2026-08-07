using System.Net;

namespace rptr.Services.Spotify;

public sealed class SpotifyApiException : Exception
{
    public SpotifyApiException(HttpStatusCode statusCode, string message)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public HttpStatusCode StatusCode { get; }
}
