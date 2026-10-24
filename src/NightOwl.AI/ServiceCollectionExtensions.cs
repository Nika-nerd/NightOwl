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

            // В старых версиях пакета метод назывался AsChatClient()
            return client.GetChatClient(o.Model).AsIChatClient();
        });

        services.AddSingleton<IQueryPlanner, QueryPlanner>();
        services.AddSingleton<IRelevanceFilter, RelevanceFilter>();
        services.AddSingleton<IClaimExtractor, ClaimExtractor>();
        services.AddSingleton<IClaimGrouper, ClaimGrouper>();
        services.AddSingleton<IAnswerSynthesizer, AnswerSynthesizer>();

        return services;
    }
}