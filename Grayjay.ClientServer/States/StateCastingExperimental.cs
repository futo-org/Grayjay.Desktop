using System.Net;
using Grayjay.ClientServer.Casting;

namespace Grayjay.ClientServer.States;

using Logger = Desktop.POC.Logger;

class DiscoveryEventHandler : FCast.SenderSDK.DeviceDiscovererEventHandler
{
    public event Action<FCast.SenderSDK.DeviceInfo>? OnAvailable;
    public event Action<FCast.SenderSDK.DeviceInfo>? OnChanged;
    public event Action<string>? OnRemoved;

    public void DeviceAvailable(FCast.SenderSDK.DeviceInfo deviceInfo) => OnAvailable?.Invoke(deviceInfo);

    public void DeviceChanged(FCast.SenderSDK.DeviceInfo deviceInfo) => OnChanged?.Invoke(deviceInfo);

    public void DeviceRemoved(string deviceName) => OnRemoved?.Invoke(deviceName);
}

class CastLogger: FCast.SenderSDK.LogHandler {
    public void Log(FCast.SenderSDK.LogLevel level, String tag, String message) {
        Logger.l(
            level switch {
                FCast.SenderSDK.LogLevel.Error => Desktop.POC.LogLevel.Error,
                FCast.SenderSDK.LogLevel.Warn => Desktop.POC.LogLevel.Warning,
                FCast.SenderSDK.LogLevel.Info => Desktop.POC.LogLevel.Info,
                FCast.SenderSDK.LogLevel.Debug => Desktop.POC.LogLevel.Verbose,
                _ => Desktop.POC.LogLevel.Debug,
            },
            tag,
            message
        );
    }
}

public class StateCastingExperimental: StateCasting
{
    override public event Action<CastingDevice?>? ActiveDeviceChanged;

    private FCast.SenderSDK.CastContext _context = new FCast.SenderSDK.CastContext();

    private List<CastingDeviceInfo> _lastUpdate = new List<CastingDeviceInfo>();

    override protected bool HasUpdatedChanged(List<CastingDeviceInfo> current) {
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
                Logger.e(nameof(StateCastingExperimental), "Failed to notify active device changed.", e);
            }
        });
    }

    private String FormatDeviceInfo(FCast.SenderSDK.DeviceInfo devInfo) {
        return $"{{ name = {devInfo.name}, protocol = {devInfo.protocol}, addresses = [{String.Join(", ", devInfo.addresses.Select(addr => FCast.SenderSDK.FcastSenderSdkMethods.UrlFormatIpAddr(addr)))}], port = {devInfo.port} }}";
    }

    override public void Start() {
        FCast.SenderSDK.FcastSenderSdkMethods.InitCustomLogger(new CastLogger());
        DiscoveryEventHandler eventHandler = new DiscoveryEventHandler();
        eventHandler.OnAvailable += (info) => {
            Logger.d(nameof(StateCastingExperimental), $"Device available: {FormatDeviceInfo(info)}");
            CastingDeviceInfo compatInfo = CastingDeviceInfo.FromRsInfo(info);
            CastingDeviceExperimentalWrapper comaptDevice =
                new CastingDeviceExperimentalWrapper(_context.CreateDeviceFromInfo(info), compatInfo);
            _castingDevices[compatInfo.Id] = comaptDevice;
            _broadcastDevicesDebouncer.Call();
        };

        eventHandler.OnChanged += (info) => {
            Logger.d(nameof(StateCastingExperimental), $"Device changed: {FormatDeviceInfo(info)}");
            CastingDevice? dev = _castingDevices[info.name];
            if (dev != null && dev is CastingDeviceExperimentalWrapper expDev) {
                expDev.UpdateInfo(info);
            }
            _broadcastDevicesDebouncer.Call();
        };

        eventHandler.OnRemoved += (deviceName) => {
            Logger.d(nameof(StateCastingExperimental), $"Device removed: {deviceName}");
            _castingDevices.Remove(deviceName);
            _broadcastDevicesDebouncer.Call();
        };

        _context.StartDiscovery(eventHandler);
    }

    private FCast.SenderSDK.IpAddr IPAddressToRsIpAddr(IPAddress a) {
        byte[] bytes = a.GetAddressBytes();
        if (bytes.Length == 4)
        {
            return new FCast.SenderSDK.IpAddr.V4(bytes[0], bytes[1], bytes[2], bytes[3]);
        }
        else if (bytes.Length == 16)
        {
            return new FCast.SenderSDK.IpAddr.V6(bytes[0], bytes[1], bytes[2], bytes[3], bytes[4], bytes[5], bytes[6], bytes[7], bytes[8], bytes[9], bytes[10], bytes[11], bytes[12], bytes[13], bytes[14], bytes[15], (uint)a.ScopeId);
        }
        else
        {
            throw new Exception($"Ip address of length {bytes.Length} is invalid");
        }
    }

    override public CastingDevice CreateDevice(CastingDeviceInfo info) {
        FCast.SenderSDK.ProtocolType protoType = info.Type switch
        {
            CastProtocolType.Chromecast => FCast.SenderSDK.ProtocolType.Chromecast,
            CastProtocolType.FCast => FCast.SenderSDK.ProtocolType.FCast,
            _ => throw new Exception($"Invalid cast protocol type {info.Type}")
        };

        FCast.SenderSDK.DeviceInfo rsDeviceInfo = new FCast.SenderSDK.DeviceInfo(
            info.Name,
            protoType,
            info.IPAddresses.Select(a => IPAddressToRsIpAddr(a)).ToArray(),
            (ushort)info.Port
        );

        return new CastingDeviceExperimentalWrapper(_context.CreateDeviceFromInfo(rsDeviceInfo), info);
    }

    override public void Dispose() {}
}
