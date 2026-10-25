namespace NightOwl.Pipeline;

public sealed class PipelineOptions
{
    /// <summary>Сколько источников использовать в ответе.</summary>
    public int MaxSources { get; set; } = 4;

    /// <summary>Запас кандидатов на случай, если часть страниц не скачается.</summary>
    public int CandidateReserve { get; set; } = 2;

    /// <summary>Сколько поисковых запросов из плана использовать (ru + en = 2).</summary>
    public int QueriesPerQuestion { get; set; } = 2;

    public int MaxDownloadConcurrency { get; set; } = 4;

    /// <summary>Отдельный вызов LLM для фильтра релевантности (по умолчанию выключен, экономим квоту).</summary>
    public bool UseRelevanceFilter { get; set; } = false;

    public int MaxClaimsToVerify { get; set; } = 40;
    public int TopClaimsInAnswer { get; set; } = 8;
}