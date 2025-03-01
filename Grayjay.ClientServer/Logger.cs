namespace Grayjay.Desktop.POC
{
    public enum LogLevel
    {
        Verbose,
        Info,
        Warning,
        Error
    }

    public class Log : IDisposable
    {
        public class Config
        {
            public string? LogFilePath { get; set; }
            public bool LogToFile { get; set; } = true;
            public bool WriteToDebug { get; set; } = true;
            public string TimestampFormat { get; set; } = "yyyy-MM-dd HH:mm:ss.fff";
            public bool UseUtcTime { get; set; } = false;
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
            if (_config.LogToFile) InitializeLogWriter();
            if (_config.LogToFile && _config.FlushIntervalMs > 0)
            {
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
                    LogFallback($"Failed to initialize log file: {ex.Message}");
                    _config.LogToFile = false;
                }
            }
        }

        private void LogFallback(string message)
        {
            lock (_consoleLock)
            {
                Console.WriteLine(message);
            }
            if (_config.WriteToDebug) System.Diagnostics.Debug.WriteLine(message);
        }

        public void l(LogLevel level, string tag, string message, Exception? ex = null)
        {
            if (_disposed) return;

            var time = _config.UseUtcTime ? DateTime.UtcNow : DateTime.Now;
            string timestamp = time.ToString(_config.TimestampFormat);
            string levelStr = level.ToString().ToUpper();
            string logMessage = $"[{timestamp}] [{levelStr}] [{tag}] {message}";
            if (ex != null) logMessage += $"\nException: {ex.Message}\nStack Trace: {ex.StackTrace}";

            lock (_consoleLock)
            {
                ConsoleColor originalColor = Console.ForegroundColor;
                Console.ForegroundColor = level switch
                {
                    LogLevel.Verbose => ConsoleColor.DarkGray,
                    LogLevel.Info => ConsoleColor.White,
                    LogLevel.Warning => ConsoleColor.Yellow,
                    LogLevel.Error => ConsoleColor.Red,
                    _ => ConsoleColor.White
                };
                Console.WriteLine(logMessage);
                Console.ForegroundColor = originalColor;
            }

            if (_config.WriteToDebug) System.Diagnostics.Debug.WriteLine(logMessage);

            if (_config.LogToFile && _logWriter != null)
            {
                lock (_lock)
                {
                    try
                    {
                        _logWriter.WriteLine(logMessage);
                    }
                    catch (Exception writeEx)
                    {
                        LogFallback($"Failed to write to log: {writeEx.Message}");
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
                LogFallback($"Failed to flush log: {flushEx.Message}");
            }
        }

        public void Verbose<T>(string message, Exception? ex = null) => l(LogLevel.Verbose, typeof(T).Name, message, ex);
        public void Info<T>(string message, Exception? ex = null) => l(LogLevel.Info, typeof(T).Name, message, ex);
        public void Warning<T>(string message, Exception? ex = null) => l(LogLevel.Warning, typeof(T).Name, message, ex);
        public void Error<T>(string message, Exception? ex = null) => l(LogLevel.Error, typeof(T).Name, message, ex);
        public void v<T>(string message, Exception? ex = null) => l(LogLevel.Verbose, typeof(T).Name, message, ex);
        public void i<T>(string message, Exception? ex = null) => l(LogLevel.Info, typeof(T).Name, message, ex);
        public void w<T>(string message, Exception? ex = null) => l(LogLevel.Warning, typeof(T).Name, message, ex);
        public void e<T>(string message, Exception? ex = null) => l(LogLevel.Error, typeof(T).Name, message, ex);
        public void Verbose(string tag, string message, Exception? ex = null) => l(LogLevel.Verbose, tag, message, ex);
        public void Info(string tag, string message, Exception? ex = null) => l(LogLevel.Info, tag, message, ex);
        public void Warning(string tag, string message, Exception? ex = null) => l(LogLevel.Warning, tag, message, ex);
        public void Error(string tag, string message, Exception? ex = null) => l(LogLevel.Error, tag, message, ex);
        public void v(string tag, string message, Exception? ex = null) => l(LogLevel.Verbose, tag, message, ex);
        public void i(string tag, string message, Exception? ex = null) => l(LogLevel.Info, tag, message, ex);
        public void w(string tag, string message, Exception? ex = null) => l(LogLevel.Warning, tag, message, ex);
        public void e(string tag, string message, Exception? ex = null) => l(LogLevel.Error, tag, message, ex);

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
        private static Log.Config _staticConfig = new Log.Config()
        {
            LogFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "log.txt")
        };

        private static readonly Lazy<Log> _staticLogger = new Lazy<Log>(() => new Log(_staticConfig));
        public static void Verbose<T>(string message, Exception? ex = null) => _staticLogger.Value.Verbose<T>(message, ex);
        public static void Info<T>(string message, Exception? ex = null) => _staticLogger.Value.Info<T>(message, ex);
        public static void Warning<T>(string message, Exception? ex = null) => _staticLogger.Value.Warning<T>(message, ex);
        public static void Error<T>(string message, Exception? ex = null) => _staticLogger.Value.Error<T>(message, ex);
        public static void v<T>(string message, Exception? ex = null) => _staticLogger.Value.v<T>(message, ex);
        public static void i<T>(string message, Exception? ex = null) => _staticLogger.Value.i<T>(message, ex);
        public static void w<T>(string message, Exception? ex = null) => _staticLogger.Value.w<T>(message, ex);
        public static void e<T>(string message, Exception? ex = null) => _staticLogger.Value.e<T>(message, ex);
        public static void Verbose(string tag, string message, Exception? ex = null) => _staticLogger.Value.Verbose(tag, message, ex);
        public static void Info(string tag, string message, Exception? ex = null) => _staticLogger.Value.Info(tag, message, ex);
        public static void Warning(string tag, string message, Exception? ex = null) => _staticLogger.Value.Warning(tag, message, ex);
        public static void Error(string tag, string message, Exception? ex = null) => _staticLogger.Value.Error(tag, message, ex);
        public static void v(string tag, string message, Exception? ex = null) => _staticLogger.Value.v(tag, message, ex);
        public static void i(string tag, string message, Exception? ex = null) => _staticLogger.Value.i(tag, message, ex);
        public static void w(string tag, string message, Exception? ex = null) => _staticLogger.Value.w(tag, message, ex);
        public static void e(string tag, string message, Exception? ex = null) => _staticLogger.Value.e(tag, message, ex);

        public static void DisposeStaticLogger()
        {
            if (_staticLogger.IsValueCreated)
                _staticLogger.Value.Dispose();
        }
    }
}