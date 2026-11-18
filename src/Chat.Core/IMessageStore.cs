namespace Chat.Core;

/// <summary>
/// Where the chat keeps its messages. The server only knows this interface, so the same code works with
/// the in-memory store (lost when the server stops) or the SQLite one (kept on disk between runs).
/// Implementations must be thread-safe: every client connection uses the store at the same time.
/// </summary>
public interface IMessageStore
{
    void Add(ChatMessage message);

    /// <summary>The last <paramref name="count"/> messages, oldest first.</summary>
    IReadOnlyList<ChatMessage> Recent(int count);

    /// <summary>The most recent user messages containing <paramref name="text"/> (case-insensitive), oldest first.</summary>
    IReadOnlyList<ChatMessage> Search(string text, int limit);

    /// <summary>Message count per author, most active first.</summary>
    IReadOnlyList<AuthorStats> MessagesPerAuthor(int limit);
}
