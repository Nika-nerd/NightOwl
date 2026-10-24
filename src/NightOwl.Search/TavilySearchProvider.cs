using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using NightOwl.Core;

namespace NightOwl.Search;

public sealed class TavilyOptions
{
    public string ApiKey { get; set; } = "";
}

public sealed class TavilySearchProvider(HttpClient http, IOptions<TavilyOptions> options) : ISearchProvider
{
    public string Name => "Tavily";

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(SearchRequest request, CancellationToken ct = default)
    {
        var body = new TavilyRequest
        {
            Query = request.Query,
            MaxResults = request.MaxResults,
            Topic = request.NewsOnly ? "news" : "general",
            IncludeDomains = request.IncludeDomains?.ToList()
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, "search")
        {
            Content = JsonContent.Create(body)
        };
        message.Headers.Authorization = new("Bearer", options.Value.ApiKey);

        using var response = await http.SendAsync(message, ct);
        response.EnsureSuccessStatusCode();

        var data = await response.Content.ReadFromJsonAsync<TavilyResponse>(ct);

        return data?.Results?
            .Select(r => new SearchResult(
                r.Title ?? "",
                r.Url ?? "",
                r.Content ?? "",
                DateTimeOffset.TryParse(r.PublishedDate, out var date) ? date : null,
                Name))
            .Where(r => r.Url.Length > 0)
            .ToList() ?? [];
    }

    private sealed class TavilyRequest
    {
        [JsonPropertyName("query")] public string Query { get; set; } = "";
        [JsonPropertyName("max_results")] public int MaxResults { get; set; }
        [JsonPropertyName("topic")] public string Topic { get; set; } = "general";
        [JsonPropertyName("include_domains"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public List<string>? IncludeDomains { get; set; }
    }

    private sealed class TavilyResponse
    {
        [JsonPropertyName("results")] public List<TavilyItem>? Results { get; set; }
    }

    private sealed class TavilyItem
    {
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("url")] public string? Url { get; set; }
        [JsonPropertyName("content")] public string? Content { get; set; }
        [JsonPropertyName("published_date")] public string? PublishedDate { get; set; }
    }
}