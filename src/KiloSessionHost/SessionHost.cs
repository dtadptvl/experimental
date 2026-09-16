using System.IO.Pipes;
using KiloSession.Shared;

namespace KiloSessionHost;

internal sealed class SessionHost : IDisposable
{
    private readonly HostConfig _config;
    private readonly ByteRingBuffer _ring;
    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _writerGate = new(1, 1);
    private readonly object _clientGate = new();
    private ConPtySession? _pty;
    private StreamWriter? _clientWriter;
    private bool _running;
    private bool _disposed;

    public SessionHost(HostConfig config)
    {
        _config = config;
        _ring = new ByteRingBuffer(config.RingBufferBytes);
    }

    public async Task RunAsync()
    {
        _pty = ConPtySession.Start(
            _config.Executable,
            _config.Arguments,
            _config.WorkingDirectory);
        _running = true;

        // ConPTY uses synchronous anonymous pipe handles. Keep the blocking read on a
        // dedicated worker so it can never prevent the named-pipe server from opening.
        var outputTask = Task.Run(() => PumpOutputAsync(_pty, _stop.Token));
        var exitTask = WatchExitAsync(_pty);

        try
        {
            await AcceptClientsAsync(_stop.Token);
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
        }
        finally
        {
            _stop.Cancel();
            // Closing the pseudo console removes its write endpoint and unblocks the
            // synchronous output reader with EOF.
            _pty?.Dispose();
            try { await outputTask; } catch { }
            try { await exitTask; } catch { }
        }
    }

    private async Task AcceptClientsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && _running)
        {
            await using var pipe = new NamedPipeServerStream(
                _config.PipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);

            await pipe.WaitForConnectionAsync(cancellationToken);

            using var reader = new StreamReader(pipe, leaveOpen: true);
            using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };

            await AttachClientAsync(writer);
            try
            {
                while (pipe.IsConnected && !cancellationToken.IsCancellationRequested)
                {
                    var line = await reader.ReadLineAsync(cancellationToken);
                    if (line is null) break;
                    var message = ProtocolJson.Deserialize(line);
                    if (message is not null) await HandleClientMessageAsync(message, cancellationToken);
                }
            }
            catch (IOException)
            {
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            finally
            {
                lock (_clientGate)
                {
                    if (ReferenceEquals(_clientWriter, writer)) _clientWriter = null;
                }
            }
        }
    }

    private async Task AttachClientAsync(StreamWriter writer)
    {
        await _writerGate.WaitAsync();
        try
        {
            await writer.WriteLineAsync(ProtocolJson.Serialize(new PipeMessage
            {
                Type = "hello",
                ProjectId = _config.ProjectId,
                InstanceId = _config.InstanceId,
                HostPid = Environment.ProcessId,
                ChildPid = _pty?.ProcessId,
                Running = _running
            }));

            var snapshot = _ring.Snapshot();
            await writer.WriteLineAsync(ProtocolJson.Serialize(new PipeMessage
            {
                Type = "snapshot",
                Data = Convert.ToBase64String(snapshot)
            }));

            await writer.WriteLineAsync(ProtocolJson.Serialize(new PipeMessage
            {
                Type = "status",
                Running = _running,
                ChildPid = _pty?.ProcessId
            }));

            lock (_clientGate) _clientWriter = writer;
        }
        finally
        {
            _writerGate.Release();
        }
    }

    private async Task HandleClientMessageAsync(PipeMessage message, CancellationToken cancellationToken)
    {
        if (_pty is null) return;

        switch (message.Type)
        {
            case "input" when !string.IsNullOrEmpty(message.Data):
                await _pty.WriteAsync(Convert.FromBase64String(message.Data), cancellationToken);
                break;

            case "resize" when message.Cols is > 0 && message.Rows is > 0:
                _pty.Resize(message.Cols.Value, message.Rows.Value);
                break;

            case "ping":
                await BroadcastAsync(new PipeMessage { Type = "pong", Running = _running });
                break;

            case "terminate":
                _ = Task.Run(async () =>
                {
                    try { await _pty.TerminateTreeAsync(); } catch { }
                });
                break;
        }
    }

    private async Task PumpOutputAsync(ConPtySession pty, CancellationToken cancellationToken)
    {
        var buffer = new byte[32 * 1024];
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                // The FileStream wraps a synchronous CreatePipe handle. This call is
                // intentionally executed on the worker created in RunAsync.
                var read = pty.Output.Read(buffer, 0, buffer.Length);
                if (read <= 0) break;

                var chunk = new byte[read];
                Buffer.BlockCopy(buffer, 0, chunk, 0, read);
                _ring.Append(chunk);
                await BroadcastAsync(new PipeMessage
                {
                    Type = "output",
                    Data = Convert.ToBase64String(chunk)
                });
            }
        }
        catch (IOException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async Task WatchExitAsync(ConPtySession pty)
    {
        await pty.WaitForExitAsync();
        _running = false;
        await BroadcastAsync(new PipeMessage
        {
            Type = "status",
            Running = false,
            ChildPid = pty.ProcessId
        });
        await Task.Delay(150);
        _stop.Cancel();
    }

    private async Task BroadcastAsync(PipeMessage message)
    {
        StreamWriter? writer;
        lock (_clientGate) writer = _clientWriter;
        if (writer is null) return;

        await _writerGate.WaitAsync();
        try
        {
            lock (_clientGate) writer = _clientWriter;
            if (writer is null) return;
            try
            {
                await writer.WriteLineAsync(ProtocolJson.Serialize(message));
            }
            catch (IOException)
            {
                lock (_clientGate)
                {
                    if (ReferenceEquals(_clientWriter, writer)) _clientWriter = null;
                }
            }
            catch (ObjectDisposedException)
            {
                lock (_clientGate)
                {
                    if (ReferenceEquals(_clientWriter, writer)) _clientWriter = null;
                }
            }
        }
        finally
        {
            _writerGate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stop.Cancel();
        lock (_clientGate) _clientWriter = null;
        _pty?.Dispose();
        _writerGate.Dispose();
        _stop.Dispose();
    }
}
