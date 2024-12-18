using System.Security.Cryptography;
using Grayjay.ClientServer.Crypto;

namespace Grayjay.Desktop.Tests
{
    [TestClass]
    public class EncryptionProviderTests
    {
        private byte[] GenerateRandomBytes(int size)
        {
            byte[] rnd = new byte[size];
            using RandomNumberGenerator rng = RandomNumberGenerator.Create();
            rng.GetBytes(rnd);
            return rnd;
        }

        [TestMethod]
        public void Test_Roundtrip()
        {
            var data = GenerateRandomBytes(32);
            var encrypted = EncryptionProvider.Instance.Encrypt(data);
            var decrypted = EncryptionProvider.Instance.Decrypt(encrypted);
            CollectionAssert.AreEqual(data, decrypted);
        }
    }
}
