using Grayjay.ClientServer.Serializers;
using SyncClient;
using SyncShared;
using System.Text;

namespace Grayjay.ClientServer.Sync
{
    public static class SyncExtensions
    {
        public static Task SendJsonDataAsync(this SyncSession session, byte subOpcode, object data, CancellationToken cancellationToken = default)
            => session.SendAsync(Opcode.DATA, subOpcode, Encoding.UTF8.GetBytes(GJsonSerializer.AndroidCompatible.SerializeObj(data)), contentEncoding: ContentEncoding.Gzip, cancellationToken: cancellationToken);
    }
}
