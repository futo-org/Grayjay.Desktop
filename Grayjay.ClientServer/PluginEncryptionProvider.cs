using Grayjay.ClientServer.Crypto;
using Grayjay.Engine;
using System.Text;

namespace Grayjay.ClientServer
{
    public class PluginEncryptionProvider : IPluginEncryptionProvider
    {
        public string Decrypt(string data)
        {
            return EncryptionProvider.Instance.Decrypt(data);
        }

        public string Encrypt(string data)
        {
            return EncryptionProvider.Instance.Encrypt(data);
        }
    }
}
