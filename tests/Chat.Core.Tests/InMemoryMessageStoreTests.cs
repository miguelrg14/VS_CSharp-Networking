using Chat.Core;

namespace Chat.Core.Tests;

public class InMemoryMessageStoreTests : MessageStoreContract
{
    protected override IMessageStore CreateStore() => new InMemoryMessageStore();

    [Fact]
    public void WhenFull_TheOldestMessageIsDropped()
    {
        var store = new InMemoryMessageStore(capacity: 3);
        for (int i = 1; i <= 4; i++)
        {
            store.Add(Message(i, "ana", $"{i}"));
        }

        Assert.Equal(new[] { "2", "3", "4" }, store.Recent(10).Select(m => m.Text));
    }
}
