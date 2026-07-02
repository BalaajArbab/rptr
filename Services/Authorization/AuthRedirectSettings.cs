namespace rptr.Services.Authorization;

public sealed class AuthRedirectSettings
{
    public string SuccessUri { get; set; } = "https://localhost:7088/scalar/v1";
    public string FailureUri { get; set; } = "https://localhost:7088/scalar/v1";
}
