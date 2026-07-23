namespace rptr.Services.Authorization;

public sealed class AuthRedirectSettings
{
    public string? SuccessUri { get; set; }
    public string? FailureUri { get; set; }
}
