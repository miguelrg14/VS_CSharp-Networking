namespace Chat.Core;

/// <summary>Anything that can receive chat lines: a TCP connection in the app, a fake in the tests.</summary>
public interface IChatMember
{
    string Nickname { get; }

    Task SendAsync(string line, CancellationToken cancellationToken = default);
}

/// <summary>
/// Who is in the chat and what has been said. Thread-safe: every client connection runs concurrently,
/// so members are added, removed and listed under a lock, and messages are sent outside of it
/// (sending can be slow, and it must not block everybody else).
/// </summary>
public sealed class ChatRoom
{
    private readonly Dictionary<string, IChatMember> _members = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _gate = new();

    public ChatRoom(ChatHistory? history = null)
    {
        History = history ?? new ChatHistory();
    }

    public ChatHistory History { get; }

    public IReadOnlyList<string> Nicknames
    {
        get
        {
            lock (_gate)
            {
                return [.. _members.Keys.Order(StringComparer.OrdinalIgnoreCase)];
            }
        }
    }

    /// <summary>Adds a member if the nickname is valid and nobody else is using it (case-insensitive).</summary>
    public bool TryJoin(IChatMember member, out string? error)
    {
        if (!ChatProtocol.IsValidNickname(member.Nickname, out error))
        {
            return false;
        }

        lock (_gate)
        {
            if (!_members.TryAdd(member.Nickname, member))
            {
                error = $"The nickname '{member.Nickname}' is already in use.";
                return false;
            }
        }

        return true;
    }

    public bool Leave(IChatMember member)
    {
        lock (_gate)
        {
            // Only remove the entry if it really belongs to this member.
            return _members.TryGetValue(member.Nickname, out IChatMember? current)
                && ReferenceEquals(current, member)
                && _members.Remove(member.Nickname);
        }
    }

    /// <summary>Records a message from <paramref name="sender"/> and delivers it to everyone else.</summary>
    public Task SendMessageAsync(IChatMember sender, string text, CancellationToken cancellationToken = default)
    {
        string line = $"{sender.Nickname}: {ChatProtocol.Sanitize(text)}";
        History.Add(line);
        return BroadcastAsync(line, except: sender, cancellationToken);
    }

    /// <summary>A server notice (someone joined or left). It goes to the history too.</summary>
    public Task AnnounceAsync(string notice, IChatMember? except = null, CancellationToken cancellationToken = default)
    {
        string line = $"* {notice} *";
        History.Add(line);
        return BroadcastAsync(line, except, cancellationToken);
    }

    private async Task BroadcastAsync(string line, IChatMember? except, CancellationToken cancellationToken)
    {
        IChatMember[] recipients;
        lock (_gate)
        {
            recipients = [.. _members.Values.Where(m => !ReferenceEquals(m, except))];
        }

        // One slow or broken connection must not stop the others from getting the message.
        Task[] sends = [.. recipients.Select(m => SendSafelyAsync(m, line, cancellationToken))];
        await Task.WhenAll(sends).ConfigureAwait(false);
    }

    private static async Task SendSafelyAsync(IChatMember member, string line, CancellationToken cancellationToken)
    {
        try
        {
            await member.SendAsync(line, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            // The member disconnected while we were sending; its own connection loop will remove it.
        }
    }
}
