using System.Globalization;
using System.Net;
using Chat.Core;

namespace Chat.Server;

public static class Program
{
    /// <summary>Usage: Chat.Server [port]. The default port is 12000.</summary>
    public static async Task<int> Main(string[] args)
    {
        int port = ChatProtocol.DefaultPort;
        if (args.Length > 0 && (!int.TryParse(args[0], NumberStyles.None, CultureInfo.InvariantCulture, out port) || port is < 1 or > 65535))
        {
            Console.Error.WriteLine("Usage: Chat.Server [port]   (port between 1 and 65535)");
            return 1;
        }

        using var shutdown = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true; // Stop gracefully instead of killing the process.
            shutdown.Cancel();
        };

        await using var server = new ChatServer(IPAddress.Any, port, message => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}"));
        try
        {
            server.Start();
        }
        catch (System.Net.Sockets.SocketException ex)
        {
            Console.Error.WriteLine($"Cannot listen on port {port}: {ex.Message}");
            return 1;
        }

        await server.RunAsync(shutdown.Token);
        return 0;
    }
}
