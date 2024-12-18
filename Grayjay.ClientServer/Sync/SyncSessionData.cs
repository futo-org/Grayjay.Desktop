namespace Grayjay.ClientServer.Sync
{
    public class SyncSessionData
    {
        public string PublicKey { get; set; }

        public DateTime LastHistory { get; set; } = DateTime.MinValue;


        public SyncSessionData() { }
        public SyncSessionData(string publicKey) { PublicKey = publicKey; }
    }
}
