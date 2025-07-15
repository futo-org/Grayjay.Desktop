using FUTO.MDNS;
using Grayjay.ClientServer.Casting;
using Grayjay.ClientServer.Store;
using Grayjay.Desktop.POC;

namespace Grayjay.ClientServer.States;

using Logger = Desktop.POC.Logger;

public class StateCasting : IDisposable
{
    private ServiceDiscoverer? _serviceDiscoverer = null;
    private readonly Dictionary<string, CastingDevice> _castingDevices = new Dictionary<string, CastingDevice>();
    private readonly object _castingDeviceLock = new object();

    //TODO: Add index for id ?
    private readonly ManagedStore<CastingDeviceInfo> _pinnedDevices = new ManagedStore<CastingDeviceInfo>("pinnedDevices")
        .WithUnique(v => v.Id)
        .WithBackup();

    public List<CastingDeviceInfo> PinnedDevices => _pinnedDevices.GetObjects();
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

    private CastingDevice? _activeDevice;
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

    public event Action<CastingDevice?>? ActiveDeviceChanged;
    public event Action<bool>? IsPlayingChanged;
    public event Action<TimeSpan>? DurationChanged;
    public event Action<TimeSpan>? TimeChanged;
    public event Action<double>? VolumeChanged;
    public event Action<double>? SpeedChanged;
    public event Action<CastConnectionState>? StateChanged;
    private readonly Debouncer _broadcastDevicesDebouncer;
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
    private bool HasUpdatedChanged(List<CastingDeviceInfo> current)
    {
        var last = _lastUpdate;
        return last.Count != current.Count || (current.Count > 0 && !current.Any(x => last.FirstOrDefault(y => y.Id == x.Id) != x));
    }

    public async void Start()
    {
        Logger.i(nameof(StateCasting), "Casting listener starting");
        _serviceDiscoverer = new ServiceDiscoverer(
            "_googlecast._tcp.local",
            "_airplay._tcp.local",
            "_fastcast._tcp.local",
            "_fcast._tcp.local"
        );

        _serviceDiscoverer.OnServicesUpdated += (services) =>
        {
            List<CastingDeviceInfo> deviceInfos;
            lock (_castingDevices)
            {
                deviceInfos = services.Select(s =>
                {
                    var name = s.Texts.FirstOrDefault(s => s.StartsWith("md="))?.Substring("md=".Length);
                    CastProtocolType castProtocolType;
                    if (s.Name.EndsWith("._googlecast._tcp.local"))
                    {
                        castProtocolType = CastProtocolType.Chromecast;
                        if (name == null)
                            name = s.Name.Substring(0, s.Name.Length - "._googlecast._tcp.local".Length);
                    }
                    else if (s.Name.EndsWith("._airplay._tcp.local"))
                    {
                        castProtocolType = CastProtocolType.Airplay;
                        if (name == null)
                            name = s.Name.Substring(0, s.Name.Length - "._airplay._tcp.local".Length);
                    }
                    else if (s.Name.EndsWith("._fastcast._tcp.local"))
                    {
                        castProtocolType = CastProtocolType.FCast;
                        if (name == null)
                            name = s.Name.Substring(0, s.Name.Length - "._fastcast._tcp.local".Length);
                    }
                    else if (s.Name.EndsWith("._fcast._tcp.local"))
                    {
                        castProtocolType = CastProtocolType.FCast;
                        if (name == null)
                            name = s.Name.Substring(0, s.Name.Length - "._fcast._tcp.local".Length);
                    }
                    else
                        return null;

                    return new CastingDeviceInfo()
                    {
                        Addresses = s.Addresses
                            .Where(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork || (a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 && a.IsIPv4MappedToIPv6))
                            .Select(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork ? a.ToString() : a.MapToIPv4().ToString())
                            .ToList(),
                        Id = s.Name,
                        Name = name,
                        Port = s.Port,
                        Type = castProtocolType
                    };
                }).Where(s => s != null).Select(s => s!).ToList();

                foreach (var deviceInfo in deviceInfos)
                    _castingDevices[deviceInfo.Id] = deviceInfo.ToCastingDevice();
            }

            _broadcastDevicesDebouncer.Call();
        };

        /*_serviceDiscoverer.ServiceInstanceShutdown += (s, args) =>
        {
            var txtRecordStrings = args.Message.AdditionalRecords.OfType<TXTRecord>().SelectMany(r => r.Strings).ToList();
            var name = txtRecordStrings.FirstOrDefault(s => s.StartsWith("md="))?.Substring("md=".Length) ?? args.ServiceInstanceName.Labels[0];
            var id = txtRecordStrings.FirstOrDefault(s => s.StartsWith("id="))?.Substring("id=".Length) ?? args.ServiceInstanceName.Labels[0];
            var serviceName = args.ServiceInstanceName;

            Logger.i(nameof(StateCasting), $"Lost mDNS {serviceName} (name: {name}, id: {id}).");
            _castingDevices.Remove(id, out _);
        };*/

        _ = Task.Run(async () =>
        {
            try
            {
                await _serviceDiscoverer.RunAsync();
            }
            catch (Exception e)
            {
                Logger.Error(nameof(StateApp), $"Exception occurred when broadcasting records: {e.Message}, {e.StackTrace}", e);
            }
        });
    }

