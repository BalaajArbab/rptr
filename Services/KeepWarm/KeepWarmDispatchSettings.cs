using System.ComponentModel.DataAnnotations;

namespace rptr.Services.KeepWarm;

public sealed class KeepWarmDispatchSettings
{
    public const string SectionName = "KeepWarmDispatch";

    public bool Enabled { get; init; }

    [Required]
    public string Owner { get; init; } = string.Empty;

    [Required]
    public string Repository { get; init; } = string.Empty;

    [Required]
    public string WorkflowFile { get; init; } = string.Empty;

    [Required]
    public string Ref { get; init; } = "main";

    [Range(1, 60)]
    public int IntervalMinutes { get; init; } = 12;

    public string Token { get; init; } = string.Empty;
}
