using Grayjay.Desktop.POC;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Grayjay.ClientServer
{
    public class OSHelper
    {
        public static void OpenFolder(string folder)
        {
            if (OperatingSystem.IsWindows())
            {
                Process.Start("explorer", folder);
            }
            else if (OperatingSystem.IsLinux())
            {
                Process.Start("xdg-open", folder);
            }
            else if (OperatingSystem.IsMacOS())
            {
                Process.Start("open", folder);
            }
        }

        public static void OpenFile(string file)
        {
            if (OperatingSystem.IsWindows())
            {
                Process.Start("explorer", new string[] { file });
            }
            else if (OperatingSystem.IsLinux())
            {
                Process.Start("xdg-open", new string[] { file });
            }
            else if (OperatingSystem.IsMacOS())
            {
                Process.Start("open", new string[] { file });
            }
        }

        public static void OpenUrl(string uri)
        {

            if (string.IsNullOrEmpty(uri))
                throw new BadHttpRequestException("Missing uri");

            try
            {
                Process.Start(uri);
            }
            catch
            {
                // hack because of this: https://github.com/dotnet/corefx/issues/10361
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    uri = uri.Replace("&", "^&");
                    Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    Process.Start("xdg-open", uri);
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    Process.Start("open", uri);
                }
                else
                {
                    throw;
                }
            }
        }

        public static string GetComputerName()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return Environment.MachineName;
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return ExecuteCommand("scutil --get ComputerName").Trim();
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                string hostname;

                try
                {
                    hostname = Environment.MachineName;
                    if (!string.IsNullOrEmpty(hostname))
                        return hostname;
                }
                catch (Exception err)
                {
                    Logger.Error<OSHelper>("Error fetching hostname, trying different method...", err);
                }

                try
                {
                    hostname = ExecuteCommand("hostnamectl hostname").Trim();
                    if (!string.IsNullOrEmpty(hostname))
                        return hostname;
                }
                catch (Exception err2)
                {
                    Logger.Error<OSHelper>("Error fetching hostname again, using generic name...", err2);
                    hostname = "linux device";
                }

                return hostname;
            }
            else
                return Environment.MachineName;
        }

        private static string ExecuteCommand(string command)
        {
            ProcessStartInfo processInfo = new ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = $"-c \"{command}\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (Process process = new Process { StartInfo = processInfo })
            {
                process.Start();
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                return output;
            }
        }
    }
}
