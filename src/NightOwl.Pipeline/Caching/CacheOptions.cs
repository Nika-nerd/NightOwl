namespace NightOwl.Pipeline.Caching;

public sealed class CacheOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>Путь к файлу БД. По умолчанию ~/.local/share/NightOwl/cache.db (общий для CLI и API).</summary>
    public string? Path { get; set; }

    public TimeSpan AnswerTtl { get; set; } = TimeSpan.FromHours(24);
    public TimeSpan AnswerTtlFresh { get; set; } = TimeSpan.FromHours(1);
    public TimeSpan SearchTtl { get; set; } = TimeSpan.FromHours(6);
    public TimeSpan NewsSearchTtl { get; set; } = TimeSpan.FromMinutes(30);
    public TimeSpan PageTtl { get; set; } = TimeSpan.FromHours(24);

    public string ResolvePath() => !string.IsNullOrWhiteSpace(Path)
        ? Path
        : System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NightOwl", "cache.db");
}