using FUTO.MDNS;
using Grayjay.ClientServer.Casting;

namespace Grayjay.ClientServer.States;

using Logger = Desktop.POC.Logger;

public class StateCastingLegacy : StateCasting
{
    private ServiceDiscoverer? _serviceDiscoverer = null;
    override public event Action<CastingDevice?>? ActiveDeviceChanged;
    private List<CastingDeviceInfo> _lastUpdate = new List<CastingDeviceInfo>();

    override protected bool HasUpdatedChanged(List<CastingDeviceInfo> current)
    {
        var last = _lastUpdate;
        return last.Count != current.Count || (current.Count > 0 && !current.Any(x => last.FirstOrDefault(y => y.Id == x.Id) != x));
    }

    override public void Connect(CastingDevice castingDevice)
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

    override public void Disconnect()
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

    override public async void Start()
    {
        Logger.i(nameof(StateCastingLegacy), "Casting listener starting");
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
                    _castingDevices[deviceInfo.Id] = CreateDevice(deviceInfo);
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

    override public CastingDevice CreateDevice(CastingDeviceInfo info)
    {
        return info.Type switch
        {
            CastProtocolType.Chromecast => new ChromecastCastingDevice(info),
            CastProtocolType.Airplay => new AirPlayCastingDevice(info),
            CastProtocolType.FCast => new FCastCastingDevice(info),
            _ => throw new Exception($"Invalid cast protocol type {info.Type}")
        };
    }

    override public void Dispose()
    {
        Logger.i(nameof(StateCasting), "Disposing.");
        Disconnect();
        _serviceDiscoverer?.Dispose();
        _serviceDiscoverer = null;
    }
}
