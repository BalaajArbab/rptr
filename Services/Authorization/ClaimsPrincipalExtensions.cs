using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace rptr.Services.Authorization;

public static class ClaimsPrincipalExtensions
{
    public const string SessionTypeClaim = "session_type";
    public const string GuestSessionType = "guest";
    public const string GuestSessionKeyClaim = "guest_session_key";
    public const string GuestSessionNumberClaim = "guest_session_number";

    public static bool TryGetUserId(this ClaimsPrincipal principal, out int userId)
    {
        var value = principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal.FindFirstValue(JwtRegisteredClaimNames.Sub);

        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out userId)
            && userId > 0;
    }

    public static bool IsGuest(this ClaimsPrincipal principal) =>
        string.Equals(
            principal.FindFirstValue(SessionTypeClaim),
            GuestSessionType,
            StringComparison.Ordinal);

    public static bool TryGetGuestSessionKey(
        this ClaimsPrincipal principal,
        out Guid sessionKey)
    {
        sessionKey = Guid.Empty;
        return principal.IsGuest()
            && Guid.TryParse(
                principal.FindFirstValue(GuestSessionKeyClaim),
                out sessionKey);
    }

    public static bool TryGetGuestSessionNumber(
        this ClaimsPrincipal principal,
        out long sessionNumber)
    {
        sessionNumber = 0;
        return principal.IsGuest()
            && long.TryParse(
                principal.FindFirstValue(GuestSessionNumberClaim),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out sessionNumber)
            && sessionNumber > 0;
    }
}
