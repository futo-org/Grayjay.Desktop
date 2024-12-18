using Newtonsoft.Json;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

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
                return Path.GetFileName("Grayjay.Desktop.CEF.exe");
            else if (OperatingSystem.IsLinux())
                return Path.GetFileName("Grayjay.Desktop.CEF");
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
