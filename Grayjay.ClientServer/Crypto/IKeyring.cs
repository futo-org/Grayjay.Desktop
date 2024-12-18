namespace Grayjay.ClientServer.Crypto;

public interface IKeyring
{
    void SaveKey(string keyName, byte[] key);
    byte[]? RetrieveKey(string keyName);
    void DeleteKey(string keyName);
}