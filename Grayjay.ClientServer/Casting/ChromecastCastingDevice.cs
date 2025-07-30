using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Grayjay.Desktop.POC;
using Grayjay.ClientServer.Protobuffers;
using Google.Protobuf;
using System.Text.Json.Nodes;
using System.Text.Json;
using System.Net.Security;
using Noise;

namespace Grayjay.ClientServer.Casting;

public class ChromecastCastingDevice : CastingDevice
{
    private struct MediaLoadInformation
    {
        public required string ContentId;
        public required string StreamType;
        public required string ContentType;
        public required TimeSpan ResumePosition;
        public required TimeSpan Duration;
        public double? Speed;
    };

    private const int MAX_LAUNCH_RETRIES = 3;

    private CancellationTokenSource? _cancellationTokenSource;
    private Stream? _stream = null;
    private int _requestId = 0;
    private string? _transportId = null;
    private int? _mediaSessionId = null;
    private string? _sessionId = null;
    private bool _launching = false;
    private bool _started = false;
    private bool _autoLaunchEnabled = true;
    private int _launchRetries;
    private DateTime? _lastLaunchTime = null;
    private MediaLoadInformation? _mediaLoadInformation = null;
    private SemaphoreSlim _writerSemaphore = new SemaphoreSlim(1);
    private SemaphoreSlim _readerSemaphore = new SemaphoreSlim(1);
    private Task? _retryTask = null;

    public ChromecastCastingDevice(CastingDeviceInfo deviceInfo) : base(deviceInfo) { }

    public override bool CanSetVolume => true;
    public override bool CanSetSpeed => true;
    private IPEndPoint? _localEndPoint = null;
    public override IPEndPoint? LocalEndPoint => _localEndPoint;

    public override async Task ChangeSpeedAsync(double speed, CancellationToken cancellationToken = default)
    {
        var transportId = _transportId;
        var mediaSessionId = _mediaSessionId;
        if (transportId == null || mediaSessionId == null)
            return;

        var clamped = Math.Clamp(speed, 1.0, 2.0);
        PlaybackState.SetSpeed(clamped);

        var setSpeedObject = new JsonObject
        {
            ["type"] = "SET_PLAYBACK_RATE",
            ["mediaSessionId"] = mediaSessionId.Value,
            ["playbackRate"] = clamped,
            ["requestId"] = _requestId++
        };

        await SendChannelMessageAsync("sender-0", transportId, "urn:x-cast:com.google.cast.media", setSpeedObject.ToJsonString(), cancellationToken);
    }

    public override async Task ChangeVolumeAsync(double volume, CancellationToken cancellationToken = default)
    {
        var setVolumeObject = new JsonObject
        {
            ["type"] = "SET_VOLUME",
            ["volume"] = new JsonObject
            {
                ["level"] = volume
            },
            ["requestId"] = _requestId++
        };

        await SendChannelMessageAsync("sender-0", "receiver-0", "urn:x-cast:com.google.cast.receiver", setVolumeObject.ToJsonString(), cancellationToken);
    }

    private async Task ConnectMediaChannelAsync(string transportId, CancellationToken cancellationToken = default)
    {
        var connectObject = new JsonObject
        {
            ["type"] = "CONNECT",
            ["connType"] = 0,
            ["requestId"] = _requestId++
        };

        await SendChannelMessageAsync("sender-0", transportId, "urn:x-cast:com.google.cast.tp.connection", connectObject.ToJsonString(), cancellationToken);
    }

    private async Task RequestMediaStatusAsync(CancellationToken cancellationToken = default)
    {
        var transportId = _transportId;
        if (transportId == null)
            return;

        var requestMediaStatusObject = new JsonObject
        {
            ["type"] = "GET_STATUS",
            ["requestId"] = _requestId++
        };

        await SendChannelMessageAsync("sender-0", transportId, "urn:x-cast:com.google.cast.media", requestMediaStatusObject.ToJsonString(), cancellationToken);
    }

