using System.ComponentModel.DataAnnotations;

namespace rptr.Services.GuestSessions;

public sealed class GuestSessionSettings
{
    public const string SectionName = "GuestSessions";

    [Range(30, 900)]
    public int IdleTimeoutSeconds { get; init; } = 300;

    [Range(5, 120)]
    public int MaximumDurationMinutes { get; init; } = 57;

    [Range(1, 1440)]
    public int FeedbackTokenGraceMinutes { get; init; } = 15;

    public string BaselinePath { get; init; } = "GuestData/guest-baseline.db";

    public string EntryCode { get; init; } = string.Empty;

    public string CredentialEncryptionKey { get; init; } = string.Empty;
}
