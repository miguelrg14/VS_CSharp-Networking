using System.Globalization;
using Chat.Core;
using Microsoft.Data.Sqlite;

namespace Chat.Storage.Sqlite;

/// <summary>
/// Keeps the chat messages in a SQLite database file, so the history survives server restarts.
/// Every query uses parameters ($name), never text glued into the SQL, so a message such as
/// "'; DROP TABLE messages; --" is stored and searched as plain text (no SQL injection).
/// </summary>
public sealed class SqliteMessageStore : IMessageStore, IDisposable
{
    /// <summary>Stored in PRAGMA user_version; increase it and add a migration step when the schema changes.</summary>
    public const int SchemaVersion = 1;

    private const string MessageColumns = "sent_at, author, text";

    // A SQLite connection must not be used by two threads at once, and every client connection
    // runs on its own task, so all the database work goes through this lock.
    private readonly Lock _gate = new();
    private readonly SqliteConnection _connection;

    public SqliteMessageStore(string connectionString)
    {
        _connection = new SqliteConnection(connectionString);
        _connection.Open();
        try
        {
            Migrate();
        }
        catch
        {
            _connection.Dispose();
            throw;
        }
    }

    /// <summary>Opens (or creates) the database file at <paramref name="path"/>.</summary>
    public static SqliteMessageStore OpenFile(string path) => new(new SqliteConnectionStringBuilder
    {
        DataSource = path,
        Mode = SqliteOpenMode.ReadWriteCreate,
        Pooling = false, // Closes the file as soon as the store is disposed.
    }.ToString());

    /// <summary>A database that only lives in memory, for tests.</summary>
    public static SqliteMessageStore InMemory() => new("Data Source=:memory:");

    public void Add(ChatMessage message)
    {
        lock (_gate)
        {
            using SqliteCommand command = Command(
                "INSERT INTO messages (sent_at, author, text) VALUES ($sentAt, $author, $text);");
            command.Parameters.AddWithValue("$sentAt", message.SentAt.ToString("O", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$author", (object?)message.Author ?? DBNull.Value);
            command.Parameters.AddWithValue("$text", message.Text);
            command.ExecuteNonQuery();
        }
    }

    /// <summary>The inner query takes the newest rows; the outer one puts them back in chronological order.</summary>
    public IReadOnlyList<ChatMessage> Recent(int count) => ReadMessages(
        $"""
        SELECT {MessageColumns} FROM (
            SELECT id, {MessageColumns} FROM messages
            ORDER BY id DESC
            LIMIT $limit)
        ORDER BY id;
        """,
        ("$limit", count));

    /// <summary>
    /// LIKE treats % and _ as wildcards, so they are escaped first: searching "100%" looks for the
    /// text "100%", not for "100 followed by anything". SQLite's LIKE ignores case for ASCII letters.
    /// </summary>
    public IReadOnlyList<ChatMessage> Search(string text, int limit) => ReadMessages(
        $"""
        SELECT {MessageColumns} FROM (
            SELECT id, {MessageColumns} FROM messages
            WHERE author IS NOT NULL AND text LIKE $pattern ESCAPE '\'
            ORDER BY id DESC
            LIMIT $limit)
        ORDER BY id;
        """,
        ("$pattern", $"%{EscapeLike(text)}%"),
        ("$limit", limit));

    public IReadOnlyList<AuthorStats> MessagesPerAuthor(int limit)
    {
        lock (_gate)
        {
            using SqliteCommand command = Command(
                """
                SELECT author, COUNT(*) AS messages
                FROM messages
                WHERE author IS NOT NULL
                GROUP BY author COLLATE NOCASE
                ORDER BY messages DESC, author COLLATE NOCASE
                LIMIT $limit;
                """);
            command.Parameters.AddWithValue("$limit", limit);

            using SqliteDataReader reader = command.ExecuteReader();
            var stats = new List<AuthorStats>();
            while (reader.Read())
            {
                stats.Add(new AuthorStats(reader.GetString(0), reader.GetInt32(1)));
            }

            return stats;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _connection.Dispose();
        }
    }

    internal static string EscapeLike(string text) =>
        text.Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace("%", @"\%", StringComparison.Ordinal)
            .Replace("_", @"\_", StringComparison.Ordinal);

    /// <summary>Creates the schema in a new database and refuses databases made by a newer version of the app.</summary>
    private void Migrate()
    {
        using SqliteCommand versionQuery = Command("PRAGMA user_version;");
        int version = Convert.ToInt32(versionQuery.ExecuteScalar(), CultureInfo.InvariantCulture);
        if (version > SchemaVersion)
        {
            throw new InvalidOperationException(
                $"The database uses schema version {version}, but this server only knows version {SchemaVersion}.");
        }

        if (version == 0)
        {
            using SqliteCommand create = Command(
                """
                CREATE TABLE IF NOT EXISTS messages (
                    id      INTEGER PRIMARY KEY AUTOINCREMENT,
                    sent_at TEXT    NOT NULL,              -- ISO 8601 with the UTC offset
                    author  TEXT    NULL,                  -- NULL for server notices
                    text    TEXT    NOT NULL CHECK (length(text) <= 500)
                );
                CREATE INDEX IF NOT EXISTS ix_messages_author ON messages (author COLLATE NOCASE);
                PRAGMA user_version = 1;
                """);
            create.ExecuteNonQuery();
        }
    }

    private IReadOnlyList<ChatMessage> ReadMessages(string sql, params (string Name, object Value)[] parameters)
    {
        lock (_gate)
        {
            using SqliteCommand command = Command(sql);
            foreach ((string name, object value) in parameters)
            {
                command.Parameters.AddWithValue(name, value);
            }

            using SqliteDataReader reader = command.ExecuteReader();
            var messages = new List<ChatMessage>();
            while (reader.Read())
            {
                messages.Add(new ChatMessage(
                    DateTimeOffset.Parse(reader.GetString(0), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                    reader.IsDBNull(1) ? null : reader.GetString(1),
                    reader.GetString(2)));
            }

            return messages;
        }
    }

    private SqliteCommand Command(string sql)
    {
        SqliteCommand command = _connection.CreateCommand();
        command.CommandText = sql;
        return command;
    }
}
