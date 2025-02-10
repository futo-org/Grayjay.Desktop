using System.Reflection;

namespace Grayjay.ClientServer.Constants
{
    public static class App
    {
        public static int Version { get; } = Assembly.GetExecutingAssembly()?.GetName()?.Version?.Minor ?? -1;
        public static string VersionType { get; } = "stable";
    }
}
