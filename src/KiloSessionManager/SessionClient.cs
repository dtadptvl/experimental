using System.IO.Pipes;
using KiloSession.Shared;

namespace KiloSessionManager;

internal sealed class SessionClient : IDisposable
{
    private readonly ProjectEntry _project;
    private readonly RuntimeRef _runtime;
    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private readonly CancellationTokenSource _stop = new();
    private NamedPipeClientStream? _pipe;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private Task? _readTask;
    private bool _disposed;

    public event Action<byte[], bool>? OutputReceived;
    public event Action<bool>? StatusChanged;
    public event Action? Disconnected;

    public SessionClient(ProjectEntry project, RuntimeRef runtime)
    {
        _project = project;
        _runtime = runtime;
    }

    public async Task ConnectAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        _pipe = new NamedPipeClientStream(
            ".",
            _runtime.PipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _stop.Token);
        timeoutCts.CancelAfter(timeout);
        await _pipe.ConnectAsync(timeoutCts.Token);

        _reader = new StreamReader(_pipe, leaveOpen: true);
        _writer = new StreamWriter(_pipe, leaveOpen: true) { AutoFlush = true };

        var helloLine = await _reader.ReadLineAsync(timeoutCts.Token)
            ?? throw new IOException("SessionHost disconnected before handshake.");
        var hello = ProtocolJson.Deserialize(helloLine)
            ?? throw new IOException("Invalid SessionHost handshake.");

        if (hello.Type != "hello" ||
            !string.Equals(hello.ProjectId, _project.Id, StringComparison.Ordinal) ||
            !string.Equals(hello.InstanceId, _runtime.InstanceId, StringComparison.Ordinal))
        {
            throw new IOException("SessionHost identity mismatch.");
        }

        if (hello.Running.HasValue) StatusChanged?.Invoke(hello.Running.Value);
        _readTask = ReadLoopAsync(_stop.Token);
    }

    public Task SendInputAsync(byte[] data) => SendAsync(new PipeMessage
    {
        Type = "input",
        Data = Convert.ToBase64String(data)
    });

    public Task ResizeAsync(int cols, int rows) => SendAsync(new PipeMessage
    {
        Type = "resize",
        Cols = cols,
        Rows = rows
    });

    public Task TerminateAsync() => SendAsync(new PipeMessage { Type = "terminate" });

    private async Task SendAsync(PipeMessage message)
    {
        if (_disposed || _writer is null) return;
        await _sendGate.WaitAsync(_stop.Token);
        try
        {
            await _writer.WriteLineAsync(ProtocolJson.Serialize(message));
        }
        finally
        {
            _sendGate.Release();
        }
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && _reader is not null)
            {
                var line = await _reader.ReadLineAsync(cancellationToken);
                if (line is null) break;
                var message = ProtocolJson.Deserialize(line);
                if (message is null) continue;

                switch (message.Type)
                {
                    case "snapshot":
                        OutputReceived?.Invoke(
                            string.IsNullOrEmpty(message.Data) ? Array.Empty<byte>() : Convert.FromBase64String(message.Data),
                            true);
                        break;
                    case "output" when !string.IsNullOrEmpty(message.Data):
                        OutputReceived?.Invoke(Convert.FromBase64String(message.Data), false);
                        break;
                    case "status" when message.Running.HasValue:
                        StatusChanged?.Invoke(message.Running.Value);
                        break;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (IOException)
        {
        }
        finally
        {
            if (!_disposed) Disconnected?.Invoke();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stop.Cancel();
        try { _pipe?.Dispose(); } catch { }
        _reader?.Dispose();
        _writer?.Dispose();
        _sendGate.Dispose();
        _stop.Dispose();
    }
}
