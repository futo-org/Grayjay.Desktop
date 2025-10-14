using System.Net;
using Grayjay.Desktop.POC;

namespace Grayjay.ClientServer.Casting;

public class CastingDeviceExperimentalWrapper : CastingDevice
{
    internal FCast.SenderSDK.CastingDevice inner;
    private CastingDeviceInfo info;

    internal void UpdateInfo(FCast.SenderSDK.DeviceInfo info) {
        inner.SetAddresses(info.addresses);
        inner.SetPort(info.port);
        this.info = CastingDeviceInfo.FromRsInfo(info);
    }

    internal class EventHandler: FCast.SenderSDK.DeviceEventHandler {
        private Action<IPAddress> _localEndPointChanged;
        private CastingDeviceConnectionState ConnectionState;
        private CastingDevicePlaybackState PlaybackState;

        public EventHandler(Action<IPAddress> _localEndPointChanged, CastingDeviceConnectionState ConnectionState, CastingDevicePlaybackState PlaybackState) {
            this.ConnectionState = ConnectionState;
            this.PlaybackState = PlaybackState;
            this._localEndPointChanged = _localEndPointChanged;
        }

        public void ConnectionStateChanged(FCast.SenderSDK.DeviceConnectionState state) {
            switch (state) {
            case FCast.SenderSDK.DeviceConnectionState.Connecting:
                ConnectionState.SetState(CastConnectionState.Connecting);
                break;
            case FCast.SenderSDK.DeviceConnectionState.Connected(
                FCast.SenderSDK.IpAddr usedRemoteAddr,
                FCast.SenderSDK.IpAddr localAddr
            ):
                _localEndPointChanged(localAddr switch {
                        FCast.SenderSDK.IpAddr.V4(byte @o1, byte @o2, byte @o3, byte @o4) =>
                            new IPAddress([@o1, @o2, @o3, @o4]),
                        FCast.SenderSDK.IpAddr.V6(
                            byte @o1,
                            byte @o2,
                            byte @o3,
                            byte @o4,
                            byte @o5,
                            byte @o6,
                            byte @o7,
                            byte @o8,
                            byte @o9,
                            byte @o10,
                            byte @o11,
                            byte @o12,
                            byte @o13,
                            byte @o14,
                            byte @o15,
                            byte @o16,
                            uint @scopeId
                        ) =>
                            new IPAddress(
                                [@o1, @o2, @o3, @o4, @o5, @o6, @o7, @o8, @o9, @o10, @o11, @o12, @o13, @o14, @o15, @o16],
                                @scopeId
                            ),
                    });
                ConnectionState.SetState(CastConnectionState.Connected);
                break;
            case FCast.SenderSDK.DeviceConnectionState.Reconnecting:
                ConnectionState.SetState(CastConnectionState.Connecting);
                break;
            case FCast.SenderSDK.DeviceConnectionState.Disconnected:
                ConnectionState.SetState(CastConnectionState.Disconnected);
                break;
            }
        }

        public void VolumeChanged(double volume) => PlaybackState.SetVolume(volume);
        public void TimeChanged(double time) => PlaybackState.SetTime(TimeSpan.FromSeconds(time));
        public void PlaybackStateChanged(FCast.SenderSDK.PlaybackState state) => PlaybackState.SetIsPlaying(state switch
        {
            FCast.SenderSDK.PlaybackState.Playing => true,
            _ => false
        });
        public void DurationChanged(double duration) => PlaybackState.SetDuration(TimeSpan.FromSeconds(duration));
        public void SpeedChanged(double speed) => PlaybackState.SetSpeed(speed);
        public void SourceChanged(FCast.SenderSDK.Source @source) {}
        public void KeyEvent(FCast.SenderSDK.KeyEvent @event) {}
        public void MediaEvent(FCast.SenderSDK.MediaEvent @event) {}
        public void PlaybackError(string message) => Logger.e(nameof(CastingDeviceExperimentalWrapper), $"Playback error: {@message}");
    }

