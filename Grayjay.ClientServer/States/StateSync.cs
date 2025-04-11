using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Grayjay.ClientServer.Controllers;
using Grayjay.ClientServer.Crypto;
using Grayjay.ClientServer.Dialogs;
using Grayjay.ClientServer.Serializers;
using Grayjay.ClientServer.Settings;
using Grayjay.ClientServer.Store;
using Grayjay.ClientServer.Sync;
using Grayjay.ClientServer.Sync.Internal;
using Noise;
using SyncClient;

using Opcode = SyncShared.Opcode;
using Logger = Grayjay.Desktop.POC.Logger;
using SyncShared;
using System.Threading;

namespace Grayjay.ClientServer.States;

public class StateSync : IDisposable
{
    private readonly StringArrayStore _authorizedDevices = new StringArrayStore("authorizedDevices", Array.Empty<string>()).Load();
    private readonly StringStore _syncKeyPair = new StringStore("syncKeyPair").Load();
    private readonly DictionaryStore<string, string> _lastAddressStorage = new DictionaryStore<string, string>("lastAddressStorage").Load();
    private readonly DictionaryStore<string, string> _nameStorage = new DictionaryStore<string, string>("rememberedNameStorage").Load();

    private readonly DictionaryStore<string, SyncSessionData> _syncSessionData = new DictionaryStore<string, SyncSessionData>("syncSessionData", new Dictionary<string, SyncSessionData>())
        .Load();

    private TcpListener? _serverSocket;
    private CancellationTokenSource? _cancellationTokenSource;
    private readonly Dictionary<string, SyncSession> _sessions = new Dictionary<string, SyncSession>();
    private readonly Dictionary<string, long> _lastConnectTimes = new Dictionary<string, long>();
    private readonly FUTO.MDNS.ServiceDiscoverer _serviceDiscoverer;
    private KeyPair? _keyPair;
    public string? PublicKey { get; private set; }
    public event Action<string>? DeviceRemoved;
    public event Action<string, SyncSession>? DeviceUpdatedOrAdded;
    public const string RelayServer = "relay.grayjay.app";
    private SyncSocketSession? _relaySession;
    public string? _pairingCode = Utilities.GenerateReadablePassword(8); //TODO: Set to null whenever pairing is not desired

    private static readonly SyncHandlers _handlers = new GrayjaySyncHandlers();
    public StateSync()
    {
        _serviceDiscoverer = new FUTO.MDNS.ServiceDiscoverer(["_gsync._tcp.local"]);
        _serviceDiscoverer.OnServicesUpdated += HandleServiceUpdated;
    }

