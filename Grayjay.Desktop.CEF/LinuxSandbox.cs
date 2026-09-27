using Grayjay.ClientServer.Constants;
using Grayjay.ClientServer.Controllers;
using Grayjay.ClientServer.Settings;
using Grayjay.ClientServer.States;
using JustCef;
using System.Diagnostics;
using System.Runtime.InteropServices;

using Logger = Grayjay.Desktop.POC.Logger;

namespace Grayjay.Desktop.CEF
{
    public static class LinuxSandbox
    {
        private const string DisabledFileName = "sandbox_disabled";
        private const string PromptDeclinedFileName = "sandbox_prompt_declined";
        private const string AppArmorProfilePath = "/etc/apparmor.d/grayjay";
        private const string AppArmorRestrictPath = "/proc/sys/kernel/apparmor_restrict_unprivileged_userns";
        private const string PkexecPath = "/usr/bin/pkexec";
        private const int PkexecNotAuthorized = 127;
        private const int PkexecDismissed = 126;
        private const string ZypakWrapperPath = "/app/bin/zypak-wrapper";

        private static bool _blockedByAppArmor = false;
        private static bool _useZypak = false;

        [DllImport("libc")]
        private static extern uint geteuid();

        [DllImport("libc", SetLastError = true)]
        private static extern IntPtr realpath(string path, IntPtr resolvedPath);

        [DllImport("libc")]
        private static extern void free(IntPtr ptr);

        private static string DisabledFile => Path.Combine(Directories.Base, DisabledFileName);
        private static string PromptDeclinedFile => Path.Combine(Directories.Base, PromptDeclinedFileName);

        public static bool ShouldUse(string[] args)
        {
            string? reason = GetDisableReason(args);
            if (reason != null)
            {
                Logger.i(nameof(LinuxSandbox), $"Sandbox disabled: {reason}.");
                return false;
            }

            Logger.i(nameof(LinuxSandbox), "Sandbox enabled.");
            return true;
        }

        public static void MarkFailed()
        {
            TryWrite(DisabledFile, App.Version.ToString());
        }

        private static string? GetDisableReason(string[] args)
        {
            string? env = Environment.GetEnvironmentVariable("GRAYJAY_NO_SANDBOX");
            if (!string.IsNullOrEmpty(env) && env != "0")
                return "GRAYJAY_NO_SANDBOX is set";
            if (args.Contains("--no-sandbox"))
                return "--no-sandbox was passed";
            if (GrayjaySettings.Instance.Browser.DisableSandbox)
                return "disabled in settings";
            if (geteuid() == 0)
                return "running as root";
            bool flatpak = Environment.GetEnvironmentVariable("FLATPAK_ID") != null || File.Exists("/.flatpak-info");
            if (flatpak && !File.Exists(ZypakWrapperPath))
                return "running inside Flatpak without zypak";
            if (Environment.GetEnvironmentVariable("SNAP") != null)
                return "running inside Snap";

            if (!flatpak && !IsSuidHelperConfigured())
            {
                if (ReadFirstLine("/proc/sys/kernel/unprivileged_userns_clone") == "0")
                    return "unprivileged user namespaces are disabled";
                if (ReadFirstLine("/proc/sys/user/max_user_namespaces") == "0")
                    return "user namespaces are disabled";
                if (IsAppArmorRestricted() && !IsAppArmorProfileInstalled())
                {
                    _blockedByAppArmor = true;
                    return "AppArmor restricts unprivileged user namespaces";
                }
            }

            string? disabledVersion = ReadFirstLine(DisabledFile);
            if (disabledVersion != null)
            {
                if (disabledVersion == App.Version.ToString())
                    return "the sandbox failed to start before on this version";
                TryDelete(DisabledFile);
            }

            _useZypak = flatpak;
            return null;
        }

        public static void ConfigureProcess(JustCefProcess process, string rootCachePath)
        {
            if (!_useZypak)
                return;

            Logger.i(nameof(LinuxSandbox), "Launching JustCef through zypak.");
            process.LauncherPath = ZypakWrapperPath;
            process.EnvironmentVariables["ZYPAK_CEF_LIBRARY_PATH"] = Path.Combine(AppContext.BaseDirectory, "cef", "libcef.so");
            process.EnvironmentVariables["ZYPAK_EXPOSE_WIDEVINE_PATH"] = Path.Combine(rootCachePath, "WidevineCdm");
        }

        public static async Task OfferAppArmorProfileAsync()
        {
            try
            {
                if (!_blockedByAppArmor || File.Exists(PromptDeclinedFile) || !File.Exists(PkexecPath))
                    return;

                string? profile = CreateAppArmorProfile();
                if (profile == null)
                    return;

                await StateWindow.WaitForReadyAsync();
                await StateUI.Dialog("", "Enable the browser sandbox",
                    "Ubuntu blocks the user namespaces the Chromium sandbox needs, so Grayjay currently runs without it. " +
                    "Grayjay can install the AppArmor profile below, which allows user namespaces for Grayjay only. " +
                    "This asks for your administrator password once. Moving the Grayjay folder requires doing this again.",
                    profile, 0,
                    new StateUI.DialogAction("Not now", () => { }),
                    new StateUI.DialogAction("Don't ask again", () => TryWrite(PromptDeclinedFile, "")),
                    new StateUI.DialogAction("Enable", () => _ = InstallAppArmorProfileAsync(profile), StateUI.ActionStyle.Primary));
            }
            catch (Exception e)
            {
                Logger.w(nameof(LinuxSandbox), "Failed to offer the AppArmor profile.", e);
            }
        }

