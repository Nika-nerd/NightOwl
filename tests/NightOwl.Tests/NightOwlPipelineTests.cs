using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NightOwl.Core;
using NightOwl.Credibility;
using NightOwl.Pipeline;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;

namespace NightOwl.Tests;

public class NightOwlPipelineTests
{
    private static readonly QueryPlan Plan = new("ru", false, new[] { "корь Европа 2025" });

    private readonly SourceClassifier _classifier =
        new(new SourcesConfig { Official = new() { Domains = ["who.int"] } });

    private readonly IQueryPlanner _planner = Substitute.For<IQueryPlanner>();
    private readonly ISearchProvider _search = Substitute.For<ISearchProvider>();
    private readonly IRelevanceFilter _filter = Substitute.For<IRelevanceFilter>();
    private readonly IContentExtractor _extractor = Substitute.For<IContentExtractor>();
    private readonly IClaimExtractor _claimExtractor = Substitute.For<IClaimExtractor>();
    private readonly IClaimVerifier _verifier = Substitute.For<IClaimVerifier>();
    private readonly IAnswerSynthesizer _synthesizer = Substitute.For<IAnswerSynthesizer>();

    public NightOwlPipelineTests()
    {
        _planner.PlanAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Plan);
    }

    private NightOwlPipeline Create() => new(
        _planner, _search, _classifier, _filter, _extractor, _claimExtractor, _verifier,
        new ConfidenceScorer(), _synthesizer,
        Options.Create(new PipelineOptions()),
        NullLogger<NightOwlPipeline>.Instance);

    [Fact]
    public async Task NothingFound_ReturnsEmptyAnswerWithZeroConfidence()
    {
        IReadOnlyList<SearchResult> empty = Array.Empty<SearchResult>();
        _search.SearchAsync(Arg.Any<SearchRequest>(), Arg.Any<CancellationToken>()).Returns(empty);

        var answer = await Create().AskAsync("вопрос");

        answer.Claims.ShouldBeEmpty();
        answer.OverallConfidence.ShouldBe(0);
        answer.Summary.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task VerifierFails_FallsBackToIndividualScoringWithWarning()
    {
        var result = new SearchResult("title", "https://www.who.int/a", "snippet", null, "test");
        IReadOnlyList<SearchResult> results = new[] { result };
        _search.SearchAsync(Arg.Any<SearchRequest>(), Arg.Any<CancellationToken>()).Returns(results);

        var doc = new SourceDocument(result, "who.int", SourceTier.Official, "text", DateTimeOffset.UtcNow);
        _extractor.ExtractAsync(Arg.Any<SearchResult>(), Arg.Any<CancellationToken>()).Returns(doc);

        IReadOnlyList<Claim> claims = new[] { new Claim("Факт", result.Url, SourceTier.Official, doc.PublishedAt) };
        _claimExtractor
            .ExtractManyAsync(Arg.Any<IReadOnlyList<SourceDocument>>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(claims);

        _verifier
            .VerifyAsync(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<IReadOnlyList<Claim>>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("429"));

        _synthesizer
            .SynthesizeAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<VerifiedClaim>>(),
                Arg.Any<IReadOnlyList<SourceDocument>>(), Arg.Any<CancellationToken>())
            .Returns("Ответ [1]");

        var answer = await Create().AskAsync("вопрос");

        answer.Claims.Count.ShouldBe(1);
        answer.OverallConfidence.ShouldBeGreaterThan(0);
        answer.Summary.ShouldBe("Ответ [1]");
        answer.Warnings.ShouldContain(w => w.Contains("кросс-проверки"));
    }
}