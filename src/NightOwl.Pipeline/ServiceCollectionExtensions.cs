using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NightOwl.AI;
using NightOwl.Core;
using NightOwl.Credibility;
using NightOwl.Extraction;
using NightOwl.Search;

namespace NightOwl.Pipeline;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Регистрирует всё ядро NightOwl. Ключи берутся из конфигурации:
    /// Tavily:ApiKey, Gemini:ApiKey, Gemini:Model (+ необязательные Gemini:FallbackModels, Gemini:MinSecondsBetweenCalls).
    /// Настройки пайплайна читаются из секции "NightOwl".
    /// </summary>
    public static IServiceCollection AddNightOwl(this IServiceCollection services, IConfiguration configuration)
    {
        string Require(string key) => configuration[key]
            ?? throw new InvalidOperationException(
                $"Не задан параметр {key}. Задай его: dotnet user-secrets set \"{key}\" \"...\" --project <проект>");

        var tavilyKey = Require("Tavily:ApiKey");
        var geminiKey = Require("Gemini:ApiKey");
        var geminiModel = Require("Gemini:Model");

        services.AddNightOwlSearch(o => o.ApiKey = tavilyKey);
        services.AddNightOwlExtraction();
        services.AddNightOwlAi(o =>
        {
            o.ApiKey = geminiKey;
            o.Model = geminiModel;
            o.MinSecondsBetweenCalls = configuration.GetValue("Gemini:MinSecondsBetweenCalls", 3.0);
            o.FallbackModels = configuration.GetSection("Gemini:FallbackModels").Get<List<string>>() ?? [];
        });

        services.AddSingleton(_ =>
            SourceClassifier.FromFile(Path.Combine(AppContext.BaseDirectory, "sources.json")));
        services.AddSingleton<ISourceClassifier>(sp => sp.GetRequiredService<SourceClassifier>());
        services.AddSingleton<IConfidenceScorer, ConfidenceScorer>();
        services.AddSingleton<IClaimVerifier, ClaimVerifier>();

        services.Configure<PipelineOptions>(configuration.GetSection("NightOwl"));
        services.AddSingleton<INightOwlPipeline, NightOwlPipeline>();

        return services;
    }
}