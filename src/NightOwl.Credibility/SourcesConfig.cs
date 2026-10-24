namespace NightOwl.Credibility;

public sealed class TierRules
{
    public List<string> Suffixes { get; set; } = [];
    public List<string> Domains { get; set; } = [];
}

public sealed class SourcesConfig
{
    public TierRules Official { get; set; } = new();
    public TierRules Media { get; set; } = new();
    public TierRules Reference { get; set; } = new();
    public TierRules Unofficial { get; set; } = new();
}