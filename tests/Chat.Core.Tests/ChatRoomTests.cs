using Chat.Core;

namespace Chat.Core.Tests;

public class ChatProtocolTests
{
    [Theory]
    [InlineData("ana", true)]
    [InlineData("Ana_92", true)]
    [InlineData("josé-luis", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("two words", false)]
    [InlineData("abcdefghijklmnopqrstu", false)] // 21 characters
    public void IsValidNickname_AcceptsOnlySimpleNames(string nickname, bool expected)
    {
        Assert.Equal(expected, ChatProtocol.IsValidNickname(nickname, out _));
    }

    [Fact]
    public void Sanitize_RemovesLineBreaksAndCutsLongMessages()
    {
        Assert.Equal("one two", ChatProtocol.Sanitize("one\ntwo\r"));
        Assert.Equal(ChatProtocol.MaxMessageLength, ChatProtocol.Sanitize(new string('x', 2000)).Length);
    }
}

public class ChatRoomTests
{
    private sealed class FakeMember(string nickname) : IChatMember
    {
        public string Nickname { get; } = nickname;

        public List<string> Received { get; } = [];

        public Task SendAsync(string line, CancellationToken cancellationToken = default)
        {
            lock (Received)
            {
                Received.Add(line);
            }

            return Task.CompletedTask;
        }
    }

    private sealed class BrokenMember(string nickname) : IChatMember
    {
        public string Nickname { get; } = nickname;

        public Task SendAsync(string line, CancellationToken cancellationToken = default) =>
            Task.FromException(new IOException("Connection reset."));
    }

    [Fact]
    public void Nicknames_AreUniqueIgnoringCase()
    {
        var room = new ChatRoom();

        Assert.True(room.TryJoin(new FakeMember("Ana"), out _));
        Assert.False(room.TryJoin(new FakeMember("ana"), out string? error));
        Assert.Contains("already in use", error);
        Assert.False(room.TryJoin(new FakeMember("bad name"), out _));
        Assert.Equal(new[] { "Ana" }, room.Nicknames);
    }

    [Fact]
    public async Task Messages_GoToEveryoneExceptTheSender_AndIntoTheHistory()
    {
        var room = new ChatRoom();
        var ana = new FakeMember("ana");
        var ben = new FakeMember("ben");
        var cris = new FakeMember("cris");
        room.TryJoin(ana, out _);
        room.TryJoin(ben, out _);
        room.TryJoin(cris, out _);

        await room.SendMessageAsync(ana, "hello");

        Assert.Empty(ana.Received);
        Assert.Equal(new[] { "ana: hello" }, ben.Received);
        Assert.Equal(new[] { "ana: hello" }, cris.Received);
        Assert.Equal(new[] { "ana: hello" }, room.Store.Recent(10).Select(m => m.ToString()));
    }

    [Fact]
    public async Task OneBrokenConnection_DoesNotStopTheOthers()
    {
        var room = new ChatRoom();
        var ana = new FakeMember("ana");
        var ben = new FakeMember("ben");
        room.TryJoin(ana, out _);
        room.TryJoin(new BrokenMember("ghost"), out _);
        room.TryJoin(ben, out _);

        await room.AnnounceAsync("server restarting soon");

        Assert.Equal(new[] { "* server restarting soon *" }, ana.Received);
        Assert.Equal(new[] { "* server restarting soon *" }, ben.Received);
    }

    [Fact]
    public void Leave_OnlyRemovesTheSameMember()
    {
        var room = new ChatRoom();
        var ana = new FakeMember("ana");
        room.TryJoin(ana, out _);

        Assert.False(room.Leave(new FakeMember("ana"))); // Same name, different connection.
        Assert.True(room.Leave(ana));
        Assert.Empty(room.Nicknames);
    }
}
