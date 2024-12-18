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
    public CastingDevice ToCastingDevice()
    {
        switch (Type)
        {
            case CastProtocolType.Chromecast:
                return new ChromecastCastingDevice(this);
            case CastProtocolType.Airplay:
                return new AirPlayCastingDevice(this);
            case CastProtocolType.FCast:
                return new FCastCastingDevice(this);
            default:
                throw new Exception($"Not a valid cast protocol type {Type}.");
        }
    }
}