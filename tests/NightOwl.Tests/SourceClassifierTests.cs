using NightOwl.Core;
using NightOwl.Credibility;
using Shouldly;

namespace NightOwl.Tests;

public class SourceClassifierTests
{
    private readonly SourceClassifier _sut = new(new SourcesConfig
    {
        Official = new() { Suffixes = [".gov"], Domains = ["who.int"] },
        Media = new() { Domains = ["bbc.com", "tass.ru"] },
        Reference = new() { Domains = ["wikipedia.org"] },
        Unofficial = new() { Domains = ["reddit.com"] }
    });

    [Theory]
    [InlineData("https://www.nasa.gov/news", SourceTier.Official)]
    [InlineData("https://www.who.int/ru", SourceTier.Official)]
    [InlineData("https://www.bbc.com/news", SourceTier.Media)]
    [InlineData("https://tass.ru/mir", SourceTier.Media)]
    [InlineData("https://ru.wikipedia.org/wiki/X", SourceTier.Reference)]
    [InlineData("https://reddit.com/r/test", SourceTier.Unofficial)]
    [InlineData("https://random-blog.example", SourceTier.Unofficial)]
    [InlineData("https://notbbc.com", SourceTier.Unofficial)]
    [InlineData("not a url", SourceTier.Unofficial)]
    public void Classify_ReturnsExpectedTier(string url, SourceTier expected)
        => _sut.Classify(url).ShouldBe(expected);
}