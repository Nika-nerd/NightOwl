using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NightOwl.Core;
using NightOwl.Credibility;
using System.ClientModel;
using System.Text.Json;

namespace NightOwl.Pipeline;

public sealed class NightOwlPipeline(
    IQueryPlanner planner,
    ISearchProvider search,
    ISourceClassifier classifier,
    IRelevanceFilter relevanceFilter,
    IContentExtractor extractor,
    IClaimExtractor claimExtractor,
    IClaimVerifier verifier,
    IConfidenceScorer scorer,
    IAnswerSynthesizer synthesizer,
    IOptions<PipelineOptions> options,
    ILogger<NightOwlPipeline> logger) : INightOwlPipeline
{
    private const int OfficialResults = 5;
    private const int MediaResults = 5;
    private const int GeneralResults = 8;

    private readonly PipelineOptions _opt = options.Value;

    public async Task<NightOwlAnswer> AskAsync(
        string question, IProgress<PipelineProgress>? progress = null, CancellationToken ct = default)
    {
        var warnings = new List<string>();
        void Report(PipelineStage stage, string message) => progress?.Report(new PipelineProgress(stage, message));

        // 1. План запросов (LLM)
        Report(PipelineStage.Planning, "Планирую поиск...");
        var plan = await PlanAsync(question, warnings, ct);

        // 2. Поиск по уровням
        Report(PipelineStage.Searching, "Ищу: официальные источники, СМИ, весь веб...");
        var found = await SearchAsync(plan, warnings, ct);
        if (found.Count == 0)
            return NoResult(question, plan, warnings, "По вашему запросу ничего не найдено.");

        // 3. Фильтр релевантности (по желанию)
        var relevant = await FilterAsync(question, found, warnings, ct, Report);

        // 4. Выбор и загрузка источников
        Report(PipelineStage.Downloading, $"Скачиваю до {_opt.MaxSources} источников...");
        var docs = (await DownloadAsync(relevant, ct))
            .OrderBy(d => d.Tier)
            .ThenByDescending(d => d.PublishedAt ?? DateTimeOffset.MinValue)
            .ToList();

        if (docs.Count == 0)
            return NoResult(question, plan, warnings, "Не удалось загрузить ни один источник.");

        // 5. Факты: один пакетный вызов LLM
        Report(PipelineStage.ExtractingClaims, "Извлекаю факты из источников...");
        IReadOnlyList<Claim> claims;
        try
        {
            claims = await claimExtractor.ExtractManyAsync(docs, question, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Claim extraction failed");
            warnings.Add($"Не удалось извлечь факты: {Describe(ex)}");
            return NoResult(question, plan, warnings,
                "Не удалось извлечь факты из источников. Повторите запрос позже.", docs);
        }

        if (claims.Count == 0)
            return NoResult(question, plan, warnings, "В найденных источниках нет фактов по вашему вопросу.", docs);

        // В ответ попадают только источники, из которых реально извлечены факты
        var usedDocs = docs
            .Where(d => claims.Any(c => string.Equals(c.SourceUrl, d.Origin.Url, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        // 6. Кросс-проверка и процент достоверности
        Report(PipelineStage.Verifying, "Сверяю источники между собой...");
        IReadOnlyList<VerifiedClaim> verified;
        try
        {
            verified = await verifier.VerifyAsync(
                question, plan.NeedsFreshness, claims.Take(_opt.MaxClaimsToVerify).ToList(), ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Claim verification failed");
            warnings.Add($"Сопоставление источников недоступно ({Describe(ex)}): " +
                         "факты показаны без кросс-проверки, проценты занижены.");
            verified = ScoreIndividually(claims, plan.NeedsFreshness);
        }

        if (verified.Count == 0)
            return NoResult(question, plan, warnings, "Надёжной информации по этому вопросу не найдено.", usedDocs);

        // 7. Итоговый ответ
        Report(PipelineStage.Answering, "Формулирую ответ...");
        var top = verified.Take(_opt.TopClaimsInAnswer).ToList();
        string summary;
        try
        {
            summary = await synthesizer.SynthesizeAsync(question, top, usedDocs, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Answer synthesis failed");
            warnings.Add($"Связный текст ответа недоступен ({Describe(ex)}): показан список фактов.");
            summary = FallbackSummary(top, usedDocs);
        }

        // Общая уверенность: среднее по трём лучше всего подтверждённым фактам
        var overall = verified
            .OrderByDescending(v => v.Supporting.Count)
            .ThenByDescending(v => v.Confidence)
            .Take(3)
            .Average(v => v.Confidence);

        return new NightOwlAnswer(question, summary, overall, verified, usedDocs, plan, warnings);
    }

    private async Task<QueryPlan> PlanAsync(string question, List<string> warnings, CancellationToken ct)
    {
        try
        {
            return await planner.PlanAsync(question, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Query planning failed");
            warnings.Add($"План запросов недоступен ({Describe(ex)}): поиск по исходному вопросу.");
            return new QueryPlan("other", false, new[] { question });
        }
    }

    private async Task<List<SearchResult>> SearchAsync(QueryPlan plan, List<string> warnings, CancellationToken ct)
    {
        var requests = new List<(string Label, SearchRequest Request)>();

        foreach (var q in plan.Queries.Take(_opt.QueriesPerQuestion))
        {
            requests.Add(("официальные", new SearchRequest(
                q, classifier.GetDomains(SourceTier.Official), NewsOnly: false, MaxResults: OfficialResults)));
            requests.Add(("СМИ", new SearchRequest(
                q, classifier.GetDomains(SourceTier.Media), NewsOnly: plan.NeedsFreshness, MaxResults: MediaResults)));
            requests.Add(("общий", new SearchRequest(q, MaxResults: GeneralResults)));
        }

        // Все поиски параллельно; порядок результатов сохраняется
        var batches = await Task.WhenAll(requests.Select(async r =>
        {
            try
            {
                return await search.SearchAsync(r.Request, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Search failed ({Label})", r.Label);
                lock (warnings) warnings.Add($"Поиск ({r.Label}) не удался: {Describe(ex)}");
                return (IReadOnlyList<SearchResult>)Array.Empty<SearchResult>();
            }
        }));

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return batches.SelectMany(b => b).Where(r => seen.Add(r.Url)).ToList();
    }

    private async Task<IReadOnlyList<SearchResult>> FilterAsync(
        string question, List<SearchResult> found, List<string> warnings, CancellationToken ct,
        Action<PipelineStage, string> report)
    {
        if (!_opt.UseRelevanceFilter) return found;

        report(PipelineStage.Filtering, "Фильтрую по релевантности...");
        try
        {
            return await relevanceFilter.FilterAsync(question, found, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Relevance filter failed");
            warnings.Add($"Фильтр релевантности недоступен ({Describe(ex)}): использованы все результаты.");
            return found;
        }
    }

    private async Task<List<SourceDocument>> DownloadAsync(IReadOnlyList<SearchResult> relevant, CancellationToken ct)
    {
        // Берём с запасом: часть страниц может не скачаться, тогда подставится следующий кандидат
        var candidates = SourceSelector.Select(relevant, classifier, _opt.MaxSources + _opt.CandidateReserve);

        using var gate = new SemaphoreSlim(_opt.MaxDownloadConcurrency);
        var loaded = await Task.WhenAll(candidates.Select(async r =>
        {
            await gate.WaitAsync(ct);
            try { return await extractor.ExtractAsync(r, ct); }
            finally { gate.Release(); }
        }));

        // Порядок сохраняется: сначала самые приоритетные кандидаты
        return loaded.OfType<SourceDocument>().Take(_opt.MaxSources).ToList();
    }

    // Запасной режим без LLM: каждый факт оценивается отдельно, без сопоставления источников
    private IReadOnlyList<VerifiedClaim> ScoreIndividually(IReadOnlyList<Claim> claims, bool needsFreshness)
    {
        var now = DateTimeOffset.UtcNow;
        return claims
            .Select(c =>
            {
                var group = new ClaimGroup(c.Text, new[] { c }, Array.Empty<Claim>());
                return new VerifiedClaim(c.Text, scorer.Score(group, needsFreshness, now), group.Supporting, group.Contradicting);
            })
            .OrderByDescending(v => v.Confidence)
            .ToList();
    }

    private static string FallbackSummary(IReadOnlyList<VerifiedClaim> top, IReadOnlyList<SourceDocument> sources)
    {
        var number = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < sources.Count; i++) number[sources[i].Origin.Url] = i + 1;

        var lines = top.Take(5).Select(v =>
        {
            var refs = string.Concat(v.Supporting
                .Select(c => number.TryGetValue(c.SourceUrl, out var n) ? $"[{n}]" : "")
                .Distinct());
            return $"• {v.Text} {refs}".TrimEnd();
        });

        return "Проверенные факты:\n" + string.Join("\n", lines);
    }

    private static NightOwlAnswer NoResult(
        string question, QueryPlan plan, List<string> warnings, string summary,
        IReadOnlyList<SourceDocument>? sources = null)
        => new(question, summary, 0,
            Array.Empty<VerifiedClaim>(), sources ?? Array.Empty<SourceDocument>(), plan, warnings);

        private static string Describe(Exception ex)
    {
        var message = ex.Message.Replace("\r", " ").Replace("\n", " ").Trim();

        if (ex is ClientResultException apiError && ExtractApiMessage(apiError) is { } detail)
            message = $"{message} — {detail}";

        return message;
    }

    // Достаёт текст ошибки из тела ответа Gemini: {"error":{"message":"..."}} или [{"error":{...}}]
    private static string? ExtractApiMessage(ClientResultException ex)
    {
        try
        {
            var body = ex.GetRawResponse()?.Content?.ToString();
            if (string.IsNullOrWhiteSpace(body)) return null;

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0
                ? doc.RootElement[0]
                : doc.RootElement;

            var text = root.TryGetProperty("error", out var error) && error.TryGetProperty("message", out var message)
                ? message.GetString()
                : body;

            text = text?.Replace("\r", " ").Replace("\n", " ").Trim();
            return text is { Length: > 200 } ? text[..200] + "…" : text;
        }
        catch
        {
            return null;
        }
    }
}