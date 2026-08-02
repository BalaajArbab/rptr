using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using rptr.Services.Authorization;

namespace rptr.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthorizationController : ControllerBase
    {
        private readonly IAuthService _authService;

        public AuthorizationController(IAuthService authService)
        {
            _authService = authService;
        }

        [AllowAnonymous]
        [HttpGet("login", Name = "StartSpotifyLogin")]
        [EndpointSummary("Start Spotify authorization")]
        [EndpointDescription(
            "Browser navigation endpoint that sets a short-lived state-validation cookie and redirects to Spotify. " +
            "Do not call this endpoint through a generated API client.")]
        [ProducesResponseType(StatusCodes.Status302Found)]
        public IActionResult Login()
        {
            var login = _authService.CreateLoginRedirect();

            Response.Cookies.Append(
                login.StateCookieName,
                login.StateCookieValue,
                new CookieOptions
                {
                    HttpOnly = true,
                    Secure = true,
                    SameSite = SameSiteMode.Lax,
                    Expires = login.ExpiresAt,
                    Path = "/"
                });

            return Redirect(login.AuthorizationUrl);
        }

        [AllowAnonymous]
        [HttpGet("callback", Name = "HandleSpotifyCallback")]
        [EndpointSummary("Complete Spotify authorization")]
        [EndpointDescription(
            "Spotify OAuth redirect target. Validates the callback and state cookie, updates the local user and Spotify tokens, " +
            "creates a handoff code that expires after two minutes, and redirects to the configured frontend result page. " +
            "This endpoint is invoked by Spotify rather than through a generated API client.")]
        [ProducesResponseType(StatusCodes.Status302Found)]
        public async Task<IActionResult> Callback(
            [FromQuery] string? code,
            [FromQuery] string? state,
            [FromQuery] string? error,
            CancellationToken cancellationToken)
        {
            var result = await _authService.HandleCallbackAsync(
                new SpotifyCallbackRequest(
                    code,
                    state,
                    error,
                    Request.Cookies[_authService.StateCookieName]),
                cancellationToken);

            Response.Cookies.Delete(
                _authService.StateCookieName,
                new CookieOptions
                {
                    Secure = true,
                    SameSite = SameSiteMode.Lax,
                    Path = "/"
                });

            return Redirect(result.RedirectUri);
        }

        [AllowAnonymous]
        [HttpPost("session", Name = "CreateAuthSession")]
        [EndpointSummary("Exchange a handoff code for an authenticated session")]
        [EndpointDescription(
            "Consumes a short-lived, single-use handoff code issued after successful Spotify authorization and returns a backend JWT, " +
            "a Spotify access token, and the user profile. A valid code is consumed as soon as the exchange begins, so this request " +
            "must not be retried automatically. This endpoint does not create or refresh a persistent session cookie.")]
        [ProducesResponseType<AuthSessionResponse>(StatusCodes.Status200OK)]
        [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(void), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<AuthSessionResponse>> Session(
            [FromBody] AuthSessionRequest request,
            CancellationToken cancellationToken)
        {
            var session = await _authService.CreateSessionAsync(request.Code, cancellationToken);
            if (session is null)
            {
                return Unauthorized();
            }

            return Ok(session);
        }

        [Authorize]
        [HttpPost("spotify-token", Name = "GetSpotifyAccessToken")]
        [EndpointSummary("Get a current Spotify access token")]
        [EndpointDescription(
            "Returns the authenticated user's Spotify access token, refreshing it server-side when it is expired or close to expiry. " +
            "The Spotify refresh token is never returned.")]
        [ProducesResponseType<SpotifyAccessTokenResponse>(StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(void), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<SpotifyAccessTokenResponse>> SpotifyToken(CancellationToken cancellationToken)
        {
            var token = await _authService.GetSpotifyAccessTokenAsync(User, cancellationToken);
            if (token is null)
            {
                return Unauthorized();
            }

            return Ok(token);
        }
    }
}
