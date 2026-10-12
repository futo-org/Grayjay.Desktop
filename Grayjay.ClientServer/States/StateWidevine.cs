using Grayjay.Engine.Packages;
using JustCef;

using Logger = Grayjay.Desktop.POC.Logger;

namespace Grayjay.ClientServer.States;

public static class StateWidevine
{
    private static readonly object _lock = new object();
    private static WidevineStatus? _status;
    private static bool _clientEvaluatedWithoutPlayback;
    private static bool _linuxCdmPlaybackAvailable;

    public static WidevineStatus? Status
    {
        get
        {
            lock (_lock)
            {
                return _status;
            }
        }
    }

    public static bool IsPlaybackAvailable
    {
        get
        {
            lock (_lock)
            {
                return IsAvailable(_status);
            }
        }
    }

    /// <summary>
    /// True when a plugin that checks for HLSWidevineSource was evaluated while DRM playback was unavailable.
    /// </summary>
    public static bool ClientEvaluatedWithoutPlayback
    {
        get
        {
            lock (_lock)
            {
                return _clientEvaluatedWithoutPlayback;
            }
        }
    }

    public static event Action? PlaybackBecameAvailable;

    // Plugins read bridge.supportedFeatures once, when their script is evaluated.
    public static void NoteClientEvaluating(string? script)
    {
        if (script == null || !script.Contains("HLSWidevineSource"))
        {
            return;
        }

        lock (_lock)
        {
            if (!IsAvailable(_status))
            {
                _clientEvaluatedWithoutPlayback = true;
            }
        }
    }

    private static bool IsAvailable(WidevineStatus? status)
    {
        return _linuxCdmPlaybackAvailable || status?.State == WidevineState.Ready;
    }

    public static void SetLinuxCdmPlaybackAvailable(bool available)
    {
        bool becameAvailable;
        lock (_lock)
        {
            bool wasAvailable = IsAvailable(_status);
            _linuxCdmPlaybackAvailable = available;
            becameAvailable = ApplyAvailability(wasAvailable, _status);
        }
        if (becameAvailable) RaisePlaybackBecameAvailable();
    }

    // Applies the status JustCef publishes once: at startup when a CDM is installed, otherwise after the update.
    public static void SetStatus(WidevineStatus status)
    {
        bool becameAvailable;
        lock (_lock)
        {
            bool wasAvailable = IsAvailable(_status);
            _status = status;
            becameAvailable = ApplyAvailability(wasAvailable, status);
        }
        if (becameAvailable)
            RaisePlaybackBecameAvailable();
    }

    // Must be called while holding _lock; returns whether availability went from false to true.
    private static bool ApplyAvailability(bool wasAvailable, WidevineStatus? status)
    {
        bool isAvailable = IsAvailable(status);
        PackageBridge.WidevineSupported = isAvailable;
        return isAvailable && !wasAvailable;
    }

    private static void RaisePlaybackBecameAvailable()
    {
        try
        {
            PlaybackBecameAvailable?.Invoke();
        }
        catch (Exception ex)
        {
            Logger.w(nameof(StateWidevine), "A Widevine availability handler failed: " + ex.Message, ex);
        }
    }
}
