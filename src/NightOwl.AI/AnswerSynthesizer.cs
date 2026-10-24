using System.Text;
using Microsoft.Extensions.AI;
using NightOwl.Core;

namespace NightOwl.AI;

public sealed class AnswerSynthesizer(IChatClient chat) : IAnswerSynthesizer
{
    private const string Prompt = """
        Ты — модуль формирования ответа в системе NightOwl.
        Тебе даны вопрос и проверенные факты с процентом уверенности и ссылками на источники.

        Правила:
        - Отвечай на языке вопроса, кратко и по делу (3-8 предложений).
        - Используй ТОЛЬКО переданные факты. Никаких внешних знаний.
        - После каждого утверждения ставь ссылки на источники в формате [1][3], номера бери из полей «Подтверждают».
        - Приоритет у фактов с более высокой уверенностью.
        - Если источники противоречат друг другу, прямо опиши противоречие и укажи, какая версия подкреплена более надёжными источниками.
        - Если уверенность факта ниже 50%, пометь его как «надёжно не подтверждено».
        - Если фактов недостаточно, честно скажи, что надёжной информации не найдено.
        - Не выводи список источников и проценты, их добавит программа.
        """;

    public Task<string> SynthesizeAsync(
        string question,
        IReadOnlyList<VerifiedClaim> claims,
        IReadOnlyList<SourceDocument> sources,
        CancellationToken ct = default)
    {
        var number = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < sources.Count; i++)
            number[sources[i].Origin.Url] = i + 1;

        string Refs(IEnumerable<Claim> list)
        {
            var refs = list
                .Select(c => number.TryGetValue(c.SourceUrl, out var n) ? $"[{n}]" : null)
                .Where(r => r is not null)
                .Distinct()
                .ToList();
            return refs.Count == 0 ? "нет" : string.Join(", ", refs);
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Сегодня: {DateTime.UtcNow:yyyy-MM-dd}");
        sb.AppendLine($"Вопрос: {question}");
        sb.AppendLine();
        sb.AppendLine("Проверенные факты:");

        for (var i = 0; i < claims.Count; i++)
        {
            var c = claims[i];
            sb.AppendLine($"Факт {i + 1}: {c.Text}");
            sb.AppendLine($"  Уверенность: {(int)Math.Round(c.Confidence * 100)}%");
            sb.AppendLine($"  Подтверждают: {Refs(c.Supporting)}");
            sb.AppendLine($"  Противоречат: {Refs(c.Contradicting)}");
        }

        sb.AppendLine();
        sb.AppendLine("Источники:");
        for (var i = 0; i < sources.Count; i++)
        {
            var s = sources[i];
            sb.AppendLine($"[{i + 1}] {s.Domain} ({s.Tier}, {s.PublishedAt?.ToString("yyyy-MM-dd") ?? "н/д"})");
        }

        return LlmJson.AskTextAsync(chat, Prompt, sb.ToString(), ct);
    }
}