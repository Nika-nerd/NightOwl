using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using NightOwl.Core;
using NightOwl.Pipeline.Caching;
using NSubstitute;
using Shouldly;

namespace NightOwl.Tests;

public class CacheKeyTests
{
    [Fact]
    public void NormalizeQuestion_IgnoresCaseSpacesAndTrailingPunctuation()
        => CacheKey.NormalizeQuestion("  Сколько   Случаев кори?  ")
            .ShouldBe(CacheKey.NormalizeQuestion("сколько случаев кори"));
}

public class SqliteCacheStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"nightowl-test-{Guid.NewGuid():N}.db");

    [Fact]
    public async Task SetThenGet_ReturnsStoredValue()
    {
        var sut = new SqliteCacheStore(_path);
        var value = new SearchResult("Заголовок", "https://who.int/a", "текст", null, "test");

        await sut.SetAsync("k", value, TimeSpan.FromMinutes(5));
        var loaded = await sut.GetAsync<SearchResult>("k");

        loaded.ShouldNotBeNull();
        loaded.Title.ShouldBe("Заголовок");
    }

    [Fact]
    public async Task ExpiredEntry_ReturnsNull()
    {
        var sut = new SqliteCacheStore(_path);

        await sut.SetAsync("k", new SearchResult("t", "https://a.example", "s", null, "p"), TimeSpan.FromMilliseconds(50));
        await Task.Delay(150);

        (await sut.GetAsync<SearchResult>("k")).ShouldBeNull();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var file in Directory.GetFiles(Path.GetTempPath(), Path.GetFileName(_path) + "*"))
            File.Delete(file);
    }
}

public class CachedPipelineTests
{
    private sealed class MemoryCacheStore : ICacheStore
    {
        private readonly Dictionary<string, object> _items = new();

        public Task<T?> GetAsync<T>(string key, CancellationToken ct = default) where T : class
            => Task.FromResult(_items.TryGetValue(key, out var v) ? v as T : null);

        public Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct = default) where T : class
        {
            _items[key] = value;
            return Task.CompletedTask;
        }
    }

    private readonly INightOwlPipeline _inner = Substitute.For<INightOwlPipeline>();

    private CachedPipeline Create() => new(_inner, new MemoryCacheStore(), Options.Create(new CacheOptions()));

    private static NightOwlAnswer Answer(params string[] warnings)
    {
        var claim = new Claim("Факт", "https://who.int/a", SourceTier.Official, null);
        var verified = new VerifiedClaim("Факт", 0.9, new[] { claim }, Array.Empty<Claim>());
        return new NightOwlAnswer("q", "Ответ", 0.9, new[] { verified }, Array.Empty<SourceDocument>(),
            new QueryPlan("ru", false, new[] { "q" }), warnings);
    }

    private void InnerReturns(NightOwlAnswer answer)
        => _inner.AskAsync(Arg.Any<string>(), Arg.Any<IProgress<PipelineProgress>>(), Arg.Any<CancellationToken>())
            .Returns(answer);

    [Fact]
    public async Task SecondCall_IsServedFromCache()
    {
        InnerReturns(Answer());
        var sut = Create();

        var first = await sut.AskAsync("Сколько случаев кори?");
        var second = await sut.AskAsync("  сколько   случаев кори ");

        first.FromCache.ShouldBeFalse();
        second.FromCache.ShouldBeTrue();
        second.Summary.ShouldBe("Ответ");
        await _inner.Received(1).AskAsync(
            Arg.Any<string>(), Arg.Any<IProgress<PipelineProgress>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DegradedAnswer_IsNotCached()
    {
        InnerReturns(Answer("LLM недоступна (429)"));
        var sut = Create();

        await sut.AskAsync("вопрос");
        await sut.AskAsync("вопрос");

        await _inner.Received(2).AskAsync(
            Arg.Any<string>(), Arg.Any<IProgress<PipelineProgress>>(), Arg.Any<CancellationToken>());
    }
}