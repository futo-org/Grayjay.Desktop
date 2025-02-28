using Grayjay.Desktop.POC;
using System.Diagnostics;

namespace Grayjay.ClientServer
{
    public class RequestLoggingMiddleware
    {
        private readonly RequestDelegate _next;

        public RequestLoggingMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var stopwatch = Stopwatch.StartNew();
            Logger.v<RequestLoggingMiddleware>($"Request started: {context.Request.Method} {context.Request.Path}");

            try
            {
                await _next(context);
            }
            finally
            {
                stopwatch.Stop();

                Logger.v<RequestLoggingMiddleware>($"Request ended: {context.Request.Method} {context.Request.Path}, " +
                    $"Status: {context.Response.StatusCode}, " +
                    $"Duration: {stopwatch.ElapsedMilliseconds}ms");
            }
        }
    }
}
