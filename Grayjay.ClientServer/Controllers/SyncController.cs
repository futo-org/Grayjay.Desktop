using System.Net;
using System.Security.Cryptography.X509Certificates;
using Grayjay.ClientServer.Dialogs;
using Grayjay.ClientServer.States;
using Grayjay.ClientServer.Sync;
using Grayjay.ClientServer.Sync.Internal;
using Grayjay.ClientServer.Sync.Models;
using Microsoft.AspNetCore.Mvc;
using SyncClient;

namespace Grayjay.ClientServer.Controllers
{
    [Route("[controller]/[action]")]
    public class SyncController : ControllerBase
    {
        [HttpGet]
        public ActionResult<string> GetPairingUrl()
        {
            return Ok(StateSync.Instance.GetPairingUrl());
        }

        [HttpGet]
        public ActionResult<dynamic> GetDevices()
        {
            return Ok(StateSync.Instance.GetAllDevices().Select(pk => 
            {
                var session = StateSync.Instance.GetSession(pk);
                return new SyncDevice
                {
                    PublicKey = pk, 
                    DisplayName = session?.DisplayName ?? StateSync.Instance.GetCachedName(pk),
                    Metadata = session?.Connected == true ? "Connected" : "Disconnected",
                    LinkType = (int)(session?.LinkType ?? LinkType.None)
                };
            }).ToList());
        }
        [HttpGet]
        public ActionResult<List<SyncDevice>> GetOnlineDevices()
        {
            return Ok(StateSync.Instance.GetAllDevices().Select(pk =>
            {
                var session = StateSync.Instance.GetSession(pk);
                if (session?.Connected != true)
                    return null;
                return new SyncDevice
                {
                    PublicKey = pk,
                    DisplayName = session?.DisplayName,
                    Metadata = session?.Connected == true ? "Connected" : "Disconnected",
                    LinkType = (int)(session?.LinkType ?? LinkType.None)
                };
            }).Where(x=>x != null).ToList());
        }

        [HttpGet]
        public ActionResult<bool> HasAtLeastOneDevice()
        {
            return Ok(StateSync.Instance.HasAtLeastOneDevice());
        }

        [HttpGet]
        public ActionResult ValidateSyncDeviceInfoFormat([FromBody] SyncDeviceInfo syncDeviceInfo)
        {
            return Ok();
        }

        [HttpGet]
        public async Task<ActionResult> AddDevice([FromBody] SyncDeviceInfo syncDeviceInfo)
        {
            var dialog = new SyncStatusDialog();
            await dialog.Show();

            try
            {
                await StateSync.Instance.ConnectAsync(syncDeviceInfo, (complete, message) => 
                {
                    if (complete.GetValueOrDefault(false))
                        dialog.SetSuccess();
                    else
                        dialog.SetMessage(message);
                });
            }
            catch (Exception e)
            {
                dialog.SetError(e.Message);
            }
            
            return Ok();
        }

        [HttpGet]
        public async Task<ActionResult> RemoveDevice(string publicKey)
        {
            await StateSync.Instance.DeleteDeviceAsync(publicKey);
            return Ok();
        }



        //Functions
        [HttpGet]
        public async Task<IActionResult> SendToDevice(string device, string url, int position = 0)
        {
            var session = StateSync.Instance.GetSession(device);

            if (session == null)
            {
                return Ok(new
                {
                    Success = false,
                    Message = "Device not found"
                });
            }
            if (!session.Connected || !session.IsAuthorized)
            {
                return Ok(new
                {
                    Success = false,
                    Message = "Device not connected or authorized"
                });
            }
            await session.SendJsonDataAsync(GJSyncOpcodes.SendToDevice, new SendToDevicePackage()
            {
                Url = url,
                Position = position
            });
            return Ok();
        }
    }
}
