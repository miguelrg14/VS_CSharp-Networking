namespace Chat.Core;

/// <summary>One entry of the chat: a message from a user, or a notice from the server (no author).</summary>
public sealed record ChatMessage(DateTimeOffset SentAt, string? Author, string Text)
{
    public bool IsNotice => Author is null;

    /// <summary>How the message is shown to the clients.</summary>
    public override string ToString() => IsNotice ? $"* {Text} *" : $"{Author}: {Text}";
}

/// <summary>How many messages one author has written.</summary>
public sealed record AuthorStats(string Author, int Messages);
