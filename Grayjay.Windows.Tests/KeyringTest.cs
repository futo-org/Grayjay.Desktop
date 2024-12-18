using Grayjay.ClientServer.Crypto;
using System.Runtime.Versioning;
using System.Text;

namespace Grayjay.Windows.Tests
{
    [TestClass]
    public class KeyringTest
    {
        [SupportedOSPlatform("windows")]
        [TestMethod]
        public void KeyringSaveLoad()
        {
            var keyring = new WindowsKeyring();
            var keyName = "Grayjay_Test_Key";
            var secret = "SECRET";

            var resBefore = keyring.RetrieveKey(keyName);
            if (resBefore != null)
            {
                var retrievedBefore = Encoding.UTF8.GetString(keyring.RetrieveKey(keyName)!);
                Assert.AreEqual(secret, retrievedBefore);

                keyring.DeleteKey(keyName);
            }

            var res = keyring.RetrieveKey(keyName);
            Assert.IsNull(keyring.RetrieveKey(keyName));

            keyring.SaveKey(keyName, Encoding.UTF8.GetBytes(secret));

            var retrieved = Encoding.UTF8.GetString(keyring.RetrieveKey(keyName)!);
            Assert.AreEqual(secret, retrieved);
        }
    }
}