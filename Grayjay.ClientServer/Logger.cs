using Grayjay.ClientServer.Constants;
using Grayjay.ClientServer.Settings;

namespace Grayjay.Desktop.POC
{
    public enum LogLevel : int
    {
        None,
        Error,
        Warning,
        Info,
        Verbose,
        Debug
    }

    public class Log : IDisposable
    {
        public class Config
        {
            public string? LogFilePath { get; set; }
            public LogLevel FileLogLevel { get; set; } = LogLevel.Info;
#if DEBUG
            public LogLevel ConsoleLogLevel { get; set; } = LogLevel.Info;
            public LogLevel DebugLogLevel { get; set; } = LogLevel.Info;
#else
            public LogLevel ConsoleLogLevel { get; set; } = LogLevel.Error;
            public LogLevel DebugLogLevel { get; set; } = LogLevel.None;
#endif
            public int FlushIntervalMs { get; set; } = 3000;
        }

        private readonly Config _config;
        private StreamWriter? _logWriter;
        private readonly object _lock = new object();
        private readonly object _consoleLock = new object();
        private bool _disposed;
        private CancellationTokenSource _flushCancellationTokenSource = new CancellationTokenSource();

        public Log(Config? config = null)
        {
            _config = config ?? new Config();
            if (_config.FileLogLevel > LogLevel.None)
            {
                InitializeLogWriter();

                if (_config.FlushIntervalMs <= 0)
                    throw new Exception("Flush interval must be greater than zero");

                _ = Task.Run(async () =>
                {
                    var delay = TimeSpan.FromMilliseconds(_config.FlushIntervalMs);
                    try
                    {
                        while (!_flushCancellationTokenSource.IsCancellationRequested)
                        {
                            await FlushLogAsync();
                            await Task.Delay(delay, _flushCancellationTokenSource.Token);
                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("Log flusher exited with exception: " + e.ToString());
                    }
                });
            }
        }

        private void InitializeLogWriter()
        {
            var logFilePath = _config.LogFilePath;
            if (logFilePath == null)
                throw new Exception("Log file path must be set.");

            lock (_lock)
            {
                try
                {
                    _logWriter?.Dispose();
                    _logWriter = new StreamWriter(logFilePath, append: false) { AutoFlush = false };
                }
                catch (Exception ex)
                {
                    LogFallback(LogLevel.Error, $"Failed to initialize log file: {ex.Message}");
                    _config.FileLogLevel = LogLevel.None;
                }
            }
        }

        private void LogFallback(LogLevel level, string message)
        {
            if (_config.ConsoleLogLevel > LogLevel.None && level <= _config.ConsoleLogLevel)
            {
                lock (_consoleLock)
                {
                    Console.WriteLine(message);
                }
            }
            if (_config.DebugLogLevel > LogLevel.None && level <= _config.DebugLogLevel) 
                System.Diagnostics.Debug.WriteLine(message);
        }

        public void l(LogLevel level, string tag, string message, Exception? ex = null)
        {
            if (_disposed) 
                return;

            var logMessage = Logger.FormatLogMessage(level, tag, message, ex);
            if (_config.ConsoleLogLevel > LogLevel.None && level <= _config.ConsoleLogLevel)
            {
                lock (_consoleLock)
                {
                    ConsoleColor originalColor = Console.ForegroundColor;
                    Console.ForegroundColor = level switch
                    {
                        LogLevel.Verbose => ConsoleColor.DarkGray,
                        LogLevel.Debug => ConsoleColor.DarkGray,
                        LogLevel.Info => ConsoleColor.White,
                        LogLevel.Warning => ConsoleColor.Yellow,
                        LogLevel.Error => ConsoleColor.Red,
                        _ => ConsoleColor.White
                    };
                    Console.WriteLine(logMessage);
                    Console.ForegroundColor = originalColor;
                }
            }

            if (_config.DebugLogLevel > LogLevel.None && level <= _config.DebugLogLevel) 
                System.Diagnostics.Debug.WriteLine(logMessage);

            if (_config.FileLogLevel > LogLevel.None && level <= _config.FileLogLevel && _logWriter != null)
            {
                lock (_lock)
                {
                    try
                    {
                        _logWriter.WriteLine(logMessage);
                    }
                    catch (Exception writeEx)
                    {
                        LogFallback(LogLevel.Error, $"Failed to write to log: {writeEx.Message}");
                    }
                }
            }
        }

        private async Task FlushLogAsync()
        {
            if (_disposed || _logWriter == null) return;

            try
            {
                await _logWriter.FlushAsync();
            }
            catch (Exception flushEx)
            {
                LogFallback(LogLevel.Error, $"Failed to flush log: {flushEx.Message}");
            }
        }

        public void Debug<T>(string message, Exception? ex = null) => l(LogLevel.Debug, typeof(T).Name, message, ex);
        public void Verbose<T>(string message, Exception? ex = null) => l(LogLevel.Verbose, typeof(T).Name, message, ex);
        public void Info<T>(string message, Exception? ex = null) => l(LogLevel.Info, typeof(T).Name, message, ex);
        public void Warning<T>(string message, Exception? ex = null) => l(LogLevel.Warning, typeof(T).Name, message, ex);
        public void Error<T>(string message, Exception? ex = null) => l(LogLevel.Error, typeof(T).Name, message, ex);
        public void d<T>(string message, Exception? ex = null) => l(LogLevel.Debug, typeof(T).Name, message, ex);
        public void v<T>(string message, Exception? ex = null) => l(LogLevel.Verbose, typeof(T).Name, message, ex);
        public void i<T>(string message, Exception? ex = null) => l(LogLevel.Info, typeof(T).Name, message, ex);
        public void w<T>(string message, Exception? ex = null) => l(LogLevel.Warning, typeof(T).Name, message, ex);
        public void e<T>(string message, Exception? ex = null) => l(LogLevel.Error, typeof(T).Name, message, ex);
        public void Debug(string tag, string message, Exception? ex = null) => l(LogLevel.Debug, tag, message, ex);
        public void Verbose(string tag, string message, Exception? ex = null) => l(LogLevel.Verbose, tag, message, ex);
        public void Info(string tag, string message, Exception? ex = null) => l(LogLevel.Info, tag, message, ex);
        public void Warning(string tag, string message, Exception? ex = null) => l(LogLevel.Warning, tag, message, ex);
        public void Error(string tag, string message, Exception? ex = null) => l(LogLevel.Error, tag, message, ex);
        public void d(string tag, string message, Exception? ex = null) => l(LogLevel.Debug, tag, message, ex);
        public void v(string tag, string message, Exception? ex = null) => l(LogLevel.Verbose, tag, message, ex);
        public void i(string tag, string message, Exception? ex = null) => l(LogLevel.Info, tag, message, ex);
        public void w(string tag, string message, Exception? ex = null) => l(LogLevel.Warning, tag, message, ex);
        public void e(string tag, string message, Exception? ex = null) => l(LogLevel.Error, tag, message, ex);
        public bool WillLog(LogLevel logLevel) => logLevel <= _config.FileLogLevel || logLevel <= _config.ConsoleLogLevel;

        public void Dispose()
        {
            if (_disposed) return;
            lock (_lock)
            {
                _flushCancellationTokenSource.Cancel();
                _logWriter?.Dispose();
                _logWriter = null;
                _disposed = true;
            }
        }
    }

