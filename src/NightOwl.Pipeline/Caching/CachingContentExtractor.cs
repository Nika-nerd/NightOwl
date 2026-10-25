using Microsoft.Extensions.Options;
using NightOwl.Core;

namespace NightOwl.Pipeline.Caching;

public sealed class CachingContentExtractor(
    IContentExtractor inner, ICacheStore cache, IOptions<CacheOptions> options) : IContentExtractor
{
    public async Task<SourceDocument?> ExtractAsync(SearchResult result, CancellationToken ct = default)
    {
        var key = CacheKey.For("page", result.Url);

        var cached = await cache.GetAsync<SourceDocument>(key, ct);
        if (cached is not null) return cached with { Origin = result }; // свежие title/snippet из текущего поиска

        var document = await inner.ExtractAsync(result, ct);
        if (document is not null)
            await cache.SetAsync(key, document, options.Value.PageTtl, ct);

        return document;
    }
}