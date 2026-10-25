namespace NightOwl.Core;

public interface ISearchProvider
{
    string Name { get; }
    Task<IReadOnlyList<SearchResult>> SearchAsync(SearchRequest request, CancellationToken ct = default);
}

public interface IContentExtractor
{
    Task<SourceDocument?> ExtractAsync(SearchResult result, CancellationToken ct = default);
}

public interface ISourceClassifier
{
    SourceTier Classify(string url);
}

public interface IClaimExtractor
{
    Task<IReadOnlyList<Claim>> ExtractAsync(SourceDocument doc, string question, CancellationToken ct = default);
    Task<IReadOnlyList<Claim>> ExtractManyAsync(
        IReadOnlyList<SourceDocument> docs, string question, CancellationToken ct = default);
}

public interface IClaimVerifier
{
    Task<IReadOnlyList<VerifiedClaim>> VerifyAsync(
        string question, bool needsFreshness, IReadOnlyList<Claim> claims, CancellationToken ct = default);
}

public interface IClaimGrouper
{
    Task<IReadOnlyList<ClaimGroup>> GroupAsync(
        string question, IReadOnlyList<Claim> claims, CancellationToken ct = default);
}

public interface IConfidenceScorer
{
    double Score(ClaimGroup group, bool needsFreshness, DateTimeOffset now);
}

public interface IAnswerSynthesizer
{
    Task<string> SynthesizeAsync(
        string question,
        IReadOnlyList<VerifiedClaim> claims,
        IReadOnlyList<SourceDocument> sources,
        CancellationToken ct = default);
}

public interface INightOwlPipeline
{
    Task<NightOwlAnswer> AskAsync(string question, CancellationToken ct = default);
}



public interface IQueryPlanner
{
    Task<QueryPlan> PlanAsync(string question, CancellationToken ct = default);
}

public interface IRelevanceFilter
{
    Task<IReadOnlyList<SearchResult>> FilterAsync(
        string question, IReadOnlyList<SearchResult> candidates, CancellationToken ct = default);
}