    private async Task MediaPlayAsync(CancellationToken cancellationToken = default)
    {
        var transportId = _transportId;
        if (transportId == null)
            return;

        var mediaLoadInformation = _mediaLoadInformation;
        if (mediaLoadInformation == null)
            return;

        var loadObject = new JsonObject
        {
            ["type"] = "LOAD",
            ["media"] = new JsonObject
            {
                ["contentId"] = mediaLoadInformation.Value.ContentId,
                ["streamType"] = mediaLoadInformation.Value.StreamType,
                ["contentType"] = mediaLoadInformation.Value.ContentType
            },
            ["requestId"] = _requestId++
        };

        var resumePosition = mediaLoadInformation.Value.ResumePosition.TotalSeconds;
        if (resumePosition > 0.0)
            loadObject.Add("currentTime", resumePosition);

        //TODO: This replace is necessary to get rid of backward slashes added by the JSON Object serializer, is this necessary here also?
        //val json = loadObject.toString().replace("\\/","/");
        await SendChannelMessageAsync("sender-0", transportId, "urn:x-cast:com.google.cast.media", loadObject.ToJsonString(), cancellationToken);
    }

    public override async Task MediaLoadAsync(string streamType, string contentType, string contentId, TimeSpan resumePosition, TimeSpan duration, double? speed = null, CancellationToken cancellationToken = default)
    {
        _mediaLoadInformation = new MediaLoadInformation
        {
            StreamType = streamType,
            ContentType = contentType,
            ContentId = contentId,
            ResumePosition = resumePosition,
            Duration = duration,
            Speed = speed
        };

        PlaybackState.SetTime(resumePosition);
        PlaybackState.SetDuration(duration);

        await MediaPlayAsync(cancellationToken);
    }

    public override async Task MediaPauseAsync(CancellationToken cancellationToken = default)
    {
        var transportId = _transportId;
        if (transportId == null)
            return;

        var mediaSessionId = _mediaSessionId;
        if (mediaSessionId == null)
            return;

        var pauseObject = new JsonObject
        {
            ["type"] = "PAUSE",
            ["mediaSessionId"] = mediaSessionId.Value,
            ["requestId"] = _requestId++
        };

        await SendChannelMessageAsync("sender-0", transportId, "urn:x-cast:com.google.cast.media", pauseObject.ToJsonString(), cancellationToken);
    }

    public override async Task MediaResumeAsync(CancellationToken cancellationToken = default)
    {
        var transportId = _transportId;
        if (transportId == null)
            return;

        var mediaSessionId = _mediaSessionId;
        if (mediaSessionId == null)
            return;

        var playObject = new JsonObject
        {
            ["type"] = "PLAY",
            ["mediaSessionId"] = mediaSessionId.Value,
            ["requestId"] = _requestId++
        };

        await SendChannelMessageAsync("sender-0", transportId, "urn:x-cast:com.google.cast.media", playObject.ToJsonString(), cancellationToken);
    }

    public override async Task MediaSeekAsync(TimeSpan time, CancellationToken cancellationToken = default)
    {
        var transportId = _transportId;
        if (transportId == null)
            return;

        var mediaSessionId = _mediaSessionId;
        if (mediaSessionId == null)
            return;

        var seekObject = new JsonObject
        {
            ["type"] = "SEEK",
            ["mediaSessionId"] = mediaSessionId.Value,
            ["requestId"] = _requestId++,
            ["currentTime"] = time.TotalSeconds
        };

        await SendChannelMessageAsync("sender-0", transportId, "urn:x-cast:com.google.cast.media", seekObject.ToJsonString(), cancellationToken);
    }

    public override async Task MediaStopAsync(CancellationToken cancellationToken = default)
    {
        var transportId = _transportId;
        if (transportId == null)
            return;

        var mediaSessionId = _mediaSessionId;
        if (mediaSessionId == null)
            return;

        var stopObject = new JsonObject
        {
            ["type"] = "STOP",
            ["mediaSessionId"] = mediaSessionId.Value,
            ["requestId"] = _requestId++
        };

        await SendChannelMessageAsync("sender-0", transportId, "urn:x-cast:com.google.cast.media", stopObject.ToJsonString(), cancellationToken);
    }

