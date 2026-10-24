using NightOwl.Core;

namespace NightOwl.Credibility;

public sealed class ConfidenceScorer : IConfidenceScorer
{
    private static readonly Dictionary<SourceTier, double> TierWeights = new()
    {
        [SourceTier.Official] = 1.0,
        [SourceTier.Media] = 0.75,
        [SourceTier.Reference] = 0.55,
        [SourceTier.Unofficial] = 0.25
    };

    public double Score(ClaimGroup group, bool needsFreshness, DateTimeOffset now)
    {
        if (group.Supporting.Count == 0) return 0;

        var support = WeightByDomain(group.Supporting);
        var contra = WeightByDomain(group.Contradicting);

        var supportSum = support.Values.Sum();
        var contraSum = contra.Values.Sum();

        var baseWeight = support.Values.Max();
        var agreement = Math.Min(1.0, supportSum / 2.0);
        var freshness = Freshness(group.Supporting, needsFreshness, now);
        var diversity = Diversity(group.Supporting);
        var penalty = contraSum == 0 ? 0 : 0.5 * contraSum / (supportSum + contraSum);

        var score = baseWeight * 0.35 + agreement * 0.35 + freshness * 0.20 + diversity * 0.10 - penalty;
        return Math.Clamp(score, 0, 1);
    }

    // Один домен считается один раз, берём его лучший вес
    private static Dictionary<string, double> WeightByDomain(IEnumerable<Claim> claims)
        => claims
            .GroupBy(c => DomainHelper.GetRootDomain(c.SourceUrl))
            .ToDictionary(g => g.Key, g => g.Max(c => TierWeights[c.Tier]));

    private static double Freshness(IEnumerable<Claim> claims, bool needsFreshness, DateTimeOffset now)
    {
        var newest = claims.Max(c => c.PublishedAt);
        if (newest is null) return 0.5;

        var ageDays = Math.Max(0, (now - newest.Value).TotalDays);
        var halfLifeDays = needsFreshness ? 30.0 : 1095.0;
        return Math.Pow(0.5, ageDays / halfLifeDays);
    }

    private static double Diversity(IEnumerable<Claim> claims)
        => claims.Select(c => c.Tier).Distinct().Count() switch
        {
            <= 1 => 0.3,
            2 => 0.7,
            _ => 1.0
        };
}