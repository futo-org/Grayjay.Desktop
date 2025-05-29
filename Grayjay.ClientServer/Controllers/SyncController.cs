using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Grayjay.ClientServer.Dialogs;
using Grayjay.ClientServer.Settings;
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
                var session = StateSync.Instance.SyncService?.GetSession(pk) ?? null;
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
                var session = StateSync.Instance.SyncService?.GetSession(pk);
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

        public class StatusResponse
        {
            public bool ServerSocketStarted { get; init; }
            public bool RelayConnected { get; init; }
            public bool ServerSocketFailedToStart { get; init; }
        }

        [HttpGet]
        public ActionResult<StatusResponse> Status()
        {
            return Ok(new StatusResponse
            {
                RelayConnected = StateSync.Instance.SyncService?.RelayConnected ?? false,
                ServerSocketStarted = StateSync.Instance.SyncService?.ServerSocketStarted ?? false,
                ServerSocketFailedToStart = StateSync.Instance.SyncService?.ServerSocketFailedToStart ?? false
            });
        }

        [HttpGet]
        public ActionResult<bool> HasAtLeastOneDevice()
        {
            return Ok(StateSync.Instance.HasAtLeastOneDevice());
        }

        public class ValidateSyncDeviceInfoFormatRequest
        {
            public required string Url { get; init; }
        }

        public class ValidateSyncDeviceInfoFormatResponse
        {
            public bool Valid { get; init; }
            public string? Message { get; init; }
        }

        [HttpPost]
        public ActionResult<ValidateSyncDeviceInfoFormatResponse> ValidateSyncDeviceInfoFormat([FromBody] ValidateSyncDeviceInfoFormatRequest f)
        {
            var url = f.Url;
            if (string.IsNullOrEmpty(url))
            {
                return Ok(new ValidateSyncDeviceInfoFormatResponse
                {
                    Valid = false,
                    Message = "URL must not be null or empty."
                });
            }

            url = url.Trim();
            if (!url.StartsWith("grayjay://sync/"))
            {
                return Ok(new ValidateSyncDeviceInfoFormatResponse
                {
                    Valid = false,
                    Message = "URL should start with 'grayjay://sync/'."
                });
            }

            byte[] deviceFormatBytes;
            try
            {
                deviceFormatBytes = url.Substring("grayjay://sync/".Length).DecodeBase64Url();
            }
            catch (Exception e)
            {
                return Ok(new ValidateSyncDeviceInfoFormatResponse
                {
                    Valid = false,
                    Message = "Not a valid base64."
                });
            }

            try
            {
                var jsonString = Encoding.UTF8.GetString(deviceFormatBytes);
                var syncDeviceInfo = JsonSerializer.Deserialize<SyncDeviceInfo>(jsonString);
            }
            catch
            {
                return Ok(new ValidateSyncDeviceInfoFormatResponse
                {
                    Valid = false,
                    Message = "Not a valid JSON."
                });
            }

            return Ok(new ValidateSyncDeviceInfoFormatResponse
            {
                Valid = true,
                Message = null
            });
        }

        public class AddDeviceRequest
        {
            public required string Url { get; init; }
        }

        [HttpPost]
        public async Task<ActionResult> AddDevice([FromBody] AddDeviceRequest r)
        {
            var syncManager = StateSync.Instance.SyncService;
            if (syncManager == null)
                throw new Exception("SyncManager must be started first.");

            var dialog = new SyncStatusDialog();
            await dialog.Show();

            try
            {
                var url = r.Url;
                if (string.IsNullOrEmpty(url))
                    throw new Exception("URL must not be null or empty.");

                url = url.Trim();
                if (!url.StartsWith("grayjay://sync/"))
                    throw new Exception("URL should start with 'grayjay://sync/'.");

                byte[] deviceFormatBytes = url.Substring("grayjay://sync/".Length).DecodeBase64Url();
                var jsonString = Encoding.UTF8.GetString(deviceFormatBytes);
                var syncDeviceInfo = JsonSerializer.Deserialize<SyncDeviceInfo>(jsonString)!;
                await syncManager.ConnectAsync(syncDeviceInfo, (complete, message) => 
                {
                    if (complete.HasValue)
                    {
                        if (complete.Value)
                        {
                            dialog.SetSuccess();
                        }
                        else
                        {
                            dialog.SetError(message);
                        }
                    }
                    else
                    {
                        dialog.SetPairing(message);
                    }
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
            var session = StateSync.Instance.SyncService?.GetSession(device);
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
