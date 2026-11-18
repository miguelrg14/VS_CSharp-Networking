using System.Net;
using Chat.Core;

namespace Chat.Core.Tests;

/// <summary>
/// End-to-end tests: a real server on a free local port and real TCP clients.
/// Every wait has a timeout, so a bug makes a test fail instead of hanging the build.
/// </summary>
public sealed class ChatServerTests : IAsyncLifetime
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly CancellationTokenSource _stop = new();
    private ChatServer _server = null!;
    private Task _running = Task.CompletedTask;

    public Task InitializeAsync()
    {
        _server = new ChatServer(IPAddress.Loopback, port: 0);
        _server.Start();
        _running = _server.RunAsync(_stop.Token);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _stop.CancelAsync();
        await _running.WaitAsync(Timeout);
        await _server.DisposeAsync();
        _stop.Dispose();
    }

    [Fact]
    public async Task Client_ChoosesANickname_AndIsWelcomed()
    {
        await using ChatClient ana = await ChatClient.ConnectAsync("127.0.0.1", _server.Port);

        Assert.Equal(ChatProtocol.NicknamePrompt, await NextLine(ana));
        await ana.SendAsync("ana");

        Assert.Equal("Hi ana! Online: ana.", await NextLine(ana));
    }

    [Fact]
    public async Task TakenNickname_IsRejected_ThenAnotherOneIsAccepted()
    {
        await using ChatClient ana = await Join("ana");
        await using ChatClient second = await ChatClient.ConnectAsync("127.0.0.1", _server.Port);
        await NextLine(second); // Prompt.

        await second.SendAsync("ANA");
        Assert.Contains("already in use", await NextLine(second));

        await second.SendAsync("ben");
        Assert.Equal("Hi ben! Online: ana, ben.", await NextLine(second));
    }

    [Fact]
    public async Task Messages_AreBroadcastToTheOtherClients()
    {
        await using ChatClient ana = await Join("ana");
        await using ChatClient ben = await Join("ben");
        await WaitFor(ana, "* ben joined the chat *");

        await ana.SendAsync("hola, ¿qué tal?");

        await WaitFor(ben, "ana: hola, ¿qué tal?");
    }

    [Fact]
    public async Task NewMembers_ReceiveTheRecentHistory()
    {
        await using (ChatClient ana = await Join("ana"))
        {
            await ana.SendAsync("first message");
            await ana.SendAsync(ChatProtocol.QuitCommand);
            await WaitFor(ana, "Bye!");
        }

        await using ChatClient ben = await Join("ben", skipWelcome: false);

        await WaitFor(ben, "ana: first message");
    }

    [Fact]
    public async Task Disconnecting_IsAnnouncedAndFreesTheNickname()
    {
        await using ChatClient ben = await Join("ben");
        ChatClient ana = await Join("ana");
        await WaitFor(ben, "* ana joined the chat *");

        await ana.DisposeAsync(); // Closing the connection without saying /quit.

        await WaitFor(ben, "* ana left the chat *");
        await using ChatClient anaAgain = await Join("ana");
    }

    [Fact]
    public async Task WhoCommand_ListsTheConnectedUsers()
    {
        await using ChatClient ana = await Join("ana");
        await using ChatClient ben = await Join("ben");

        await ben.SendAsync(ChatProtocol.WhoCommand);

        await WaitFor(ben, "Online: ana, ben");
    }

    /// <summary>Connects, sends the nickname and (by default) reads past the welcome lines.</summary>
    private async Task<ChatClient> Join(string nickname, bool skipWelcome = true)
    {
        ChatClient client = await ChatClient.ConnectAsync("127.0.0.1", _server.Port);
        await NextLine(client);
        await client.SendAsync(nickname);
        if (skipWelcome)
        {
            await WaitFor(client, ChatProtocol.HelpText);
        }

        return client;
    }

    private static async Task<string> NextLine(ChatClient client) =>
        await client.ReceiveAsync().AsTask().WaitAsync(Timeout) ?? throw new IOException("The server closed the connection.");

    /// <summary>Reads lines until <paramref name="expected"/> arrives (other lines, like notices, may come first).</summary>
    private static async Task WaitFor(ChatClient client, string expected)
    {
        var seen = new List<string>();
        using var timeout = new CancellationTokenSource(Timeout);
        while (!timeout.IsCancellationRequested)
        {
            string line = await NextLine(client);
            if (line == expected)
            {
                return;
            }

            seen.Add(line);
        }

        throw new TimeoutException($"Did not receive \"{expected}\". Received: {string.Join(" | ", seen)}");
    }
}
