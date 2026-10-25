using NightOwl.Core;

namespace NightOwl.Pipeline.Caching;

// Используется, когда кэш выключен (NightOwl:Cache:Enabled = false)
public sealed class NullCacheStore : ICacheStore
{
    public Task<T?> GetAsync<T>(string key, CancellationToken ct = default) where T : class
        => Task.FromResult<T?>(null);

    public Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default) where T : class
        => Task.CompletedTask;
}