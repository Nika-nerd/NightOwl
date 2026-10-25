using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NightOwl.AI;
using NightOwl.Core;
using NightOwl.Credibility;
using NightOwl.Extraction;
using NightOwl.Search;
using Spectre.Console;
using NightOwl.Pipeline;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);
builder.Configuration.AddUserSecrets<Program>();
builder.Logging.SetMinimumLevel(LogLevel.Critical);

string Require(string key) => builder.Configuration[key]
    ?? throw new InvalidOperationException($"Не задан {key} (dotnet user-secrets set \"{key}\" ...)");

var tavilyKey = Require("Tavily:ApiKey");
var geminiKey = Require("Gemini:ApiKey");
var geminiModel = Require("Gemini:Model");

builder.Services.AddNightOwlSearch(o => o.ApiKey = tavilyKey);
builder.Services.AddNightOwlExtraction();
builder.Services.AddNightOwlAi(o =>
{
    o.ApiKey = geminiKey;
    o.Model = geminiModel;
    o.MinSecondsBetweenCalls = 3;
    o.FallbackModels = builder.Configuration.GetSection("Gemini:FallbackModels").Get<List<string>>() ?? [];
});

builder.Services.AddSingleton(_ =>
    SourceClassifier.FromFile(Path.Combine(AppContext.BaseDirectory, "sources.json")));
builder.Services.AddSingleton<ISourceClassifier>(sp => sp.GetRequiredService<SourceClassifier>());
builder.Services.AddSingleton<IConfidenceScorer, ConfidenceScorer>();
builder.Services.AddSingleton<IClaimVerifier, ClaimVerifier>();

using var host = builder.Build();

var search = host.Services.GetRequiredService<ISearchProvider>();
var classifier = host.Services.GetRequiredService<SourceClassifier>();
var extractor = host.Services.GetRequiredService<IContentExtractor>();
var planner = host.Services.GetRequiredService<IQueryPlanner>();
var relevance = host.Services.GetRequiredService<IRelevanceFilter>();

var question = args.Length > 0 ? string.Join(' ', args) : "latest WHO statement on measles";
AnsiConsole.MarkupLine($"[bold]Вопрос:[/] {Markup.Escape(question)}\n");

// ---------- 1. План запросов (LLM) ----------
QueryPlan plan;
try
{
    plan = await AnsiConsole.Status().StartAsync("Планирую поиск...", _ => planner.PlanAsync(question));
}
catch (Exception ex)
{
    AnsiConsole.MarkupLine($"[yellow]LLM недоступна ({Markup.Escape(ex.Message)}). Ищу по исходному вопросу.[/]");
    plan = new QueryPlan("other", false, [question]);
}

AnsiConsole.MarkupLine($"[bold]Язык:[/] {plan.QuestionLanguage}  [bold]Нужна свежесть:[/] {plan.NeedsFreshness}");
foreach (var q in plan.Queries)
    AnsiConsole.MarkupLine($"  • {Markup.Escape(q)}");
AnsiConsole.WriteLine();

// ---------- 2. Поиск по уровням ----------
var requests = new List<(string Label, SearchRequest Request)>();
foreach (var q in plan.Queries.Take(2))
{
    requests.Add(("Official", new SearchRequest(q, classifier.GetDomains(SourceTier.Official), NewsOnly: false, MaxResults: 5)));
    requests.Add(("Media",    new SearchRequest(q, classifier.GetDomains(SourceTier.Media),    NewsOnly: plan.NeedsFreshness, MaxResults: 5)));
    requests.Add(("General",  new SearchRequest(q, MaxResults: 8)));
}

var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
var found = new List<SearchResult>();
var labelByUrl = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

await AnsiConsole.Status().StartAsync("Ищу...", async _ =>
{
    foreach (var (label, request) in requests)
    {
        IReadOnlyList<SearchResult> results;
        try
        {
            results = await search.SearchAsync(request);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLine($"[red]{label}: ошибка поиска — {Markup.Escape(ex.Message)}[/]");
            continue;
        }

        foreach (var r in results.Where(r => seen.Add(r.Url)))
        {
            found.Add(r);
            labelByUrl[r.Url] = label;
        }
    }
});

AnsiConsole.MarkupLine($"Найдено уникальных результатов: [bold]{found.Count}[/]");

