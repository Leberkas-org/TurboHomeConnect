using System.Text.Json;
using System.Text.Json.Serialization;

namespace TurboHomeConnect.OAuth;

/// <summary>
/// Persists the token set to a single JSON file. Safe enough for development and container
/// scenarios where you mount a volume — but the file is plain text, so don't use it for
/// production secrets without OS-level disk encryption.
/// </summary>
public sealed class FileTokenStore : IPersistedTokenStore, IDisposable
{
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public FileTokenStore(string path)
    {
        _path = path ?? throw new ArgumentNullException(nameof(path));
    }

    public void Dispose() => _gate.Dispose();

    public async Task<PersistedToken?> LoadAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_path))
            {
                return null;
            }

            await using var stream = File.OpenRead(_path);
            return await JsonSerializer.DeserializeAsync(stream, FileTokenStoreContext.Default.PersistedToken, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(PersistedToken token, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            // Atomic-ish write: serialize to a temp file then rename.
            var tmp = _path + ".tmp";
            await using (var stream = File.Create(tmp))
            {
                await JsonSerializer.SerializeAsync(stream, token, FileTokenStoreContext.Default.PersistedToken, cancellationToken)
                    .ConfigureAwait(false);
            }
            File.Move(tmp, _path, overwrite: true);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }
        }
        finally
        {
            _gate.Release();
        }
    }
}

[JsonSerializable(typeof(PersistedToken))]
internal sealed partial class FileTokenStoreContext : JsonSerializerContext;