    public void Dispose()
    {
        Logger.i(nameof(StateCasting), "Disposing.");
        Disconnect();
        _serviceDiscoverer?.Dispose();
        _serviceDiscoverer = null;
    }

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

    public void Connect(CastingDevice castingDevice)
    {
        if (ActiveDevice == castingDevice)
            return;

        try
        {
            _ = _pinnedDevices.SaveAsync(castingDevice.DeviceInfo);
        }
        catch (Exception e)
        {
            Logger.w(nameof(StateCasting), "Failed to save pinned device.", e);
        }

        lock (_castingDeviceLock)
        {
            var oldActiveDevice = ActiveDevice;
            if (oldActiveDevice != null)
                UnbindEvents(oldActiveDevice);

            BindEvents(castingDevice);
            castingDevice.Start();

            _activeDevice = castingDevice;
            oldActiveDevice?.Stop();
        }

        ActiveDeviceChanged?.Invoke(castingDevice);

        Task.Run(async () =>
        {
            try
            {
                await GrayjayServer.Instance.WebSocket.Broadcast(castingDevice.DeviceInfo, "activeDeviceChanged");
            }
            catch (Exception e)
            {
                Logger.e(nameof(StateCasting), "Failed to notify active device changed.", e);
            }
        });
    }

    private void StartTimeLoop()
    {
        StopTimeLoop();
        _updateTimeCts = new CancellationTokenSource();
        _ = Task.Run(async () => await UpdateTimeLoop(_updateTimeCts.Token));
    }

    private void StopTimeLoop()
    {
        if (_updateTimeCts != null)
        {
            _updateTimeCts.Cancel();
            _updateTimeCts.Dispose();
            _updateTimeCts = null;
        }
    }

    public void Disconnect()
    {
        lock (_castingDeviceLock)
        {
            var oldActiveDevice = ActiveDevice;
            if (oldActiveDevice != null)
                UnbindEvents(oldActiveDevice);

            StopTimeLoop();
            _activeDevice = null;
            oldActiveDevice?.Stop();
        }

        ActiveDeviceChanged?.Invoke(null);

        Task.Run(async () =>
        {
            try
            {
                await GrayjayServer.Instance.WebSocket.Broadcast(null, "activeDeviceChanged");
            }
            catch (Exception e)
            {
                Logger.e(nameof(StateCasting), "Failed to notify active device changed.", e);
            }
        });
    }

    private void BindEvents(CastingDevice castingDevice)
    {
        castingDevice.PlaybackState.IsPlayingChanged += HandleIsPlayingChanged;
        castingDevice.PlaybackState.DurationChanged += HandleDurationChanged;
        castingDevice.PlaybackState.TimeChanged += HandleTimeChanged;
        castingDevice.PlaybackState.VolumeChanged += HandleVolumeChanged;
        castingDevice.PlaybackState.SpeedChanged += HandleSpeedChanged;
        castingDevice.ConnectionState.StateChanged += HandleStateChanged;
    }

    private void UnbindEvents(CastingDevice castingDevice)
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

    private static object _lockObject = new object();
    private static StateCasting? _instance = null;
    public static StateCasting Instance
    {
        get
        {
            lock (_lockObject)
            {
                if (_instance == null)
                    _instance = new StateCasting();
                return _instance;
            }
        }
    }
}