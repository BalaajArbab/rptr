using rptr.Services.Authorization;
using rptr.Services.GuestSessions;

namespace rptr.Services.Snippets;

public sealed class SnippetServiceRouter : ISnippetService
{
    private readonly SnippetService _normalService;
    private readonly IGuestRuntimeStore _runtimeStore;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<SnippetService> _serviceLogger;

    public SnippetServiceRouter(
        SnippetService normalService,
        IGuestRuntimeStore runtimeStore,
        IHttpContextAccessor httpContextAccessor,
        ILogger<SnippetService> serviceLogger)
    {
        _normalService = normalService;
        _runtimeStore = runtimeStore;
        _httpContextAccessor = httpContextAccessor;
        _serviceLogger = serviceLogger;
    }

    public Task<ServiceResult<SnippetResponse>> CreateAsync(
        int userId,
        CreateSnippetRequest request,
        CancellationToken cancellationToken = default) =>
        RouteAsync(
            service => service.CreateAsync(userId, request, cancellationToken),
            () => _normalService.CreateAsync(userId, request, cancellationToken),
            cancellationToken);

    public Task<ServiceResult<SnippetResponse>> UpdateAsync(
        int userId,
        int snippetId,
        UpdateSnippetRequest request,
        CancellationToken cancellationToken = default) =>
        RouteAsync(
            service => service.UpdateAsync(userId, snippetId, request, cancellationToken),
            () => _normalService.UpdateAsync(userId, snippetId, request, cancellationToken),
            cancellationToken);

    public Task<ServiceResult<bool>> DeleteAsync(
        int userId,
        int snippetId,
        CancellationToken cancellationToken = default) =>
        RouteAsync(
            service => service.DeleteAsync(userId, snippetId, cancellationToken),
            () => _normalService.DeleteAsync(userId, snippetId, cancellationToken),
            cancellationToken);

    private Task<T> RouteAsync<T>(
        Func<SnippetService, Task<T>> guestOperation,
        Func<Task<T>> normalOperation,
        CancellationToken cancellationToken)
    {
        if (_httpContextAccessor.HttpContext?.User.TryGetGuestSessionKey(out var sessionKey) != true)
        {
            return normalOperation();
        }

        return _runtimeStore.ExecuteAsync(
            sessionKey,
            context => guestOperation(new SnippetService(context, _serviceLogger)),
            cancellationToken);
    }
}
