namespace Chat.Core;

/// <summary>
/// The last messages of the chat, shared by every client connection. Several connections add messages
/// at the same time, so every access is protected by a lock. When it is full, the oldest message is dropped.
/// </summary>
public sealed class ChatHistory
{
    private readonly Queue<string> _messages = new();
    private readonly Lock _gate = new();

    public ChatHistory(int capacity = 50)
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

    public void Add(string message)
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

    /// <summary>A copy of the messages, oldest first. Callers can read it without holding the lock.</summary>
    public IReadOnlyList<string> Snapshot()
    {
        lock (_gate)
        {
            return [.. _messages];
        }
    }
}
