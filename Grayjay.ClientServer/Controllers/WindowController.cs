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
        public async Task StartWindow()
        {
            if (GrayjayServer.Instance.WindowProvider != null && !GrayjayServer.Instance.HeadlessMode)
            {
                await GrayjayServer.Instance.WindowProvider.CreateWindowAsync(
                    url: $"{GrayjayServer.Instance.BaseUrl}/web/index.html", 
                    title: "Grayjay (Sub)",
                    minimumWidth: 900,
                    minimumHeight: 550,
                    preferredWidth: 1300,
                    preferredHeight: 950
                );
            }
            else if (!GrayjayServer.Instance.ServerMode)
                OSHelper.OpenUrl($"{GrayjayServer.Instance.BaseUrl}/web/index.html");
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
    }
}
