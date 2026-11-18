using System.Globalization;
using System.Net.Sockets;
using Chat.Core;

namespace Chat.Client;

public static class Program
{
    /// <summary>Usage: Chat.Client [host] [port]. Defaults: localhost, 12000.</summary>
    public static async Task<int> Main(string[] args)
    {
        string host = args.Length > 0 ? args[0] : "localhost";
        int port = ChatProtocol.DefaultPort;
        if (args.Length > 1 && (!int.TryParse(args[1], NumberStyles.None, CultureInfo.InvariantCulture, out port) || port is < 1 or > 65535))
        {
            Console.Error.WriteLine("Usage: Chat.Client [host] [port]");
            return 1;
        }

        ChatClient client;
        try
        {
            Console.WriteLine($"Connecting to {host}:{port}...");
            client = await ChatClient.ConnectAsync(host, port);
        }
        catch (SocketException ex)
        {
            Console.Error.WriteLine($"Could not connect: {ex.Message}. Is Chat.Server running?");
            return 1;
        }

        await using (client)
        {
            // Messages from other people can arrive at any moment, so they are received in the background
            // while the main flow waits for what the user types.
            Task receiving = client.ReceiveAllAsync(Console.WriteLine);

            while (!receiving.IsCompleted && Console.ReadLine() is string line)
            {
                await client.SendAsync(line);
                if (line.Trim().Equals(ChatProtocol.QuitCommand, StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }
            }

            // Wait (briefly) for the server's goodbye, then close.
            await Task.WhenAny(receiving, Task.Delay(TimeSpan.FromSeconds(2)));
        }

        Console.WriteLine("Disconnected.");
        return 0;
    }
}
