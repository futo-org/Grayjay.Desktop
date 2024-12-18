using Grayjay.ClientServer.Exceptions;
using Grayjay.ClientServer.Models;
using Grayjay.Engine.Exceptions;
using Microsoft.AspNetCore.Diagnostics;

namespace Grayjay.ClientServer.ExceptionHandlers
{
    public class ScriptExceptionHandler : IExceptionHandler
    {
        public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
        {
            if(exception is ScriptException scriptException)
            {
                httpContext.Response.StatusCode = 550;
                await httpContext.Response.WriteAsJsonAsync(ExceptionModel.FromException(scriptException));
                return true;
            }
            else if(exception is DialogException dialogException)
            {
                httpContext.Response.StatusCode = 550;
                await httpContext.Response.WriteAsJsonAsync(dialogException.Model);
                return true;
            }
            else if(exception != null)
            {
                httpContext.Response.StatusCode = 550;
                await httpContext.Response.WriteAsJsonAsync(ExceptionModel.FromException("Uncaught Exception", exception));
                return true;
            }
            return false;
        }
    }
}
