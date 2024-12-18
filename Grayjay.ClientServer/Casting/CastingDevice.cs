using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Grayjay.Desktop.POC;

namespace Grayjay.ClientServer.Casting;

public class CastingDeviceConnectionState
{
    public CastConnectionState State { get; private set; } = CastConnectionState.Disconnected;
    public event Action<CastConnectionState>? StateChanged;
    public void SetState(CastConnectionState state)
    {
        State = state;
        StateChanged?.Invoke(State);
    }
}

public class CastingDevicePlaybackState
{
    public bool IsPlaying { get; private set; }
    public event Action<bool>? IsPlayingChanged;
    public void SetIsPlaying(bool isPlaying)
    {
        IsPlaying = isPlaying;
        IsPlayingChanged?.Invoke(isPlaying);
    }

    public TimeSpan Duration { get; private set; }
    public event Action<TimeSpan>? DurationChanged;
    public void SetDuration(TimeSpan duration)
    {
        Duration = duration;
        DurationChanged?.Invoke(duration);
    }

    public TimeSpan Time { get; private set; }
    public event Action<TimeSpan>? TimeChanged;
    private DateTime? _lastTimeChanged = null;
    public void SetTime(TimeSpan time)
    {
        Time = time;
        TimeChanged?.Invoke(time);
        _lastTimeChanged = DateTime.Now;
    }

    public TimeSpan ExpectedCurrentTime
    {
        get
        {
            if (IsPlaying && _lastTimeChanged != null)
                return DateTime.Now - _lastTimeChanged.Value + Time;
            else
                return Time;
        }
    }

    public double Volume { get; private set; }
    public event Action<double>? VolumeChanged;
    public void SetVolume(double volume)
    {
        Volume = volume;
        VolumeChanged?.Invoke(volume);
    }

    public double Speed { get; private set; }
    public event Action<double>? SpeedChanged;
    public void SetSpeed(double speed)
    {
        Speed = speed;
        SpeedChanged?.Invoke(speed);
    }

    public bool IsSame(CastingDevicePlaybackState state)
    {
        return IsPlaying == state.IsPlaying &&
            Duration == state.Duration &&
            Volume == state.Volume &&
            ExpectedCurrentTime == state.ExpectedCurrentTime &&
            Speed == state.Speed &&
            Time == state.Time;
    }
}

public abstract class CastingDevice
{
    public CastingDeviceInfo DeviceInfo { get; }
    public readonly CastingDeviceConnectionState ConnectionState = new CastingDeviceConnectionState();
    public readonly CastingDevicePlaybackState PlaybackState = new CastingDevicePlaybackState();
    public abstract bool CanSetVolume { get; }
    public abstract bool CanSetSpeed { get; }
    public abstract IPEndPoint? LocalEndPoint { get; }
    
    public CastingDevice(CastingDeviceInfo deviceInfo)
    {
        DeviceInfo = deviceInfo;
    }

    public bool IsSame(CastingDevice device)
    {
        return DeviceInfo.Equals(device.DeviceInfo) &&
            ConnectionState.State == device.ConnectionState.State &&
            PlaybackState.IsSame(device.PlaybackState) &&
            CanSetVolume == device.CanSetVolume &&
            CanSetSpeed == device.CanSetSpeed &&
            LocalEndPoint == device.LocalEndPoint;
    }

    public abstract void Start();
    public abstract void Stop();

    public abstract Task MediaSeekAsync(TimeSpan time, CancellationToken cancellationToken = default);
    public abstract Task MediaStopAsync(CancellationToken cancellationToken = default);
    public abstract Task MediaPauseAsync(CancellationToken cancellationToken = default);
    public abstract Task MediaResumeAsync(CancellationToken cancellationToken = default);
    public abstract Task MediaLoadAsync(string streamType, string contentType, string contentId, TimeSpan resumePosition, TimeSpan duration, double? speed = null, CancellationToken cancellationToken = default);

    public abstract Task ChangeVolumeAsync(double volume, CancellationToken cancellationToken = default);
    public abstract Task ChangeSpeedAsync(double speed, CancellationToken cancellationToken = default);

    public async Task<TcpClient?> ConnectAsync(CancellationToken cancellationToken = default)
        => await Utilities.ConnectAsync(DeviceInfo.IPAddresses, DeviceInfo.Port, TimeSpan.FromSeconds(2), cancellationToken);
}