using NetworkingBasics.Echo;
using NetworkingBasics.Exercises;

namespace NetworkingBasics;

/// <summary>The exercises that led to the chat, in the order they were done in the course.</summary>
public static class Program
{
    private static readonly (string Title, Action Run)[] Items =
    [
        ("Threads: three workers running at the same time", ThreadExercises.RunParallelWorkers),
        ("Threads: one greeter thread per name you type", ThreadExercises.RunGreeters),
        ("Sockets: echo server with shared history (raw Socket API)", EchoDemo.Run),
    ];

    public static void Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        while (true)
        {
            Console.WriteLine();
            Console.WriteLine("=== Networking basics ===");
            for (int i = 0; i < Items.Length; i++)
            {
                Console.WriteLine($"{i + 1}. {Items[i].Title}");
            }

            Console.WriteLine("0. Exit");
            Console.Write("> ");

            string? input = Console.ReadLine();
            if (input is null or "0")
            {
                return;
            }

            if (int.TryParse(input, out int choice) && choice >= 1 && choice <= Items.Length)
            {
                Console.WriteLine();
                Items[choice - 1].Run();
            }
            else
            {
                Console.WriteLine($"Type a number between 0 and {Items.Length}.");
            }
        }
    }
}
