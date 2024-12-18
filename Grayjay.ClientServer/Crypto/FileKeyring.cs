namespace Grayjay.ClientServer.Crypto;

public class FileKeyring : IKeyring
{
    private readonly string _storageDirectory;

    public FileKeyring(string storageDirectory)
    {
        if (string.IsNullOrWhiteSpace(storageDirectory))
            throw new ArgumentException("Storage directory cannot be null or empty.", nameof(storageDirectory));

        _storageDirectory = storageDirectory;

        if (!Directory.Exists(_storageDirectory))
            Directory.CreateDirectory(_storageDirectory);
    }

    public void SaveKey(string keyName, byte[] key)
    {
        if (string.IsNullOrWhiteSpace(keyName))
            throw new ArgumentException("Key name cannot be null or empty.", nameof(keyName));

        if (key == null || key.Length == 0)
            throw new ArgumentException("Key cannot be null or empty.", nameof(key));

        string filePath = GetKeyFilePath(keyName);
        File.WriteAllBytes(filePath, key);
    }

    public byte[]? RetrieveKey(string keyName)
    {
        if (string.IsNullOrWhiteSpace(keyName))
            throw new ArgumentException("Key name cannot be null or empty.", nameof(keyName));

        string filePath = GetKeyFilePath(keyName);
        if (!File.Exists(filePath))
            return null;

        return File.ReadAllBytes(filePath);
    }

    public void DeleteKey(string keyName)
    {
        if (string.IsNullOrWhiteSpace(keyName))
            throw new ArgumentException("Key name cannot be null or empty.", nameof(keyName));

        string filePath = GetKeyFilePath(keyName);

        if (File.Exists(filePath))
            File.Delete(filePath);
    }

    private string GetKeyFilePath(string keyName)
    {
        string safeFileName = string.Join("_", keyName.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(_storageDirectory, safeFileName);
    }
}
