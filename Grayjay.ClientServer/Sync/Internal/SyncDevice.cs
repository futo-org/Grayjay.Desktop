using static Microsoft.ClearScript.V8.V8CpuProfile;

namespace Grayjay.ClientServer.Sync.Internal
{
    public class SyncDevice
    {
        public string PublicKey { get; set; }
        public string? DisplayName { get; set; }
        public string Metadata { get; set; }
        public int LinkType { get; set; }
    }
}
