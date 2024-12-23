using Grayjay.Desktop.POC;
using System.Diagnostics;
using System.Net;
using System.Text;

namespace Grayjay.ClientServer
{
    public static class Updater
    {
        private static string _startupArgs = "";
        public static void SetStartupArguments(string args)
        {
            _startupArgs = args;
        }
        private static string GetStartupArguments()
        {
            return _startupArgs;
        }

        public static string GetSelfExecutablePath()
        {
            if (OperatingSystem.IsWindows())
                return Path.GetFileName("Grayjay.exe");
            else if (OperatingSystem.IsLinux())
                return Path.GetFileName("Grayjay");
            else if (OperatingSystem.IsMacOS())
                return Path.GetFileName("../Grayjay.Desktop.app");
            else throw new NotImplementedException();
        }

        public static string GetUpdaterExecutablePath()
        {
            string fileName = null;
            if (OperatingSystem.IsWindows())
                fileName = "FUTO.Updater.Client.exe";
            else if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
                fileName = "FUTO.Updater.Client";
            else
                throw new NotImplementedException();

            if (File.Exists(fileName))
            {
                return Path.GetFullPath(fileName);
            }
            else
            {
                return null;
            }
        }
        public static string GetUpdaterConfigPath()
        {
            string fileName = "UpdaterConfig.json";

            if (File.Exists(fileName))
            {
                return Path.GetFullPath(fileName);
            }
            else
            {
                return null;
            }
        }
        public class UpdaterConfig
        {
            public string Server { get; set; }
            public int Version { get; set; }

            public bool HasValidServer => !string.IsNullOrEmpty(Server);
        }
        public static UpdaterConfig GetUpdaterConfig()
        {
            try
            {
                return System.Text.Json.JsonSerializer.Deserialize<UpdaterConfig>(File.ReadAllText(GetUpdaterConfigPath()));
            }
            catch(Exception ex)
            {
                Logger.e(nameof(Updater), "Failed to get updater config", ex);
                return null;
            }
        }

        public static void Update(int[] processIds, int version = -1)
        {
            string executable = GetUpdaterExecutablePath();
            if (string.IsNullOrEmpty(executable))
                throw new InvalidOperationException("No updater found");

            Process.Start(new ProcessStartInfo()
            {
                FileName = executable,
                Arguments = $"update -process_ids {string.Join(",", processIds)} -executable \"{GetSelfExecutablePath()}\"",// + 
                //    (string.IsNullOrWhiteSpace(_startupArgs) ? "" : " -executable_args " + JsonConvert.SerializeObject(GetStartupArgumentsEscaped())),
                UseShellExecute = true
            });
        }

        public static bool HasUpdate()
        {
            string executable = GetUpdaterExecutablePath();
            if (string.IsNullOrEmpty(executable))
                throw new InvalidOperationException("No updater found");

            var proc = Process.Start(executable, "check");
            proc.WaitForExit();
            switch (proc.ExitCode)
            {
                case 1:
                    return true;
                case 2:
                    return false;
                default:
                    return false;
            }
        }

        public static int GetTargetVersion()
        {
            try
            {
                var config = GetUpdaterConfig();
                if (config == null || string.IsNullOrEmpty(config.Server))
                    return -1;
                if (config.Version > 0)
                    return config.Version;
                using(WebClient client = new WebClient())
                    return int.Parse(client.DownloadString(config.Server + "/VersionLast.json"));
            }
            catch(Exception ex)
            {
                Logger.e(nameof(Updater), "Failed to get last version", ex);
                return -1;
            }
        }
        public class Changelog
        {
            public string Version { get; set; }
            public string Text { get; set; }

            public Changelog(string version, string text)
            {
                Version = version;
                Text = text;
            }
        }
        public static Changelog GetTargetChangelog()
        {
            try
            {
                var config = GetUpdaterConfig();
                if (config == null || !config.HasValidServer)
                    return null;
                var targetVersion = GetTargetVersion();
                if (targetVersion <= 0)
                    return null;
                using(WebClient client = new WebClient())
                {
                    return new Changelog(targetVersion.ToString(), client.DownloadString(config.Server + $"/{targetVersion}/linux-x64/Changelogs/{targetVersion}.txt"));
                }
            }
            catch (Exception ex)
            {
                Logger.e(nameof(Updater), "Failed to get changelog", ex);
                return null;
            }
        }


        public static void RebootTest(int[] processIds, int version = -1)
        {
            string executable = GetUpdaterExecutablePath();
            if (string.IsNullOrEmpty(executable))
                throw new InvalidOperationException("No updater found");

            Process.Start(new ProcessStartInfo()
            {
                FileName = executable,
                Arguments = $"reboot -process_ids {string.Join(",", processIds)} -executable \"{GetSelfExecutablePath()}\"" +
                  (string.IsNullOrWhiteSpace(_startupArgs) ? "" : " -executable_args " + "BASE64:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(GetStartupArguments()))),
                UseShellExecute = true
            });
        }
    }
}