        private static async Task InstallAppArmorProfileAsync(string profile)
        {
            try
            {
                var psi = new ProcessStartInfo(PkexecPath)
                {
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                psi.ArgumentList.Add("/bin/sh");
                psi.ArgumentList.Add("-c");
                psi.ArgumentList.Add($"f=$(mktemp) || exit 1; cat > \"$f\" && apparmor_parser -r \"$f\" && install -m 644 \"$f\" {AppArmorProfilePath}; r=$?; rm -f \"$f\"; exit $r");

                using var process = Process.Start(psi)!;
                await process.StandardInput.WriteAsync(profile);
                process.StandardInput.Close();
                var stdoutTask = process.StandardOutput.ReadToEndAsync();
                var stderrTask = process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();
                string output = (await stdoutTask + await stderrTask).Trim();

                if (process.ExitCode == 0)
                {
                    Logger.i(nameof(LinuxSandbox), "Installed the AppArmor profile.");
                    TryDelete(DisabledFile);
                    StateUI.Toast("Browser sandbox", "Restart Grayjay to enable the sandbox.");
                }
                else if (process.ExitCode == PkexecDismissed || (process.ExitCode == PkexecNotAuthorized && string.IsNullOrEmpty(output)))
                {
                    Logger.i(nameof(LinuxSandbox), $"Installing the AppArmor profile was cancelled ({process.ExitCode}).");
                }
                else
                {
                    Logger.w(nameof(LinuxSandbox), $"Installing the AppArmor profile failed ({process.ExitCode}): {output}");
                    await StateUI.DialogError("Failed to enable the browser sandbox", $"Installing the AppArmor profile failed with exit code {process.ExitCode}.", output);
                }
            }
            catch (Exception e)
            {
                Logger.w(nameof(LinuxSandbox), "Failed to install the AppArmor profile.", e);
                await StateUI.DialogError("Failed to enable the browser sandbox", e);
            }
        }

        private static string? CreateAppArmorProfile()
        {
            string? nativePath = GetNativePath();
            if (nativePath == null || !IsSafeAppArmorPath(nativePath))
                return null;

            return "abi <abi/4.0>,\n" +
                "include <tunables/global>\n" +
                "\n" +
                $"profile grayjay \"{nativePath}\" flags=(unconfined) {{\n" +
                "  userns,\n" +
                "\n" +
                "  include if exists <local/grayjay>\n" +
                "}\n";
        }

        private static bool IsAppArmorRestricted()
        {
            return ReadFirstLine(AppArmorRestrictPath) == "1";
        }

        private static bool IsAppArmorProfileInstalled()
        {
            try
            {
                string? nativePath = GetNativePath();
                return nativePath != null && File.Exists(AppArmorProfilePath) && File.ReadAllText(AppArmorProfilePath).Contains($"\"{nativePath}\"");
            }
            catch
            {
                return false;
            }
        }

        private static bool IsSuidHelperConfigured()
        {
            try
            {
                string helperPath = Path.Combine(AppContext.BaseDirectory, "cef", "chrome-sandbox");
                return OperatingSystem.IsLinux() && File.Exists(helperPath) && File.GetUnixFileMode(helperPath).HasFlag(UnixFileMode.SetUser);
            }
            catch
            {
                return false;
            }
        }

        private static string? GetNativePath()
        {
            string path = Path.Combine(AppContext.BaseDirectory, "cef", "justcefnative");
            if (!File.Exists(path))
                return null;

            IntPtr resolved = realpath(path, IntPtr.Zero);
            if (resolved == IntPtr.Zero)
                return null;

            try
            {
                return Marshal.PtrToStringUTF8(resolved);
            }
            finally
            {
                free(resolved);
            }
        }

        private static bool IsSafeAppArmorPath(string path)
        {
            return path.StartsWith('/') && !path.Any(c => char.IsControl(c) || "\"*?[]{}^\\#,".Contains(c));
        }

        private static string? ReadFirstLine(string path)
        {
            try
            {
                if (!File.Exists(path))
                    return null;
                return File.ReadLines(path).FirstOrDefault()?.Trim();
            }
            catch
            {
                return null;
            }
        }

        private static void TryWrite(string path, string content)
        {
            try
            {
                File.WriteAllText(path, content);
            }
            catch (Exception e)
            {
                Logger.w(nameof(LinuxSandbox), $"Failed to write '{path}'.", e);
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception e)
            {
                Logger.w(nameof(LinuxSandbox), $"Failed to delete '{path}'.", e);
            }
        }
    }
}
