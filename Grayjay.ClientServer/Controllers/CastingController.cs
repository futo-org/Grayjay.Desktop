using Grayjay.ClientServer.Casting;
using Grayjay.ClientServer.Proxy;
using Grayjay.ClientServer.States;
using Grayjay.Desktop.POC;
using Microsoft.AspNetCore.Mvc;

namespace Grayjay.ClientServer.Controllers
{
    [Route("[controller]/[action]")]
    public class CastingController : ControllerBase
    {
        [HttpGet]
        public List<CastingDeviceInfo> DiscoveredDevices()
        {
            return StateCasting.Instance.DiscoveredDevices.Select(v => v.DeviceInfo).ToList();
        }

        [HttpGet]
        public List<CastingDeviceInfo> PinnedDevices()
        {
            return StateCasting.Instance.PinnedDevices.ToList();
        }

        [HttpPost]
        public ActionResult<Guid> AddPinnedDevice([FromBody] CastingDeviceInfo castingDeviceInfo)
        {
            StateCasting.Instance.AddPinnedDevice(castingDeviceInfo);
            return Ok();
        }

        [HttpPost]
        public ActionResult RemovePinnedDevice([FromBody] CastingDeviceInfo castingDeviceInfo)
        {
            StateCasting.Instance.RemovePinnedDevice(castingDeviceInfo);
            return Ok();
        }

        [HttpGet]
        public async Task<IActionResult> Connect(string id)
        {
            var instance = GrayjayCastingServer.Instance; //TODO: Make a nicer way to ensure the instance gets created

            CastingDevice? castingDevice;
            var pinnedDeviceInfo = StateCasting.Instance.PinnedDevices.FirstOrDefault(x => x.Id == id);
            if (pinnedDeviceInfo != null)
            {
                castingDevice = pinnedDeviceInfo.Type switch
                {
                    CastProtocolType.Chromecast => new ChromecastCastingDevice(pinnedDeviceInfo),
                    CastProtocolType.Airplay => new AirPlayCastingDevice(pinnedDeviceInfo),
                    CastProtocolType.FCast => new FCastCastingDevice(pinnedDeviceInfo),
                    _ => throw new Exception($"Invalid cast protocol type {pinnedDeviceInfo.Type}")
                };
            }
            else
            {
                castingDevice = StateCasting.Instance.DiscoveredDevices.FirstOrDefault(x => x.DeviceInfo.Id == id);
                if (castingDevice != null)
                    StateCasting.Instance.PinnedDevices.Add(castingDevice.DeviceInfo);
            }

            if (castingDevice != null)
                StateCasting.Instance.Connect(castingDevice);
            else
                StateCasting.Instance.Disconnect();

            return Ok();
        }

        [HttpGet]
        public IActionResult Disconnect()
        {
            StateCasting.Instance.Disconnect();
            return Ok();
        }

        [HttpGet]
        public async Task<ActionResult> MediaSeek(double time, CancellationToken cancellationToken)
        {
            Task? task = StateCasting.Instance.ActiveDevice?.MediaSeekAsync(TimeSpan.FromSeconds(time), cancellationToken);
            if (task != null)
                await task;
            return Ok();
        }

        [HttpGet]
        public async Task<ActionResult> MediaStop(CancellationToken cancellationToken)
        {
            Task? task = StateCasting.Instance.ActiveDevice?.MediaStopAsync(cancellationToken);
            if (task != null)
                await task;
            return Ok();
        }

        [HttpGet]
        public async Task<ActionResult> MediaPause(CancellationToken cancellationToken)
        {
            Task? task = StateCasting.Instance.ActiveDevice?.MediaPauseAsync(cancellationToken);
            if (task != null)
                await task;
            return Ok();
        }

        [HttpGet]
        public async Task<ActionResult> MediaResume(CancellationToken cancellationToken)
        {
            Task? task = StateCasting.Instance.ActiveDevice?.MediaResumeAsync(cancellationToken);
            if (task != null)
                await task;
            return Ok();
        }

        [HttpGet]
        public async Task<ActionResult> MediaLoad(string streamType, double resumePosition, double duration, int videoIndex, int audioIndex, int subtitleIndex, bool videoIsLocal = false, bool audioIsLocal = false, bool subtitleIsLocal = false, double? speed = null, CancellationToken cancellationToken = default, string? tag = null)
        {
            var activeDevice = StateCasting.Instance.ActiveDevice;
            if (activeDevice == null)
                return BadRequest("No active device.");

            //TODO: Uncomment
            //var proxyInnerSources = activeDevice is FCastCastingDevice ? false : true;
            var shouldProxy = activeDevice is FCastCastingDevice ? false : true;
            var sourceDescriptor = await DetailsController.GenerateSourceProxy(this.State(), videoIndex, audioIndex, subtitleIndex, videoIsLocal, audioIsLocal, subtitleIsLocal, new ProxySettings(false, shouldProxy, proxyAddress: activeDevice.LocalEndPoint?.Address, exposeLocalAsAny: true), tag, forceReady: true);
            if (sourceDescriptor.Url.StartsWith("/"))
                sourceDescriptor.Url = $"http://{activeDevice.LocalEndPoint?.Address.ToUrlAddress()}:{GrayjayCastingServer.Instance.BaseUri!.Port}" + sourceDescriptor.Url;

            Logger.i(nameof(CastingController), $"Started casting '{sourceDescriptor.Url}' with content type '{sourceDescriptor.Type}'.");
            Task? task = StateCasting.Instance.ActiveDevice?.MediaLoadAsync(streamType, sourceDescriptor.Type, sourceDescriptor.Url, TimeSpan.FromSeconds(resumePosition), TimeSpan.FromSeconds(duration), speed, cancellationToken);
            if (task != null)
                await task;
            return Ok();
        }

        [HttpGet]
        public async Task<ActionResult> ChangeVolume(double volume, CancellationToken cancellationToken)
        {
            Task? task = StateCasting.Instance.ActiveDevice?.ChangeVolumeAsync(volume, cancellationToken);
            if (task != null)
                await task;
            return Ok();
        }

        [HttpGet]
        public async Task<ActionResult> ChangeSpeed(double speed, CancellationToken cancellationToken)
        {
            Task? task = StateCasting.Instance.ActiveDevice?.ChangeSpeedAsync(speed, cancellationToken);
            if (task != null)
                await task;
            return Ok();
        }
    }
}