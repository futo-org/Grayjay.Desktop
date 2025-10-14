using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using Grayjay.Desktop.POC;

namespace Grayjay.ClientServer.Casting;

public class AirPlayCastingDevice : CastingDeviceLegacy
{
    private CancellationTokenSource? _cancellationTokenSource;
    private readonly HttpClient _client = new HttpClient();
    private string? _sessionId = null;
    private IPEndPoint? _remoteEndPoint = null;
    private bool _started = false;
    private IPEndPoint? _localEndPoint = null;
    public override IPEndPoint? LocalEndPoint => _localEndPoint;

    public AirPlayCastingDevice(CastingDeviceInfo deviceInfo) : base(deviceInfo) { }

    public override bool CanSetVolume => false;

    public override bool CanSetSpeed => true;

    public override async Task ChangeSpeedAsync(double speed, CancellationToken cancellationToken = default)
    {
        PlaybackState.SetSpeed(speed);
        await PostAsync($"rate?value={speed}", cancellationToken);
    }

    public override Task ChangeVolumeAsync(double volume, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public override async Task MediaLoadAsync(string streamType, string contentType, string contentId, TimeSpan resumePosition, TimeSpan duration, String? title, String thumbnailUrl, double? speed = null, CancellationToken cancellationToken = default)
    {
        PlaybackState.SetTime(resumePosition);
        PlaybackState.SetDuration(duration);

        if (resumePosition.TotalSeconds > 0.0) {
            var pos = resumePosition / duration;
            Logger.i(nameof(AirPlayCastingDevice), $"resumePosition: {resumePosition}, duration: {duration}, pos: {pos}");
            await PostAsync("play", "text/parameters", $"Content-Location: {contentId}\r\nStart-Position: {pos}");
        } else {
            await PostAsync("play", "text/parameters", $"Content-Location: {contentId}\r\nStart-Position: 0");
        }

        if (speed != null)
            await ChangeSpeedAsync(speed.Value, cancellationToken);
    }

    public override async Task MediaPauseAsync(CancellationToken cancellationToken = default)
    {
        PlaybackState.SetIsPlaying(false);
        await PostAsync($"rate?value=0.000000", cancellationToken);
    }

    public override async Task MediaResumeAsync(CancellationToken cancellationToken = default)
    {
        PlaybackState.SetIsPlaying(true);
        await PostAsync($"rate?value=1.000000", cancellationToken);
    }

    public override async Task MediaSeekAsync(TimeSpan time, CancellationToken cancellationToken = default)
    {
        PlaybackState.SetTime(time);
        await PostAsync($"scrub?position={time.TotalSeconds}", cancellationToken);
    }

    public override async Task MediaStopAsync(CancellationToken cancellationToken = default)
    {
        PlaybackState.SetIsPlaying(false);
        await PostAsync($"stop", cancellationToken);
    }

    public override void Start()
    {
        if (_started)
            return;

        _started = true;

        var cancellationTokenSource = new CancellationTokenSource();
        _cancellationTokenSource = cancellationTokenSource;
        _sessionId = Guid.NewGuid().ToString();
        _localEndPoint = null;
        _remoteEndPoint = null;

        _ = Task.Run(async () => 
        {
            ConnectionState.SetState(CastConnectionState.Connecting);

            //Initial loop when REP is unknown
            while (!cancellationTokenSource.IsCancellationRequested)
            {
                try
                {
                    using var connectedClient = await ConnectAsync(cancellationTokenSource.Token);
                    if (connectedClient == null)
                    {
                        Thread.Sleep(TimeSpan.FromSeconds(1));
                        Logger.i(nameof(AirPlayCastingDevice), "Connection failed, trying again in 1 second.");
                        continue;
                    }

                    _remoteEndPoint = connectedClient.Client.RemoteEndPoint as IPEndPoint;
                    _localEndPoint = connectedClient.Client.LocalEndPoint as IPEndPoint;
                    break;
                }
                catch (Exception ex)
                {
                    Logger.e(nameof(AirPlayCastingDevice), $"Exception occurred in AirPlay loop.", ex);
                }
            }

            if (cancellationTokenSource.IsCancellationRequested)
            {
                ConnectionState.SetState(CastConnectionState.Disconnected);
                return;
            }

            //Data loop
            while (!cancellationTokenSource.IsCancellationRequested)
            {
                try
                {
                    ConnectionState.SetState(CastConnectionState.Connecting);

                    while (!cancellationTokenSource.IsCancellationRequested)
                    {
                        var serverInfo = await GetServerInfoAsync(cancellationTokenSource.Token);
                        if (serverInfo == null)
                        {
                            ConnectionState.SetState(CastConnectionState.Connecting);
                            await Task.Delay(TimeSpan.FromSeconds(1));
                            continue;
                        }

                        ConnectionState.SetState(CastConnectionState.Connected);
                        var progressInfo = await GetProgressAsync(cancellationTokenSource.Token);
                        if (progressInfo == null)
                        {
                            await Task.Delay(TimeSpan.FromSeconds(1));
                            continue;
                        }

                        
                        int progressIndex = progressInfo.ToLower().IndexOf("position: ");
                        if (progressIndex == -1) 
                        {
                            await Task.Delay(TimeSpan.FromSeconds(1));
                            continue;
                        }

                        string progressSubstring = progressInfo.Substring(progressIndex + "position: ".Length);
                        double progress;
                        if (!double.TryParse(progressSubstring, out progress)) 
                        {
                            await Task.Delay(TimeSpan.FromSeconds(1));
                            continue;
                        }

                        PlaybackState.SetTime(TimeSpan.FromSeconds(progress));

                        int durationIndex = progressInfo.ToLower().IndexOf("duration: ");
                        if (durationIndex == -1) 
                        {
                            await Task.Delay(TimeSpan.FromSeconds(1));
                            continue;
                        }

                        string durationSubstring = progressInfo.Substring(durationIndex + "duration: ".Length);
                        double duration;
                        if (!double.TryParse(durationSubstring, out duration)) 
                        {
                            await Task.Delay(TimeSpan.FromSeconds(1));
                            continue;
                        }

                        PlaybackState.SetDuration(TimeSpan.FromSeconds(duration));
                        await Task.Delay(TimeSpan.FromSeconds(1));
                    }
                }
                catch (Exception ex)
                {
                    Logger.e(nameof(AirPlayCastingDevice), $"Exception occurred in AirPlay loop.", ex);
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationTokenSource.Token);
                }
            }

            ConnectionState.SetState(CastConnectionState.Disconnected);

            if (cancellationTokenSource.IsCancellationRequested)
                return;
        });
    }