    public static class Logger
    {
        private static Log _staticLogger = new Log(new Log.Config()
        {
            LogFilePath = "log.txt",
            FileLogLevel = (LogLevel)LogLevel.Verbose,
            ConsoleLogLevel = LogLevel.Verbose,
#if DEBUG
            DebugLogLevel = (LogLevel)LogLevel.Verbose,
#else
            DebugLogLevel = LogLevel.None
#endif
        });

        public static void LoadFromSettings()
        {
            try
            {
                var newLogger = new Log(new Log.Config()
                {
                    LogFilePath = Path.Combine(Directories.Base, "log.txt"),
                    FileLogLevel = (LogLevel)GrayjaySettings.Instance.Logging.LogLevel,
#if DEBUG
                    ConsoleLogLevel = LogLevel.Verbose,
#else
                    ConsoleLogLevel = (LogLevel)GrayjaySettings.Instance.Logging.LogLevel,
#endif
#if DEBUG
                    DebugLogLevel = (LogLevel)GrayjaySettings.Instance.Logging.LogLevel,
#else
                    DebugLogLevel = LogLevel.None
#endif
                });

                var oldLogger = _staticLogger;
                _staticLogger = newLogger;
                oldLogger.Dispose();
            }
            catch (Exception e)
            {
                Console.WriteLine("Failed to update Logger:\n" + e.Message);
            }
        }

