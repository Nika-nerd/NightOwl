using System.ClientModel;
using Microsoft.Extensions.AI;

namespace NightOwl.AI;

public sealed class ThrottlingChatClient(IChatClient inner, TimeSpan minInterval, int maxRetries = 2)
    : DelegatingChatClient(inner)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTime _lastStartUtc = DateTime.MinValue;

    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var list = messages as IReadOnlyCollection<ChatMessage> ?? messages.ToList();

        for (var attempt = 0; ; attempt++)
        {
            await WaitForTurnAsync(cancellationToken);

            try
            {
                return await base.GetResponseAsync(list, options, cancellationToken);
            }
            catch (ClientResultException ex) when (IsRetryable(ex.Status) && attempt < maxRetries)
            {
                await Task.Delay(GetDelay(ex, attempt), cancellationToken);
            }
        }
    }

    public static bool IsRetryable(int status) => status is 429 or 503;

    private static TimeSpan GetDelay(ClientResultException ex, int attempt)
    {
        // Уважаем заголовок Retry-After, если сервер его прислал (но не дольше 60 с)
        if (ex.GetRawResponse()?.Headers.TryGetValue("Retry-After", out var value) == true
            && int.TryParse(value, out var seconds))
            return TimeSpan.FromSeconds(Math.Clamp(seconds, 1, 60));

        return ex.Status == 429
            ? TimeSpan.FromSeconds(10 * (attempt + 1))
            : TimeSpan.FromSeconds(3 * (attempt + 1));
    }

    private async Task WaitForTurnAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var wait = _lastStartUtc + minInterval - DateTime.UtcNow;
            if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);
            _lastStartUtc = DateTime.UtcNow;
        }
        finally
        {
            _gate.Release();
        }
    }

      private static bool IsDailyQuota(ClientResultException ex)
    {
        try
        {
            var body = ex.GetRawResponse()?.Content?.ToString() ?? "";
            return body.Contains("PerDay", StringComparison.OrdinalIgnoreCase)
                || body.Contains("per-day", StringComparison.OrdinalIgnoreCase)
                || body.Contains("per_day", StringComparison.OrdinalIgnoreCase)
                || body.Contains("per day", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}