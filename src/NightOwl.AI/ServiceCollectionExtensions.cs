using System.ClientModel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NightOwl.Core;
using OpenAI;

namespace NightOwl.AI;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddNightOwlAi(this IServiceCollection services, Action<GeminiOptions> configure)
    {
        services.Configure(configure);

        services.AddSingleton<IChatClient>(sp =>
        {
            var o = sp.GetRequiredService<IOptions<GeminiOptions>>().Value;

            if (string.IsNullOrWhiteSpace(o.ApiKey)) throw new InvalidOperationException("Не задан Gemini:ApiKey");
            if (string.IsNullOrWhiteSpace(o.Model)) throw new InvalidOperationException("Не задан Gemini:Model");

            var client = new OpenAIClient(
                new ApiKeyCredential(o.ApiKey),
                new OpenAIClientOptions { Endpoint = new Uri(o.Endpoint) });

                  var interval = TimeSpan.FromSeconds(o.MinSecondsBetweenCalls);

            // У каждой модели свой ограничитель: лимиты считаются по моделям отдельно
            IChatClient Build(string model)
                => new ThrottlingChatClient(client.GetChatClient(model).AsIChatClient(), interval);

            var primary = Build(o.Model);

            return o.FallbackModels.Count == 0
                ? primary
                : new FallbackChatClient(primary, o.FallbackModels.Select(Build).ToList());
        });

        services.AddSingleton<IQueryPlanner, QueryPlanner>();
        services.AddSingleton<IRelevanceFilter, RelevanceFilter>();
        services.AddSingleton<IClaimExtractor, ClaimExtractor>();
        services.AddSingleton<IClaimGrouper, ClaimGrouper>();
        services.AddSingleton<IAnswerSynthesizer, AnswerSynthesizer>();

        return services;
    }
}