        public static void Debug<T>(string message, Exception? ex = null) => _staticLogger.Verbose<T>(message, ex);
        public static void Verbose<T>(string message, Exception? ex = null) => _staticLogger.Verbose<T>(message, ex);
        public static void Info<T>(string message, Exception? ex = null) => _staticLogger.Info<T>(message, ex);
        public static void Warning<T>(string message, Exception? ex = null) => _staticLogger.Warning<T>(message, ex);
        public static void Error<T>(string message, Exception? ex = null) => _staticLogger.Error<T>(message, ex);
        public static void Log<T>(LogLevel level, string message, Exception? ex = null) => _staticLogger.l(level, nameof(T), message, ex);
        public static void v<T>(string message, Exception? ex = null) => _staticLogger.v<T>(message, ex);
        public static void i<T>(string message, Exception? ex = null) => _staticLogger.i<T>(message, ex);
        public static void w<T>(string message, Exception? ex = null) => _staticLogger.w<T>(message, ex);
        public static void e<T>(string message, Exception? ex = null) => _staticLogger.e<T>(message, ex);
        public static void l<T>(LogLevel level, string message, Exception? ex = null) => _staticLogger.l(level, nameof(T), message, ex);
        public static void Debug(string tag, string message, Exception? ex = null) => _staticLogger.Verbose(tag, message, ex);
        public static void Verbose(string tag, string message, Exception? ex = null) => _staticLogger.Verbose(tag, message, ex);
        public static void Info(string tag, string message, Exception? ex = null) => _staticLogger.Info(tag, message, ex);
        public static void Warning(string tag, string message, Exception? ex = null) => _staticLogger.Warning(tag, message, ex);
        public static void Error(string tag, string message, Exception? ex = null) => _staticLogger.Error(tag, message, ex);
        public static void Log(LogLevel level, string tag, string message, Exception? ex = null) => _staticLogger.l(level, tag, message, ex);
        public static void d(string tag, string message, Exception? ex = null) => _staticLogger.v(tag, message, ex);
        public static void v(string tag, string message, Exception? ex = null) => _staticLogger.v(tag, message, ex);
        public static void i(string tag, string message, Exception? ex = null) => _staticLogger.i(tag, message, ex);
        public static void w(string tag, string message, Exception? ex = null) => _staticLogger.w(tag, message, ex);
        public static void e(string tag, string message, Exception? ex = null) => _staticLogger.e(tag, message, ex);
        public static void l(LogLevel level, string tag, string message, Exception? ex = null) => _staticLogger.l(level, tag, message, ex);
        public static bool WillLog(LogLevel level) => _staticLogger.WillLog(level);

        public static string FormatLogMessage(LogLevel level, string tag, string message, Exception? ex = null)
        {
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            string levelStr = level.ToString().ToUpper();
            string logMessage = $"[{timestamp}] [{levelStr}] [{tag}] {message}";
            if (ex != null)
                logMessage += $"\nException: {ex.Message}\nStack Trace: {ex.StackTrace}";
            return logMessage;
        }

        public static void DisposeStaticLogger()
        {
            _staticLogger.Dispose();
        }
    }
}