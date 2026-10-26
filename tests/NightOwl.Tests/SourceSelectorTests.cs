using NightOwl.Core;
using NightOwl.Credibility;
using Shouldly;

namespace NightOwl.Tests;

public class SourceSelectorTests
{
    private readonly SourceClassifier _classifier = new(new SourcesConfig
    {
        Official = new() { Domains = ["who.int", "un.org", "unicef.org"] },
        Media = new() { Domains = ["bbc.com", "reuters.com", "tass.ru"] }
    });

    private static SearchResult R(string url, int? daysAgo = null, double? relevance = null)
        => new("title", url, "snippet",
            daysAgo is null ? null : DateTimeOffset.UtcNow.AddDays(-daysAgo.Value), "test", relevance);

    [Fact]
    public void Select_MixesOfficialAndMedia()
    {
        var items = new[]
        {
            R("https://who.int/a"), R("https://un.org/b"), R("https://unicef.org/c"),
            R("https://bbc.com/d", 1), R("https://reuters.com/e", 2), R("https://blog.example/f")
        };

        var picked = SourceSelector.Select(items, _classifier, 4);

        picked.Count.ShouldBe(4);
        picked.Count(r => _classifier.Classify(r.Url) == SourceTier.Official).ShouldBe(2);
        picked.Count(r => _classifier.Classify(r.Url) == SourceTier.Media).ShouldBe(2);
    }

    [Fact]
    public void Select_TakesOneSourcePerRootDomain()
    {
        var items = new[] { R("https://who.int/a"), R("https://www.who.int/b"), R("https://bbc.com/c") };

        SourceSelector.Select(items, _classifier, 4).Count.ShouldBe(2);
    }

    [Fact]
    public void Select_FillsFromOtherTiersWhenSomeAreMissing()
    {
        var items = new[] { R("https://a.example/1"), R("https://b.example/2"), R("https://c.example/3") };

        SourceSelector.Select(items, _classifier, 2).Count.ShouldBe(2);
    }

    [Fact]
    public void Select_PrefersRelevantSourcesOverIrrelevantOfficialOnes()
    {
        var items = new[]
        {
            R("https://who.int/a", relevance: 0.05),      // официальный, но не по теме
            R("https://un.org/b", relevance: 0.04),
            R("https://bbc.com/c", relevance: 0.90),
            R("https://reuters.com/d", relevance: 0.80),
            R("https://ranking.example/e", relevance: 0.85)
        };

        var urls = SourceSelector.Select(items, _classifier, 3).Select(r => r.Url).ToList();

        urls.ShouldNotContain("https://who.int/a");
        urls.ShouldNotContain("https://un.org/b");
        urls.Count.ShouldBe(3);
    }

    [Fact]
    public void Select_BackfillsWithBestRemainingWhenTooFewAreRelevant()
    {
        var items = new[]
        {
            R("https://a.example/1", relevance: 0.90),
            R("https://b.example/2", relevance: 0.05),
            R("https://c.example/3", relevance: 0.04)
        };

        SourceSelector.Select(items, _classifier, 3).Count.ShouldBe(3);
    }
}