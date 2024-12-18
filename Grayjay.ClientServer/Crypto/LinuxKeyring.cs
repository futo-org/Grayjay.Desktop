using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Grayjay.ClientServer.Crypto;

[SupportedOSPlatform("linux")]
public class LinuxKeyring : IKeyring
{
    private const string LibSecret = "libsecret-1.so.0";

    [DllImport(LibSecret, EntryPoint = "secret_password_store_sync", CallingConvention = CallingConvention.Cdecl)]
    private static extern bool secret_password_store_sync(ref SecretSchema schema, IntPtr collection, string label, string password, IntPtr cancellable, ref IntPtr error, string attribute1, string value1, IntPtr endOfAttributes);

    [DllImport(LibSecret, EntryPoint = "secret_password_lookup_sync", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr secret_password_lookup_sync(ref SecretSchema schema, IntPtr cancellable, ref IntPtr error, string attribute1, string value1, IntPtr endOfAttributes);

    [DllImport(LibSecret, EntryPoint = "secret_password_clear_sync", CallingConvention = CallingConvention.Cdecl)]
    private static extern bool secret_password_clear_sync(ref SecretSchema schema, IntPtr cancellable, ref IntPtr error, string attribute1, string value1, IntPtr endOfAttributes);

    [DllImport(LibSecret, EntryPoint = "secret_password_free", CallingConvention = CallingConvention.Cdecl)]
    private static extern void secret_password_free(IntPtr password);

    [DllImport("glib-2.0", EntryPoint = "g_error_free", CallingConvention = CallingConvention.Cdecl)]
    private static extern void g_error_free(IntPtr error);

    [StructLayout(LayoutKind.Sequential)]
    private struct SecretSchema
    {
        public string name;
        public int flags;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public SecretSchemaAttribute[] attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecretSchemaAttribute
    {
        public string? name;
        public SecretSchemaAttributeType type;
    }

    private enum SecretSchemaAttributeType
    {
        SECRET_SCHEMA_ATTRIBUTE_STRING = 0
    }

    private SecretSchema _schema;

    public LinuxKeyring(string schemaNamespace)
    {
        _schema = new SecretSchema
        {
            name = schemaNamespace,
            flags = 0,
            attributes = new SecretSchemaAttribute[32]
        };

        _schema.attributes[0] = new SecretSchemaAttribute { name = "keyName", type = SecretSchemaAttributeType.SECRET_SCHEMA_ATTRIBUTE_STRING };
        _schema.attributes[1] = new SecretSchemaAttribute { name = null, type = SecretSchemaAttributeType.SECRET_SCHEMA_ATTRIBUTE_STRING };
    }

    public void SaveKey(string keyName, byte[] key)
    {
        IntPtr error = IntPtr.Zero;
        string password = Convert.ToBase64String(key);

        bool result = secret_password_store_sync(ref _schema, IntPtr.Zero, keyName, password, IntPtr.Zero, ref error, "keyName", keyName, IntPtr.Zero);
        if (!result)
            HandleError(error);
    }

    public byte[]? RetrieveKey(string keyName)
    {
        IntPtr error = IntPtr.Zero;
        IntPtr passwordPtr = secret_password_lookup_sync(ref _schema, IntPtr.Zero, ref error, "keyName", keyName, IntPtr.Zero);

        if (error != IntPtr.Zero)
        {
            HandleError(error);
            return null;
        }

        if (passwordPtr == IntPtr.Zero)
            return null;

        try
        {
            string? password = Marshal.PtrToStringAnsi(passwordPtr);
            return password != null ? Convert.FromBase64String(password) : null;
        }
        finally
        {
            secret_password_free(passwordPtr);
        }
    }

    public void DeleteKey(string keyName)
    {
        IntPtr error = IntPtr.Zero;
        bool result = secret_password_clear_sync(ref _schema, IntPtr.Zero, ref error, "keyName", keyName, IntPtr.Zero);
        if (!result)
            HandleError(error);
    }

    private void HandleError(IntPtr errorPtr)
    {
        if (errorPtr != IntPtr.Zero)
        {
            IntPtr messagePtr = Marshal.ReadIntPtr(errorPtr);
            try
            {
                string? message = Marshal.PtrToStringAnsi(messagePtr);
                if (message != null)
                    throw new Exception($"Error: {message}");
            }
            finally
            {
                g_error_free(errorPtr);
            }
        }
    }
}