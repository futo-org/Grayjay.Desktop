using Grayjay.ClientServer.States;
using Grayjay.Desktop.POC;
using Grayjay.Desktop.POC.Port.States;
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

        [HttpGet]
        public void Ready()
        {
            var state = this.State();
            if (state != null)
            {
                state.Ready = true;
                StateWindow.StateReadyChanged(state, true);
            }
        }


        [HttpGet]
        public async Task<bool> Delay(int ms)
        {
            await Task.Delay(ms);
            return true;
        }

        [HttpGet]
        public string Echo(string str)
        {
            return str;
        }
    }
}
