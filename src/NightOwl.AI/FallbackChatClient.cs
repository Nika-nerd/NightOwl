using System.ClientModel;
using Microsoft.Extensions.AI;

namespace NightOwl.AI;

// Если основная модель недоступна (429/503) даже после повторов, пробуем следующую
public sealed class FallbackChatClient(IChatClient primary, IReadOnlyList<IChatClient> fallbacks)
    : DelegatingChatClient(primary)
{
    public override async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var list = messages as IReadOnlyCollection<ChatMessage> ?? messages.ToList();
        ClientResultException? last = null;

        try
        {
            return await base.GetResponseAsync(list, options, cancellationToken);
        }
        catch (ClientResultException ex) when (ThrottlingChatClient.IsRetryable(ex.Status))
        {
            last = ex;
        }

        foreach (var fallback in fallbacks)
        {
            try
            {
                return await fallback.GetResponseAsync(list, options, cancellationToken);
            }
            catch (ClientResultException ex) when (ThrottlingChatClient.IsRetryable(ex.Status))
            {
                last = ex;
            }
        }

        throw last!;
    }
}