using Microsoft.Extensions.DependencyInjection;
using Polly;

namespace NightOwl.Extraction;

public static class ServiceCollectionExtensions
{
    // Регистрирует конкретный ContentExtractor. Привязка к IContentExtractor делается в AddNightOwl.
    public static IServiceCollection AddNightOwlExtraction(this IServiceCollection services)
    {
        services.AddHttpClient<ContentExtractor>(c =>
        {
            c.Timeout = TimeSpan.FromSeconds(30);
            c.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (compatible; NightOwlBot/0.1; +https://github.com/Nika-nerd/NightOwl)");
            c.DefaultRequestHeaders.AcceptLanguage.ParseAdd("ru,en;q=0.8");
        })
        .AddResilienceHandler("extraction", builder => builder.AddTimeout(TimeSpan.FromSeconds(15)));

        return services;
    }
}