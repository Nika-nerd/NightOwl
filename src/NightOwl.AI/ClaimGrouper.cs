using System.Text;
using Microsoft.Extensions.AI;
using NightOwl.Core;

namespace NightOwl.AI;

public sealed class ClaimGrouper(IChatClient chat) : IClaimGrouper
{
    private const string Prompt = """
        Ты — модуль сопоставления фактов в системе проверки фактов NightOwl.
        Тебе дан вопрос и пронумерованные утверждения из разных источников в формате:
        [номер] утверждение | домен | уровень | дата

        Сгруппируй утверждения, которые говорят об одном и том же факте.

        Правила:
        - Верни ТОЛЬКО JSON-массив: [{"statement":"...","supporting":[0,3],"contradicting":[5]}]
        - statement — одна чёткая формулировка факта на языке вопроса.
        - supporting — номера утверждений, подтверждающих statement (совпадают по смыслу и по ключевым числам и датам).
        - contradicting — номера утверждений, которые прямо противоречат statement (другие числа, даты, противоположный смысл).
        - Разная формулировка противоречием не является. Округление («около 10 тысяч» и «10 234») — это подтверждение.
        - Каждое утверждение может быть в supporting только одной группы. Утверждение, которое никто не подтверждает, образует собственную группу.
        - Оставь только группы, относящиеся к вопросу. Не выдумывай новые утверждения.
        """;

    public async Task<IReadOnlyList<ClaimGroup>> GroupAsync(
        string question, IReadOnlyList<Claim> claims, CancellationToken ct = default)
    {
        if (claims.Count == 0) return Array.Empty<ClaimGroup>();

        var sb = new StringBuilder();
        sb.AppendLine($"Вопрос: {question}");
        sb.AppendLine();
        sb.AppendLine("Утверждения:");

        for (var i = 0; i < claims.Count; i++)
        {
            var c = claims[i];
            var host = Uri.TryCreate(c.SourceUrl, UriKind.Absolute, out var u) ? u.Host : c.SourceUrl;
            var date = c.PublishedAt?.ToString("yyyy-MM-dd") ?? "н/д";
            sb.AppendLine($"[{i}] {c.Text} | {host} | {c.Tier} | {date}");
        }

        var dtos = await LlmJson.AskAsync<List<GroupDto>>(chat, Prompt, sb.ToString(), ct);

        var groups = new List<ClaimGroup>();
        var usedAsSupport = new HashSet<int>();

        foreach (var dto in dtos)
        {
            if (string.IsNullOrWhiteSpace(dto.Statement)) continue;

            var supportIdx = Valid(dto.Supporting, claims.Count).Where(usedAsSupport.Add).ToList();
            if (supportIdx.Count == 0) continue;

            var contraIdx = Valid(dto.Contradicting, claims.Count).Where(i => !supportIdx.Contains(i)).ToList();

            groups.Add(new ClaimGroup(
                dto.Statement.Trim(),
                supportIdx.Select(i => claims[i]).ToList(),
                contraIdx.Select(i => claims[i]).ToList()));
        }

        return groups;
    }

    private static IEnumerable<int> Valid(List<int>? indexes, int count)
        => (indexes ?? []).Where(i => i >= 0 && i < count).Distinct();

    private sealed class GroupDto
    {
        public string? Statement { get; set; }
        public List<int>? Supporting { get; set; }
        public List<int>? Contradicting { get; set; }
    }
}