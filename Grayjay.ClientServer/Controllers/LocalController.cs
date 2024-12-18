using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Grayjay.ClientServer.Controllers
{
    [Route("[controller]/[action]")]
    public class LocalController: ControllerBase
    {
        [HttpGet]
        public IActionResult Open(string uri)
        {
            if (string.IsNullOrEmpty(uri))
                throw new BadHttpRequestException("Missing uri");

            try
            {
                Process.Start(uri);
            }
            catch
            {
                // hack because of this: https://github.com/dotnet/corefx/issues/10361
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    uri = uri.Replace("&", "^&");
                    Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    Process.Start("xdg-open", uri);
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    Process.Start("open", uri);
                }
                else
                {
                    throw;
                }
            }
            return Ok();
        }
    }
}
