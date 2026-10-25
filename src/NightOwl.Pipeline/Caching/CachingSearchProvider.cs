using System.Text.Json;
using Microsoft.Extensions.Options;
using NightOwl.Core;

namespace NightOwl.Pipeline.Caching;

public sealed class CachingSearchProvider(
    ISearchProvider inner, ICacheStore cache, IOptions<CacheOptions> options) : ISearchProvider
{
    public string Name => inner.Name;

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(SearchRequest request, CancellationToken ct = default)
    {
        var key = CacheKey.For("search", $"{inner.Name}|{JsonSerializer.Serialize(request)}");

        var cached = await cache.GetAsync<List<SearchResult>>(key, ct);
        if (cached is not null) return cached;

        var results = await inner.SearchAsync(request, ct);

        if (results.Count > 0) // пустой результат может быть временным, не кэшируем
        {
            var o = options.Value;
            await cache.SetAsync(key, results.ToList(), request.NewsOnly ? o.NewsSearchTtl : o.SearchTtl, ct);
        }

        return results;
    }
}