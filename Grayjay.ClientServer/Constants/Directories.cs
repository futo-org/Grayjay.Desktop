using System.Reflection;

namespace Grayjay.ClientServer.Constants;

public static class Directories
{
    private static readonly Lazy<string> _userDirectory = new Lazy<string>(() =>
    {
        string userDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string dir;

        if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
            dir = Path.Combine(userDir, "Grayjay");
        else if (OperatingSystem.IsMacOS())
            dir = Path.Combine(userDir, "Library/Application Support", "Grayjay");
        else
            throw new NotImplementedException("Unknown operating system");

        EnsureDirectoryExists(dir);
        return dir;
    });

    private static readonly Lazy<string> _baseDirectory = new Lazy<string>(() =>
    {
        string? assemblyFile = Assembly.GetEntryAssembly()?.Location;
        string dir;

        if (!string.IsNullOrEmpty(assemblyFile))
            dir = Path.GetDirectoryName(assemblyFile)!;
        else
            dir = AppContext.BaseDirectory;

        if (!File.Exists(Path.Combine(dir, "Portable")) || OperatingSystem.IsMacOS())
            dir = User;

        EnsureDirectoryExists(dir);
        return dir;
    });

    private static readonly Lazy<string> _temporaryDirectory = new Lazy<string>(() =>
    {
        string dir = Path.Combine(Base, "temp_files");
        EnsureDirectoryExists(dir);
        return dir;
    });

    public static string User => _userDirectory.Value;

    public static string Base => _baseDirectory.Value;

    public static string Temporary => _temporaryDirectory.Value;

    private static void EnsureDirectoryExists(string path)
    {
        if (!Directory.Exists(path))
            Directory.CreateDirectory(path);
    }
}