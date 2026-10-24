using System.Text.Json;
using Microsoft.Extensions.AI;

namespace NightOwl.AI;

internal static class LlmJson
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static async Task<T> AskAsync<T>(
        IChatClient client, string systemPrompt, string userPrompt, CancellationToken ct)
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, systemPrompt),
            new(ChatRole.User, userPrompt)
        };

        var response = await client.GetResponseAsync(messages, new ChatOptions { Temperature = 0.1f }, ct);

        var json = ExtractJson(response.Text);
        return JsonSerializer.Deserialize<T>(json, JsonOptions)
               ?? throw new InvalidOperationException("LLM вернул пустой JSON");
    }


     public static async Task<string> AskTextAsync(
        IChatClient client, string systemPrompt, string userPrompt, CancellationToken ct)
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, systemPrompt),
            new(ChatRole.User, userPrompt)
        };

        var response = await client.GetResponseAsync(messages, new ChatOptions { Temperature = 0.2f }, ct);
        return response.Text.Trim();
    }

    private static string ExtractJson(string text)
    {
        var start = text.IndexOfAny(new[] { '{', '[' });
        var end = text.LastIndexOfAny(new[] { '}', ']' });

        if (start < 0 || end <= start)
            throw new InvalidOperationException($"LLM не вернул JSON: {text}");

        return text[start..(end + 1)];
    }
}