using System.Globalization;
using System.Net;
using System.Net.Sockets;
using Chat.Core;
using Chat.Storage.Sqlite;

namespace Chat.Server;

public static class Program
{
    public const string DefaultDatabase = "chat-history.db";

    private static readonly string Usage =
        "Usage: Chat.Server [port] [--db <file> | --no-db]\n" +
        $"  port      1-65535, default {ChatProtocol.DefaultPort}\n" +
        $"  --db      SQLite file for the history, default {DefaultDatabase}\n" +
        "  --no-db   keep the history in memory only (lost when the server stops)";

    public static async Task<int> Main(string[] args)
    {
        if (!TryParseArguments(args, out int port, out string? database, out string? error))
        {
            Console.Error.WriteLine(error);
            Console.Error.WriteLine(Usage);
            return 1;
        }

        using var shutdown = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true; // Stop gracefully instead of killing the process.
            shutdown.Cancel();
        };

        void Log(string message) => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}");

        IMessageStore store;
        try
        {
            store = database is null ? new InMemoryMessageStore() : SqliteMessageStore.OpenFile(database);
        }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException or InvalidOperationException)
        {
            Console.Error.WriteLine($"Cannot open the database {database}: {ex.Message}");
            return 1;
        }

        Log(database is null
            ? "History kept in memory only (--no-db)."
            : $"History stored in {Path.GetFullPath(database)}.");

        try
        {
            await using var server = new ChatServer(IPAddress.Any, port, Log, new ChatRoom(store));
            try
            {
                server.Start();
            }
            catch (SocketException ex)
            {
                Console.Error.WriteLine($"Cannot listen on port {port}: {ex.Message}");
                return 1;
            }

            await server.RunAsync(shutdown.Token);
            return 0;
        }
        finally
        {
            (store as IDisposable)?.Dispose();
        }
    }

    private static bool TryParseArguments(IReadOnlyList<string> args, out int port, out string? database, out string? error)
    {
        port = ChatProtocol.DefaultPort;
        database = DefaultDatabase;
        error = null;
        bool portSeen = false;

        for (int i = 0; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--no-db":
                    database = null;
                    break;
                case "--db" when i + 1 < args.Count:
                    database = args[++i];
                    break;
                case "--db":
                    error = "--db needs a file name.";
                    return false;
                case var value when !portSeen && int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) && parsed is >= 1 and <= 65535:
                    port = parsed;
                    portSeen = true;
                    break;
                default:
                    error = $"Unknown argument: {args[i]}";
                    return false;
            }
        }

        return true;
    }
}
