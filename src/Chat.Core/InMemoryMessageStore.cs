namespace Chat.Core;

/// <summary>
/// Keeps the last messages in memory. When it is full, the oldest message is dropped.
/// Several connections add messages at the same time, so every access is protected by a lock.
/// </summary>
public sealed class InMemoryMessageStore : IMessageStore
{
    private readonly Queue<ChatMessage> _messages = new();
    private readonly Lock _gate = new();

    public InMemoryMessageStore(int capacity = 1000)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        Capacity = capacity;
    }

    public int Capacity { get; }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _messages.Count;
            }
        }
    }

    public void Add(ChatMessage message)
    {
        lock (_gate)
        {
            if (_messages.Count == Capacity)
            {
                _messages.Dequeue();
            }

            _messages.Enqueue(message);
        }
    }

    public IReadOnlyList<ChatMessage> Recent(int count)
    {
        lock (_gate)
        {
            return [.. _messages.Skip(Math.Max(0, _messages.Count - count))];
        }
    }

    public IReadOnlyList<ChatMessage> Search(string text, int limit)
    {
        lock (_gate)
        {
            List<ChatMessage> matches = [.. _messages.Where(m => !m.IsNotice && m.Text.Contains(text, StringComparison.OrdinalIgnoreCase))];
            return matches[Math.Max(0, matches.Count - limit)..];
        }
    }

    public IReadOnlyList<AuthorStats> MessagesPerAuthor(int limit)
    {
        lock (_gate)
        {
            return
            [
                .. _messages
                    .Where(m => !m.IsNotice)
                    .GroupBy(m => m.Author!, StringComparer.OrdinalIgnoreCase)
                    .Select(g => new AuthorStats(g.Key, g.Count()))
                    .OrderByDescending(s => s.Messages)
                    .ThenBy(s => s.Author, StringComparer.OrdinalIgnoreCase)
                    .Take(limit),
            ];
        }
    }
}
