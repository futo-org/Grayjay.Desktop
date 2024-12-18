using System.Security.Cryptography;
using Grayjay.ClientServer.Crypto;

namespace Grayjay.Desktop.Tests
{
    [TestClass]
    public class KeyringTests
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
            var id = Guid.NewGuid().ToString();
            byte[] pw1 = GenerateRandomBytes(32);
            byte[] pw2 = GenerateRandomBytes(32);

            Keyring.Instance.SaveKey(id, pw1);
            CollectionAssert.AreEqual(pw1, Keyring.Instance.RetrieveKey(id));
            Keyring.Instance.SaveKey(id, pw2);
            CollectionAssert.AreEqual(pw2, Keyring.Instance.RetrieveKey(id));
            Keyring.Instance.DeleteKey(id);
            Assert.IsNull(Keyring.Instance.RetrieveKey(id));
        }
    }
}
