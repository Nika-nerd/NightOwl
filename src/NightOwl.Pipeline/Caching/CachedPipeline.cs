using Microsoft.Extensions.Options;
using NightOwl.Core;

namespace NightOwl.Pipeline.Caching;

public sealed class CachedPipeline(
    INightOwlPipeline inner, ICacheStore cache, IOptions<CacheOptions> options) : INightOwlPipeline
{
    public async Task<NightOwlAnswer> AskAsync(
        string question, IProgress<PipelineProgress>? progress = null, CancellationToken ct = default)
    {
        var key = CacheKey.For("answer", CacheKey.NormalizeQuestion(question));

        var cached = await cache.GetAsync<NightOwlAnswer>(key, ct);
        if (cached is not null)
        {
            progress?.Report(new PipelineProgress(PipelineStage.Answering, "Ответ найден в кэше"));
            return cached with { Question = question, FromCache = true };
        }

        var answer = await inner.AskAsync(question, progress, ct);

        // Кэшируем только полноценные ответы: с фактами и без предупреждений о сбоях
        if (answer.Claims.Count > 0 && answer.Warnings.Count == 0)
        {
            var o = options.Value;
            var ttl = answer.Plan.NeedsFreshness ? o.AnswerTtlFresh : o.AnswerTtl;
            await cache.SetAsync(key, answer, ttl, ct);
        }

        return answer;
    }
}