using System.ComponentModel.DataAnnotations;
using rptr.Services.Activity;

namespace rptr.UnitTests.Activity;

[Trait("Category", "Unit")]
public sealed class DemoVideoInteractionActivityEventRequestTests
{
    [Fact]
    public void Validate_WithSupportedMilestone_IsValid()
    {
        var request = new DemoVideoInteractionActivityEventRequest
        {
            PlaybackSessionId = Guid.NewGuid(),
            Action = "milestone",
            PositionMs = 120_000,
            DurationMs = 480_000,
            Percentage = 25
        };

        var results = Validate(request);

        Assert.Empty(results);
    }

    [Fact]
    public void Validate_SeekWithoutPositions_IsInvalid()
    {
        var request = new DemoVideoInteractionActivityEventRequest
        {
            PlaybackSessionId = Guid.NewGuid(),
            Action = "seeked",
            PositionMs = 120_000,
            DurationMs = 480_000
        };

        var results = Validate(request);

        Assert.Contains(results, result =>
            result.MemberNames.Contains(nameof(request.FromPositionMs)));
        Assert.Contains(results, result =>
            result.MemberNames.Contains(nameof(request.ToPositionMs)));
    }

    [Fact]
    public void Validate_FailureWithoutErrorDetails_IsInvalid()
    {
        var request = new DemoVideoInteractionActivityEventRequest
        {
            PlaybackSessionId = Guid.NewGuid(),
            Action = "failed"
        };

        var results = Validate(request);

        Assert.Contains(results, result =>
            result.MemberNames.Contains(nameof(request.ErrorCode)));
    }

    private static IReadOnlyList<ValidationResult> Validate(object value)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(
            value,
            new ValidationContext(value),
            results,
            validateAllProperties: true);
        return results;
    }
}
