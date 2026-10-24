using System.Text.Json;
using NightOwl.Core;

namespace NightOwl.Credibility;

public sealed class SourceClassifier : ISourceClassifier
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly SourcesConfig _config;

    public SourceClassifier(SourcesConfig config) => _config = config;

    public static SourceClassifier FromFile(string path)
    {
        var json = File.ReadAllText(path);
        var config = JsonSerializer.Deserialize<SourcesConfig>(json, JsonOptions)
                     ?? throw new InvalidOperationException($"Не удалось прочитать {path}");
        return new SourceClassifier(config);
    }

    public IReadOnlyList<string> GetDomains(SourceTier tier) => GetRules(tier).Domains;

    public SourceTier Classify(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return SourceTier.Unofficial;

        var host = uri.Host.ToLowerInvariant();
        if (host.StartsWith("www.")) host = host[4..];

        // Порядок важен: сначала самые надёжные уровни
        if (Matches(host, _config.Official))   return SourceTier.Official;
        if (Matches(host, _config.Media))      return SourceTier.Media;
        if (Matches(host, _config.Reference))  return SourceTier.Reference;

        return SourceTier.Unofficial; // явный список или значение по умолчанию
    }

    private TierRules GetRules(SourceTier tier) => tier switch
    {
        SourceTier.Official  => _config.Official,
        SourceTier.Media     => _config.Media,
        SourceTier.Reference => _config.Reference,
        _                    => _config.Unofficial
    };

    private static bool Matches(string host, TierRules rules)
    {
        foreach (var domain in rules.Domains)
            if (host == domain || host.EndsWith("." + domain))
                return true;

        foreach (var suffix in rules.Suffixes)
        {
            var bare = suffix.TrimStart('.');
            if (host == bare || host.EndsWith("." + bare))
                return true;
        }

        return false;
    }
}