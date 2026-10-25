using NightOwl.Core;

namespace NightOwl.Api;

public record AskRequest(string? Question);

public record SourceDto(
    int Number, string Title, string Url, string Domain, SourceTier Tier, DateTimeOffset? PublishedAt);

public record ClaimDto(
    string Text, int ConfidencePercent, IReadOnlyList<int> Supporting, IReadOnlyList<int> Contradicting);

public record AnswerDto(
    string Question,
    string Summary,
    int OverallConfidencePercent,
    IReadOnlyList<ClaimDto> Claims,
    IReadOnlyList<SourceDto> Sources,
    string Language,
    bool NeedsFreshness,
    IReadOnlyList<string> Queries,
    IReadOnlyList<string> Warnings,
    bool FromCache);

public static class AnswerMapper
{
    // Номера источников в DTO = номера [1], [2] в тексте ответа
    public static AnswerDto ToDto(this NightOwlAnswer a)
    {
        var number = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < a.Sources.Count; i++)
            number[a.Sources[i].Origin.Url] = i + 1;

        IReadOnlyList<int> Numbers(IEnumerable<Claim> claims) => claims
            .Select(c => number.TryGetValue(c.SourceUrl, out var n) ? n : 0)
            .Where(n => n > 0)
            .Distinct()
            .OrderBy(n => n)
            .ToList();

        return new AnswerDto(
            a.Question,
            a.Summary,
            (int)Math.Round(a.OverallConfidence * 100),
            a.Claims
                .Select(c => new ClaimDto(
                    c.Text, (int)Math.Round(c.Confidence * 100), Numbers(c.Supporting), Numbers(c.Contradicting)))
                .ToList(),
            a.Sources
                .Select((s, i) => new SourceDto(i + 1, s.Origin.Title, s.Origin.Url, s.Domain, s.Tier, s.PublishedAt))
                .ToList(),
            a.Plan.QuestionLanguage,
            a.Plan.NeedsFreshness,
            a.Plan.Queries,
            a.Warnings,
            a.FromCache);
    }
}