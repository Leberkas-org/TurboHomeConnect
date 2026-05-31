namespace TurboHomeConnect.OAuth;

/// <summary>
/// Pluggable token persistence. Implement against secure storage (DPAPI, Keychain, etc.)
/// to survive process restarts. The in-memory default <see cref="InMemoryTokenStore"/>
/// is fine for short-lived processes and tests.
/// </summary>
public interface IPersistedTokenStore
{
    Task<PersistedToken?> LoadAsync(CancellationToken cancellationToken);
    Task SaveAsync(PersistedToken token, CancellationToken cancellationToken);
    Task ClearAsync(CancellationToken cancellationToken);
}

public sealed class InMemoryTokenStore : IPersistedTokenStore
{
    private PersistedToken? _current;
    private readonly object _gate = new();

    public Task<PersistedToken?> LoadAsync(CancellationToken cancellationToken)
    {
        lock (_gate) return Task.FromResult(_current);
    }

    public Task SaveAsync(PersistedToken token, CancellationToken cancellationToken)
    {
        lock (_gate) _current = token;
        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken cancellationToken)
    {
        lock (_gate) _current = null;
        return Task.CompletedTask;
    }
}
