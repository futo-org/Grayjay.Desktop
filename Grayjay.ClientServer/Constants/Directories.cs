using System.Reflection;

namespace Grayjay.ClientServer.Constants;

public static class Directories
{
    private static readonly Lazy<string> _baseDirectory = new Lazy<string>(() =>
    {
        string? assemblyFile = Assembly.GetEntryAssembly()?.Location;
        string dir;

        if (!string.IsNullOrEmpty(assemblyFile))
            dir = Path.GetDirectoryName(assemblyFile)!;
        else
            dir = AppContext.BaseDirectory;

        if (!File.Exists(Path.Combine(dir, "Portable")) || OperatingSystem.IsMacOS())
        {
            string userDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (OperatingSystem.IsWindows())
            {
                string oldStyleDir = Path.Combine(userDir, "Grayjay");
                if (Directory.Exists(oldStyleDir))
                    dir = oldStyleDir;
                else
                {
                    string appDataDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                    dir = Path.Combine(appDataDir, "Grayjay");
                }
            }
            else if (OperatingSystem.IsLinux())
            {
                string oldStyleDir = Path.Combine(userDir, "Grayjay");
                if (Directory.Exists(oldStyleDir))
                    dir = oldStyleDir;
                else
                {
                    string? xdgDataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
                    if (!string.IsNullOrEmpty(xdgDataHome) && Directory.Exists(xdgDataHome))
                        dir = Path.Combine(xdgDataHome, "Grayjay");
                    else
                    {
                        string localShareDir = Path.Combine(userDir, ".local", "share");
                        if (Directory.Exists(localShareDir))
                            dir = Path.Combine(localShareDir, "Grayjay");
                        else
                        {
                            string configDir = Path.Combine(userDir, ".config");
                            if (Directory.Exists(configDir))
                                dir = Path.Combine(configDir, "Grayjay");
                            else
                                dir = oldStyleDir; // Fallback to old-style
                        }
                    }
                }
            }
            else if (OperatingSystem.IsMacOS())
            {
                string oldStyleDir = Path.Combine(userDir, "Library/Application Support", "Grayjay");
                string newStyleDir = Path.Combine(userDir, "Containers", "com.futo.grayjay.desktop", "Data", "Library", "Application Support");
                if (Directory.Exists(oldStyleDir))
                    dir = oldStyleDir;
                else
                    dir = newStyleDir;
            }
            else
            {
                throw new NotImplementedException("Unknown operating system");
            }

            EnsureDirectoryExists(dir);
            // TODO: Allow users to choose their own directory?
            return dir;
        }

        EnsureDirectoryExists(dir);
        return dir;
    });

    private static readonly Lazy<string> _temporaryDirectory = new Lazy<string>(() =>
    {
        string dir = Path.Combine(Base, "temp_files");
        EnsureDirectoryExists(dir);
        return dir;
    });

    public static string Base => _baseDirectory.Value;

    public static string Temporary => _temporaryDirectory.Value;

    private static void EnsureDirectoryExists(string path)
    {
        if (!Directory.Exists(path))
            Directory.CreateDirectory(path);
    }
}