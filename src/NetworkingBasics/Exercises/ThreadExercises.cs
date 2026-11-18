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
        Console.WriteLine("Type names: each one starts its own thread that greets every 2 seconds. Type \"exit\" to stop them all.");
        using var greeters = new GreeterPool(TimeSpan.FromSeconds(2), Console.WriteLine);
        while (Console.ReadLine() is string name && !name.Trim().Equals("exit", StringComparison.OrdinalIgnoreCase))
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                greeters.Start(name.Trim());
            }
        }

        greeters.StopAll();
        Console.WriteLine($"Stopped {greeters.StartedCount} greeter thread(s).");
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
/// One background thread per name. They all watch the same CancellationToken, so a single call
/// stops every one of them (a loop that checks a variable that never changes would run forever).
/// </summary>
public sealed class GreeterPool(TimeSpan interval, Action<string> output) : IDisposable
{
    private readonly CancellationTokenSource _stop = new();
    private readonly List<Thread> _threads = [];

    public int StartedCount => _threads.Count;

    public void Start(string name)
    {
        CancellationToken token = _stop.Token;
        var thread = new Thread(() =>
        {
            // WaitOne returns true as soon as the token is cancelled, so stopping does not wait for the interval.
            do
            {
                output($"Hello, {name}!");
            }
            while (!token.WaitHandle.WaitOne(interval));
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
