using System.Net.Sockets;

namespace Grayjay.ClientServer.Casting;

public abstract class CastingDeviceLegacy: CastingDevice
{
    CastingDeviceInfo devInfo;
    public CastingDeviceLegacy(CastingDeviceInfo deviceInfo)
    {
        devInfo = deviceInfo;
    }

    public override CastingDeviceInfo DeviceInfo { get => devInfo; set => devInfo = value; }

    public async Task<TcpClient?> ConnectAsync(CancellationToken cancellationToken = default)
        => await Utilities.ConnectAsync(DeviceInfo.IPAddresses, DeviceInfo.Port, TimeSpan.FromSeconds(2), cancellationToken);
}