// ---------- 3. Фильтр релевантности (LLM): по умолчанию выключен, экономим квоту ----------
// Включить: dotnet user-secrets set "NightOwl:UseRelevanceFilter" "true" --project src/NightOwl.Cli
var useRelevanceFilter = builder.Configuration["NightOwl:UseRelevanceFilter"] == "true";

IReadOnlyList<SearchResult> relevant = found;
if (useRelevanceFilter)
{
    try
    {
        relevant = await AnsiConsole.Status().StartAsync("Фильтрую по релевантности...",
            _ => relevance.FilterAsync(question, found));
    }
    catch (Exception ex)
    {
        AnsiConsole.MarkupLine($"[yellow]Фильтр недоступен ({Markup.Escape(ex.Message.Replace("\n", " "))}). Оставляю все результаты.[/]");
    }

    AnsiConsole.MarkupLine($"После фильтра: [bold]{relevant.Count}[/] (отсеяно {found.Count - relevant.Count})\n");
}

var table = new Table().AddColumns("Pass", "Tier", "Дата", "Заголовок", "URL");
foreach (var r in relevant.OrderBy(r => classifier.Classify(r.Url)))
{
    table.AddRow(
        labelByUrl[r.Url],
        classifier.Classify(r.Url).ToString(),
        r.PublishedAt?.ToString("yyyy-MM-dd") ?? "-",
        Markup.Escape(r.Title.Length > 60 ? r.Title[..60] + "…" : r.Title),
        Markup.Escape(r.Url));
}
AnsiConsole.Write(table);

// ---------- 4. Извлечение содержимого ----------
const int MaxSources = 4;

// Берём с запасом (+2): часть страниц может не скачаться, и тогда подставится следующий кандидат
var top = SourceSelector.Select(relevant, classifier, MaxSources + 2);

var docs = new List<SourceDocument>();
using var gate = new SemaphoreSlim(4);

await AnsiConsole.Status().StartAsync("Скачиваю страницы...", async _ =>
{
    var tasks = top.Select(async r =>
    {
        await gate.WaitAsync();
        try { return await extractor.ExtractAsync(r); }
        finally { gate.Release(); }
    });

    var loaded = await Task.WhenAll(tasks); // порядок сохраняется: сначала самые приоритетные
    docs.AddRange(loaded.OfType<SourceDocument>().Take(MaxSources));
});

var docTable = new Table().AddColumns("Tier", "Домен", "Дата", "Символов", "Начало текста");
foreach (var d in docs.OrderBy(d => d.Tier))
{
    var preview = d.Text.Replace('\n', ' ');
    docTable.AddRow(
        d.Tier.ToString(),
        d.Domain,
        d.PublishedAt?.ToString("yyyy-MM-dd") ?? "-",
        d.Text.Length.ToString(),
        Markup.Escape(preview.Length > 100 ? preview[..100] + "…" : preview));
}

AnsiConsole.MarkupLine($"\n[bold]Используем источников: {docs.Count}[/] (кандидатов: {top.Count})");
AnsiConsole.Write(docTable);

// ---------- 5. Факты (claims): один пакетный вызов LLM на все источники ----------
var orderedDocs = docs
    .OrderBy(d => d.Tier)
    .ThenByDescending(d => d.PublishedAt ?? DateTimeOffset.MinValue)
    .ToList();

var claimExtractor = host.Services.GetRequiredService<IClaimExtractor>();
var verifier = host.Services.GetRequiredService<IClaimVerifier>();
var synthesizer = host.Services.GetRequiredService<IAnswerSynthesizer>();
var scorer = host.Services.GetRequiredService<IConfidenceScorer>();

var allClaims = new List<Claim>();
try
{
    var extracted = await AnsiConsole.Status().StartAsync("Извлекаю факты из источников...",
        _ => claimExtractor.ExtractManyAsync(orderedDocs, question));
    allClaims.AddRange(extracted);
}
catch (Exception ex)
{
    AnsiConsole.MarkupLine($"[red]Не удалось извлечь факты: {Markup.Escape(ex.Message.Replace("\n", " "))}[/]");
    return;
}

if (allClaims.Count == 0)
{
    AnsiConsole.MarkupLine("[red]В найденных источниках нет фактов по вашему вопросу.[/]");
    return;
}

// В список источников попадают только те, из которых реально извлечены факты
var usedDocs = orderedDocs
    .Where(d => allClaims.Any(c => string.Equals(c.SourceUrl, d.Origin.Url, StringComparison.OrdinalIgnoreCase)))
    .ToList();

