using System.Net;
using System.Net.Sockets;
using Grayjay.Desktop.POC;

namespace Grayjay.ClientServer.Casting;

public class FCastCastingDevice : CastingDevice
{
    private CancellationTokenSource? _cancellationTokenSource;
    private FCastSession? _session = null;
    private bool _started = false;

    public FCastCastingDevice(CastingDeviceInfo deviceInfo) : base(deviceInfo) { }

    public override bool CanSetVolume => true;

    public override bool CanSetSpeed => true;

    private IPEndPoint? _localEndPoint = null;
    public override IPEndPoint? LocalEndPoint => _localEndPoint;
    private DateTime _lastPong = DateTime.Now;

    public override async Task ChangeSpeedAsync(double speed, CancellationToken cancellationToken = default)
    {
        if (_session == null)
            return;

        PlaybackState.SetSpeed(speed);
        await _session.SendMessageAsync(Opcode.SetSpeed, new SetSpeedMessage
        {
            Speed = speed
        }, cancellationToken);
    }

    public override async Task ChangeVolumeAsync(double volume, CancellationToken cancellationToken = default)
    {
        if (_session == null)
            return;
        
        PlaybackState.SetVolume(volume);
        await _session.SendMessageAsync(Opcode.SetVolume, new SetVolumeMessage
        {
            Volume = volume
        }, cancellationToken);
    }

    public override async Task MediaLoadAsync(string streamType, string contentType, string contentId, TimeSpan resumePosition, TimeSpan duration, double? speed = null, CancellationToken cancellationToken = default)
    {
        if (_session == null)
            return;

        PlaybackState.SetTime(resumePosition);
        PlaybackState.SetDuration(duration);

        await _session.SendMessageAsync(Opcode.Play, new PlayMessage
        {
            Container = contentType,
            Speed = speed ?? 1.0,
            Time = resumePosition.TotalSeconds,
            Url = contentId
        }, cancellationToken);
    }

    public override async Task MediaPauseAsync(CancellationToken cancellationToken = default)
    {
        if (_session == null)
            return;

        PlaybackState.SetIsPlaying(false);
        await _session.SendMessageAsync(Opcode.Pause, cancellationToken);
    }

    public override async Task MediaResumeAsync(CancellationToken cancellationToken = default)
    {
        if (_session == null)
            return;

        PlaybackState.SetIsPlaying(true);
        await _session.SendMessageAsync(Opcode.Resume, cancellationToken);
    }

    public override async Task MediaSeekAsync(TimeSpan time, CancellationToken cancellationToken = default)
    {
        if (_session == null)
            return;

        PlaybackState.SetTime(time);
        await _session.SendMessageAsync(Opcode.Seek, new SeekMessage
        {
            Time = time.TotalSeconds
        }, cancellationToken);
    }

    public override async Task MediaStopAsync(CancellationToken cancellationToken = default)
    {
        if (_session == null)
            return;

        PlaybackState.SetIsPlaying(false);
        await _session.SendMessageAsync(Opcode.Stop, cancellationToken);
    }

    public override void Start()
    {
        if (_started)
            return;

        _started = true;

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
                        Logger.i(nameof(FCastCastingDevice), "Connection failed, trying again in 1 second.");
                        continue;
                    }

                    rep = connectedClient.Client.RemoteEndPoint as IPEndPoint;
                    break;
                }
                catch (Exception ex)
                {
                    Logger.e(nameof(FCastCastingDevice), $"Exception occurred in FCast loop.", ex);
                }
            }

            if (cancellationTokenSource.IsCancellationRequested)
            {
                ConnectionState.SetState(CastConnectionState.Disconnected);
                return;
            }

            TcpClient? client = null;

            await Task.WhenAll(
            [
                Task.Run(async () =>
                {
                    //Data loop
                    while (!cancellationTokenSource.IsCancellationRequested)
                    {
                        try
                        {
                            ConnectionState.SetState(CastConnectionState.Connecting);

                            var sessionToDispose = _session;
                            if (sessionToDispose != null)
                            {
                                _session = null;
                                sessionToDispose?.Dispose();
                            }

                            client?.Dispose();
                            _localEndPoint = null;

                            FCastSession newSession;
                            if (connectedClient != null)
                            {
                                newSession = new FCastSession(connectedClient.GetStream());
                                client = connectedClient;
                                connectedClient = null;
                            }
                            else
                            {
                                var c = new TcpClient();
                                await c.ConnectAsync(rep!, cancellationTokenSource.Token);
                                newSession = new FCastSession(c.GetStream());
                                client = c;
                            }

                            _lastPong = DateTime.Now;

                            newSession.OnPlaybackUpdate += (update) =>
                            {
                                PlaybackState.SetDuration(TimeSpan.FromSeconds(update.Duration));
                                PlaybackState.SetSpeed(update.Speed);
                                PlaybackState.SetTime(TimeSpan.FromSeconds(update.Time));
                                PlaybackState.SetIsPlaying(update.State == 1);
                            };

                            newSession.OnVolumeUpdate += (update) =>
                            {
                                PlaybackState.SetVolume(update.Volume);
                            };

                            newSession.OnPong += () =>
                            {
                                _lastPong = DateTime.Now;
                            };

                            _session = newSession;
                            _localEndPoint = client.Client.LocalEndPoint as IPEndPoint;
                            ConnectionState.SetState(CastConnectionState.Connected);
                            await newSession.ReceiveLoopAsync(cancellationTokenSource.Token);
                        }
                        catch (Exception ex)
                        {
                            Logger.e(nameof(FCastCastingDevice), $"Exception occurred in FCast loop.", ex);
                            await Task.Delay(TimeSpan.FromSeconds(2), cancellationTokenSource.Token);
                        }
                    }
                }),
                Task.Run(async () =>
                {
                    //Ping loop
                    while (!cancellationTokenSource.IsCancellationRequested)
                    {
                        try
                        {
                            await Task.Delay(TimeSpan.FromSeconds(5), cancellationTokenSource.Token);
                            if (cancellationTokenSource.IsCancellationRequested)
                                break;

                            var session = _session;
                            if (session == null)
                                continue;

                            try
                            {
                                await session.SendMessageAsync(Opcode.Ping, cancellationTokenSource.Token);

                                var durationSinceLastPong = DateTime.Now - _lastPong;
                                if (durationSinceLastPong > TimeSpan.FromSeconds(15))
                                {
                                    Logger.i<FCastCastingDevice>($"Duration since last pong ({durationSinceLastPong.TotalSeconds} seconds) is too long, closing session.");
                                    session.Dispose();
                                }
                            }
                            catch (Exception e)
                            {
                                Logger.e(nameof(FCastCastingDevice), $"Exception occurred in FCast ping loop while sending ping, closing session.", e);
                                session.Dispose();
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.e(nameof(FCastCastingDevice), $"Exception occurred in FCast ping loop.", ex);
                        }
                    }
                })
            ]);


            ConnectionState.SetState(CastConnectionState.Disconnected);

            try
            {
                _session?.Dispose();
                _session = null;
                client?.Dispose();
            }
            catch (Exception ex)
            {
                Logger.e(nameof(FCastCastingDevice), $"Exception occurred while disposing clients.", ex);
            }

            if (cancellationTokenSource.IsCancellationRequested)
                return;
        });
    }

    public override void Stop()
    {
        Logger.i(nameof(FCastCastingDevice), "Stop called.");

        if (!_started)
            return;

        _started = false;
        
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource = null;
    }
}