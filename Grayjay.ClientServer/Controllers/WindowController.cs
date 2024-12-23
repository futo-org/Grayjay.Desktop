using Grayjay.ClientServer.States;
using Grayjay.Desktop.POC;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Mvc;
using System.Runtime.ConstrainedExecution;
using System.Threading;

namespace Grayjay.ClientServer.Controllers
{
    [Route("[controller]/[action]")]
    public class WindowController : ControllerBase
    {
        [HttpGet]
        public void StartWindow()
        {
            new Thread(async () =>
            {
                if (GrayjayServer.Instance.WindowProvider != null && !GrayjayServer.Instance.HeadlessMode)
                    await GrayjayServer.Instance.WindowProvider.CreateWindow("Grayjay (Sub)", 1280, 720, $"{GrayjayServer.Instance.BaseUrl}/web/index.html");
                else if(!GrayjayServer.Instance.ServerMode)
                    OSHelper.OpenUrl($"{GrayjayServer.Instance.BaseUrl}/web/index.html");
            }).Start();
        }
    }
}
