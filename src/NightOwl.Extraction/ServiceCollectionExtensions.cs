using Microsoft.Extensions.DependencyInjection;
using NightOwl.Core;
using Polly;

namespace NightOwl.Extraction;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddNightOwlExtraction(this IServiceCollection services)
    {
        services.AddHttpClient<IContentExtractor, ContentExtractor>(c =>
        {
            c.Timeout = TimeSpan.FromSeconds(30); // запасной предел
            c.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (compatible; NightOwlBot/0.1; +https://github.com/your/nightowl)");
            c.DefaultRequestHeaders.AcceptLanguage.ParseAdd("ru,en;q=0.8");
        })

        .AddResilienceHandler("extraction", builder => builder.AddTimeout(TimeSpan.FromSeconds(15)));

        return services;
    }
}