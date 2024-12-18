using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Grayjay.ClientServer.Crypto;

[SupportedOSPlatform("windows")]
public class WindowsKeyring : IKeyring
{
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredWriteW(ref CREDENTIAL userCredential, uint flags);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredReadW(string target, CRED_TYPE type, int reservedFlag, out IntPtr credentialPtr);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CredDeleteW(string target, CRED_TYPE type, int flags);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern void CredFree(IntPtr buffer);

    private enum CRED_TYPE : uint
    {
        GENERIC = 1
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public uint Flags;
        public CRED_TYPE Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }

    public void SaveKey(string keyName, byte[] key)
    {
        var credential = new CREDENTIAL
        {
            Type = CRED_TYPE.GENERIC,
            TargetName = Marshal.StringToCoTaskMemUni(keyName),
            CredentialBlobSize = (uint)key.Length,
            CredentialBlob = Marshal.AllocCoTaskMem(key.Length),
            Persist = (uint)1 // CRED_PERSIST_LOCAL_MACHINE
        };

        try
        {
            Marshal.Copy(key, 0, credential.CredentialBlob, key.Length);

            var result = CredWriteW(ref credential, 0);
            if (!result)
                throw new Exception("Failed to save key.");
        }
        finally
        {
            Marshal.FreeCoTaskMem(credential.TargetName);
            Marshal.FreeCoTaskMem(credential.CredentialBlob);
        }
    }

    public byte[]? RetrieveKey(string keyName)
    {
        IntPtr credPtr;

        var readResult = CredReadW(keyName, CRED_TYPE.GENERIC, 0, out credPtr);
        if (!readResult)
            return null;

        try
        {
            var s = Marshal.PtrToStructure(credPtr, typeof(CREDENTIAL));
            if (s == null)
                return null;

            var credential = (CREDENTIAL)s;

            byte[] key = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, key, 0, (int)credential.CredentialBlobSize);

            return key;
        }
        finally
        {
            CredFree(credPtr);
        }
    }

    public void DeleteKey(string keyName)
    {
        if (!CredDeleteW(keyName, CRED_TYPE.GENERIC, 0))
            throw new Exception("Failed to delete key.");
    }
}