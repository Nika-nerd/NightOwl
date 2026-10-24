using NightOwl.Core;
using NightOwl.Credibility;
using Shouldly;

namespace NightOwl.Tests;

public class ConfidenceScorerTests
{
    private static readonly DateTimeOffset Now = new(2025, 6, 1, 0, 0, 0, TimeSpan.Zero);
    private readonly ConfidenceScorer _sut = new();

    private static Claim C(string url, SourceTier tier, int daysAgo)
        => new("факт", url, tier, Now.AddDays(-daysAgo));

    private static ClaimGroup G(Claim[] support, params Claim[] contra)
        => new("факт", support, contra);

    [Fact]
    public void OfficialPlusMedia_ScoresHigherThanSingleUnofficial()
    {
        var strong = G([C("https://who.int/a", SourceTier.Official, 1), C("https://bbc.com/b", SourceTier.Media, 1)]);
        var weak = G([C("https://forum.example/c", SourceTier.Unofficial, 1)]);

        _sut.Score(strong, true, Now).ShouldBeGreaterThan(_sut.Score(weak, true, Now));
        _sut.Score(strong, true, Now).ShouldBeGreaterThan(0.85);
    }

    [Fact]
    public void Contradiction_LowersScore()
    {
        var support = new[] { C("https://who.int/a", SourceTier.Official, 1) };
        var clean = G(support);
        var disputed = G(support, C("https://cdc.gov/x", SourceTier.Official, 1));

        _sut.Score(disputed, true, Now).ShouldBeLessThan(_sut.Score(clean, true, Now));
    }

    [Fact]
    public void OldSource_ScoresLowerWhenFreshnessNeeded()
    {
        var fresh = G([C("https://who.int/a", SourceTier.Official, 2)]);
        var old = G([C("https://who.int/a", SourceTier.Official, 400)]);

        _sut.Score(old, true, Now).ShouldBeLessThan(_sut.Score(fresh, true, Now));
    }

    [Fact]
    public void SameRootDomain_CountsAsOneSource()
    {
        var oneDomain = G([C("https://un.org/a", SourceTier.Official, 1), C("https://news.un.org/b", SourceTier.Official, 1)]);
        var single = G([C("https://un.org/a", SourceTier.Official, 1)]);

        _sut.Score(oneDomain, true, Now).ShouldBe(_sut.Score(single, true, Now), 0.0001);
    }

    [Fact]
    public void NoSupport_ReturnsZero()
        => _sut.Score(G([]), true, Now).ShouldBe(0);
}