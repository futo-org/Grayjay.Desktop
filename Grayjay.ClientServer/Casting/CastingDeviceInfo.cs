using System.Net;
using System.Text.Json.Serialization;

namespace Grayjay.ClientServer.Casting;

public class CastingDeviceInfo
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required CastProtocolType Type { get; init; }
    public required List<string> Addresses { get; set; }
    [JsonIgnore]
    public List<IPAddress> IPAddresses => Addresses.Select(v => IPAddress.Parse(v)).ToList();
    public required int Port { get; init; }

    public override bool Equals(object? obj)
    {
        if (!(obj is CastingDeviceInfo i))
            return false;

        return Name == i.Name && Type == i.Type && Port == i.Port && Addresses.OrderBy(x => x).SequenceEqual(i.Addresses.OrderBy(x => x));
    }

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 23 + Name.GetHashCode();
            hash = hash * 23 + Type.GetHashCode();
            hash = hash * 23 + Port.GetHashCode();
            foreach (var address in Addresses.OrderBy(x => x))
            {
                hash = hash * 23 + address.GetHashCode();
            }
            return hash;
        }
    }

    public static CastingDeviceInfo FromRsInfo(FCast.SenderSDK.DeviceInfo rsInfo) {
        return new CastingDeviceInfo()
        {
            Addresses = rsInfo.addresses.Select(a => FCast.SenderSDK.FcastSenderSdkMethods.UrlFormatIpAddr(a)).ToList(),
            Id = rsInfo.name,
            Name = rsInfo.name,
            Port = rsInfo.port,
            Type = rsInfo.protocol switch
            {
                FCast.SenderSDK.ProtocolType.Chromecast => CastProtocolType.Chromecast,
                FCast.SenderSDK.ProtocolType.FCast => CastProtocolType.FCast,
            }
        };
    }
}
