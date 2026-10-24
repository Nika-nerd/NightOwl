using Microsoft.Extensions.DependencyInjection;
using NightOwl.Core;

namespace NightOwl.Search;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddNightOwlSearch(this IServiceCollection services, Action<TavilyOptions> configureTavily)
    {
        services.Configure(configureTavily);

        services.AddHttpClient<ISearchProvider, TavilySearchProvider>(c =>
        {
            c.BaseAddress = new Uri("https://api.tavily.com/");
            c.Timeout = TimeSpan.FromSeconds(60);
        })
        .AddStandardResilienceHandler(); // ретраи, circuit breaker, таймауты (Microsoft.Extensions.Http.Resilience)

        return services;
    }
}
