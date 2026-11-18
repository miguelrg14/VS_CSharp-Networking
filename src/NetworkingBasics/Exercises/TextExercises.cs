using System.Globalization;

namespace NetworkingBasics.Exercises;

/// <summary>Review exercises from the first week of the course.</summary>
public static class TextExercises
{
    public const string SumCommand = "sum";

    public static void RunSum()
    {
        Console.WriteLine($"Type whole numbers, one per line. Type \"{SumCommand}\" to see the total.");
        long total = 0;
        while (Console.ReadLine() is string line && !line.Trim().Equals(SumCommand, StringComparison.OrdinalIgnoreCase))
        {
            if (TryParseNumber(line, out long number))
            {
                total += number;
            }
            else
            {
                Console.WriteLine($"\"{line}\" is not a whole number, it was ignored.");
            }
        }

        Console.WriteLine($"Total: {total}");
    }

    public static void RunSecondToLastWord()
    {
        Console.WriteLine("Type a sentence:");
        string sentence = Console.ReadLine() ?? string.Empty;
        Console.WriteLine(SecondToLastWord(sentence) is string word
            ? $"The second-to-last word is \"{word}\"."
            : "The sentence needs at least two words.");
    }

    public static bool TryParseNumber(string text, out long number) =>
        long.TryParse(text.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out number);

    /// <summary>
    /// Splitting on whitespace and dropping empty entries handles several spaces in a row, tabs and
    /// leading/trailing spaces. A fixed array of 100 words would fail on long sentences, and indexing
    /// "last - 1" without checking would crash on one-word sentences.
    /// </summary>
    public static string? SecondToLastWord(string sentence)
    {
        string[] words = sentence.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return words.Length >= 2 ? words[^2] : null;
    }
}
