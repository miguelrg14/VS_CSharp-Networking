using System.Net;
using System.Net.Sockets;
using System.Text;

namespace NetworkingBasics.Echo;

/// <summary>
/// The course's echo server, written with the low-level Socket API (Bind, Listen, Accept, Receive, Send)
/// and one thread per client. It answers every message in upper case, and "history" returns every
/// message received so far from any client. The chat projects use the higher-level TcpListener instead.
/// </summary>
public sealed class EchoServer : IDisposable
{
    public const string HistoryCommand = "history";
    public const string ExitCommand = "exit";

    private readonly Socket _listener = new(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
    private readonly List<string> _history = [];
    private readonly Lock _historyGate = new();
    private Thread? _acceptThread;

    public int Port => ((IPEndPoint)_listener.LocalEndPoint!).Port;

    /// <summary>Port 0 lets the operating system pick a free port.</summary>
    public void Start(int port = 0)
    {
        _listener.Bind(new IPEndPoint(IPAddress.Loopback, port));
        _listener.Listen(backlog: 10);
        _acceptThread = new Thread(AcceptLoop) { IsBackground = true, Name = "Echo accept loop" };
        _acceptThread.Start();
    }

    public IReadOnlyList<string> History
    {
        get
        {
            lock (_historyGate)
            {
                return [.. _history];
            }
        }
    }

    /// <summary>What the server answers to one message. Kept separate from the socket code so it can be tested.</summary>
    public string Reply(string message)
    {
        // Every client thread shares the same history, so it is only touched while holding the lock.
        lock (_historyGate)
        {
            if (message.Equals(HistoryCommand, StringComparison.OrdinalIgnoreCase))
            {
                return _history.Count == 0 ? "(history is empty)" : string.Join(" | ", _history);
            }

            _history.Add(message);
        }

        return message.ToUpperInvariant();
    }

    public void Dispose()
    {
        _listener.Close(); // Makes Accept throw, which ends the accept loop.
        _acceptThread?.Join();
    }

    private void AcceptLoop()
    {
        try
        {
            while (true)
            {
                Socket client = _listener.Accept(); // Blocks until someone connects.
                new Thread(() => Serve(client)) { IsBackground = true, Name = "Echo client" }.Start();
            }
        }
        catch (Exception ex) when (ex is SocketException or ObjectDisposedException)
        {
            // The listener was closed: stop accepting.
        }
    }

    private void Serve(Socket client)
    {
        using (client)
        {
            var reader = new SocketLineReader(client);
            try
            {
                while (reader.ReadLine() is string message && !message.Equals(ExitCommand, StringComparison.OrdinalIgnoreCase))
                {
                    SocketLineReader.SendLine(client, Reply(message));
                }
            }
            catch (SocketException)
            {
                // The client vanished; nothing else to do.
            }
        }
    }
}

/// <summary>
/// Receive returns whatever bytes have arrived: half a message, or two at once. This class keeps the
/// leftovers in a buffer and hands out complete lines, which is how message boundaries are restored on TCP.
/// </summary>
public sealed class SocketLineReader(Socket socket)
{
    private readonly byte[] _receiveBuffer = new byte[512];
    private readonly List<byte> _pending = [];

    /// <summary>The next line without its newline, or null when the other side closed the connection.</summary>
    public string? ReadLine()
    {
        while (true)
        {
            int newline = _pending.IndexOf((byte)'\n');
            if (newline >= 0)
            {
                string line = Encoding.UTF8.GetString([.. _pending.Take(newline)]).TrimEnd('\r');
                _pending.RemoveRange(0, newline + 1);
                return line;
            }

            int received = socket.Receive(_receiveBuffer);
            if (received == 0)
            {
                return null; // 0 bytes means the connection was closed (not "an empty message").
            }

            _pending.AddRange(_receiveBuffer.AsSpan(0, received).ToArray());
        }
    }

    public static void SendLine(Socket socket, string line)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(line + "\n");
        int sent = 0;
        while (sent < bytes.Length)
        {
            sent += socket.Send(bytes, sent, bytes.Length - sent, SocketFlags.None); // Send may not send everything at once.
        }
    }
}
