using Microsoft.Extensions.DependencyInjection;

namespace NightOwl.Search;

public static class ServiceCollectionExtensions
{
    // Регистрирует конкретный TavilySearchProvider. Привязка к ISearchProvider делается в AddNightOwl.
    public static IServiceCollection AddNightOwlSearch(this IServiceCollection services, Action<TavilyOptions> configureTavily)
    {
        services.Configure(configureTavily);

        services.AddHttpClient<TavilySearchProvider>(c =>
        {
            c.BaseAddress = new Uri("https://api.tavily.com/");
            c.Timeout = TimeSpan.FromSeconds(60);
        })
        .AddStandardResilienceHandler();

        return services;
    }
}
