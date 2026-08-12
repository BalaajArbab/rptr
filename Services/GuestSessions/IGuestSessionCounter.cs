namespace rptr.Services.GuestSessions;

public interface IGuestSessionCounter
{
    Task<long> GetNextAsync(CancellationToken cancellationToken = default);
}
