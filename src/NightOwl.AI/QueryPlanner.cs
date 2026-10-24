using Microsoft.Extensions.AI;
using NightOwl.Core;

namespace NightOwl.AI;

public sealed class QueryPlanner(IChatClient chat) : IQueryPlanner
{
    private const string Prompt = """
        Ты — модуль планирования поиска в системе проверки фактов NightOwl.
        По вопросу пользователя составь поисковые запросы.

        Правила:
        - Верни ТОЛЬКО JSON, без пояснений и без markdown.
        - Формат: {"language":"ru|en|other","needsFreshness":true|false,"queries":["..."]}
        - queries: от 2 до 4 коротких запросов в стиле ключевых слов.
        - Порядок: 1-й запрос на языке вопроса, 2-й на английском (если вопрос уже на английском, то на русском), остальные — вариации.
        - needsFreshness = true, если ответ может меняться со временем (новости, цены, статистика, законы, события, слова «последний», «сейчас», «сегодня»).
        - Если needsFreshness = true, добавь в запросы текущий год или месяц.
        - Не отвечай на вопрос и не выдумывай факты, только формулируй запросы.
        """;

    public async Task<QueryPlan> PlanAsync(string question, CancellationToken ct = default)
    {
        var user = $"Сегодняшняя дата: {DateTime.UtcNow:yyyy-MM-dd}\nВопрос: {question}";
        var dto = await LlmJson.AskAsync<PlanDto>(chat, Prompt, user, ct);

        var queries = (dto.Queries ?? [])
            .Where(q => !string.IsNullOrWhiteSpace(q))
            .Select(q => q.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(4)
            .ToList();

        if (queries.Count == 0) queries.Add(question);

        return new QueryPlan(dto.Language ?? "other", dto.NeedsFreshness, queries);
    }

    private sealed class PlanDto
    {
        public string? Language { get; set; }
        public bool NeedsFreshness { get; set; }
        public List<string>? Queries { get; set; }
    }
}