AnsiConsole.MarkupLine($"\nИзвлечено фактов: [bold]{allClaims.Count}[/], источников с фактами: [bold]{usedDocs.Count}[/]");

// ---------- 6. Кросс-проверка и процент достоверности ----------
IReadOnlyList<VerifiedClaim> verified;
try
{
    verified = await AnsiConsole.Status().StartAsync("Сверяю источники между собой...",
        _ => verifier.VerifyAsync(question, plan.NeedsFreshness, allClaims.Take(40).ToList()));
}
catch (Exception ex)
{
    // Запасной режим без LLM: каждый факт оцениваем отдельно, без сопоставления между источниками
    AnsiConsole.MarkupLine(
        $"[yellow]Сопоставление источников недоступно ({Markup.Escape(ex.Message.Replace("\n", " "))}). " +
        "Показываю факты без кросс-проверки, проценты занижены.[/]");

    var now = DateTimeOffset.UtcNow;
    verified = allClaims
        .Select(c =>
        {
            var g = new ClaimGroup(c.Text, new[] { c }, Array.Empty<Claim>());
            return new VerifiedClaim(c.Text, scorer.Score(g, plan.NeedsFreshness, now), g.Supporting, g.Contradicting);
        })
        .OrderByDescending(v => v.Confidence)
        .ToList();
}

if (verified.Count == 0)
{
    AnsiConsole.MarkupLine("[red]Надёжной информации по этому вопросу не найдено.[/]");
    return;
}

var numberByUrl = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
for (var i = 0; i < usedDocs.Count; i++)
    numberByUrl[usedDocs[i].Origin.Url] = i + 1;

string Refs(IEnumerable<Claim> list)
    => string.Join(", ", list
        .Select(c => numberByUrl.TryGetValue(c.SourceUrl, out var n) ? $"[{n}]" : "")
        .Where(s => s.Length > 0)
        .Distinct());

static string PercentMarkup(double confidence)
{
    var pct = (int)Math.Round(confidence * 100);
    var color = pct >= 75 ? "green" : pct >= 50 ? "yellow" : "red";
    return $"[{color}]{pct}%[/]";
}

var claimTable = new Table().AddColumns("%", "Факт", "За", "Против");
foreach (var v in verified.Take(8))
{
    claimTable.AddRow(
        PercentMarkup(v.Confidence),
        Markup.Escape(v.Text),
        Markup.Escape(Refs(v.Supporting)),
        Markup.Escape(Refs(v.Contradicting)));
}

AnsiConsole.WriteLine();
AnsiConsole.Write(claimTable);

// ---------- 7. Итоговый ответ ----------
var topClaims = verified.Take(8).ToList();

string answer;
try
{
    answer = await AnsiConsole.Status().StartAsync("Формулирую ответ...",
        _ => synthesizer.SynthesizeAsync(question, topClaims, usedDocs));
}
catch (Exception ex)
{
    // Запасной ответ без LLM: просто список проверенных фактов
    answer = "Связный текст не удалось сформировать (" + ex.Message.Replace("\n", " ") + "). Проверенные факты:\n" +
             string.Join("\n", topClaims.Take(5).Select(v => $"• {v.Text} {Refs(v.Supporting)}"));
}

// Общая уверенность: среднее по трём лучше всего подтверждённым фактам
var overall = verified
    .OrderByDescending(v => v.Supporting.Count)
    .ThenByDescending(v => v.Confidence)
    .Take(3)
    .Average(v => v.Confidence);

AnsiConsole.WriteLine();
AnsiConsole.Write(new Panel(new Text(answer))   // Text не разбирает [1][2] как разметку
    .Header($"NightOwl · уверенность {(int)Math.Round(overall * 100)}%")
    .Expand());

AnsiConsole.MarkupLine("\n[bold]Источники:[/]");
for (var i = 0; i < usedDocs.Count; i++)
{
    var d = usedDocs[i];
    AnsiConsole.MarkupLine(
        $"[[{i + 1}]] {d.Tier} · {Markup.Escape(d.Domain)} · {d.PublishedAt?.ToString("yyyy-MM-dd") ?? "н/д"} · {Markup.Escape(d.Origin.Url)}");
}

AnsiConsole.MarkupLine("\n[grey]Процент — эвристическая оценка по уровню источников, согласованности, свежести и разнообразию. Это не гарантия истинности.[/]");
