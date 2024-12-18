using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Grayjay.ClientServer.Crypto;

[SupportedOSPlatform("macos")]
public class MacOSKeyring : IKeyring
{
    [DllImport("../Frameworks/Keychain.framework/Keychain", EntryPoint = "SaveKey", CallingConvention = CallingConvention.Cdecl)]
    private static extern int LibSaveKey(string keyName, byte[] key, int keySize);

    [DllImport("../Frameworks/Keychain.framework/Keychain", EntryPoint = "RetrieveKey", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr LibRetrieveKey(string keyName, out int length);

    [DllImport("../Frameworks/Keychain.framework/Keychain", EntryPoint = "FreeKey", CallingConvention = CallingConvention.Cdecl)]
    private static extern int LibFreeKey(IntPtr key);

    [DllImport("../Frameworks/Keychain.framework/Keychain", EntryPoint = "DeleteKey", CallingConvention = CallingConvention.Cdecl)]
    private static extern int LibDeleteKey(string keyName);

    public void SaveKey(string keyName, byte[] key)
    {
        var result = LibSaveKey(keyName, key, key.Length);
        if (result != 0)
            throw new Exception($"Failed to save key {result}.");
    }

    public byte[]? RetrieveKey(string keyName)
    {
        var keyPointer = LibRetrieveKey(keyName, out int length);
        if (keyPointer == IntPtr.Zero || length == 0)
            return null;

        try
        {
            var keyData = new byte[length];
            Marshal.Copy(keyPointer, keyData, 0, length);
            return keyData;
        }
        finally
        {
            LibFreeKey(keyPointer);
        }
    }

    public void DeleteKey(string keyName)
    {
        var result = LibDeleteKey(keyName);
        if (result != 0)
            throw new Exception($"Failed to delete key {result}.");
    }
}