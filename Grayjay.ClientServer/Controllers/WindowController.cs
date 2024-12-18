using Grayjay.ClientServer.States;
using Grayjay.Desktop.POC;
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
                var window = await GrayjayServer.Instance.WindowProvider.CreateWindow("Grayjay (Sub)", 1280, 720, $"{GrayjayServer.Instance.BaseUrl}/web/index.html");
                
            }).Start();
        }
    }
}
