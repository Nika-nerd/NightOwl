namespace NightOwl.Core;

public enum SourceTier
{
    Official = 1,
    Media = 2,
    Reference = 3,
    Unofficial = 4
}

public record SearchResult(string Title, string Url, string Snippet, DateTimeOffset? PublishedAt, string Provider);

public record SourceDocument(
    SearchResult Origin,
    string Domain,
    SourceTier Tier,
    string Text,
    DateTimeOffset? PublishedAt);

public record Claim(string Text, string SourceUrl, SourceTier Tier, DateTimeOffset? PublishedAt);

public record VerifiedClaim(
    string Text,
    double Confidence,                 // 0..1
    IReadOnlyList<Claim> Supporting,
    IReadOnlyList<Claim> Contradicting);

public record NightOwlAnswer(
    string Summary,
    double OverallConfidence,
    IReadOnlyList<VerifiedClaim> Claims,
    IReadOnlyList<SourceDocument> Sources);

    public record SearchRequest(
    string Query,
    IReadOnlyList<string>? IncludeDomains = null,
    bool NewsOnly = false,
    int MaxResults = 10);

    public record QueryPlan(
    string QuestionLanguage,
    bool NeedsFreshness,
    IReadOnlyList<string> Queries);

    public record ClaimGroup(
    string Statement,
    IReadOnlyList<Claim> Supporting,
    IReadOnlyList<Claim> Contradicting);