    private async Task LaunchPlayerAsync(CancellationToken cancellationToken = default)
    {
        var launchObject = new JsonObject
        {
            ["type"] = "LAUNCH",
            ["appId"] = "CC1AD845",
            ["requestId"] = _requestId++
        };

        await SendChannelMessageAsync("sender-0", "receiver-0", "urn:x-cast:com.google.cast.receiver", launchObject.ToJsonString(), cancellationToken);
        _lastLaunchTime = DateTime.Now;
    }

    public override void Start()
    {
        if (_started)
            return;

        _started = true;
        _autoLaunchEnabled = true;
        _launching = true;
        _launchRetries = 0;
        _sessionId = null;
        _mediaSessionId = null;

        var cancellationTokenSource = new CancellationTokenSource();
        _cancellationTokenSource = cancellationTokenSource;
        _localEndPoint = null;

        _ = Task.Run(async () => 
        {
            ConnectionState.SetState(CastConnectionState.Connecting);

            //Initial loop when REP is unknown
            IPEndPoint? rep = null;
            TcpClient? connectedClient = null;
            while (!cancellationTokenSource.IsCancellationRequested)
            {
                try
                {
                    connectedClient = await ConnectAsync(cancellationTokenSource.Token);
                    if (connectedClient == null)
                    {
                        Thread.Sleep(TimeSpan.FromSeconds(1));
                        Logger.i(nameof(ChromecastCastingDevice), "Connection failed, trying again in 1 second.");
                        continue;
                    }

                    rep = connectedClient.Client.RemoteEndPoint as IPEndPoint;
                    break;
                }
                catch (Exception ex)
                {
                    Logger.e(nameof(ChromecastCastingDevice), $"Exception occurred in Chromecast loop.", ex);
                }
            }

            if (cancellationTokenSource.IsCancellationRequested)
            {
                ConnectionState.SetState(CastConnectionState.Disconnected);
                return;
            }

            TcpClient? client = null;

            //Data loop
            while (!cancellationTokenSource.IsCancellationRequested)
            {
                try
                {
                    Logger.i(nameof(ChromecastCastingDevice), "Connecting to chromecast.");
                    ConnectionState.SetState(CastConnectionState.Connecting);

                    var streamToDispose = _stream;
                    if (streamToDispose != null)
                    {
                        _stream = null;
                        streamToDispose?.Dispose();
                    }

                    client?.Dispose();
                    _requestId = 0;
                    _transportId = null;
                    _mediaSessionId = null;
                    _sessionId = null;
                    _localEndPoint = null;
                    _launchRetries = 0;

                    SslStream newStream;
                    if (connectedClient != null)
                    {
                        Logger.i(nameof(ChromecastCastingDevice), "Using connected socket.");
                        newStream = new SslStream(connectedClient.GetStream());
                        client = connectedClient;
                        connectedClient = null;
                    }
                    else
                    {
                        Logger.i(nameof(ChromecastCastingDevice), "Using new socket.");
                        var c = new TcpClient();
                        await c.ConnectAsync(rep!, cancellationTokenSource.Token);
                        newStream = new SslStream(c.GetStream());
                        client = c;
                    }

                    await newStream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
                    {
                        RemoteCertificateValidationCallback = (_, _, _, _) => true
                    }, cancellationTokenSource.Token);

                    _stream = newStream;
                    _localEndPoint = client.Client.LocalEndPoint as IPEndPoint;

                    var connectObject = new JsonObject
                    {
                        ["type"] = "CONNECT",
                        ["connType"] = 0
                    };
                    await SendChannelMessageAsync("sender-0", "receiver-0", "urn:x-cast:com.google.cast.tp.connection", connectObject.ToJsonString(), cancellationTokenSource.Token);

                    var requestStatusObject = new JsonObject
                    {
                        ["type"] = "GET_STATUS",
                        ["requestId"] = _requestId++
                    };
                    await SendChannelMessageAsync("sender-0", "receiver-0", "urn:x-cast:com.google.cast.receiver", requestStatusObject.ToJsonString(), cancellationTokenSource.Token);

                    var loopCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationTokenSource.Token);

                    var buffer = new byte[409600];
                    var receiveTask = Task.Run(async () => 
                    {
                        Logger.i(nameof(ChromecastCastingDevice), "Started receive loop.");

                        while (!loopCancellationTokenSource.IsCancellationRequested)
                        {
                            try
                            {
                                await _readerSemaphore.WaitAsync();

                                int size;
                                try
                                {
                                    byte[] sizeBytes = new byte[4];
                                    var bytesRead = await newStream.ReadAsync(sizeBytes, loopCancellationTokenSource.Token);
                                    if (bytesRead == 0)
                                        throw new Exception($"Connection was closed.");
                                    if (bytesRead != 4)
                                        throw new Exception($"Unexpected amount of size bytes {bytesRead}.");

                                    size = BinaryPrimitives.ReadInt32BigEndian(sizeBytes);
                                    Logger.i(nameof(ChromecastCastingDevice), $"Received header indicating {size} bytes.");
                                    if (size > buffer.Length)
                                    {
                                        Logger.w(nameof(ChromecastCastingDevice), $"Skipping packet that is too large {size} bytes.");
                                        await newStream.SkipAsync(size, loopCancellationTokenSource.Token);
                                        continue;
                                    }

                                    Logger.i(nameof(ChromecastCastingDevice), $"Started receiving body.");

                                    bytesRead = 0;
                                    while (!loopCancellationTokenSource.IsCancellationRequested && bytesRead < size)
                                    {
                                        var result = await newStream.ReadAsync(buffer, bytesRead, size - bytesRead, loopCancellationTokenSource.Token);
                                        if (result == 0)
                                            throw new Exception($"Connection was closed.");

                                        bytesRead += result;
                                    }
                                }
                                finally
                                {
                                    _readerSemaphore.Release();
                                }

                                Logger.i(nameof(ChromecastCastingDevice), $"Received {size} bytes.");
                                var castMessage = CastMessage.Parser.ParseFrom(buffer, 0, size);
                                if (castMessage.Namespace != "urn:x-cast:com.google.cast.tp.heartbeat")
                                    Logger.i(nameof(ChromecastCastingDevice), $"Received message: {castMessage}");

                                try
                                {
                                    await HandleMessageAsync(castMessage, loopCancellationTokenSource.Token);
                                }
                                catch (Exception e)
                                {
                                    Logger.w(nameof(ChromecastCastingDevice), "Failed to handle message.", e);
                                }
                            }
                            catch (Exception e)
                            {
                                Logger.e(nameof(ChromecastCastingDevice), "Socket exception while receiving.", e);
                                break;
                            }
                        }

                        Logger.i(nameof(ChromecastCastingDevice), "Stopped receive loop.");
                    });

                    var pingTask = Task.Run(async () =>
                    {
                        Logger.i(nameof(ChromecastCastingDevice), "Started ping loop.");

                        var pingObject = new JsonObject
                        {
                            ["type"] = "PING"
                        };
                        var pingJson = pingObject.ToJsonString();

                        while (!loopCancellationTokenSource.IsCancellationRequested)
                        {
                            try
                            {
                                await SendChannelMessageAsync("sender-0", "receiver-0", "urn:x-cast:com.google.cast.tp.heartbeat", pingJson, loopCancellationTokenSource.Token);
                                await Task.Delay(TimeSpan.FromSeconds(5), loopCancellationTokenSource.Token);
                            }
                            catch (Exception pingException)
                            {
                                Logger.e(nameof(ChromecastCastingDevice), "Failed to send ping.", pingException);
                                break;
                            }
                        }

                        Logger.i(nameof(ChromecastCastingDevice), "Stopped ping loop.");
                    });

                    await Task.WhenAny(pingTask, receiveTask);
                    loopCancellationTokenSource.Cancel();
                }
                catch (Exception ex)
                {
                    Logger.e(nameof(ChromecastCastingDevice), $"Exception occurred in ChromeCast loop.", ex);
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationTokenSource.Token);
                }
            }

