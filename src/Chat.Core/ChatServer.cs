using System.Net;
using System.Net.Sockets;

namespace Chat.Core;

/// <summary>
/// Group chat server. It accepts any number of clients; each one is served by its own asynchronous task,
/// so a slow client never blocks the others (the async version of "one thread per client").
/// </summary>
public sealed class ChatServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly Action<string> _log;
    private readonly List<Task> _clients = [];
    private readonly Lock _clientsGate = new();

    /// <param name="port">0 lets the operating system choose a free port (used by the tests).</param>
    /// <param name="log">Where server events are written; the console app passes Console.WriteLine.</param>
    public ChatServer(IPAddress address, int port, Action<string>? log = null, ChatRoom? room = null)
    {
        _listener = new TcpListener(address, port);
        _log = log ?? (_ => { });
        Room = room ?? new ChatRoom();
    }

    public ChatRoom Room { get; }

    /// <summary>The port actually in use (useful when the server was created with port 0).</summary>
    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    /// <summary>Starts listening. Call before <see cref="RunAsync"/> so that <see cref="Port"/> is known.</summary>
    public void Start()
    {
        _listener.Start();
        _log($"Server listening on port {Port}. Press Ctrl+C to stop.");
    }

    /// <summary>Accepts clients until <paramref name="cancellationToken"/> is cancelled.</summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                TcpClient client = await _listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                _log($"Connection from {client.Client.RemoteEndPoint}.");
                Task task = ServeClientAsync(client, cancellationToken);
                lock (_clientsGate)
                {
                    _clients.RemoveAll(t => t.IsCompleted);
                    _clients.Add(task);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
        finally
        {
            _listener.Stop();
            Task[] running;
            lock (_clientsGate)
            {
                running = [.. _clients];
            }

            await Task.WhenAll(running).ConfigureAwait(false);
            _log("Server stopped.");
        }
    }

    /// <summary>Stops listening. Cancel the token given to <see cref="RunAsync"/> to disconnect the clients.</summary>
    public ValueTask DisposeAsync()
    {
        _listener.Stop();
        _listener.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task ServeClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using (client)
        await using (var connection = new LineConnection(client.GetStream()))
        {
            TcpMember? member = null;
            try
            {
                member = await RegisterAsync(connection, cancellationToken).ConfigureAwait(false);
                if (member is null)
                {
                    return; // The client left before choosing a nickname.
                }

                await ChatLoopAsync(connection, member, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
            {
                // Connection lost or server stopping: handled below like a normal goodbye.
            }
            finally
            {
                if (member is not null && Room.Leave(member))
                {
                    _log($"{member.Nickname} disconnected.");
                    await Room.AnnounceAsync($"{member.Nickname} left the chat", except: member, CancellationToken.None).ConfigureAwait(false);
                }
            }
        }
    }

    /// <summary>Asks for a nickname until the client sends a valid, free one.</summary>
    private async Task<TcpMember?> RegisterAsync(LineConnection connection, CancellationToken cancellationToken)
    {
        await connection.WriteLineAsync(ChatProtocol.NicknamePrompt, cancellationToken).ConfigureAwait(false);
        while (true)
        {
            string? nickname = await connection.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (nickname is null)
            {
                return null;
            }

            var member = new TcpMember(nickname.Trim(), connection);
            if (Room.TryJoin(member, out string? error))
            {
                _log($"{member.Nickname} joined.");
                await SendWelcomeAsync(member, cancellationToken).ConfigureAwait(false);
                await Room.AnnounceAsync($"{member.Nickname} joined the chat", except: member, cancellationToken).ConfigureAwait(false);
                return member;
            }

            await connection.WriteLineAsync($"{error} Try another one:", cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>New members see who is online and the recent messages, like opening a group in a messaging app.</summary>
    private async Task SendWelcomeAsync(TcpMember member, CancellationToken cancellationToken)
    {
        await member.SendAsync($"Hi {member.Nickname}! Online: {string.Join(", ", Room.Nicknames)}.", cancellationToken).ConfigureAwait(false);
        await member.SendAsync(ChatProtocol.HelpText, cancellationToken).ConfigureAwait(false);
        await SendHistoryAsync(member, cancellationToken).ConfigureAwait(false);
    }

    private Task SendHistoryAsync(TcpMember member, CancellationToken cancellationToken) =>
        SendListAsync(member, Room.Store.Recent(ChatProtocol.HistorySize), count => $"last {Messages(count)}", "(no messages yet)", cancellationToken);

    private Task SendSearchAsync(TcpMember member, string text, CancellationToken cancellationToken) =>
        text.Length == 0
            ? member.SendAsync($"Usage: {ChatProtocol.SearchCommand} <text>", cancellationToken)
            : SendListAsync(member, Room.Store.Search(text, ChatProtocol.HistorySize), count => $"{Messages(count)} containing \"{text}\"", $"No messages contain \"{text}\".", cancellationToken);

    private async Task SendStatsAsync(TcpMember member, CancellationToken cancellationToken)
    {
        IReadOnlyList<AuthorStats> stats = Room.Store.MessagesPerAuthor(limit: 10);
        await member.SendAsync(
            stats.Count == 0
                ? "Nobody has written anything yet."
                : $"Most active: {string.Join(", ", stats.Select(s => $"{s.Author} ({s.Messages})"))}",
            cancellationToken).ConfigureAwait(false);
    }

    private static string Messages(int count) => count == 1 ? "1 message" : $"{count} messages";

    /// <summary>Sends a header with the count, the messages with their time, and a closing line.</summary>
    private static async Task SendListAsync(
        TcpMember member, IReadOnlyList<ChatMessage> messages, Func<int, string> header, string emptyText, CancellationToken cancellationToken)
    {
        if (messages.Count == 0)
        {
            await member.SendAsync(emptyText, cancellationToken).ConfigureAwait(false);
            return;
        }

        await member.SendAsync($"--- {header(messages.Count)} ---", cancellationToken).ConfigureAwait(false);
        foreach (ChatMessage message in messages)
        {
            await member.SendAsync($"[{message.SentAt.ToLocalTime():HH:mm}] {message}", cancellationToken).ConfigureAwait(false);
        }

        await member.SendAsync("---", cancellationToken).ConfigureAwait(false);
    }

    private async Task ChatLoopAsync(LineConnection connection, TcpMember member, CancellationToken cancellationToken)
    {
        while (await connection.ReadLineAsync(cancellationToken).ConfigureAwait(false) is string line)
        {
            string text = line.Trim();
            switch (text.ToLowerInvariant())
            {
                case "":
                    break;
                case ChatProtocol.QuitCommand:
                    await member.SendAsync("Bye!", cancellationToken).ConfigureAwait(false);
                    return;
                case ChatProtocol.WhoCommand:
                    await member.SendAsync($"Online: {string.Join(", ", Room.Nicknames)}", cancellationToken).ConfigureAwait(false);
                    break;
                case ChatProtocol.HistoryCommand:
                    await SendHistoryAsync(member, cancellationToken).ConfigureAwait(false);
                    break;
                case ChatProtocol.HelpCommand:
                    await member.SendAsync(ChatProtocol.HelpText, cancellationToken).ConfigureAwait(false);
                    break;
                case ChatProtocol.StatsCommand:
                    await SendStatsAsync(member, cancellationToken).ConfigureAwait(false);
                    break;
                case var command when command == ChatProtocol.SearchCommand || command.StartsWith(ChatProtocol.SearchCommand + " ", StringComparison.Ordinal):
                    await SendSearchAsync(member, text[ChatProtocol.SearchCommand.Length..].Trim(), cancellationToken).ConfigureAwait(false);
                    break;
                default:
                    _log($"{member.Nickname}: {text}");
                    await Room.SendMessageAsync(member, text, cancellationToken).ConfigureAwait(false);
                    break;
            }
        }
    }

    private sealed class TcpMember(string nickname, LineConnection connection) : IChatMember
    {
        public string Nickname { get; } = nickname;

        public Task SendAsync(string line, CancellationToken cancellationToken = default) =>
            connection.WriteLineAsync(line, cancellationToken);
    }
}
