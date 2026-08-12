using System.ComponentModel.DataAnnotations;

namespace rptr.Services.GuestSessions;

public sealed class GuestSessionSettings
{
    public const string SectionName = "GuestSessions";

    [Range(30, 900)]
    public int IdleTimeoutSeconds { get; init; } = 300;

    [Range(5, 120)]
    public int MaximumDurationMinutes { get; init; } = 57;
}
