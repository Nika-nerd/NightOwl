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

    private static SearchResult R(string url, int? daysAgo = null)
        => new("title", url, "snippet",
            daysAgo is null ? null : DateTimeOffset.UtcNow.AddDays(-daysAgo.Value), "test");

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
}