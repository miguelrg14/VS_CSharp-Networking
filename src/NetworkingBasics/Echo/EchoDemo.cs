using System.Net;
using System.Net.Sockets;

namespace NetworkingBasics.Echo;

/// <summary>Runs the echo server in the background and a client in the foreground, in the same process.</summary>
public static class EchoDemo
{
    public static void Run()
    {
        using var server = new EchoServer();
        server.Start();
        Console.WriteLine($"Echo server listening on 127.0.0.1:{server.Port} (in a background thread).");

        using var client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        client.Connect(new IPEndPoint(IPAddress.Loopback, server.Port));
        Console.WriteLine($"Client connected. Type messages; \"{EchoServer.HistoryCommand}\" shows everything sent so far, \"{EchoServer.ExitCommand}\" closes the connection.");

        var reader = new SocketLineReader(client);
        while (Console.ReadLine() is string message)
        {
            SocketLineReader.SendLine(client, message);
            if (message.Equals(EchoServer.ExitCommand, StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            Console.WriteLine($"Server replied: {reader.ReadLine()}");
        }

        Console.WriteLine("Connection closed.");
    }
}
