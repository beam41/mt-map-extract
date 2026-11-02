using System.Net;
using Newtonsoft.Json.Linq;

namespace HouseServer;

/// <summary>
/// The AMC server's <c>/api/world/</c> dump (~7 MB, every building on the island) behind a
/// memory + disk cache. One upstream request per <see cref="_ttl"/> at most, shared by every
/// concurrent caller; a failed refresh keeps serving the stale copy.
/// </summary>
public sealed class WorldCache(HttpClient http, string url, string cacheFile, TimeSpan ttl, ILogger<WorldCache> log)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TimeSpan _ttl = ttl;
    private Snapshot? _snapshot;

    public sealed record Snapshot(JObject World, DateTimeOffset FetchedAt);

    public async Task<Snapshot> GetAsync(CancellationToken ct)
    {
        if (Fresh(_snapshot) is { } hit) return hit;

        await _gate.WaitAsync(ct);
        try
        {
            if (Fresh(_snapshot) is { } raced) return raced;

            _snapshot ??= await LoadDiskAsync(ct);
            if (Fresh(_snapshot) is { } disk) return disk;

            try
            {
                _snapshot = await FetchAsync(ct);
            }
            catch (Exception e) when (_snapshot is not null && e is HttpRequestException or TaskCanceledException)
            {
                log.LogWarning(e, "world refresh failed, serving copy from {FetchedAt}", _snapshot.FetchedAt);
            }
            return _snapshot!;
        }
        finally
        {
            _gate.Release();
        }
    }

    private Snapshot? Fresh(Snapshot? s) => s is not null && DateTimeOffset.UtcNow - s.FetchedAt < _ttl ? s : null;

    private async Task<Snapshot?> LoadDiskAsync(CancellationToken ct)
    {
        if (!File.Exists(cacheFile)) return null;
        var text = await File.ReadAllTextAsync(cacheFile, ct);
        return new Snapshot(JObject.Parse(text), File.GetLastWriteTimeUtc(cacheFile));
    }

    private async Task<Snapshot> FetchAsync(CancellationToken ct)
    {
        log.LogInformation("fetching {Url}", url);
        var text = await http.GetStringAsync(url, ct);
        var world = JObject.Parse(text);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(cacheFile))!);
        await File.WriteAllTextAsync(cacheFile, text, ct);
        return new Snapshot(world, DateTimeOffset.UtcNow);
    }

    public static HttpClient CreateHttpClient() => new(new HttpClientHandler
    {
        AutomaticDecompression = DecompressionMethods.All,
    })
    {
        Timeout = TimeSpan.FromMinutes(2),
    };
}
