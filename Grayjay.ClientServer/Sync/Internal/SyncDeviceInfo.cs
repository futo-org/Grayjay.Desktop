using System.Text.Json.Serialization;

namespace Grayjay.ClientServer.Sync.Internal;

public class SyncDeviceInfo
{
    [JsonPropertyName("publicKey")]
    public string PublicKey { get; set; }

    [JsonPropertyName("addresses")]
    public string[] Addresses { get; set; }

    [JsonPropertyName("port")]
    public int Port { get; set; }

    public SyncDeviceInfo(string publicKey, string[] addresses, int port)
    {
        PublicKey = publicKey;
        Addresses = addresses;
        Port = port;
    }
}
