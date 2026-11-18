using System.Net;
using Chat.Core;
using Chat.Core.Tests;
using Microsoft.Data.Sqlite;

namespace Chat.Storage.Sqlite.Tests;

/// <summary>The SQLite store must pass every contract test the in-memory store passes.</summary>
public sealed class SqliteMessageStoreTests : MessageStoreContract, IDisposable
{
    private readonly List<SqliteMessageStore> _stores = [];

    protected override IMessageStore CreateStore()
    {
        SqliteMessageStore store = SqliteMessageStore.InMemory();
        _stores.Add(store);
        return store;
    }

    public void Dispose() => _stores.ForEach(s => s.Dispose());

    [Theory]
    [InlineData("100%", @"100\%")]
    [InlineData("a_b", @"a\_b")]
    [InlineData(@"c:\games", @"c:\\games")]
    [InlineData("plain", "plain")]
    public void EscapeLike_EscapesTheWildcards(string text, string expected)
    {
        Assert.Equal(expected, SqliteMessageStore.EscapeLike(text));
    }
}

/// <summary>Tests that need a real database file on disk.</summary>
public sealed class SqliteFileTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("chat-sqlite-tests").FullName;

    private string DatabasePath => Path.Combine(_directory, "chat.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void Messages_SurviveClosingAndReopeningTheDatabase()
    {
        using (SqliteMessageStore store = SqliteMessageStore.OpenFile(DatabasePath))
        {
            store.Add(new ChatMessage(DateTimeOffset.UtcNow, "ana", "see you tomorrow"));
        }

        using SqliteMessageStore reopened = SqliteMessageStore.OpenFile(DatabasePath);

        Assert.Equal("ana: see you tomorrow", Assert.Single(reopened.Recent(10)).ToString());
    }

    [Fact]
    public void ADatabaseFromANewerVersion_IsRefused()
    {
        using (var connection = new SqliteConnection($"Data Source={DatabasePath};Pooling=False"))
        {
            connection.Open();
            using SqliteCommand command = connection.CreateCommand();
            command.CommandText = $"PRAGMA user_version = {SqliteMessageStore.SchemaVersion + 1};";
            command.ExecuteNonQuery();
        }

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => SqliteMessageStore.OpenFile(DatabasePath));
        Assert.Contains("schema version", error.Message);
    }

    [Fact]
    public void TooLongMessages_AreRejectedByTheDatabase()
    {
        using SqliteMessageStore store = SqliteMessageStore.OpenFile(DatabasePath);

        // The server never sends them (ChatProtocol.Sanitize cuts them), but the CHECK constraint protects the table anyway.
        Assert.Throws<SqliteException>(() => store.Add(new ChatMessage(DateTimeOffset.UtcNow, "ana", new string('x', 501))));
    }

    [Fact]
    public async Task Server_RemembersTheChatAfterARestart()
    {
        await RunServerAsync(async port =>
        {
            await using ChatClient ana = await ChatClient.ConnectAsync("127.0.0.1", port);
            await ana.ReceiveAsync();
            await ana.SendAsync("ana");
            await ana.SendAsync("message before the restart");
            await ana.SendAsync(ChatProtocol.QuitCommand);
            await ReadUntil(ana, "Bye!");
        });

        // A brand-new server process would do exactly this: open the same file.
        await RunServerAsync(async port =>
        {
            await using ChatClient ben = await ChatClient.ConnectAsync("127.0.0.1", port);
            await ben.ReceiveAsync();
            await ben.SendAsync("ben");
            await ReadUntil(ben, "ana: message before the restart");
        });
    }

    private async Task RunServerAsync(Func<int, Task> session)
    {
        using var store = SqliteMessageStore.OpenFile(DatabasePath);
        using var stop = new CancellationTokenSource();
        await using var server = new ChatServer(IPAddress.Loopback, 0, room: new ChatRoom(store));
        server.Start();
        Task running = server.RunAsync(stop.Token);

        await session(server.Port);

        await stop.CancelAsync();
        await running.WaitAsync(TimeSpan.FromSeconds(10));
    }

    private static async Task ReadUntil(ChatClient client, string ending)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (await client.ReceiveAsync(timeout.Token) is string line)
        {
            if (line.EndsWith(ending, StringComparison.Ordinal))
            {
                return;
            }
        }

        throw new IOException($"The connection closed before \"{ending}\" arrived.");
    }
}
