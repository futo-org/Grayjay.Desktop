using Grayjay.ClientServer.Settings;

using Logger = Grayjay.Desktop.POC.Logger;

public class GrayjayLogger : ILogger
{
    private readonly string _category;

    public GrayjayLogger(string category)
    {
        _category = category;
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
            return;

        string message = formatter(state, exception);
        switch (logLevel)
        {
            case LogLevel.Trace:
                //Logger.t(_category, message, exception);
                break;
            case LogLevel.Debug:
                Logger.Debug(_category, message, exception);
                break;
            case LogLevel.Information:
                Logger.Info(_category, message, exception);
                break;
            case LogLevel.Warning:
                Logger.Warning(_category, message, exception);
                break;
            case LogLevel.Error:
                Logger.Error(_category, message, exception);
                break;
            case LogLevel.Critical:
                Logger.Error(_category, message, exception);
                break;
            default:
                Logger.Info(_category, message, exception);
                break;
        }
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        int settingsLogLevel = GrayjaySettings.Instance.Logging.LogLevel;
        int logLevelValue = (int)logLevel;

        switch (settingsLogLevel)
        {
            case 0: // None
                return false;
            case 1: // Error
                return logLevelValue >= (int)LogLevel.Error;
            case 2: // Warning
                return logLevelValue >= (int)LogLevel.Warning;
            case 3: // Information
                return logLevelValue >= (int)LogLevel.Information;
            case 4: // Verbose
                return logLevelValue >= (int)LogLevel.Debug;
            case 5: // Debug
                return logLevelValue >= (int)LogLevel.Trace;
            default:
                return false;
        }
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
}

public class NullScope : IDisposable
{
    public static NullScope Instance = new NullScope();
    public void Dispose() { }
}

public class GrayjayLoggerProvider : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName)
    {
        return new GrayjayLogger(categoryName);
    }

    public void Dispose()
    {
        
    }
}