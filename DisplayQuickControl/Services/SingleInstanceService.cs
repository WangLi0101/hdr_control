using System.IO;
using System.IO.Pipes;
using System.Security.Principal;

namespace DisplayQuickControl.Services;

public sealed class SingleInstanceService : IDisposable
{
    private readonly string _name = "DisplayQuickControl-" + WindowsIdentity.GetCurrent().User!.Value + "-" + System.Diagnostics.Process.GetCurrentProcess().SessionId;
    private Mutex? _mutex;
    private readonly CancellationTokenSource _stop = new();
    public bool Acquire() { _mutex = new Mutex(true, @"Local\" + _name, out var created); return created; }
    public async Task SignalExistingAsync()
    {
        // The first instance may still be initializing. Bounded IPC retries never apply presets.
        for (var i = 0; i < 5; i++)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", _name, PipeDirection.Out, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await client.ConnectAsync(600); await client.WriteAsync(new byte[] { 1 }); return;
            }
            catch (TimeoutException) { }
            catch (IOException) { }
        }
        throw new IOException("The existing instance did not respond. Check the tray or restart Display Quick Control.");
    }
    public async Task ListenAsync(Action show, LogService log)
    {
        while (!_stop.IsCancellationRequested) // Idle asynchronous wait; no polling.
        {
            try
            {
                using var server = new NamedPipeServerStream(_name, PipeDirection.In, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(_stop.Token);
                var buffer = new byte[1];
                using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);
                readTimeout.CancelAfter(TimeSpan.FromSeconds(2));
                if (await server.ReadAsync(buffer, readTimeout.Token) == 1 && buffer[0] == 1) show();
            }
            catch (OperationCanceledException) { if (_stop.IsCancellationRequested) break; }
            catch (IOException) { log.Write("IPC", "Connection ended."); }
        }
    }
    public void Dispose() { _stop.Cancel(); _mutex?.Dispose(); }
}
