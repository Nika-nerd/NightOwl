namespace NightOwl.Credibility;

public static class DomainHelper
{
    private static readonly HashSet<string> SecondLevel =
        new(StringComparer.OrdinalIgnoreCase) { "co", "com", "org", "net", "gov", "edu", "ac" };

    // news.un.org -> un.org, bbc.co.uk -> bbc.co.uk, minzdrav.gov.kz -> minzdrav.gov.kz
    public static string GetRootDomain(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return url;

        var labels = uri.Host.ToLowerInvariant().Split('.');
        if (labels.Length <= 2) return string.Join('.', labels);

        var take = labels[^1].Length == 2 && SecondLevel.Contains(labels[^2]) ? 3 : 2;
        return string.Join('.', labels[^take..]);
    }
}