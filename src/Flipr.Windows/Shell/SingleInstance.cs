namespace Flipr.Windows.Shell;

/// <summary>
/// One Flipr per user session. A second launch signals the first (which then opens Settings) and exits.
/// The mutex name is also used by the installer to detect a running copy (Inno Setup AppMutex).
/// </summary>
public sealed class SingleInstance : IDisposable
{
    public const string MutexName = "Flipr.Running";
    private const string ActivateEventName = "Flipr.Activate";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle? _activate;
    private RegisteredWaitHandle? _wait;
    private bool _released;

    public SingleInstance()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out bool created);
        IsFirst = created;
        _activate = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
    }

    public bool IsFirst { get; }

    /// <summary>First instance: invoke <paramref name="onActivate"/> (on a thread-pool thread) when another launch happens.</summary>
    public void ListenForActivation(Action onActivate)
    {
        if (_activate is null) return;
        _wait = ThreadPool.RegisterWaitForSingleObject(_activate, (_, _) => onActivate(), null, Timeout.Infinite, executeOnlyOnce: false);
    }

    /// <summary>Second instance: ask the first one to come forward.</summary>
    public void SignalFirstInstance() => _activate?.Set();

    /// <summary>Gives up the running-copy mutex early, e.g. just before handing over to the installer during an update.</summary>
    public void ReleaseOwnership()
    {
        if (_released) return;
        _released = true;
        if (IsFirst) { try { _mutex.ReleaseMutex(); } catch (ApplicationException) { } }
        _mutex.Dispose();
    }

    public void Dispose()
    {
        _wait?.Unregister(null);
        _activate?.Dispose();
        ReleaseOwnership();
    }
}
