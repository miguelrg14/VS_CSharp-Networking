using System.Text;

namespace Chat.Core;

/// <summary>
/// The text protocol shared by the server and the client. Every message is one line of UTF-8 text.
/// Ending messages with a newline solves "message framing": TCP is a byte stream, so one Receive call
/// can return half a message or two messages glued together, but a line reader always splits them correctly.
/// </summary>
public static class ChatProtocol
{
    public const int DefaultPort = 12000;
    public const int MaxNicknameLength = 20;
    public const int MaxMessageLength = 500;

    /// <summary>How many messages new members receive, and how many /history and /search show.</summary>
    public const int HistorySize = 20;

    public const string QuitCommand = "/quit";
    public const string HistoryCommand = "/history";
    public const string WhoCommand = "/who";
    public const string HelpCommand = "/help";
    public const string SearchCommand = "/search";
    public const string StatsCommand = "/stats";

    public const string NicknamePrompt = "Welcome! Choose a nickname:";

    public static readonly string HelpText =
        $"Commands: {WhoCommand} (who is online), {HistoryCommand} (recent messages), {SearchCommand} <text>, " +
        $"{StatsCommand} (most active users), {QuitCommand} (leave), {HelpCommand}";

    /// <summary>UTF-8 without a byte order mark, so accents and emoji arrive intact on every OS.</summary>
    public static readonly Encoding Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Letters, digits, '-' and '_', between 1 and <see cref="MaxNicknameLength"/> characters.</summary>
    public static bool IsValidNickname(string? nickname, out string? error)
    {
        if (string.IsNullOrWhiteSpace(nickname))
        {
            error = "The nickname cannot be empty.";
            return false;
        }

        if (nickname.Length > MaxNicknameLength)
        {
            error = $"The nickname can have at most {MaxNicknameLength} characters.";
            return false;
        }

        if (!nickname.All(c => char.IsLetterOrDigit(c) || c is '-' or '_'))
        {
            error = "Use only letters, digits, '-' and '_' in the nickname.";
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>Removes line breaks (they would split the message in two) and cuts messages that are too long.</summary>
    public static string Sanitize(string text)
    {
        string singleLine = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return singleLine.Length <= MaxMessageLength ? singleLine : singleLine[..MaxMessageLength];
    }
}
