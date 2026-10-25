using NightOwl.Core;

namespace NightOwl.Credibility;

public static class SourceSelector
{
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
        // Внутри уровня: свежие выше, при равенстве сохраняется порядок релевантности поиска
        var queues = candidates
            .GroupBy(r => classifier.Classify(r.Url))
            .ToDictionary(
                g => g.Key,
                g => new Queue<SearchResult>(g.OrderByDescending(r => r.PublishedAt ?? DateTimeOffset.MinValue)));

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
}