    internal CastingDeviceExperimentalWrapper(FCast.SenderSDK.CastingDevice dev, CastingDeviceInfo info) {
        inner = dev;
        this.info = info;
    }

    public override CastingDeviceInfo DeviceInfo { get => info; set => info = value; }

    public override bool CanSetVolume => inner.SupportsFeature(FCast.SenderSDK.DeviceFeature.SetVolume);

    public override bool CanSetSpeed => inner.SupportsFeature(FCast.SenderSDK.DeviceFeature.SetSpeed);

    private IPEndPoint? _localEndPoint = null;
    public override IPEndPoint? LocalEndPoint => _localEndPoint;

    public override Task ChangeSpeedAsync(double speed, CancellationToken cancellationToken = default)
    {
        try {
            inner.ChangeSpeed(speed);
        } catch (Exception e) {
            Logger.e(nameof(CastingDeviceExperimentalWrapper), "Failed to change speed", e);
        }
        return Task.CompletedTask;
    }

    public override Task ChangeVolumeAsync(double volume, CancellationToken cancellationToken = default)
    {
        try {
            inner.ChangeVolume(volume);
        } catch (Exception e) {
            Logger.e(nameof(CastingDeviceExperimentalWrapper), "Failed to change volume", e);
        }
        return Task.CompletedTask;
    }

    public override Task MediaLoadAsync(string streamType, string contentType, string contentId, TimeSpan resumePosition, TimeSpan duration, String? title, String thumbnailUrl, double? speed = null, CancellationToken cancellationToken = default)
    {
        try {
            inner.Load(
                new FCast.SenderSDK.LoadRequest.Url(
                    contentType,
                    contentId,
                    resumePosition.TotalSeconds,
                    speed,
                    PlaybackState.Volume,
                    new FCast.SenderSDK.Metadata(title, thumbnailUrl),
                    null
                )
            );
        } catch (Exception e) {
            Logger.e(nameof(CastingDeviceExperimentalWrapper), "Failed to laod media", e);
        }
        return Task.CompletedTask;
    }

    public override Task MediaPauseAsync(CancellationToken cancellationToken = default)
    {
        try {
            inner.PausePlayback();
        } catch (Exception e) {
            Logger.e(nameof(CastingDeviceExperimentalWrapper), "Failed to pause playback", e);
        }
        return Task.CompletedTask;
    }

    public override Task MediaResumeAsync(CancellationToken cancellationToken = default)
    {
        try {
            inner.ResumePlayback();
        } catch (Exception e) {
            Logger.e(nameof(CastingDeviceExperimentalWrapper), "Failed to resume playback", e);
        }
        return Task.CompletedTask;
    }

    public override Task MediaSeekAsync(TimeSpan time, CancellationToken cancellationToken = default)
    {
        try {
            inner.Seek(time.TotalSeconds);
        } catch (Exception e) {
            Logger.e(nameof(CastingDeviceExperimentalWrapper), "Failed to seek", e);
        }
        return Task.CompletedTask;
    }

    public override Task MediaStopAsync(CancellationToken cancellationToken = default)
    {
        try {
            inner.StopPlayback();
        } catch (Exception e) {
            Logger.e(nameof(CastingDeviceExperimentalWrapper), "Failed to stop playback", e);
        }
        return Task.CompletedTask;
    }

    public override void Start()
    {
        try {
            inner.Connect(
                null,
                new EventHandler((ip) => _localEndPoint = new IPEndPoint(ip, 0), ConnectionState, PlaybackState),
                1000
            );
        } catch (Exception e) {
            Logger.e(nameof(CastingDeviceExperimentalWrapper), "Failed to connect to device", e);
        }
    }

    public override void Stop()
    {
        try {
            inner.Disconnect();
        } catch (Exception e) {
            Logger.e(nameof(CastingDeviceExperimentalWrapper), "Failed to disconnect from device", e);
        }
    }
}