            ConnectionState.SetState(CastConnectionState.Disconnected);

            try
            {
                _stream?.Dispose();
                _stream = null;
                client?.Dispose();
            }
            catch (Exception ex)
            {
                Logger.e(nameof(ChromecastCastingDevice), $"Exception occurred while disposing clients.", ex);
            }

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

    private async Task SendChannelMessageAsync(string sourceId, string destinationId, string ns, string json, CancellationToken cancellationToken = default) 
    {
        var castMessage = new CastMessage()
        {
            ProtocolVersion = CastMessage.Types.ProtocolVersion.Castv210,
            SourceId = sourceId,
            DestinationId = destinationId,
            Namespace = ns,
            PayloadType = CastMessage.Types.PayloadType.String,
            PayloadUtf8 = json
        };

        await SendMessageAsync(castMessage.ToByteArray(), cancellationToken);

        if (ns != "urn:x-cast:com.google.cast.tp.heartbeat") 
            Logger.i(nameof(ChromecastCastingDevice), $"Sent channel message: {castMessage}");
    }

    private async Task SendMessageAsync(byte[] data, CancellationToken cancellationToken = default)
        => await SendMessageAsync(data, 0, data.Length, cancellationToken);

    private async Task SendMessageAsync(byte[] data, int offset, int length, CancellationToken cancellationToken = default)
    {
        var stream = _stream;
        if (stream == null)
        {
            Logger.w(nameof(ChromecastCastingDevice), $"Failed to send {data.Length} bytes, output stream is null.");
            return;
        }

        var serializedPacket = new byte[4 + length];
        BinaryPrimitives.WriteInt32BigEndian(serializedPacket, length);
        Array.Copy(data, offset, serializedPacket, 4, length);

        await _writerSemaphore.WaitAsync();

        try
        {
            await stream.WriteAsync(serializedPacket, 0, 4 + length, cancellationToken);
            Logger.i(nameof(ChromecastCastingDevice), $"Sent {length} bytes.");
        }
        catch (Exception e)
        {
            Logger.e(nameof(ChromecastCastingDevice), "Failed to send messsage.", e);
            throw;
        }
        finally
        {
            _writerSemaphore.Release();
        }
    }

