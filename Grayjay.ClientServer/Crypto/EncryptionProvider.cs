using System.Security.Cryptography;
using System.Text;

namespace Grayjay.ClientServer.Crypto;

public class EncryptionProvider
{
    private const int Version = 1;
    private const string KeyAlias = "FUTO_Grayjay";
    private const int IvSize = 12;
    private const int TagSize = 128 / 8;
    private AesGcm _aesGcm;

    private EncryptionProvider()
    {
        byte[]? key = Keyring.Instance.RetrieveKey(KeyAlias);
        if (key == null)
        {
            key = GenerateKey();
            Keyring.Instance.SaveKey(KeyAlias, key);
        }

        _aesGcm = new AesGcm(key, TagSize);
    }

    public string Encrypt(string decrypted)
    {
        byte[] decryptedBytes = Encoding.UTF8.GetBytes(decrypted);
        byte[] encryptedBytes = Encrypt(decryptedBytes);
        return Convert.ToBase64String(encryptedBytes);
    }

    public byte[] Encrypt(byte[] decrypted)
    {
        byte[] iv = GenerateIv();
        byte[] encrypted = new byte[decrypted.Length];
        byte[] tag = new byte[TagSize];
        _aesGcm.Encrypt(iv, decrypted, encrypted, tag);

        byte[] result = new byte[1 + iv.Length + encrypted.Length + tag.Length];
        result[0] = Version;
        iv.CopyTo(result, 1);
        encrypted.CopyTo(result, 1 + iv.Length);
        tag.CopyTo(result, 1 + iv.Length + encrypted.Length);

        return result;
    }

    public string Decrypt(string encrypted)
    {
        byte[] encryptedBytes = Convert.FromBase64String(encrypted);
        byte[] decryptedBytes = Decrypt(encryptedBytes);
        return Encoding.UTF8.GetString(decryptedBytes);
    }

    public byte[] Decrypt(byte[] encrypted)
    {
        byte version = encrypted[0];
        if (version != Version)
            throw new Exception("Invalid version. Upgrade required.");

        byte[] iv = new byte[IvSize];
        Array.Copy(encrypted, 1, iv, 0, IvSize);
        byte[] tag = new byte[TagSize];
        Array.Copy(encrypted, encrypted.Length - tag.Length, tag, 0, tag.Length);

        byte[] cipherText = new byte[encrypted.Length - 1 - IvSize - tag.Length];
        Array.Copy(encrypted, 1 + IvSize, cipherText, 0, cipherText.Length);

        byte[] decrypted = new byte[cipherText.Length];
        _aesGcm.Decrypt(iv, cipherText, tag, decrypted);
        return decrypted;
    }

    private byte[] GenerateKey()
    {
        using Aes aes = Aes.Create();
        aes.KeySize = 256;
        aes.GenerateKey();
        return aes.Key;
    }

    private byte[] GenerateIv()
    {
        byte[] iv = new byte[IvSize];
        using RandomNumberGenerator rng = RandomNumberGenerator.Create();
        rng.GetBytes(iv);
        return iv;
    }

    private static object _lockObject = new object();
    private static EncryptionProvider? _instance = null;
    public static EncryptionProvider Instance
    {
        get
        {
            lock (_lockObject)
            {
                if (_instance == null)
                    _instance = new EncryptionProvider();

                return _instance;
            }                
        }
    }
}