    public async Task StartAsync()
    {
        //Clean up dangling streams
        //if (Directory.Exists(SyncStream.SYNC_STREAMS_DIR))
        //    Directory.Delete(SyncStream.SYNC_STREAMS_DIR, true);

        if (_cancellationTokenSource != null)
        {
            Logger.w<StateSync>("Already started.");
            return;
        }

        _cancellationTokenSource = new CancellationTokenSource();

        if (GrayjaySettings.Instance.Synchronization.Broadcast || GrayjaySettings.Instance.Synchronization.ConnectDiscovered)
            _ = _serviceDiscoverer.RunAsync();

        try
        {
            var syncKeyPair = JsonSerializer.Deserialize<SyncKeyPair>(EncryptionProvider.Instance.Decrypt(_syncKeyPair.Value));
            _keyPair = new KeyPair(Convert.FromBase64String(syncKeyPair!.PrivateKey), Convert.FromBase64String(syncKeyPair!.PublicKey));
        }
        catch (Exception ex)
        {
            // Key pair non-existing, invalid or lost
            var p = KeyPair.Generate();

            var publicKey = p.PublicKey;
            var privateKey = p.PrivateKey;

            var syncKeyPair = new SyncKeyPair(1, Convert.ToBase64String(publicKey), Convert.ToBase64String(privateKey));
            _syncKeyPair.Save(EncryptionProvider.Instance.Encrypt(JsonSerializer.Serialize(syncKeyPair)));

            Logger.e<StateSync>("Failed to load existing key pair", ex);
            _keyPair = p;
        }

        PublicKey = Convert.ToBase64String(_keyPair.PublicKey);

        if (GrayjaySettings.Instance.Synchronization.Broadcast)
        {
            // Start broadcasting service
            await _serviceDiscoverer.BroadcastServiceAsync(GetDeviceName(), "_gsync._tcp.local", PORT, texts: new List<string> { $"pk={PublicKey.Replace('+', '-').Replace('/', '_').Replace("=", "")}" });
        }

        Logger.i<StateSync>($"Sync key pair initialized (public key = {PublicKey})");

        _ = Task.Run(async () =>
        {
            try
            {
                _serverSocket = new TcpListener(IPAddress.Any, PORT);
                _serverSocket.Start();
                Logger.i<StateSync>($"Running on port {PORT} (TCP)");

                while (!_cancellationTokenSource.IsCancellationRequested)
                {
                    var clientSocket = await _serverSocket.AcceptTcpClientAsync();
                    var session = CreateSocketSession(clientSocket, true, (session) => { });
                    await session.StartAsResponderAsync();
                }
            }
            catch (Exception e)
            {
                Logger.e<StateSync>("StateSync server socket had an unexpected error.", e);
            }
        });

        _ = Task.Run(async () =>
        {
            while (!_cancellationTokenSource.IsCancellationRequested)
            {
                const string RelayPublicKey = "xGbHRzDOvE6plRbQaFgSen82eijF+gxS0yeUaeEErkw=";

                try
                {
                    Logger.i<StateSync>("Starting relay session...");

                    var socket = new TcpClient("relay.grayjay.app", 9000);
                    _relaySession = new SyncSocketSession((socket.Client.RemoteEndPoint as IPEndPoint)!.Address.ToString(), _keyPair!,
                        socket.GetStream(),
                        socket.GetStream(),
                        isHandshakeAllowed: (_, pk, pairingCode) =>
                        {
                            Logger.v<StateSync>($"Check if handshake allowed from '{pk}'.");
                            if (pk == RelayPublicKey)
                                return true;

                            lock (_authorizedDevices)
                            {
                                if (_authorizedDevices.Contains(pk))
                                    return true;
                            }

                            Logger.v<StateSync>($"Check if handshake allowed with pairing code '{pairingCode}' with active pairing code '{_pairingCode}'.");
                            if (_pairingCode == null || pairingCode == null || pairingCode.Length == 0)
                                return false;

                            return _pairingCode == pairingCode;
                        },
                        onNewChannel: (s, c) =>
                        {
                            var remotePublicKey = c.RemotePublicKey;
                            if (remotePublicKey == null)
                            {
                                Logger.e<StateSync>("Remote public key should never be null in onNewChannel.");
                                return;
                            }

                            Logger.i<StateSync>($"New channel established from relay (pk: '{c.RemotePublicKey}').");

                            SyncSession? session;
                            lock (_sessions)
                            {
                                if (!_sessions.TryGetValue(remotePublicKey, out session) || session == null)
                                {
                                    var remoteDeviceName = _nameStorage.GetValue(remotePublicKey, null);
                                    session = CreateNewSyncSession(remotePublicKey, remoteDeviceName, (s) => { });
                                    _sessions[remotePublicKey] = session;
                                }

                                session.AddChannel(c);
                            }

                            c.SetDataHandler((_, channel, opcode, subOpcode, data) => session.HandlePacket(opcode, subOpcode, data));
                            c.SetCloseHandler((channel) => session.RemoveChannel(channel));
                        }, 
                        onChannelEstablished: async (_, channel, isResponder) =>
                        {
                            await HandleAuthorizationAsync(channel, isResponder);
                        },
                        onHandshakeComplete: async (relaySession) =>
                        {
                            try
                            {
                                while (!_cancellationTokenSource.IsCancellationRequested)
                                {
                                    string[] unconnectedAuthorizedDevices;
                                    lock (_authorizedDevices)
                                        unconnectedAuthorizedDevices = _authorizedDevices.Value.Where(pk => !IsConnected(pk)).ToArray();

                                    await relaySession.PublishConnectionInformationAsync(unconnectedAuthorizedDevices, PORT, true, false, false, true, _cancellationTokenSource.Token);
                                    var connectionInfos = await relaySession.RequestBulkConnectionInfoAsync(unconnectedAuthorizedDevices, _cancellationTokenSource.Token);
                                    foreach (var connectionInfoPair in connectionInfos)
                                    {
                                        var targetKey = connectionInfoPair.Key;
                                        var connectionInfo = connectionInfoPair.Value;
                                        var potentialLocalAddresses = connectionInfo.Ipv4Addresses.Concat(connectionInfo.Ipv6Addresses).Where(l => l != connectionInfo.RemoteIp).ToList();
                                        if (connectionInfo.AllowLocalDirect)
                                        {
                                            _ = Task.Run(async () =>
                                            {
                                                try
                                                {
                                                    Logger.Verbose<StateSync>($"Attempting to connect directly, locally to '{targetKey}'.");
                                                    await ConnectAsync(potentialLocalAddresses.Select(l => l.ToString()).ToArray(), PORT, targetKey, cancellationToken: _cancellationTokenSource.Token);
                                                }
                                                catch (Exception e)
                                                {
                                                    Logger.e<StateSync>($"Failed to start direct connection using connection info with {targetKey}.", e);
                                                }
                                            });
                                        }

                                        var remoteAddress = connectionInfo.RemoteIp;
                                        if (connectionInfo.AllowRemoteDirect)
                                        {
                                            //TODO: Try connecting directly, remotely, set allow to true when implemented, only useful for port forwarded scenarios?
                                        }

                                        if (connectionInfo.AllowRemoteHolePunched)
                                        {
                                            //TODO: Implement hole punching, set allow to true when implemented
                                        }

                                        if (connectionInfo.AllowRemoteProxied)
                                        {
                                            try
                                            {
                                                Logger.Verbose<StateSync>($"Attempting relayed connection with '{targetKey}'.");
                                                await relaySession.StartRelayedChannelAsync(targetKey, null, _cancellationTokenSource.Token);
                                            }
                                            catch (Exception e)
                                            {
                                                Logger.e<StateSync>($"Failed to start relayed channel with {targetKey}.", e);
                                            }
                                        }
                                    }

                                    await Task.Delay(TimeSpan.FromSeconds(15), _cancellationTokenSource.Token);
                                }
                            }
                            catch (Exception e)
                            {
                                Logger.e<StateSync>("Unhandled exception in relay session.", e);
                                relaySession.Dispose();
                            }
                        });

                    _relaySession.Authorizable = AlwaysAuthorized.Instance;
                    await _relaySession.StartAsInitiatorAsync(RelayPublicKey, null, _cancellationTokenSource.Token);

                    Logger.i<StateSync>("Started relay session.");
                }
                catch (Exception e)
                {
                    Logger.e<StateSync>("Relay session failed.", e);
                    await Task.Delay(5000);
                }
                finally
                {
                    _relaySession?.Dispose();
                    _relaySession = null;
                }
            }
        });

        if (GrayjaySettings.Instance.Synchronization.ConnectLast)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    Logger.i<StateSync>("Running auto reconnector");

                    while (!_cancellationTokenSource.IsCancellationRequested)
                    {
                        List<string> authorizedDevices;
                        lock (_authorizedDevices)
                        {
                            authorizedDevices = _authorizedDevices.Value.ToList();
                        }

                        Dictionary<string, string> lastKnownMap;
                        lock (_lastAddressStorage)
                        {
                            if (_lastAddressStorage.Value != null)
                                lastKnownMap = _lastAddressStorage.Value.ToDictionary();
                            else
                                lastKnownMap = new();
                        }

                        var pairs = authorizedDevices
                            .Where(pk => !IsConnected(pk) && lastKnownMap.TryGetValue(pk, out var addr) && addr != null)
                            .Select(pk => new { PublicKey = pk, LastAddress = lastKnownMap[pk] })
                            .ToList();

                        foreach (var pair in pairs)
                        {
                            try
                            {
                                var syncDeviceInfo = new SyncDeviceInfo(pair.PublicKey, [ pair.LastAddress ], PORT, null);
                                await ConnectAsync(syncDeviceInfo);
                            }
                            catch (Exception e)
                            {
                                Logger.i<StateSync>("Failed to connect to " + pair.PublicKey, e);
                            }
                        }

                        if (_cancellationTokenSource.Token.WaitHandle.WaitOne(5000))
                            break;
                    }
                }
                catch (Exception e)
                {
                    Logger.e<StateSync>("StateSync connect thread had an unexpected error.", e);
                }
            });
        }
    }

    private SyncSession CreateNewSyncSession(string remotePublicKey, string? remoteDeviceName, Action<SyncSession>? onAuthorized)
    {
        return new SyncSession(remotePublicKey, onAuthorized: async (sess, isNewlyAuthorized, isNewSession) =>
        {
            if (!isNewSession)
            {
                return;
            }

            var rdn = sess.RemoteDeviceName;
            if (rdn != null)
                _nameStorage.SetAndSave(remotePublicKey, rdn);

            onAuthorized?.Invoke(sess);
            lock (_authorizedDevices)
            {
                if (!_authorizedDevices.Value.Contains(remotePublicKey))
                    _authorizedDevices.Save(_authorizedDevices.Value.Concat([remotePublicKey]).ToArray());
            }

            StateWebsocket.SyncDevicesChanged();
            DeviceUpdatedOrAdded?.Invoke(remotePublicKey, sess);

            await CheckForSyncAsync(sess);
        }, onUnauthorized: sess => {
            StateUI.Dialog(new StateUI.DialogDescriptor()
            {
                Text = "Device Unauthorized",
                TextDetails = $"Device [{sess.DisplayName}] tried to connect but was unauthorized (key change?), would you like to remove the device?",
                Actions = new List<StateUI.DialogAction>()
                {
                    new StateUI.DialogAction()
                    {
                        Text = "Ignore",
                        Action = (resp) =>
                        {

                        }
                    },
                    new StateUI.DialogAction()
                    {
                        Text = "Remove",
                        Action = (resp) =>
                        {
                            Unauthorize(remotePublicKey);
                        }
                    }
                }
            });
        }, onConnectedChanged: (sess, connected) =>
        {
            Logger.i<StateSync>($"{sess.RemotePublicKey} connected: {connected}");
            StateWebsocket.SyncDevicesChanged();
            DeviceUpdatedOrAdded?.Invoke(remotePublicKey, sess);
        }, onClose: sess =>
        {
            Logger.i<StateSync>($"{sess.RemotePublicKey} closed");

            lock (_sessions)
            {
                _sessions.Remove(remotePublicKey);
            }

            DeviceRemoved?.Invoke(remotePublicKey);
        }, dataHandler: (sess, opcode, subOpcode, data) =>
        {
            _handlers.Handle(sess, opcode, subOpcode, data);
        }, remoteDeviceName);
    }
    public SyncDeviceInfo GetSyncDeviceInfo()
    {
        return new SyncDeviceInfo(
            publicKey: PublicKey!, 
            addresses: Utilities.GetIPs().Select(x => x.ToString()).ToArray(),
            port: PORT,
            pairingCode: _pairingCode);
    }

    public SyncSessionData GetSyncSessionData(string key)
    {
        return _syncSessionData.GetValue(key, new SyncSessionData(key)) ?? new SyncSessionData(key);
    }
    public void UpdateSyncSessionData(string key, Action<SyncSessionData> updater)
    {
        var ses = GetSyncSessionData(key);
        updater(ses);
        SaveSyncSessionData(ses);
    }
    public void SaveSyncSessionData(SyncSessionData data)
    {
        _syncSessionData.SetAndSave(data.PublicKey, data);
    }

    public string GetPairingUrl()
    {
        var deviceInfo = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(GetSyncDeviceInfo())))
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
        return $"grayjay://sync/{deviceInfo}";
    }

    private string GetDeviceName()
    {
        var manufacturer = Environment.MachineName;
        return manufacturer;
    }

    public bool IsConnected(string publicKey)
    {
        lock (_sessions)
        {
            return _sessions.TryGetValue(publicKey, out var v) && v != null && v.Connected;
        }
    }

    public bool IsAuthorized(string publicKey)
    {
        lock (_authorizedDevices)
        {
            return _authorizedDevices.Value.Contains(publicKey);
        }
    }

    public SyncSession? GetSession(string publicKey)
    {
        lock (_sessions)
        {
            return _sessions.ContainsKey(publicKey) ? _sessions[publicKey] : null;
        }
    }
    public List<SyncSession> GetSessions()
    {
        lock(_sessions)
        {
            return _sessions.Values.ToList();
        }
    }

    private void HandleServiceUpdated(List<FUTO.MDNS.DnsService> services)
    {
        if (!GrayjaySettings.Instance.Synchronization.ConnectDiscovered)
            return;

        foreach (var s in services)
        {
            var addresses = s.Addresses.Select(v => v.ToString()).ToArray();
            var port = s.Port;

            if (s.Name.EndsWith("._gsync._tcp.local"))
            {
                var name = s.Name.Substring(0, s.Name.Length - "._gsync._tcp.local".Length);
                var urlSafePkey = s.Texts.Find(t => t.StartsWith("pk="))?.Substring("pk=".Length);

                if (string.IsNullOrEmpty(urlSafePkey)) continue;

                string base64 = urlSafePkey.Replace('-', '+').Replace('_', '/');

                int padding = 4 - (base64.Length % 4);
                if (padding < 4)
                    base64 += new string('=', padding);
                var pkey = Convert.ToBase64String(Convert.FromBase64String(base64));

                var authorized = IsAuthorized(pkey);
                if (authorized && !IsConnected(pkey))
                {
                    var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

                    lock (_lastConnectTimes)
                    {
                        if (_lastConnectTimes.TryGetValue(pkey, out var lastConnectTime) && now - lastConnectTime < 30000)
                            continue;

                        _lastConnectTimes[pkey] = now;
                    }
                    Logger.i<StateSync>($"Found authorized device '{name}' with pkey={pkey}, attempting to connect");

                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await ConnectAsync(addresses, port, pkey);
                            Logger.i<StateSync>($"Connected to found authorized device '{name}' with pkey={pkey}.");
                        }
                        catch (Exception ex)
                        {
                            Logger.e<StateSync>($"Failed to connect to {pkey}", ex);
                        }
                    });
                }
            }
        }
    }

    private void Unauthorize(string remotePublicKey)
    {
        Logger.i<StateSync>($"{remotePublicKey} unauthorized received");
        lock (_authorizedDevices)
        {
            _authorizedDevices.Save(_authorizedDevices.Value.Where(pk => pk != remotePublicKey).ToArray());
        }
        StateWebsocket.SyncDevicesChanged();
        DeviceRemoved?.Invoke(remotePublicKey);
    }

    public string? GetCachedName(string publicKey)
    {
        return _nameStorage.GetValue(publicKey, null);
    }

    private SyncSocketSession CreateSocketSession(TcpClient socket, bool isResponder, Action<SyncSession>? onAuthorized)
    {
        SyncSession? session = null;
        ChannelSocket? channelSocket = null;
        return new SyncSocketSession((socket.Client.RemoteEndPoint as IPEndPoint)!.Address.ToString(), _keyPair!,
            socket.GetStream(),
            socket.GetStream(),
            onClose: s =>
            {
                if (session != null && channelSocket != null)
                    session.RemoveChannel(channelSocket);
            },
            isHandshakeAllowed: (_, pk, pairingCode) =>
            {
                Logger.v<StateSync>($"Check if handshake allowed from '{pk}'.");

                lock (_authorizedDevices)
                {
                    if (_authorizedDevices.Contains(pk))
                        return true;
                }

                Logger.v<StateSync>($"Check if handshake allowed with pairing code '{pairingCode}' with active pairing code '{_pairingCode}'.");
                if (_pairingCode == null || pairingCode == null || pairingCode.Length == 0)
                    return false;

                return _pairingCode == pairingCode;
            },
            onHandshakeComplete: async s =>
            {
                var remotePublicKey = s.RemotePublicKey;
                if (remotePublicKey == null)
                {
                    s.Dispose();
                    return;
                }

                Logger.i<StateSync>($"Handshake complete with (LocalPublicKey = {s.LocalPublicKey}, RemotePublicKey = {s.RemotePublicKey})");

                lock (_sessions)
                {
                    if (!_sessions.TryGetValue(remotePublicKey, out session))
                    {
                        var remoteDeviceName = _nameStorage.GetValue(remotePublicKey, null);
                        Logger.i<StateSync>($"{s.RemotePublicKey} authorized");
                        lock (_lastAddressStorage)
                        {
                            if (_lastAddressStorage.Value == null)
                                _lastAddressStorage.Save(new() { { remotePublicKey, s.RemoteAddress } });
                            else
                            {
                                _lastAddressStorage.Value[remotePublicKey] = s.RemoteAddress;
                                _lastAddressStorage.SaveThis();
                            }
                        }

                        session = CreateNewSyncSession(remotePublicKey, remoteDeviceName, onAuthorized);
                        _sessions[remotePublicKey] = session;
                    }

                    channelSocket = new ChannelSocket(s);
                    session.AddChannel(channelSocket);
                }

                await HandleAuthorizationAsync(channelSocket, isResponder);
            },
            onData: (s, opcode, subOpcode, data) => session?.HandlePacket(opcode, subOpcode, data));
    }

    private async Task HandleAuthorizationAsync(IChannel channel, bool isResponder, CancellationToken cancellationToken = default)
    {
        SyncSession syncSession = ((SyncSession?)channel.SyncSession)!;
        var remotePublicKey = channel.RemotePublicKey!;
        if (isResponder)
        {
            var isAuthorized = IsAuthorized(remotePublicKey);
            if (!isAuthorized)
            {
                //Show sync confirm dialog
                var dialog = new SyncConfirmDialog(remotePublicKey);
                _ = dialog.Show();
            }
            else
            {
                await syncSession.AuthorizeAsync(cancellationToken);
                Logger.i<StateSync>($"Connection authorized for {remotePublicKey} because already authorized");
            }
        }
        else
        {
            await syncSession.AuthorizeAsync(cancellationToken);
            Logger.i<StateSync>($"Connection authorized for {remotePublicKey} because initiator");
        }
    }

    public Task BroadcastJsonAsync(byte subOpcode, object data, CancellationToken cancellationToken = default) => BroadcastAsync(Opcode.DATA, subOpcode, GJsonSerializer.AndroidCompatible.SerializeObj(data), cancellationToken);
    public Task BroadcastAsync(Opcode opcode, byte subOpcode, string data, CancellationToken cancellationToken = default) => BroadcastAsync(opcode, subOpcode, Encoding.UTF8.GetBytes(data), cancellationToken);
    public async Task BroadcastAsync(Opcode opcode, byte subOpcode, byte[] data, CancellationToken cancellationToken = default)
    {
        //TODO: Should be done in parallel
        foreach(var session in GetSessions())
        {
            try
            {
                if (session.IsAuthorized && session.Connected)
                    await session.SendAsync(opcode, subOpcode, data, cancellationToken: cancellationToken);
            }
            catch(Exception ex)
            {
                Logger.w(nameof(StateSync), $"Failed to broadcast {opcode} to {session.RemotePublicKey}: {ex.Message}", ex);
            }
        }
    }

    public async Task CheckForSyncAsync(SyncSession session, CancellationToken cancellationToken = default)
    {
        Stopwatch watch = Stopwatch.StartNew();


        //Temporary only send subscriptions till full export is made
        //var export = StateBackup.Export();
        //session.Send(GJSyncOpcodes.SyncExport, export.AsZip());

        Logger.i(nameof(StateSync), "New session [" + session.RemotePublicKey.ToString() + "]");
        await session.SendJsonDataAsync(GJSyncOpcodes.SyncStateExchange, StateSync.Instance.GetSyncSessionData(session.RemotePublicKey), cancellationToken);


        watch.Stop();
        //Logger.i(nameof(StateSync), $"Generated and sent sync export in {watch.Elapsed.TotalMicroseconds}ms");
    }

    public void Stop()
    {
        _cancellationTokenSource?.Cancel();
        _cancellationTokenSource = null;
        _serviceDiscoverer.Dispose();

        _serverSocket?.Stop();
        _serverSocket = null;

        lock (_sessions)
        {
            foreach (var session in _sessions)
                session.Value.Dispose();
            _sessions.Clear();
        }
    }

    public async Task ConnectAsync(SyncDeviceInfo deviceInfo, Action<bool?, string>? onStatusUpdate = null, CancellationToken cancellationToken = default)
    {
        try
        {
            await ConnectAsync(deviceInfo.Addresses, deviceInfo.Port, deviceInfo.PublicKey, deviceInfo.PairingCode, onStatusUpdate, cancellationToken);
        }
        catch (Exception e)
        {
            Logger.e<StateSync>("Failed to connect directly.", e);
            var relaySession = _relaySession;
            if (relaySession != null)
            {
                onStatusUpdate?.Invoke(null, "Connecting via relay...");
                await relaySession.StartRelayedChannelAsync(deviceInfo.PublicKey, deviceInfo.PairingCode, cancellationToken);
                onStatusUpdate?.Invoke(true, "Connected");
            }
            else
            {
                throw new Exception("Failed to connect.");
            }
        }
    }

    private async Task<SyncSocketSession> ConnectAsync(string[] addresses, int port, string remotePublicKey, string? pairingCode = null, Action<bool?, string>? onStatusUpdate = null, CancellationToken cancellationToken = default)
    {
        onStatusUpdate?.Invoke(null, "Connecting directly...");

        var socket = new TcpClient(addresses[0], port);
        var session = CreateSocketSession(socket, false, (s) =>
        {
            onStatusUpdate?.Invoke(true, "Authorized");
        });

        onStatusUpdate?.Invoke(null, "Handshaking...");
        await session.StartAsInitiatorAsync(remotePublicKey, pairingCode, cancellationToken);
        return session;
    }

    public bool HasAtLeastOneDevice()
    {
        lock (_authorizedDevices)
        {
            return _authorizedDevices.Value.Length > 0;
        }
    }

    public List<string> GetAllDevices()
    {
        lock (_authorizedDevices)
        {
            return new List<string>(_authorizedDevices.Value);
        }
    }

    public async Task DeleteDeviceAsync(string publicKey)
    {
        await Task.Run(async () =>
        {
            try
            {
                var session = GetSession(publicKey);
                try
                {
                    if (session != null)
                        await session.UnauthorizeAsync();
                }
                catch (Exception ex)
                {
                    Logger.w<StateSync>("Failed to send unauthorize.", ex);
                }
                session?.Close();

                lock (_sessions)
                {
                    _sessions.Remove(publicKey);
                }

                lock (_authorizedDevices)
                {
                    _authorizedDevices.Save(_authorizedDevices.Value.Where(pk => pk != publicKey).ToArray());
                }

                StateWebsocket.SyncDevicesChanged();
                DeviceRemoved?.Invoke(publicKey);
            }
            catch (Exception ex)
            {
                Logger.w<StateSync>($"Failed to send unauthorize (delete): {ex}");
            }
        });
    }

    public void Dispose()
    {
        Stop();
    }

    private const int PORT = 12315;

    private static StateSync? _instance;
    public static StateSync Instance => _instance ?? (_instance = new StateSync());
    public static void Cleanup()
    {
        _instance?.Dispose();
        _instance = null;
    }
}
