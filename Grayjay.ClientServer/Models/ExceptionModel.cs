using Grayjay.Engine.Exceptions;

namespace Grayjay.ClientServer.Models
{
    public class ExceptionModel
    {
        public string Type { get; set; } = EXCEPTION_GENERAL;
        public string TypeName { get; set; }
        public string? Title { get; set; }
        public string? Message { get; set; }
        public string? Code { get; set; }
        public string? Stacktrace { get; set; }

        public string? PluginID { get; set; }
        public string? PluginName { get; set; }

        public bool CanRetry { get; set; } = false;


        public static ExceptionModel FromException(Exception ex, bool canRetry = false) => FromException(null, ex, canRetry);
        public static ExceptionModel FromException(string title, Exception ex, bool canRetry = false)
        {
            if (ex is ScriptException scriptEx)
            {
                string plugin = scriptEx.Config.Name;
                return new ExceptionModel()
                {
                    Type = EXCEPTION_SCRIPT,
                    Title = (!string.IsNullOrEmpty(title)) ? $"[{scriptEx.Config.Name}] {title}" : $"[{scriptEx.Config.Name}] {ex.Message}",
                    Message = (!string.IsNullOrEmpty(title)) ? ex.Message : null,
                    Code = scriptEx.Code,
                    CanRetry = canRetry,
                    TypeName = ex?.GetType()?.Name,
                    PluginID = scriptEx.Config.ID,
                    PluginName = scriptEx.Config.Name
                };
            }
            return new ExceptionModel()
            {
                Type = EXCEPTION_GENERAL,
                Title = (!string.IsNullOrEmpty(title)) ? title : ex.Message,
                Message = (!string.IsNullOrEmpty(title)) ? ex.Message : null,
                CanRetry = canRetry,
                TypeName = ex?.GetType()?.Name,
                Stacktrace = ex?.StackTrace
            };
        }


        public const string EXCEPTION_GENERAL = "general";
        public const string EXCEPTION_SCRIPT = "script";
    }
}
