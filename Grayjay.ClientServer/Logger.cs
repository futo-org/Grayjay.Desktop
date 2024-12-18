using Grayjay.ClientServer.Constants;
using Grayjay.ClientServer.Settings;

namespace Grayjay.Desktop.POC
{
    public static class Logger
    {
        private static string _logFile;
        private static bool _logToFile = true;
        private static StreamWriter? _logWriter = null;

        static Logger()
        {
            _logFile = Path.Combine(Directories.Base, "log.txt");
            if (_logToFile)
                _logWriter = new StreamWriter(_logFile, false);
        }

        private static void logToFile(string msg)
        {
            if (_logWriter != null)
            {
                lock (_logWriter)
                {
                    _logWriter.WriteLine(msg);
                    _logWriter.Flush();
                }
            }
        }

        public static void i<T>(string msg, Exception ex = null) => i(nameof(T), msg);
        public static void i(string tag, string msg, Exception ex = null)
        {
            Console.WriteLine($"{tag}:{msg}\n{ex}");
            System.Diagnostics.Debug.WriteLine($"{tag}:{msg}\n{ex}");
            logToFile($"i {tag}:{msg}\n{ex}");
        }

        public static void i<T>(string msg) => i(nameof(T), msg);
        public static void i(string tag, string msg)
        {
            Console.WriteLine($"{tag}:{msg}");
            System.Diagnostics.Debug.WriteLine($"{tag}:{msg}");
            logToFile($"i {tag}:{msg}");
        }
        public static void w<T>(string msg, Exception ex = null) => w(nameof(T), msg, ex);
        public static void w(string tag, string msg, Exception ex = null)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"{tag}:{msg}\n{ex}");
            Console.ResetColor();
            System.Diagnostics.Debug.WriteLine($"{tag}:{msg}\n{ex}");
            logToFile($"w {tag}:{msg}\n{ex}");
        }

        public static void v<T>(string msg) => v(nameof(T), msg);
        public static void v(string tag, string msg)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"{tag}:{msg}");
            Console.ResetColor();
            System.Diagnostics.Debug.WriteLine($"{tag}:{msg}");
            logToFile($"v {tag}:{msg}");
        }


        public static void e<T>(string msg, Exception ex) => e(nameof(T), msg, ex);
        public static void e(string tag, string msg, Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"{tag}:{msg}\n{ex}");
            Console.ResetColor();
            System.Diagnostics.Debug.WriteLine($"{tag}:{msg}\n{ex}");
            logToFile($"e {tag}:{msg}\n{ex}");
        }

        public static void e(string tag, string msg)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"{tag}:{msg}");
            Console.ResetColor();
            System.Diagnostics.Debug.WriteLine($"{tag}:{msg}");
            logToFile($"e {tag}:{msg}");
        }
    }
}
