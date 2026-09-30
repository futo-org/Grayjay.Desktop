using Grayjay.Desktop.POC;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics;

namespace Grayjay.ClientServer
{
    public class TokenMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly GrayjayServer _server;

        private List<PathString> _excludedPaths = new List<PathString>()
        {
            new PathString("/Developer"),
            new PathString("/Player"),
            new PathString("/Ump"),
            new PathString("/Window"),
            new PathString("/Handle")
        };
        private HashSet<string> _excluded = new HashSet<string>()
        {

        };

        public TokenMiddleware(RequestDelegate next, GrayjayServer server)
        {
            _next = next;
            _server = server;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            if (_server.UseTokenSecurity && !_excluded.Contains(context.Request.Path) && !_excludedPaths.Any(x=>context.Request.Path.StartsWithSegments(x)))
            {
                string token = context.Request.Headers["_token"];
                if(token == null || !_server.HasToken(token))
                    throw new UnauthorizedAccessException("No valid token");
            }
            await _next(context);
        }
    }
}
