using Grayjay.ClientServer.Casting;
using Grayjay.ClientServer.Store;

namespace Grayjay.ClientServer.States;

using Logger = Desktop.POC.Logger;

abstract public class StateCasting : IDisposable
{
    protected readonly object _castingDeviceLock = new object();
    protected readonly Dictionary<string, CastingDevice> _castingDevices = new Dictionary<string, CastingDevice>();

    //TODO: Add index for id ?
    protected readonly ManagedStore<CastingDeviceInfo> _pinnedDevices = new ManagedStore<CastingDeviceInfo>("pinnedDevices")
        .WithUnique(v => v.Id)
        .WithBackup();

    public List<CastingDeviceInfo> PinnedDevices => _pinnedDevices.GetObjects()
        .Where(dev => !(Grayjay.ClientServer.Settings.GrayjaySettings.Instance.Casting.Experimental && dev.Type == CastProtocolType.Airplay))
        .ToList();
    public List<CastingDevice> DiscoveredDevices
    {
        get
        {
            lock (_castingDeviceLock)
            {
                return _castingDevices.Values.ToList();
            }
        }
    }

    protected CastingDevice? _activeDevice;
    public CastingDevice? ActiveDevice
    {
        get
        {
            lock (_castingDeviceLock)
            {
                return _activeDevice;
            }
        }
    }

    public virtual event Action<CastingDevice?>? ActiveDeviceChanged;
    public event Action<bool>? IsPlayingChanged;
    public event Action<TimeSpan>? DurationChanged;
    public event Action<TimeSpan>? TimeChanged;
    public event Action<double>? VolumeChanged;
    public event Action<double>? SpeedChanged;
    public event Action<CastConnectionState>? StateChanged;
    protected readonly Debouncer _broadcastDevicesDebouncer;
    private CancellationTokenSource? _updateTimeCts;

    private List<CastingDeviceInfo> _lastUpdate = new List<CastingDeviceInfo>();

    public StateCasting()
    {
        try
        {
            _pinnedDevices.Load();
        }
        catch (Exception e)
        {
            Logger.i(nameof(StateCasting), $"Failed to load pinned devices '{e.Message}': {e.StackTrace}");
        }

        _broadcastDevicesDebouncer = new Debouncer(TimeSpan.FromSeconds(1), BroadcastDiscoveredDevices);

        GrayjayServer.Instance.WebSocket.OnNewClient += (c) =>
        {
            BroadcastDiscoveredDevices(true);
        };
    }

    private async void BroadcastDiscoveredDevices() => BroadcastDiscoveredDevices(false);
    private async void BroadcastDiscoveredDevices(bool force = false)
    {
        try
        {
            var current = DiscoveredDevices.Select(v => v.DeviceInfo).ToList();
            if (force || HasUpdatedChanged(current))
            {
                _lastUpdate = current;
                await GrayjayServer.Instance.WebSocket.Broadcast(current, "discoveredDevicesUpdated");
            }
        }
        catch (Exception e)
        {
            Logger.i(nameof(StateCasting), $"Broadcast discovered devices failed '{e.Message}': {e.StackTrace}");
        }
    }
    abstract protected bool HasUpdatedChanged(List<CastingDeviceInfo> current);

    public abstract void Start();

    public abstract void Dispose();

    private async Task UpdateTimeLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            CastingDevice? device;
            lock (_castingDeviceLock)
            {
                device = _activeDevice;
                if (device == null)
                    return;
            }

            await Task.Delay(1000, ct);

            if (!device.PlaybackState.IsPlaying)
                continue;