    public override void Stop()
    {
        if (!_started)
            return;

        _started = false;

        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource = null;
    }

    private async Task<bool> PostAsync(string path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_sessionId)) return false;
        if (_remoteEndPoint == null) return false;

        try
        {
            var headers = new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase)
            {
                {"X-Apple-Device-ID", "0xdc2b61a0ce79"},
                {"User-Agent", "MediaControl/1.0"},
                {"X-Apple-Session-ID", _sessionId},
                {"Content-Length", "0"}
            };

            var url = $"http://{_remoteEndPoint.Address.ToUrlAddress()}:{_remoteEndPoint.Port}/{path}";
            Logger.i(nameof(AirPlayCastingDevice), $"POST {url}");

            var request = new HttpRequestMessage(HttpMethod.Post, url);
            foreach (var header in headers)
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);

            var response = await _client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return false;

            return true;
        }
        catch (Exception e)
        {
            Logger.e(nameof(AirPlayCastingDevice), $"Failed to POST {path}", e);
            return false;
        }
    }

    private async Task<bool> PostAsync(string path, string contentType, string body, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(_sessionId)) return false;
        if (_remoteEndPoint == null) return false;

        try
        {
            var headers = new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase)
            {
                {"X-Apple-Device-ID", "0xdc2b61a0ce79"},
                {"User-Agent", "MediaControl/1.0"},
                {"X-Apple-Session-ID", _sessionId}
            };

            var url = $"http://{_remoteEndPoint.Address.ToUrlAddress()}:{_remoteEndPoint.Port}/{path}";
            Logger.i(nameof(AirPlayCastingDevice), $"POST {url}:\n{body}");

            var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new ByteArrayContent(Encoding.UTF8.GetBytes(body))
            };

            request.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
            foreach (var header in headers)
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);

            var response = await _client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return false;

            return true;
        }
        catch (Exception e)
        {
            Logger.e(nameof(AirPlayCastingDevice), $"Failed to POST {path} {body}", e);
            return false;
        }
    }

    private async Task<string?> GetProgressAsync(CancellationToken cancellationToken = default) 
    {
        return await GetAsync("scrub", cancellationToken);
    }

    private async Task<string?> GetServerInfoAsync(CancellationToken cancellationToken = default) 
    {
        return await GetAsync("server-info", cancellationToken);
    }

    private async Task<string?> GetAsync(string path, CancellationToken cancellationToken = default)
    {
        if (_remoteEndPoint == null) return null;

        try
        {
            var headers = new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase)
            {
                {"X-Apple-Device-ID", "0xdc2b61a0ce79"},
                {"User-Agent", "MediaControl/1.0"}
            };

            if (!string.IsNullOrEmpty(_sessionId))
                headers["X-Apple-Session-ID"] = _sessionId;


            var url = $"http://{_remoteEndPoint.Address.ToUrlAddress()}:{_remoteEndPoint.Port}/{path}";
            Logger.i(nameof(AirPlayCastingDevice), $"GET {url}");

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            foreach (var header in headers)
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);

            var response = await _client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return null;

            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (string.IsNullOrEmpty(responseBody))
                return null;

            return responseBody;
        }
        catch (Exception e)
        {
            Logger.e(nameof(AirPlayCastingDevice), $"Failed to GET {path}", e);
            return null;
        }
    }
}
