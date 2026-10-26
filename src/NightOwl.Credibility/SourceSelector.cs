
using NightOwl.Core;

namespace NightOwl.Credibility;

public static class SourceSelector
{
    // Результат считается «не по теме», если его оценка ниже абсолютного порога
    // или ниже половины оценки лучшего результата
    private const double MinAbsoluteRelevance = 0.15;
    private const double RelativeThreshold = 0.5;

    // Порядок, в котором «раздаём» слоты: сначала два официальных и два СМИ
    private static readonly SourceTier[] SlotOrder =
    [
        SourceTier.Official, SourceTier.Media,
        SourceTier.Official, SourceTier.Media,
        SourceTier.Reference, SourceTier.Unofficial
    ];

    private static readonly SourceTier[] FillOrder =
    [
        SourceTier.Official, SourceTier.Media, SourceTier.Reference, SourceTier.Unofficial
    ];

    public static IReadOnlyList<SearchResult> Select(
        IEnumerable<SearchResult> candidates, ISourceClassifier classifier, int max)
    {
        // 1. Сначала отсекаем то, что не относится к вопросу: уровень источника важен
        //    только среди релевантных результатов
        var pool = FilterByRelevance(candidates.ToList(), max);

        // 2. Внутри уровня: релевантные выше, при равенстве свежие выше
        var queues = pool
            .GroupBy(r => classifier.Classify(r.Url))
            .ToDictionary(
                g => g.Key,
                g => new Queue<SearchResult>(g
                    .OrderByDescending(r => r.Relevance ?? 0)
                    .ThenByDescending(r => r.PublishedAt ?? DateTimeOffset.MinValue)));

        var picked = new List<SearchResult>();
        var usedDomains = new HashSet<string>(); // независимость: один домен = один источник

        bool TryTake(SourceTier tier)
        {
            if (!queues.TryGetValue(tier, out var queue)) return false;

            while (queue.Count > 0)
            {
                var result = queue.Dequeue();
                if (!usedDomains.Add(DomainHelper.GetRootDomain(result.Url))) continue;

                picked.Add(result);
                return true;
            }

            return false;
        }

        foreach (var tier in SlotOrder)
        {
            if (picked.Count >= max) break;
            TryTake(tier);
        }

        foreach (var tier in FillOrder)
            while (picked.Count < max && TryTake(tier)) { }

        return picked;
    }

    private static List<SearchResult> FilterByRelevance(List<SearchResult> all, int max)
    {
        var best = all.Max(r => r.Relevance);
        if (best is null) return all; // у провайдера нет оценок: работаем как раньше

        var threshold = Math.Max(MinAbsoluteRelevance, best.Value * RelativeThreshold);
        var kept = all.Where(r => r.Relevance is null || r.Relevance >= threshold).ToList();

        // Если подходящих слишком мало, добираем самыми релевантными из отсеянных
        if (kept.Count < max)
            kept.AddRange(all.Except(kept).OrderByDescending(r => r.Relevance ?? 0).Take(max - kept.Count));

        return kept;
    }
}