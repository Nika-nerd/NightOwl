using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using NightOwl.Core;
using SmartReader;

namespace NightOwl.Extraction;

public sealed partial class ContentExtractor : IContentExtractor
{
    private const int MaxHtmlBytes = 2 * 1024 * 1024; // 2 МБ
    private const int MaxTextChars = 12_000;          // лимит текста на документ для LLM
    private const int MinSnippetFallbackChars = 200;

    private readonly HttpClient _http;
    private readonly ISourceClassifier _classifier;
    private readonly ILogger<ContentExtractor> _logger;

    static ContentExtractor()
    {
        // Нужно для старых русскоязычных сайтов в windows-1251
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public ContentExtractor(HttpClient http, ISourceClassifier classifier, ILogger<ContentExtractor> logger)
    {
        _http = http;
        _classifier = classifier;
        _logger = logger;
    }

    public async Task<SourceDocument?> ExtractAsync(SearchResult result, CancellationToken ct = default)
    {
        var tier = _classifier.Classify(result.Url);
        var domain = GetDomain(result.Url);

        try
        {
            using var response = await _http.GetAsync(result.Url, HttpCompletionOption.ResponseHeadersRead, ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("{Url}: HTTP {Status}", result.Url, (int)response.StatusCode);
                return Fallback(result, domain, tier);
            }

            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (mediaType is not null && !mediaType.Contains("html", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation("{Url}: пропущен тип {Type}", result.Url, mediaType);
                return Fallback(result, domain, tier);
            }

            var html = await ReadLimitedAsync(response, ct);
            var article = new Reader(result.Url, html).GetArticle();

            if (!article.IsReadable || string.IsNullOrWhiteSpace(article.TextContent))
                return Fallback(result, domain, tier);

            var text = Normalize(article.TextContent);
            DateTimeOffset? published = ToOffset(article.PublicationDate) ?? result.PublishedAt;

            return new SourceDocument(result, domain, tier, text, published);
        }

         catch (OperationCanceledException) when (ct.IsCancellationRequested)
    {
        throw; // пользователь сам отменил операцию
    }
    catch (Exception ex)
    {
        // Любой сбой одной страницы (таймаут, DNS, TLS, парсинг) не должен ронять весь запрос
        _logger.LogWarning("{Url}: {Type}: {Message}", result.Url, ex.GetType().Name, ex.Message);
        return Fallback(result, domain, tier);
    }
    
    }

    // Если страницу не скачали (403, PDF, JS-сайт), используем сниппет поисковика, если он достаточно длинный
    private static SourceDocument? Fallback(SearchResult r, string domain, SourceTier tier)
        => r.Snippet.Length >= MinSnippetFallbackChars
            ? new SourceDocument(r, domain, tier, r.Snippet, r.PublishedAt)
            : null;

    private static async Task<string> ReadLimitedAsync(HttpResponseMessage response, CancellationToken ct)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var ms = new MemoryStream();
        var buffer = new byte[16 * 1024];
        int read;
        while ((read = await stream.ReadAsync(buffer, ct)) > 0)
        {
            ms.Write(buffer, 0, read);
            if (ms.Length >= MaxHtmlBytes) break;
        }

        Encoding encoding = Encoding.UTF8;
        var charset = response.Content.Headers.ContentType?.CharSet?.Trim('"');
        if (!string.IsNullOrEmpty(charset))
        {
            try { encoding = Encoding.GetEncoding(charset); } catch (ArgumentException) { }
        }

        return encoding.GetString(ms.ToArray());
    }

    private static string Normalize(string text)
    {
        text = HorizontalSpace().Replace(text, " ");
        text = ManyNewLines().Replace(text, "\n\n").Trim();
        return text.Length > MaxTextChars ? text[..MaxTextChars] : text;
    }

    private static DateTimeOffset? ToOffset(DateTime? date)
    {
        if (date is null || date.Value == default) return null;
        var d = date.Value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(date.Value, DateTimeKind.Utc)
            : date.Value;
        return new DateTimeOffset(d);
    }

    private static string GetDomain(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return "";
        var host = uri.Host.ToLowerInvariant();
        return host.StartsWith("www.") ? host[4..] : host;
    }

    [GeneratedRegex(@"[ \t\u00A0]+")] private static partial Regex HorizontalSpace();
    [GeneratedRegex(@"(\s*\n\s*){3,}")] private static partial Regex ManyNewLines();
}
