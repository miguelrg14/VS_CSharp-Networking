namespace Chat.Core;

/// <summary>
/// Reads and writes whole lines of text over a network stream. Writes are serialized with a semaphore:
/// several tasks (broadcasts from other clients, replies to this one) may write to the same connection
/// at the same time, and their bytes must not get mixed up.
/// </summary>
public sealed class LineConnection : IAsyncDisposable
{
    private readonly StreamReader _reader;
    private readonly StreamWriter _writer;
    private readonly SemaphoreSlim _writeGate = new(1, 1);

    public LineConnection(Stream stream)
    {
        _reader = new StreamReader(stream, ChatProtocol.Encoding, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
        _writer = new StreamWriter(stream, ChatProtocol.Encoding, leaveOpen: true) { AutoFlush = true, NewLine = "\n" };
    }

    /// <summary>Returns the next line, or null when the other side closed the connection.</summary>
    public ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken = default) =>
        _reader.ReadLineAsync(cancellationToken);

    public async Task WriteLineAsync(string line, CancellationToken cancellationToken = default)
    {
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _writer.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _writer.DisposeAsync().ConfigureAwait(false);
        _reader.Dispose();
        _writeGate.Dispose();
    }
}
