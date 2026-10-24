using System.Text;
using Microsoft.Extensions.AI;
using NightOwl.Core;

namespace NightOwl.AI;

public sealed class RelevanceFilter(IChatClient chat) : IRelevanceFilter
{
    private const double Threshold = 0.5;

    private const string Prompt = """
        Ты — модуль оценки релевантности в системе проверки фактов NightOwl.
        Тебе дан вопрос и список найденных материалов (заголовок, домен, фрагмент).
        Для каждого материала оцени, помогает ли он ответить именно на этот вопрос.

        Правила:
        - Верни ТОЛЬКО JSON-массив, без пояснений и без markdown.
        - Формат: [{"index":0,"relevant":true,"score":0.9}]
        - score от 0 до 1. Материал на смежную, но другую тему (другая болезнь, другая страна, другой период) — нерелевантен.
        - Оцени каждый материал из списка.
        """;

    public async Task<IReadOnlyList<SearchResult>> FilterAsync(
        string question, IReadOnlyList<SearchResult> candidates, CancellationToken ct = default)
    {
        if (candidates.Count == 0) return candidates;

        var sb = new StringBuilder();
        sb.AppendLine($"Сегодняшняя дата: {DateTime.UtcNow:yyyy-MM-dd}");
        sb.AppendLine($"Вопрос: {question}");
        sb.AppendLine();
        sb.AppendLine("Материалы:");

        for (var i = 0; i < candidates.Count; i++)
        {
            var c = candidates[i];
            var snippet = c.Snippet.Length > 300 ? c.Snippet[..300] : c.Snippet;
            var host = Uri.TryCreate(c.Url, UriKind.Absolute, out var u) ? u.Host : "";
            sb.AppendLine($"[{i}] {c.Title} | {host} | {snippet.Replace('\n', ' ')}");
        }

        var verdicts = await LlmJson.AskAsync<List<VerdictDto>>(chat, Prompt, sb.ToString(), ct);

        var keep = verdicts
            .Where(v => v.Relevant && v.Score >= Threshold && v.Index >= 0 && v.Index < candidates.Count)
            .Select(v => v.Index)
            .ToHashSet();

        return candidates.Where((_, i) => keep.Contains(i)).ToList();
    }

    private sealed class VerdictDto
    {
        public int Index { get; set; }
        public bool Relevant { get; set; }
        public double Score { get; set; }
    }
}