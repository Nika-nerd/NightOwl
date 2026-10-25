using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace NightOwl.Pipeline.Caching;

public static class CacheKey
{
    public static string For(string prefix, string value)
        => $"{prefix}:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))}";

    // «  Сколько   случаев? » и «сколько случаев» дают один и тот же ключ
    public static string NormalizeQuestion(string question)
    {
        var text = Regex.Replace(question.Trim().ToLowerInvariant(), @"\s+", " ");
        return text.TrimEnd('?', '!', '.', ' ');
    }
}