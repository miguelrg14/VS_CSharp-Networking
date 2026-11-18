using Chat.Core;

namespace Chat.Core.Tests;

/// <summary>
/// The behaviour every <see cref="IMessageStore"/> must have. The in-memory store and the SQLite store
/// both inherit these tests, so they are guaranteed to work the same way.
/// </summary>
public abstract class MessageStoreContract
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    protected abstract IMessageStore CreateStore();

    protected static ChatMessage Message(int minute, string? author, string text) => new(Start.AddMinutes(minute), author, text);

    [Fact]
    public void Recent_ReturnsTheLastMessagesOldestFirst()
    {
        IMessageStore store = CreateStore();
        for (int i = 1; i <= 5; i++)
        {
            store.Add(Message(i, "ana", $"message {i}"));
        }

        Assert.Equal(new[] { "message 3", "message 4", "message 5" }, store.Recent(3).Select(m => m.Text));
        Assert.Equal(5, store.Recent(100).Count);
    }

    [Fact]
    public void Messages_KeepTheirAuthorTextAndTime()
    {
        IMessageStore store = CreateStore();
        ChatMessage original = Message(7, "josé", "¿qué tal? 🎮");
        store.Add(original);
        store.Add(Message(8, null, "ana joined the chat"));

        IReadOnlyList<ChatMessage> recent = store.Recent(2);

        Assert.Equal(original, recent[0]);
        Assert.True(recent[1].IsNotice);
    }

    [Fact]
    public void Search_IgnoresCaseAndNotices()
    {
        IMessageStore store = CreateStore();
        store.Add(Message(1, "ana", "Level 3 is easy"));
        store.Add(Message(2, null, "level notice"));
        store.Add(Message(3, "ben", "no match here"));
        store.Add(Message(4, "ben", "the last LEVEL"));

        Assert.Equal(new[] { "Level 3 is easy", "the last LEVEL" }, store.Search("level", 10).Select(m => m.Text));
        Assert.Equal(new[] { "the last LEVEL" }, store.Search("level", 1).Select(m => m.Text));
    }

    [Theory]
    [InlineData("100%")]
    [InlineData("a_b")]
    [InlineData("it's")]
    [InlineData("'; DROP TABLE messages; --")]
    public void Search_TreatsSpecialCharactersAsText(string text)
    {
        IMessageStore store = CreateStore();
        store.Add(Message(1, "ana", $"before {text} after"));
        store.Add(Message(2, "ana", "100 percent, ab, its"));

        Assert.Single(store.Search(text, 10));
        Assert.Equal(2, store.Recent(10).Count); // Nothing was dropped.
    }

    [Fact]
    public void MessagesPerAuthor_CountsAndSortsTheAuthors()
    {
        IMessageStore store = CreateStore();
        store.Add(Message(1, "ana", "1"));
        store.Add(Message(2, "ben", "2"));
        store.Add(Message(3, "ben", "3"));
        store.Add(Message(4, null, "notice"));
        store.Add(Message(5, "cris", "4"));

        Assert.Equal(
            new[] { new AuthorStats("ben", 2), new AuthorStats("ana", 1), new AuthorStats("cris", 1) },
            store.MessagesPerAuthor(10));
        Assert.Single(store.MessagesPerAuthor(1));
    }

    [Fact]
    public void Store_IsSafeToUseFromManyThreads()
    {
        IMessageStore store = CreateStore();

        Parallel.For(0, 500, i => store.Add(Message(i, $"user{i % 5}", $"message {i}")));

        Assert.Equal(500, store.Recent(1000).Count);
        Assert.Equal(500, store.MessagesPerAuthor(10).Sum(s => s.Messages));
    }
}
