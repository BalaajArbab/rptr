using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using rptr.Services.GuestSessions;

namespace rptr.Controllers;

[ApiController]
[Route("api/authorization/guest-session/status")]
public sealed class GuestSessionStatusController : ControllerBase
{
    private readonly IGuestLeaseStore _leaseStore;
    private readonly TimeProvider _timeProvider;

    public GuestSessionStatusController(
        IGuestLeaseStore leaseStore,
        TimeProvider timeProvider)
    {
        _leaseStore = leaseStore;
        _timeProvider = timeProvider;
    }

    [AllowAnonymous]
    [HttpGet(Name = "GetGuestSessionStatus")]
    [EndpointSummary("Check guest demo availability")]
    [EndpointDescription(
        "Reports whether the single process-local guest demo lease is currently occupied. " +
        "This response is advisory; the guest-session POST endpoint atomically determines whether acquisition succeeds.")]
    [ProducesResponseType<GuestSessionAvailabilityResponse>(StatusCodes.Status200OK, "application/json")]
    public async Task<ActionResult<GuestSessionAvailabilityResponse>> Get(
        CancellationToken cancellationToken)
    {
        var activeLease = await _leaseStore.GetCurrentAsync(cancellationToken);
        if (activeLease is null)
        {
            return Ok(new GuestSessionAvailabilityResponse(false, null, null));
        }

        var now = _timeProvider.GetUtcNow();
        var idleExpiresInSeconds = RemainingSeconds(activeLease.IdleExpiresAt, now);
        var absoluteExpiresInSeconds = RemainingSeconds(activeLease.AbsoluteExpiresAt, now);

        return Ok(new GuestSessionAvailabilityResponse(
            true,
            idleExpiresInSeconds,
            absoluteExpiresInSeconds));
    }

    private static int RemainingSeconds(
        DateTimeOffset expiresAt,
        DateTimeOffset now) =>
        Math.Max(0, (int)Math.Ceiling((expiresAt - now).TotalSeconds));
}
