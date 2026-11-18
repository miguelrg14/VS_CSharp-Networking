using System.Net;
using System.Net.Sockets;
using NetworkingBasics.Echo;
using NetworkingBasics.Exercises;

namespace NetworkingBasics.Tests;

public class ThreadExercisesTests
{
    [Fact]
    public void RunWorkers_WaitsForEveryThread()
    {
        var lines = new List<string>();

        ThreadExercises.RunWorkers(
            [new ThreadExercises.Worker("A", 3, 5), new ThreadExercises.Worker("B", 2, 5)],
            line =>
            {
                lock (lines)
                {
                    lines.Add(line);
                }
            });

        // When RunWorkers returns, both threads have written every line, including the final one.
        Assert.Equal(3 + 1 + 2 + 1, lines.Count);
        Assert.Contains("A is done.", lines);
        Assert.Contains("B is done.", lines);
    }

    [Fact]
    public void Greeter_EndsByItselfAfterItsGreetings()
    {
        var lines = new List<string>();
        using var pool = new GreeterPool(TimeSpan.FromMilliseconds(10), greetings: 3, line =>
        {
            lock (lines)
            {
                lines.Add(line);
            }
        });

        pool.Start("Ana");
        SpinWait.SpinUntil(() => pool.RunningCount == 0, TimeSpan.FromSeconds(5));

        Assert.Equal(0, pool.RunningCount);
        Assert.Equal(3, lines.Count(l => l.Contains("Hello, Ana!")));
        Assert.Contains("[Ana's thread] Done, this thread ends now.", lines);
    }

    [Fact]
    public void GreeterPool_StopsEveryThreadAtOnce()
    {
        var lines = new List<string>();
        var pool = new GreeterPool(TimeSpan.FromHours(1), greetings: 3, line =>
        {
            lock (lines)
            {
                lines.Add(line);
            }
        });

        pool.Start("Ana");
        pool.Start("Ben");
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        pool.StopAll();
        pool.Dispose();

        // Stopping does not wait for the one-hour interval.
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5));
        Assert.Equal(2, pool.StartedCount);
        Assert.Contains("[Ana's thread] Hello, Ana! (1/3)", lines);
        Assert.Contains("[Ben's thread] Hello, Ben! (1/3)", lines);
    }
}

public class EchoServerTests
{
    [Fact]
    public void Reply_UppercasesAndRemembersMessages()
    {
        using var server = new EchoServer();

        Assert.Equal("(history is empty)", server.Reply("history"));
        Assert.Equal("HOLA", server.Reply("hola"));
        Assert.Equal("ADIÓS", server.Reply("adiós"));
        Assert.Equal("hola | adiós", server.Reply("HISTORY"));
    }

    [Fact]
    public void EchoServer_AnswersOverARealSocket_AndSharesTheHistoryBetweenClients()
    {
        using var server = new EchoServer();
        server.Start();

        using Socket first = Connect(server.Port);
        using Socket second = Connect(server.Port);
        var firstReader = new SocketLineReader(first);
        var secondReader = new SocketLineReader(second);

        SocketLineReader.SendLine(first, "from the first");
        Assert.Equal("FROM THE FIRST", firstReader.ReadLine());

        SocketLineReader.SendLine(second, "history");
        Assert.Equal("from the first", secondReader.ReadLine());

        SocketLineReader.SendLine(first, EchoServer.ExitCommand);
        Assert.Null(firstReader.ReadLine()); // The server closed the connection.
    }

    [Fact]
    public void LineReader_JoinsLinesSplitAcrossSeveralPackets()
    {
        using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        listener.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        listener.Listen(1);
        using Socket sender = Connect(((IPEndPoint)listener.LocalEndPoint!).Port);
        using Socket receiver = listener.Accept();

        // One line in two pieces, then two lines in a single send.
        sender.Send("hel"u8.ToArray());
        sender.Send("lo\nsecond\nthird\n"u8.ToArray());

        var reader = new SocketLineReader(receiver);
        Assert.Equal("hello", reader.ReadLine());
        Assert.Equal("second", reader.ReadLine());
        Assert.Equal("third", reader.ReadLine());
    }

    private static Socket Connect(int port)
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.Connect(new IPEndPoint(IPAddress.Loopback, port));
        socket.ReceiveTimeout = 10_000; // A bug fails the test instead of hanging it.
        return socket;
    }
}
