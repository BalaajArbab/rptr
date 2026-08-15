using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using rptr.Services.Authorization;
using rptr.Services.GuestSessions;

namespace rptr.UnitTests.GuestSessions;

[Trait("Category", "Unit")]
public sealed class GuestFeedbackTokenServiceTests
{
    private const string BackendSigningToken =
        "unit-test-signing-token-that-is-deliberately-long-and-never-secret";
    private static readonly DateTimeOffset IssuedAt =
        DateTimeOffset.UtcNow.AddMinutes(-1);

    [Fact]
    public void Create_IncludesFeedbackIdentityAndSessionNumber()
    {
        var service = CreateService(feedbackGraceMinutes: 15);
        var lease = CreateLease(sessionNumber: 73);

        var result = service.Create(lease);
        var token = new JwtSecurityTokenHandler().ReadJwtToken(result.Token);

        Assert.Equal(GuestFeedbackTokenService.Issuer, token.Issuer);
        Assert.Contains(GuestFeedbackTokenService.Audience, token.Audiences);
        Assert.Equal(
            GuestFeedbackTokenService.SessionType,
            token.Claims.Single(
                claim => claim.Type == ClaimsPrincipalExtensions.SessionTypeClaim).Value);
        Assert.Equal(
            "73",
            token.Claims.Single(
                claim => claim.Type == ClaimsPrincipalExtensions.GuestSessionNumberClaim).Value);
    }

    [Fact]
    public void Create_ExpiresAfterLeaseAndConfiguredGracePeriod()
    {
        var service = CreateService(feedbackGraceMinutes: 20);
        var lease = CreateLease(sessionNumber: 1);

        var result = service.Create(lease);

        Assert.Equal(lease.AbsoluteExpiresAt.AddMinutes(20), result.ExpiresAt);
        var token = new JwtSecurityTokenHandler().ReadJwtToken(result.Token);
        Assert.Equal(result.ExpiresAt.ToUnixTimeSeconds(), new DateTimeOffset(token.ValidTo).ToUnixTimeSeconds());
    }

    [Fact]
    public void CreateValidationParameters_AcceptsFeedbackToken()
    {
        var configuration = CreateConfiguration();
        var service = CreateService(configuration: configuration);
        var result = service.Create(CreateLease(sessionNumber: 5));

        var principal = new JwtSecurityTokenHandler().ValidateToken(
            result.Token,
            GuestFeedbackTokenService.CreateValidationParameters(configuration),
            out _);

        Assert.True(principal.Identity?.IsAuthenticated);
        Assert.Equal(
            "5",
            principal.FindFirstValue(
                ClaimsPrincipalExtensions.GuestSessionNumberClaim));
    }

    [Fact]
    public void FeedbackToken_CannotBeValidatedWithBackendSigningKey()
    {
        var service = CreateService();
        var result = service.Create(CreateLease(sessionNumber: 9));
        var backendValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(BackendSigningToken)),
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = false
        };

        Assert.ThrowsAny<SecurityTokenException>(() =>
            new JwtSecurityTokenHandler().ValidateToken(
                result.Token,
                backendValidationParameters,
                out _));
    }

    private static GuestFeedbackTokenService CreateService(
        int feedbackGraceMinutes = 15,
        IConfiguration? configuration = null) =>
        new(
            Options.Create(new GuestSessionSettings
            {
                FeedbackTokenGraceMinutes = feedbackGraceMinutes
            }),
            configuration ?? CreateConfiguration());

    private static IConfiguration CreateConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AppSettings:Token"] = BackendSigningToken
            })
            .Build();

    private static GuestLease CreateLease(long sessionNumber)
    {
        var absoluteExpiresAt = IssuedAt.AddMinutes(57);
        return new GuestLease(
            sessionNumber,
            Guid.NewGuid(),
            IssuedAt,
            IssuedAt,
            IssuedAt.AddMinutes(5),
            absoluteExpiresAt);
    }
}
