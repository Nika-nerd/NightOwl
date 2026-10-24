using NightOwl.Core;

namespace NightOwl.Pipeline;

public sealed class ClaimVerifier(IClaimGrouper grouper, IConfidenceScorer scorer) : IClaimVerifier
{
    public async Task<IReadOnlyList<VerifiedClaim>> VerifyAsync(
        string question, bool needsFreshness, IReadOnlyList<Claim> claims, CancellationToken ct = default)
    {
        if (claims.Count == 0) return Array.Empty<VerifiedClaim>();

        var groups = await grouper.GroupAsync(question, claims, ct);
        var now = DateTimeOffset.UtcNow;

        return groups
            .Select(g => new VerifiedClaim(g.Statement, scorer.Score(g, needsFreshness, now), g.Supporting, g.Contradicting))
            .OrderByDescending(v => v.Confidence)
            .ToList();
    }
}