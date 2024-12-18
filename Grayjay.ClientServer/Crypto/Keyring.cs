using Grayjay.ClientServer.Constants;
using Grayjay.Desktop.POC;

namespace Grayjay.ClientServer.Crypto;

public static class Keyring
{
    private static object _lockObject = new object();
    private static IKeyring? _instance = null;
    public static IKeyring Instance
    {
        get
        {
            lock (_lockObject)
            {
                if (_instance == null)
                {
                    /*if (OperatingSystem.IsWindows())
                        _instance = new WindowsKeyring();
                    else if (OperatingSystem.IsMacOS())
                        _instance = new MacOSKeyring();
                    else if (OperatingSystem.IsLinux())
                        _instance = new LinuxKeyring("com.futo.grayjay");*/
                    //else
                    //{
                        _instance = new FileKeyring(Path.Combine(Directories.Base, "kr"));
                        Logger.i(nameof(Keyring), "Falling back to file keyring");
                    //}
                }

                return _instance;
            }                
        }
    }
}