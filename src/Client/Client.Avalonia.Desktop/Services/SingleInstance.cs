using System;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Client.Services;

internal sealed class SingleInstance : IDisposable
{
    private readonly Mutex _mutex;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly object _gate = new();
    private readonly Task? _listener;
    private Action? _activate;
    private bool _pending;
    private readonly bool _allowMultipleInstances;

    public bool IsPrimary { get; }

    public SingleInstance(bool allowMultipleInstances = false)
    {
        _allowMultipleInstances = allowMultipleInstances;
        // A pipe is a domain socket in $TMPDIR on Unix, and a socket path has at most 104 characters on
        // macOS, whose $TMPDIR alone takes about 50: the full hash of the user makes it too long
        var user = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Environment.UserName)))[..16];
        _pipeName = $"FlowRate.Desktop.{user}";
        _mutex = new Mutex(false, _pipeName);
        try
        {
            IsPrimary = _mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            IsPrimary = true;
        }

        if (IsPrimary)
            _listener = ListenAsync(_shutdown.Token);
    }

    public void SetActivationHandler(Action activate)
    {
        bool pending;
        lock (_gate)
        {
            _activate = activate;
            pending = _pending;
            _pending = false;
        }
        if (pending)
            activate();
    }

    // A request to show the window; one with a link carries it after its length
    private const byte ActivateRequest = 1;
    private const byte ActivateWithLinkRequest = 2;
    private const int MaxLinkBytes = 8 * 1024;

    // Only the primary process decides whether another instance may start.
    /// <param name="link">A flowrate:// link the system started this process with, handed to the primary one.</param>
    public async Task<bool> RequestLaunchAsync(string? link = null)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);
        var response = new byte[sizeof(int) + 1];
        await pipe.ReadExactlyAsync(response, timeout.Token).ConfigureAwait(false);
        if (response[sizeof(int)] == 1)
            return true;
        if (OperatingSystem.IsWindows())
            AllowSetForegroundWindow(BitConverter.ToInt32(response));
        var linkBytes = link == null ? null : Encoding.UTF8.GetBytes(link);
        if (linkBytes is { Length: > 0 and <= MaxLinkBytes })
        {
            var request = new byte[1 + sizeof(int) + linkBytes.Length];
            request[0] = ActivateWithLinkRequest;
            BitConverter.GetBytes(linkBytes.Length).CopyTo(request, 1);
            linkBytes.CopyTo(request, 1 + sizeof(int));
            await pipe.WriteAsync(request, timeout.Token).ConfigureAwait(false);
        }
        else
            await pipe.WriteAsync(new byte[] { ActivateRequest }, timeout.Token).ConfigureAwait(false);
        var acknowledgement = new byte[1];
        await pipe.ReadExactlyAsync(acknowledgement, timeout.Token).ConfigureAwait(false);
        return false;
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(_pipeName, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                requestTimeout.CancelAfter(TimeSpan.FromSeconds(5));
                var response = new byte[sizeof(int) + 1];
                BitConverter.GetBytes(Environment.ProcessId).CopyTo(response, 0);
                response[sizeof(int)] = _allowMultipleInstances ? (byte)1 : (byte)0;
                await pipe.WriteAsync(response, requestTimeout.Token)
                    .ConfigureAwait(false);
                if (_allowMultipleInstances)
                    continue;
                var request = new byte[1];
                await pipe.ReadExactlyAsync(request, requestTimeout.Token).ConfigureAwait(false);
                if (request[0] is not (ActivateRequest or ActivateWithLinkRequest))
                    continue;

                if (request[0] == ActivateWithLinkRequest)
                {
                    var length = new byte[sizeof(int)];
                    await pipe.ReadExactlyAsync(length, requestTimeout.Token).ConfigureAwait(false);
                    var size = BitConverter.ToInt32(length);
                    if (size is <= 0 or > MaxLinkBytes)
                        continue;
                    var link = new byte[size];
                    await pipe.ReadExactlyAsync(link, requestTimeout.Token).ConfigureAwait(false);
                    // Kept until the main window listens, when it comes while the application starts
                    IncomingLinks.Post(Encoding.UTF8.GetString(link));
                }

                Action? activate;
                lock (_gate)
                {
                    activate = _activate;
                    if (activate == null)
                        _pending = true;
                }
                activate?.Invoke();
                await pipe.WriteAsync(new byte[] { 1 }, requestTimeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (OperationCanceledException)
            {
                // A client that stalls must not prevent subsequent activation requests.
            }
            catch (IOException)
            {
                // A disconnected client must not stop the activation listener.
            }
        }
    }

    public void Dispose()
    {
        _shutdown.Cancel();
        try
        {
            _listener?.GetAwaiter().GetResult();
        }
        finally
        {
            if (IsPrimary)
                _mutex.ReleaseMutex();
            _mutex.Dispose();
            _shutdown.Dispose();
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AllowSetForegroundWindow(int processId);
}