            var expectedCurrentTime = device.PlaybackState.ExpectedCurrentTime;
            device.PlaybackState.SetTime(expectedCurrentTime);
        }
    }

    public void AddPinnedDevice(CastingDeviceInfo castingDeviceInfo)
    {
        _pinnedDevices.Save(castingDeviceInfo);
    }

    public void RemovePinnedDevice(CastingDeviceInfo castingDeviceInfo)
    {
        _pinnedDevices.Delete(castingDeviceInfo);
    }

    abstract public void Connect(CastingDevice castingDevice);

    protected void StartTimeLoop()
    {
        StopTimeLoop();
        _updateTimeCts = new CancellationTokenSource();
        _ = Task.Run(async () => await UpdateTimeLoop(_updateTimeCts.Token));
    }

    protected void StopTimeLoop()
    {
        if (_updateTimeCts != null)
        {
            _updateTimeCts.Cancel();
            _updateTimeCts.Dispose();
            _updateTimeCts = null;
        }
    }

    abstract public void Disconnect();

    protected void BindEvents(CastingDevice castingDevice)
    {
        castingDevice.PlaybackState.IsPlayingChanged += HandleIsPlayingChanged;
        castingDevice.PlaybackState.DurationChanged += HandleDurationChanged;
        castingDevice.PlaybackState.TimeChanged += HandleTimeChanged;
        castingDevice.PlaybackState.VolumeChanged += HandleVolumeChanged;
        castingDevice.PlaybackState.SpeedChanged += HandleSpeedChanged;
        castingDevice.ConnectionState.StateChanged += HandleStateChanged;
    }

    protected void UnbindEvents(CastingDevice castingDevice)
    {
        castingDevice.PlaybackState.IsPlayingChanged -= HandleIsPlayingChanged;
        castingDevice.PlaybackState.DurationChanged -= HandleDurationChanged;
        castingDevice.PlaybackState.TimeChanged -= HandleTimeChanged;
        castingDevice.PlaybackState.VolumeChanged -= HandleVolumeChanged;
        castingDevice.PlaybackState.SpeedChanged -= HandleSpeedChanged;
        castingDevice.ConnectionState.StateChanged -= HandleStateChanged;
    }

    private async void HandleIsPlayingChanged(bool isPlaying)
    {
        IsPlayingChanged?.Invoke(isPlaying);

        var activeDevice = _activeDevice;
        if (activeDevice != null && (activeDevice.DeviceInfo.Type == CastProtocolType.Airplay || activeDevice.DeviceInfo.Type == CastProtocolType.Chromecast))
        {
            if (isPlaying)
                StartTimeLoop();
            else
                StopTimeLoop();
        }

        try
        {
            await GrayjayServer.Instance.WebSocket.Broadcast(isPlaying, "activeDeviceIsPlayingChanged");
        }
        catch (Exception e)
        {
            Logger.e(nameof(StateCasting), "Failed to notify active device IsPlayingChanged.", e);
        }
    }

    private async void HandleDurationChanged(TimeSpan duration)
    {
        DurationChanged?.Invoke(duration);

        try
        {
            await GrayjayServer.Instance.WebSocket.Broadcast(duration.TotalSeconds, "activeDeviceDurationChanged");
        }
        catch (Exception e)
        {
            Logger.e(nameof(StateCasting), "Failed to notify active device DurationChanged.", e);
        }
    }

    private async void HandleTimeChanged(TimeSpan time)
    {
        TimeChanged?.Invoke(time);

        try
        {
            await GrayjayServer.Instance.WebSocket.Broadcast(time.TotalSeconds, "activeDeviceTimeChanged");
        }
        catch (Exception e)
        {
            Logger.e(nameof(StateCasting), "Failed to notify active device TimeChanged.", e);
        }
    }

    private async void HandleVolumeChanged(double volume)
    {
        VolumeChanged?.Invoke(volume);

        try
        {
            await GrayjayServer.Instance.WebSocket.Broadcast(volume, "activeDeviceVolumeChanged");
        }
        catch (Exception e)
        {
            Logger.e(nameof(StateCasting), "Failed to notify active device VolumeChanged.", e);
        }
    }

    private async void HandleSpeedChanged(double speed)
    {
        SpeedChanged?.Invoke(speed);

        try
        {
            await GrayjayServer.Instance.WebSocket.Broadcast(speed, "activeDeviceSpeedChanged");
        }
        catch (Exception e)
        {
            Logger.e(nameof(StateCasting), "Failed to notify active device SpeedChanged.", e);
        }
    }

    private async void HandleStateChanged(CastConnectionState state)
    {
        StateChanged?.Invoke(state);

        try
        {
            await GrayjayServer.Instance.WebSocket.Broadcast(state, "activeDeviceStateChanged");
        }
        catch (Exception e)
        {
            Logger.e(nameof(StateCasting), "Failed to notify active device StateChanged.", e);
        }
    }

    public abstract CastingDevice CreateDevice(CastingDeviceInfo info);

    private static object _lockObject = new object();
    private static StateCasting? _instance = null;
    public static StateCasting Instance
    {
        get
        {
            lock (_lockObject)
            {
                if (_instance == null) {
                    if (Grayjay.ClientServer.Settings.GrayjaySettings.Instance.Casting.Experimental) {
                        _instance = new StateCastingExperimental();
                    } else {
                        _instance = new StateCastingLegacy();
                    }
                }
                return _instance;
            }
        }
    }
}
