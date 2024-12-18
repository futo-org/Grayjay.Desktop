using Grayjay.Desktop.POC;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace Grayjay.ClientServer.Transcoding
{
    public static class FFMPEG
    {
        private static Regex _ffmpegVersionRegex = new Regex("ffmpeg version (.*?) Copyright");

        private static bool _isFFMPEGAvailable = false;
        private static string _ffmpegCommand = null;

        public static bool IsFFMPEGAvailable()
        {
            if (_isFFMPEGAvailable)
                return true;
            Logger.i(nameof(FFMPEG), "Determining FFMPEG command");

            string version;
            string fileName = RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "ffmpeg" : "ffmpeg.exe";
            string ffmpegPath = null;

            if (File.Exists(fileName))
            {
                ffmpegPath = fileName;
            }
            else
            {
                ffmpegPath = Environment.GetEnvironmentVariable("PATH")
                    .Split(";").FirstOrDefault(x => File.Exists(Path.Combine(x, fileName)));
                if (ffmpegPath != null)
                    ffmpegPath = Path.Combine(ffmpegPath, fileName);
            }

            _ffmpegCommand = ffmpegPath;
            Logger.i(nameof(FFMPEG), "Verifying FFMPEG: " + _ffmpegCommand);
            version = TryFFMPEGVersion();
            if (version == null)
            {
                _isFFMPEGAvailable = false;
                return false;
            } 
            Logger.i(nameof(FFMPEG), "FFMPEG Version found: " + version);
            _isFFMPEGAvailable = true;
            return true;
        }
        private static string TryFFMPEGVersion()
        {
            try
            {
                Logger.i(nameof(FFMPEG), "FFMPEG Command: " + _ffmpegCommand);
                Process p = new Process();
                p.StartInfo.FileName = _ffmpegCommand;
                p.StartInfo.Arguments = "-version";
                StringBuilder strBuilder = new StringBuilder();
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.RedirectStandardInput = true;
                p.StartInfo.RedirectStandardOutput = true;
                p.StartInfo.RedirectStandardError = true;
                p.StartInfo.CreateNoWindow = true;
                p.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
                p.Start();
                while(!p.StandardOutput.EndOfStream)
                {
                    string line = p.StandardOutput.ReadLine();
                    strBuilder.AppendLine(line);
                }
                p.WaitForExit();
                return _ffmpegVersionRegex.Match(strBuilder.ToString()).Groups[1].Value;
            }
            catch(InvalidOperationException ex)
            {
                throw new InvalidOperationException("FFMPEG is not available (" + _ffmpegCommand + ")");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("FFMPEG is not available (" + _ffmpegCommand + "): " + ex.Message);
            }
        }

        public static int Execute(string command, bool print = true)
        {
            var process = ExecuteProcess(command, print);
            process.WaitForExit();
            return process.ExitCode;
        }
        public static bool ExecuteWithTimeout(string command, TimeSpan max, bool print = true)
        {
            var process = ExecuteProcess(command, print);

            Task delay = Task.Delay(max);
            int index = Task.WaitAny(Task.Run(() => process.WaitForExit()), delay);
            if (index == 1)
            {
                process.Kill();
                return false;
            }
            return true;
        }

        private static Process ExecuteProcess(string command, bool print = true, Action<string, bool> onLog = null)
        {
            if (!IsFFMPEGAvailable())
                throw new InvalidOperationException("Ffmpeg not present");
            Process process = new Process();
            process.StartInfo.FileName = _ffmpegCommand;
            process.StartInfo.Arguments = command;
            if (print)
            {
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardInput = true;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                if (onLog == null)
                {
                    process.OutputDataReceived += (sender, args) => Logger.i(nameof(FFMPEG), args.Data);
                    process.ErrorDataReceived += (sender, args) => Logger.i(nameof(FFMPEG), args.Data);
                }
                else
                {
                    process.OutputDataReceived += (sender, args) =>
                    {
                        Logger.i(nameof(FFMPEG), args.Data);
                        onLog(args.Data, false);
                    };
                    process.ErrorDataReceived += (sender, args) =>
                    {
                        Logger.i(nameof(FFMPEG), args.Data);
                        onLog(args.Data, true);
                    };
                }
            }
            else
            {
                process.StartInfo.UseShellExecute = true;
                process.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
                if (onLog != null)
                {
                    process.StartInfo.RedirectStandardOutput = true;
                    process.StartInfo.RedirectStandardError = true;
                    process.OutputDataReceived += (sender, args) => onLog(args.Data, false);
                    process.ErrorDataReceived += (sender, args) => onLog(args.Data, true);
                }
            }
            process.Start();
            if (print || onLog != null)
            {
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
            }
            return process;
        }




    }

    public class FFMPEGSession
    {

    }
}
