
using System.Text;
using Microsoft.Extensions.AI;
using NightOwl.Core;

namespace NightOwl.AI;

public sealed class ClaimExtractor(IChatClient chat) : IClaimExtractor
{
    private const int MaxCharsPerDoc = 5000;
    private const int MaxClaimsPerDoc = 5;

    private const string Prompt = """
        Ты — модуль извлечения фактов в системе проверки фактов NightOwl.
        Тебе дан вопрос и несколько пронумерованных документов. Для КАЖДОГО документа извлеки от 0 до 5 ключевых фактических утверждений ИЗ ЕГО ТЕКСТА, которые относятся к вопросу.

        Правила:
        - Верни ТОЛЬКО JSON-массив: [{"source":0,"claims":["утверждение 1","утверждение 2"]}]
        - Если документ не содержит ничего по вопросу, верни для него "claims": [].
        - Каждое утверждение самодостаточно: кто или что, где, когда, конкретные числа и даты. Никаких «он», «это», «в этом году».
        - Относительные даты («вчера», «в прошлом месяце», «в этом году») заменяй на абсолютные, опираясь на дату публикации документа.
        - Пиши утверждения на языке вопроса, даже если документ на другом языке. Числа, имена и даты сохраняй точно.
        - Только то, что явно сказано в тексте документа. Не добавляй свои знания и не делай выводов.
        - Утверждение относится только к тому документу, из которого оно взято. Не смешивай факты разных документов.
        - Мнения и прогнозы помечай явно: «По мнению X, ...».
        """;

    public async Task<IReadOnlyList<Claim>> ExtractAsync(
        SourceDocument doc, string question, CancellationToken ct = default)
        => await ExtractManyAsync(new[] { doc }, question, ct);

    public async Task<IReadOnlyList<Claim>> ExtractManyAsync(
        IReadOnlyList<SourceDocument> docs, string question, CancellationToken ct = default)
    {
        if (docs.Count == 0) return Array.Empty<Claim>();

        var sb = new StringBuilder();
        sb.AppendLine($"Сегодня: {DateTime.UtcNow:yyyy-MM-dd}");
        sb.AppendLine($"Вопрос: {question}");

        for (var i = 0; i < docs.Count; i++)
        {
            var d = docs[i];
            var text = d.Text.Length > MaxCharsPerDoc ? d.Text[..MaxCharsPerDoc] : d.Text;

            sb.AppendLine();
            sb.AppendLine($"=== Документ [{i}] ===");
            sb.AppendLine($"Источник: {d.Domain}");
            sb.AppendLine($"Дата публикации: {d.PublishedAt?.ToString("yyyy-MM-dd") ?? "неизвестна"}");
            sb.AppendLine("Текст:");
            sb.AppendLine(text);
        }

        var dtos = await LlmJson.AskAsync<List<SourceDto>>(chat, Prompt, sb.ToString(), ct);

        var claims = new List<Claim>();
        foreach (var dto in dtos.Where(d => d.Source >= 0 && d.Source < docs.Count))
        {
            var doc = docs[dto.Source];

            var statements = (dto.Claims ?? [])
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim())
                .Distinct()
                .Take(MaxClaimsPerDoc);

            foreach (var statement in statements)
                claims.Add(new Claim(statement, doc.Origin.Url, doc.Tier, doc.PublishedAt));
        }

        return claims;
    }

    private sealed class SourceDto
    {
        public int Source { get; set; }
        public List<string>? Claims { get; set; }
    }
}