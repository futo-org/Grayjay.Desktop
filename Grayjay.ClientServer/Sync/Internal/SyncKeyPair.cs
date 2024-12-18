namespace Grayjay.ClientServer.Sync.Internal;

public class SyncKeyPair
{
    public string PublicKey { get; set; }
    public string PrivateKey { get; set; }
    public int Version { get; set; }

    public SyncKeyPair(int version, string publicKey, string privateKey)
    {
        PublicKey = publicKey;
        PrivateKey = privateKey;
        Version = version;
    }
}