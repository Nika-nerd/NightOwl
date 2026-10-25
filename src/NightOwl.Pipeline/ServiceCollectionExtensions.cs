using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NightOwl.AI;
using NightOwl.Core;
using NightOwl.Credibility;
using NightOwl.Extraction;
using NightOwl.Pipeline.Caching;
using NightOwl.Search;

namespace NightOwl.Pipeline;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddNightOwl(this IServiceCollection services, IConfiguration configuration)
    {
        string Require(string key) => configuration[key]
            ?? throw new InvalidOperationException(
                $"Не задан параметр {key}. Задай его: dotnet user-secrets set \"{key}\" \"...\" --project <проект>");

          var useOpenRouter = string.Equals(configuration["Llm:Provider"], "openrouter", StringComparison.OrdinalIgnoreCase);
    var llm = useOpenRouter ? "OpenRouter" : "Gemini";

    var tavilyKey = Require("Tavily:ApiKey");
    var llmKey = Require($"{llm}:ApiKey");
    var llmModel = Require($"{llm}:Model");

    services.AddNightOwlSearch(o => o.ApiKey = tavilyKey);
    services.AddNightOwlExtraction();
    services.AddNightOwlAi(o =>
    {
        o.ApiKey = llmKey;
        o.Model = llmModel;
        o.Endpoint = configuration[$"{llm}:Endpoint"]
            ?? (useOpenRouter ? "https://openrouter.ai/api/v1/" : o.Endpoint);
        o.MinSecondsBetweenCalls = configuration.GetValue($"{llm}:MinSecondsBetweenCalls", useOpenRouter ? 1.0 : 3.0);
        o.FallbackModels = configuration.GetSection($"{llm}:FallbackModels").Get<List<string>>() ?? [];
    });
        

        services.AddSingleton(_ =>
            SourceClassifier.FromFile(Path.Combine(AppContext.BaseDirectory, "sources.json")));
        services.AddSingleton<ISourceClassifier>(sp => sp.GetRequiredService<SourceClassifier>());
        services.AddSingleton<IConfidenceScorer, ConfidenceScorer>();
        services.AddSingleton<IClaimVerifier, ClaimVerifier>();

        // ---------- Кэш ----------
        services.Configure<CacheOptions>(configuration.GetSection("NightOwl:Cache"));
        services.AddSingleton<ICacheStore>(sp =>
        {
            var o = sp.GetRequiredService<IOptions<CacheOptions>>().Value;
            return o.Enabled ? new SqliteCacheStore(o.ResolvePath()) : new NullCacheStore();
        });

        services.AddTransient<ISearchProvider>(sp => new CachingSearchProvider(
            sp.GetRequiredService<TavilySearchProvider>(),
            sp.GetRequiredService<ICacheStore>(),
            sp.GetRequiredService<IOptions<CacheOptions>>()));

        services.AddTransient<IContentExtractor>(sp => new CachingContentExtractor(
            sp.GetRequiredService<ContentExtractor>(),
            sp.GetRequiredService<ICacheStore>(),
            sp.GetRequiredService<IOptions<CacheOptions>>()));

        // ---------- Пайплайн ----------
        // Transient: чтобы типизированные HttpClient не «залипали» в синглтоне (устаревший DNS)
        services.Configure<PipelineOptions>(configuration.GetSection("NightOwl"));
        services.AddTransient<NightOwlPipeline>();
        services.AddTransient<INightOwlPipeline>(sp => new CachedPipeline(
            sp.GetRequiredService<NightOwlPipeline>(),
            sp.GetRequiredService<ICacheStore>(),
            sp.GetRequiredService<IOptions<CacheOptions>>()));

        return services;
    }
}