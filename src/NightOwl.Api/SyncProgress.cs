namespace NightOwl.Api;

// В отличие от Progress<T>, вызывает обработчик сразу и в том же порядке
public sealed class SyncProgress<T>(Action<T> handler) : IProgress<T>
{
    public void Report(T value) => handler(value);
}