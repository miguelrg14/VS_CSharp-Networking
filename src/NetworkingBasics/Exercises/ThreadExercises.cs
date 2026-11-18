namespace NetworkingBasics.Exercises;

/// <summary>Threads: the step before serving several network clients at once.</summary>
public static class ThreadExercises
{
    public static void RunParallelWorkers()
    {
        Console.WriteLine("Three threads write at different speeds; their lines interleave because they run at the same time.");
        RunWorkers(
            [new Worker("Fast", Steps: 6, DelayMs: 150), new Worker("Medium", Steps: 4, DelayMs: 300), new Worker("Slow", Steps: 3, DelayMs: 500)],
            Console.WriteLine);
        Console.WriteLine("All the threads have finished (the main thread waited for them with Join).");
    }

    public static void RunGreeters()
    {
        Console.WriteLine(
            $"Type a name and press Enter: it starts its own thread, which greets {GreeterPool.DefaultGreetings} times " +
            "(one every 2 seconds) and then ends.");
        Console.WriteLine("Keep typing names while the others are still running. An empty line finishes.");

        using var greeters = new GreeterPool(TimeSpan.FromSeconds(2), GreeterPool.DefaultGreetings, line => Console.WriteLine($"    {line}"));
        while (Console.ReadLine() is string name && !string.IsNullOrWhiteSpace(name))
        {
            greeters.Start(name.Trim());
        }

        int stillRunning = greeters.RunningCount;
        greeters.StopAll();
        Console.WriteLine($"Threads started: {greeters.StartedCount}. Stopped early because they were still running: {stillRunning}.");
    }

    public sealed record Worker(string Name, int Steps, int DelayMs);

    /// <summary>
    /// Starts one thread per worker and waits until all of them finish. Join is the way to wait for a thread;
    /// an endless "while (true)" in the main thread would never end and leave the code after it unreachable.
    /// </summary>
    public static void RunWorkers(IReadOnlyList<Worker> workers, Action<string> output)
    {
        Thread[] threads =
        [
            .. workers.Select(worker => new Thread(() =>
            {
                for (int step = 1; step <= worker.Steps; step++)
                {
                    output($"{worker.Name}: step {step} of {worker.Steps}");
                    Thread.Sleep(worker.DelayMs);
                }

                output($"{worker.Name} is done.");
            })
            {
                Name = worker.Name,
            }),
        ];

        foreach (Thread thread in threads)
        {
            thread.Start();
        }

        foreach (Thread thread in threads)
        {
            thread.Join();
        }
    }
}

/// <summary>
/// One background thread per name. Each thread greets a fixed number of times and ends by itself,
/// so the console never fills up endlessly. They all watch the same CancellationToken, so a single call
/// stops the ones still running (a loop that checks a variable that never changes would run forever).
/// </summary>
public sealed class GreeterPool(TimeSpan interval, int greetings, Action<string> output) : IDisposable
{
    public const int DefaultGreetings = 3;

    private readonly CancellationTokenSource _stop = new();
    private readonly List<Thread> _threads = [];

    public int StartedCount => _threads.Count;

    public int RunningCount => _threads.Count(t => t.IsAlive);

    public void Start(string name)
    {
        CancellationToken token = _stop.Token;
        var thread = new Thread(() =>
        {
            for (int i = 1; i <= greetings; i++)
            {
                output($"[{name}'s thread] Hello, {name}! ({i}/{greetings})");

                // WaitOne returns true as soon as the token is cancelled, so stopping does not wait for the interval.
                if (i < greetings && token.WaitHandle.WaitOne(interval))
                {
                    return;
                }
            }

            output($"[{name}'s thread] Done, this thread ends now.");
        })
        {
            IsBackground = true, // A background thread never keeps the app alive on its own.
            Name = $"Greeter {name}",
        };

        _threads.Add(thread);
        thread.Start();
    }

    public void StopAll()
    {
        _stop.Cancel();
        foreach (Thread thread in _threads)
        {
            thread.Join();
        }
    }

    public void Dispose()
    {
        if (!_stop.IsCancellationRequested)
        {
            StopAll();
        }

        _stop.Dispose();
    }
}
