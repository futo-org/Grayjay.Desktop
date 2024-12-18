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
                Process.Start("explorer", file);
            }
            else if (OperatingSystem.IsLinux())
            {
                Process.Start("xdg-open", file);
            }
            else if (OperatingSystem.IsMacOS())
            {
                Process.Start("open", file);
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
    }
}
