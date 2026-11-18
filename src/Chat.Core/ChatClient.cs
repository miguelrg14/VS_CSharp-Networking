using System.Net.Sockets;

namespace Chat.Core;

/// <summary>Client side of the protocol: connect, send lines, and receive lines in the background.</summary>
public sealed class ChatClient : IAsyncDisposable
{
    private readonly TcpClient _tcp;
    private readonly LineConnection _connection;

    private ChatClient(TcpClient tcp)
    {
        _tcp = tcp;
        _connection = new LineConnection(tcp.GetStream());
    }

    public static async Task<ChatClient> ConnectAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        var tcp = new TcpClient();
        try
        {
            await tcp.ConnectAsync(host, port, cancellationToken).ConfigureAwait(false);
            return new ChatClient(tcp);
        }
        catch
        {
            tcp.Dispose();
            throw;
        }
    }

    public Task SendAsync(string line, CancellationToken cancellationToken = default) =>
        _connection.WriteLineAsync(line, cancellationToken);

    /// <summary>The next line from the server, or null when the server closed the connection.</summary>
    public ValueTask<string?> ReceiveAsync(CancellationToken cancellationToken = default) =>
        _connection.ReadLineAsync(cancellationToken);

    /// <summary>Calls <paramref name="onLine"/> for every line until the connection closes.</summary>
    public async Task ReceiveAllAsync(Action<string> onLine, CancellationToken cancellationToken = default)
    {
        try
        {
            while (await ReceiveAsync(cancellationToken).ConfigureAwait(false) is string line)
            {
                onLine(line);
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException)
        {
            // Connection closed: stop receiving.
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync().ConfigureAwait(false);
        _tcp.Dispose();
    }
}
