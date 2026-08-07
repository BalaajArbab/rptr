namespace rptr.Services;

public enum ServiceResultStatus
{
    Success,
    ValidationFailed,
    Unauthorized,
    Forbidden,
    NotFound,
    Conflict,
    UpstreamFailure
}

public sealed record ServiceResult<T>(
    ServiceResultStatus Status,
    T? Value = default,
    IReadOnlyDictionary<string, string[]>? Errors = null)
{
    public static ServiceResult<T> Success(T value) => new(ServiceResultStatus.Success, value);

    public static ServiceResult<T> Validation(
        IReadOnlyDictionary<string, string[]> errors) =>
        new(ServiceResultStatus.ValidationFailed, Errors: errors);

    public static ServiceResult<T> Failure(ServiceResultStatus status) => new(status);
}
