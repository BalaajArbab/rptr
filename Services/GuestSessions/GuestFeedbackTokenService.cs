using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using rptr.Services.Authorization;

namespace rptr.Services.GuestSessions;

public sealed record GuestFeedbackToken(
    string Token,
    DateTimeOffset ExpiresAt);

public interface IGuestFeedbackTokenService
{
    GuestFeedbackToken Create(GuestLease lease);
}

public sealed class GuestFeedbackTokenService : IGuestFeedbackTokenService
{
    public const string AuthenticationScheme = "GuestFeedbackBearer";
    public const string SessionType = "guest_feedback";
    public const string Issuer = "rptr-api";
    public const string Audience = "rptr-guest-feedback";

    private const string KeyPurpose = "rptr:guest-feedback:v1";
    private readonly GuestSessionSettings _settings;
    private readonly SigningCredentials _signingCredentials;

    public GuestFeedbackTokenService(
        IOptions<GuestSessionSettings> settings,
        IConfiguration configuration)
    {
        _settings = settings.Value;
        _signingCredentials = new SigningCredentials(
            CreateSigningKey(configuration),
            SecurityAlgorithms.HmacSha256Signature);
    }

    public GuestFeedbackToken Create(GuestLease lease)
    {
        var expiresAt = lease.AbsoluteExpiresAt.AddMinutes(
            _settings.FeedbackTokenGraceMinutes);
        var claims = new List<Claim>
        {
            new(
                ClaimsPrincipalExtensions.SessionTypeClaim,
                SessionType),
            new(
                ClaimsPrincipalExtensions.GuestSessionNumberClaim,
                lease.SessionNumber.ToString(CultureInfo.InvariantCulture)),
            new(
                JwtRegisteredClaimNames.Jti,
                Guid.NewGuid().ToString("N"))
        };
        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: claims,
            notBefore: lease.IssuedAt.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: _signingCredentials);

        return new GuestFeedbackToken(
            new JwtSecurityTokenHandler().WriteToken(token),
            expiresAt);
    }

    public static TokenValidationParameters CreateValidationParameters(
        IConfiguration configuration) => new()
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = CreateSigningKey(configuration),
            ValidateIssuer = true,
            ValidIssuer = Issuer,
            ValidateAudience = true,
            ValidAudience = Audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };

    private static SymmetricSecurityKey CreateSigningKey(
        IConfiguration configuration)
    {
        var backendSigningToken = configuration["AppSettings:Token"];
        if (string.IsNullOrWhiteSpace(backendSigningToken))
        {
            throw new InvalidOperationException(
                "AppSettings:Token is not configured.");
        }

        var derivedKey = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(backendSigningToken),
            Encoding.UTF8.GetBytes(KeyPurpose));
        return new SymmetricSecurityKey(derivedKey);
    }
}