    private async Task HandleMessageAsync(CastMessage message, CancellationToken cancellationToken = default)
    {
        if (message.PayloadType == CastMessage.Types.PayloadType.String)
        {
            using (JsonDocument document = JsonDocument.Parse(message.PayloadUtf8))
            {
                var root = document.RootElement;
                string? type = root.GetProperty("type").GetString();
                if (type == "RECEIVER_STATUS")
                {
                    var status = root.GetProperty("status");

                    bool sessionIsRunning = false;
                    if (status.TryGetProperty("applications", out JsonElement applications))
                    {
                        for (int i = 0; i < applications.GetArrayLength(); i++)
                        {
                            var applicationUpdate = applications[i];

                            string? appId = applicationUpdate.GetProperty("appId").GetString();
                            Logger.i(nameof(ChromecastCastingDevice), $"Status update received appId (appId: {appId})");

                            if (appId == "CC1AD845")
                            {
                                sessionIsRunning = true;
                                _autoLaunchEnabled = false;

                                if (_sessionId == null)
                                {
                                    ConnectionState.SetState(CastConnectionState.Connected);
                                    _sessionId = applicationUpdate.GetProperty("sessionId").GetString();
                                    _launchRetries = 0;

                                    string? transportId = applicationUpdate.GetProperty("transportId").GetString();
                                    if (transportId == null)
                                        continue;

                                    await ConnectMediaChannelAsync(transportId, cancellationToken);
                                    Logger.i(nameof(ChromecastCastingDevice), $"Connected to media channel {transportId}");
                                    _transportId = transportId;

                                    await RequestMediaStatusAsync(cancellationToken);
                                }
                            }
                        }
                    }

                    if (!sessionIsRunning)
                    {
                        if (_lastLaunchTime == null || DateTime.Now - _lastLaunchTime > TimeSpan.FromSeconds(5))
                        {
                            _sessionId = null;
                            _mediaSessionId = null;
                            _transportId = null;

                            if (_autoLaunchEnabled)
                            {
                                if (_launching && _launchRetries < MAX_LAUNCH_RETRIES)
                                {
                                    Logger.i(nameof(ChromecastCastingDevice), $"No player yet; attempting launch #${_launchRetries + 1}");
                                    _launchRetries++;
                                    await LaunchPlayerAsync();
                                }
                                else
                                {
                                    // Maybe the first GET_STATUS came back empty; still try launching
                                    Logger.i(nameof(ChromecastCastingDevice), $"Player not found; triggering launch #${_launchRetries + 1}");
                                    _launching = true;
                                    _launchRetries++;
                                    await LaunchPlayerAsync();
                                }
                            }
                            else
                            {
                                Logger.e(nameof(ChromecastCastingDevice), $"Player not found ($_launchRetries, _autoLaunchEnabled = $_autoLaunchEnabled); giving up.");
                                Logger.i(nameof(ChromecastCastingDevice), $"Unable to start media receiver on device");
                                Stop();
                            }
                        }
                        else
                        {
                            if (_retryTask == null)
                            {
                                Logger.i(nameof(ChromecastCastingDevice), "Scheduled retry job over 5 seconds");
                                _retryTask = Task.Run(async () =>
                                {
                                    var ct = _cancellationTokenSource?.Token ?? CancellationToken.None;
                                    await Task.Delay(5000, ct);
                                    if (!ct.IsCancellationRequested || !_started)
                                    {
                                        _retryTask = null;
                                        return;
                                    }

                                    if (_started)
                                    {
                                        try
                                        {
                                            await RequestMediaStatusAsync(ct);
                                        }
                                        catch (Exception e)
                                        {
                                            Logger.e(nameof(ChromecastCastingDevice), "Failed to get ChromeCast status.", e);
                                        }
                                    }

                                    _retryTask = null;
                                });
                            }
                        }
                    }
                    else
                    {
                        _launching = false;
                        _launchRetries = 0;
                        _autoLaunchEnabled = false;
                    }

                    var volume = status.GetProperty("volume");
                    double volumeLevel = volume.GetProperty("level").GetDouble();
                    bool volumeMuted = volume.GetProperty("muted").GetBoolean();
                    PlaybackState.SetVolume(volumeMuted ? 0.0 : volumeLevel);

                    Logger.i(nameof(ChromecastCastingDevice), $"Status update received volume (level: {volumeLevel}, muted: {volumeMuted})");
                }
                else if (type == "MEDIA_STATUS")
                {
                    var statuses = root.GetProperty("status");
                    for (int i = 0; i < statuses.GetArrayLength(); i++)
                    {
                        var status = statuses[i];
                        _mediaSessionId = status.GetProperty("mediaSessionId").GetInt32();

                        string? playerState = status.GetProperty("playerState").GetString();
                        double currentTime = status.GetProperty("currentTime").GetDouble();
                        if (status.TryGetProperty("media", out JsonElement media) && media.TryGetProperty("duration", out JsonElement duration))
                        {
                            PlaybackState.SetDuration(TimeSpan.FromSeconds(duration.GetDouble()));
                        }

                        var isPlaying = playerState == "PLAYING";
                        PlaybackState.SetIsPlaying(isPlaying);
                        if (isPlaying)
                            PlaybackState.SetTime(TimeSpan.FromSeconds(currentTime));

                        int playbackRate = status.GetProperty("playbackRate").GetInt32();
                        Logger.i(nameof(ChromecastCastingDevice), $"Media update received (mediaSessionId: {_mediaSessionId}, playerState: {playerState}, currentTime: {currentTime}, playbackRate: {playbackRate})");

                        if (_mediaLoadInformation == null)
                            await MediaStopAsync(cancellationToken);
                    }
                }
                else if (type == "CLOSE")
                {
                    if (message.SourceId == "receiver-0")
                    {
                        Logger.i(nameof(ChromecastCastingDevice), "Close received.");
                        Stop();
                    }
                }
            }
        }
        else
        {
            throw new Exception($"Payload type {message.PayloadType} is not implemented.");
        }
    }

}