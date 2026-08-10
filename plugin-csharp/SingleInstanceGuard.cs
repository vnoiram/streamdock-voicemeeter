namespace StreamDockVoicemeeter;

public sealed class SingleInstanceGuard : IDisposable
{
    private const string MutexName = @"Local\StreamDockVoicemeeterPlugin.Instance";
    private const string ShutdownEventName = @"Local\StreamDockVoicemeeterPlugin.Shutdown";
    private static readonly TimeSpan GracefulExitTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ForcedExitTimeout = TimeSpan.FromSeconds(5);

    private readonly CancellationTokenSource _disposeCancellation = new();
    private readonly EventWaitHandle _shutdownEvent;
    private readonly Mutex _mutex;
    private readonly Task _shutdownWatcher;
    private bool _ownsMutex;

    private SingleInstanceGuard(Mutex mutex, EventWaitHandle shutdownEvent, Action shutdownRequested)
    {
        _mutex = mutex;
        _shutdownEvent = shutdownEvent;
        _shutdownWatcher = Task.Run(() => WatchShutdownRequests(shutdownRequested));
    }

    public static SingleInstanceGuard Acquire(Action shutdownRequested)
    {
        var shutdownSignaled = SignalExistingInstance();
        if (shutdownSignaled) Thread.Sleep(GracefulExitTimeout);
        TerminateExistingInstances();

        var mutex = new Mutex(false, MutexName);
        var ownsMutex = false;
        try
        {
            try
            {
                ownsMutex = mutex.WaitOne(GracefulExitTimeout);
            }
            catch (AbandonedMutexException)
            {
                ownsMutex = true;
            }

            if (!ownsMutex)
            {
                TerminateExistingInstances();
                try
                {
                    ownsMutex = mutex.WaitOne(ForcedExitTimeout);
                }
                catch (AbandonedMutexException)
                {
                    ownsMutex = true;
                }
            }

            if (!ownsMutex)
            {
                throw new InvalidOperationException(
                    "Another Stream Dock Voicemeeter plugin process is still running and did not exit after shutdown requests.");
            }

            var shutdownEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShutdownEventName);
            return new SingleInstanceGuard(mutex, shutdownEvent, shutdownRequested)
            {
                _ownsMutex = true
            };
        }
        catch
        {
            if (ownsMutex) mutex.ReleaseMutex();
            mutex.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        _disposeCancellation.Cancel();
        _shutdownEvent.Set();
        try
        {
            _shutdownWatcher.Wait(TimeSpan.FromSeconds(1));
        }
        catch
        {
            // Process shutdown must not be blocked by the watcher cleanup path.
        }

        _disposeCancellation.Dispose();
        _shutdownEvent.Dispose();
        if (_ownsMutex)
        {
            _mutex.ReleaseMutex();
            _ownsMutex = false;
        }

        _mutex.Dispose();
    }

    private static bool SignalExistingInstance()
    {
        try
        {
            using var existingShutdownEvent = EventWaitHandle.OpenExisting(ShutdownEventName);
            existingShutdownEvent.Set();
            return true;
        }
        catch (WaitHandleCannotBeOpenedException)
        {
            return false;
        }
    }

    private static void TerminateExistingInstances()
    {
        var currentProcess = System.Diagnostics.Process.GetCurrentProcess();
        var currentPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(currentPath)) return;

        foreach (var process in System.Diagnostics.Process.GetProcessesByName(currentProcess.ProcessName))
        {
            using (process)
            {
                if (process.Id == currentProcess.Id) continue;
                if (!IsSameExecutable(process, currentPath)) continue;

                try
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit((int)ForcedExitTimeout.TotalMilliseconds);
                }
                catch
                {
                    // Stale process cleanup is best-effort; the mutex wait below remains authoritative.
                }
            }
        }
    }

    private static bool IsSameExecutable(System.Diagnostics.Process process, string currentPath)
    {
        try
        {
            var processPath = process.MainModule?.FileName;
            return !string.IsNullOrWhiteSpace(processPath) &&
                   string.Equals(
                       Path.GetFullPath(processPath),
                       Path.GetFullPath(currentPath),
                       StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private void WatchShutdownRequests(Action shutdownRequested)
    {
        while (!_disposeCancellation.IsCancellationRequested)
        {
            if (!_shutdownEvent.WaitOne(TimeSpan.FromMilliseconds(250))) continue;
            if (_disposeCancellation.IsCancellationRequested) return;
            shutdownRequested();
            return;
        }
    }
}
