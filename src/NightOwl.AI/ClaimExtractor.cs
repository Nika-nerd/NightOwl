using Microsoft.Extensions.AI;
using NightOwl.Core;

namespace NightOwl.AI;

public sealed class ClaimExtractor(IChatClient chat) : IClaimExtractor
{
    private const int MaxChars = 8000;

    private const string Prompt = """
        Ты — модуль извлечения фактов в системе проверки фактов NightOwl.
        Тебе дан вопрос и текст документа. Извлеки от 1 до 5 ключевых фактических утверждений ИЗ ТЕКСТА, которые относятся к вопросу.

        Правила:
        - Верни ТОЛЬКО JSON-массив строк: ["утверждение 1", "утверждение 2"]. Если в тексте нет ничего по вопросу — верни [].
        - Каждое утверждение самодостаточно: кто или что, где, когда, конкретные числа и даты. Никаких «он», «это», «в этом году».
        - Относительные даты («вчера», «в прошлом месяце», «в этом году») заменяй на абсолютные, опираясь на дату публикации документа.
        - Пиши утверждения на языке вопроса, даже если документ на другом языке. Числа, имена и даты сохраняй точно.
        - Только то, что явно сказано в тексте. Не добавляй свои знания и не делай выводов.
        - Мнения и прогнозы помечай явно: «По мнению X, ...».
        """;

    public async Task<IReadOnlyList<Claim>> ExtractAsync(
        SourceDocument doc, string question, CancellationToken ct = default)
    {
        var text = doc.Text.Length > MaxChars ? doc.Text[..MaxChars] : doc.Text;
        var date = doc.PublishedAt?.ToString("yyyy-MM-dd") ?? "неизвестна";

        var user =
            $"Сегодня: {DateTime.UtcNow:yyyy-MM-dd}\n" +
            $"Вопрос: {question}\n\n" +
            $"Источник: {doc.Domain}\n" +
            $"Дата публикации: {date}\n\n" +
            $"Текст документа:\n{text}";

        var statements = await LlmJson.AskAsync<List<string>>(chat, Prompt, user, ct);

        return statements
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => new Claim(s.Trim(), doc.Origin.Url, doc.Tier, doc.PublishedAt))
            .Take(5)
            .ToList();
    }
}