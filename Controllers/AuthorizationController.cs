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
        [HttpGet("login")]
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
        [HttpGet("callback")]
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
        [HttpPost("session")]
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
        [HttpPost("spotify-token")]
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
