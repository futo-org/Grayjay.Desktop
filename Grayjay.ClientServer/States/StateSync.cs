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
using Grayjay.Desktop.POC;
using Noise;
using static Grayjay.ClientServer.Sync.Internal.SyncSocketSession;

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
    private Thread? _thread;
    private Thread? _connectThread;
    private CancellationTokenSource? _cancellationTokenSource;
    private readonly Dictionary<string, SyncSession> _sessions = new Dictionary<string, SyncSession>();
    private readonly Dictionary<string, long> _lastConnectTimes = new Dictionary<string, long>();
    private readonly FUTO.MDNS.ServiceDiscoverer _serviceDiscoverer;
    private KeyPair? _keyPair;
    public string? PublicKey { get; private set; }
    public event Action<string>? DeviceRemoved;
    public event Action<string, SyncSession>? DeviceUpdatedOrAdded;

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

        _thread = new Thread(() =>
        {
            try
            {
                _serverSocket = new TcpListener(IPAddress.Any, PORT);
                _serverSocket.Start();
                Logger.i<StateSync>($"Running on port {PORT} (TCP)");

                while (!_cancellationTokenSource.IsCancellationRequested)
                {
                    var clientSocket = _serverSocket.AcceptTcpClient();
                    var session = CreateSocketSession(clientSocket, true, (session, socketSession) => { });
                    session.StartAsResponder();
                }
            }
            catch (Exception e)
            {
                Logger.e<StateSync>("StateSync server socket had an unexpected error.", e);
            }
        });
        _thread.Start();

        if (GrayjaySettings.Instance.Synchronization.ConnectLast)
        {
            _connectThread = new Thread(() =>
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

                        var pairs = authorizedDevices.Select(publicKey => 
                        {
                            var connected = IsConnected(publicKey);
                            if (connected)
                                return null;

                            if (!lastKnownMap.TryGetValue(publicKey, out var lastAddress) || lastAddress == null)
                                return null;

                            return new 
                            {
                                PublicKey = publicKey, 
                                LastAddress = lastAddress
                            };
                        }).Where(v => v != null).Select(v => v!).ToList();

                        foreach (var pair in pairs)
                        {
                            try
                            {
                                var syncDeviceInfo = new SyncDeviceInfo(pair.PublicKey, [ pair.LastAddress ], PORT);
                                Connect(syncDeviceInfo);
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
            _connectThread.Start();
        }
    }

    public SyncDeviceInfo GetSyncDeviceInfo()
    {
        return new SyncDeviceInfo(
            publicKey: PublicKey!, 
            addresses: Utilities.GetIPs().Select(x => x.ToString()).ToArray(),
            port: PORT);
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

                var syncDeviceInfo = new SyncDeviceInfo(pkey, addresses, port);
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

                    try
                    {
                        Connect(syncDeviceInfo);
                    }
                    catch (Exception ex)
                    {
                        Logger.e<StateSync>($"Failed to connect to {pkey}", ex);
                    }
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

    private SyncSocketSession CreateSocketSession(TcpClient socket, bool isResponder, Action<SyncSession, SyncSocketSession> onAuthorized)
    {
        SyncSession? session = null;

        return new SyncSocketSession((socket.Client.RemoteEndPoint as IPEndPoint)!.Address.ToString(), _keyPair!,
            socket.GetStream(),
            socket.GetStream(),
            onClose: s => session?.RemoveSocketSession(s),
            onHandshakeComplete: async s =>
            {
                var remotePublicKey = s.RemotePublicKey;
                if (remotePublicKey == null)
                {
                    s.Stop();
                    return;
                }

                Logger.i<StateSync>($"Handshake complete with (LocalPublicKey = {s.LocalPublicKey}, RemotePublicKey = {s.RemotePublicKey})");

                lock (_sessions)
                {
                    if (!_sessions.TryGetValue(remotePublicKey, out session))
                    {
                        var remoteDeviceName = _nameStorage.GetValue(remotePublicKey, null);
                        session = new SyncSession(remotePublicKey, onAuthorized: async (sess, isNewlyAuthorized, isNewSession) =>
                        {
                            if (!isNewSession) {
                                return;
                            }

                            Logger.i<StateSync>($"{s.RemotePublicKey} authorized");
                            lock(_lastAddressStorage) 
                            {
                                if (_lastAddressStorage.Value == null)
                                    _lastAddressStorage.Save(new() { { remotePublicKey, s.RemoteAddress } });
                                else
                                {
                                    _lastAddressStorage.Value[remotePublicKey] = s.RemoteAddress;
                                    _lastAddressStorage.SaveThis();   
                                }
                            }

                            var rpk = s.RemotePublicKey;
                            var rdn = sess.RemoteDeviceName;
                            if (rpk != null && rdn != null)
                                _nameStorage.SetAndSave(rpk, rdn);

                            onAuthorized(sess, s);
                            lock (_authorizedDevices)
                            {
                                if (!_authorizedDevices.Value.Contains(remotePublicKey))
                                    _authorizedDevices.Save(_authorizedDevices.Value.Concat([ remotePublicKey ]).ToArray());
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
                            Logger.i<StateSync>($"{s.RemotePublicKey} connected: {connected}");
                            StateWebsocket.SyncDevicesChanged();
                            DeviceUpdatedOrAdded?.Invoke(remotePublicKey, sess);
                        }, onClose: sess => 
                        {
                            Logger.i<StateSync>($"{s.RemotePublicKey} closed");

                            lock (_sessions)
                            {
                                _sessions.Remove(remotePublicKey);
                            }

                            DeviceRemoved?.Invoke(remotePublicKey);
                        }, remoteDeviceName);

                        _sessions[remotePublicKey] = session;
                    }

                    session.AddSocketSession(s);
                }

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
                        await session.AuthorizeAsync(s);
                        Logger.i<StateSync>($"Connection authorized for {remotePublicKey} because already authorized");
                    }
                }
                else
                {
                    await session.AuthorizeAsync(s);
                    Logger.i<StateSync>($"Connection authorized for {remotePublicKey} because initiator");
                }
            },
            onData: (s, opcode, subOpcode, data) => session?.HandlePacket(s, opcode, subOpcode, data));
    }

    public Task BroadcastJsonAsync(byte subOpcode, object data, CancellationToken cancellationToken = default) => BroadcastAsync((byte)Opcode.DATA, subOpcode, GJsonSerializer.AndroidCompatible.SerializeObj(data), cancellationToken);
    public Task BroadcastAsync(byte opcode, byte subOpcode, string data, CancellationToken cancellationToken = default) => BroadcastAsync(opcode, subOpcode, Encoding.UTF8.GetBytes(data), cancellationToken);
    public async Task BroadcastAsync(byte opcode, byte subOpcode, byte[] data, CancellationToken cancellationToken = default)
    {
        //TODO: Should be done in parallel
        foreach(var session in GetSessions())
        {
            try
            {
                if (session.IsAuthorized && session.Connected)
                    await session.SendAsync(opcode, subOpcode, data, cancellationToken);
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

        //_thread?.Join();
        _thread = null;
        _connectThread = null;
    }

    public SyncSocketSession Connect(SyncDeviceInfo deviceInfo, Action<SyncSocketSession?, bool, string>? onStatusUpdate = null)
    {
        onStatusUpdate?.Invoke(null, false, "Connecting...");
        var socket = new TcpClient(deviceInfo.Addresses[0], deviceInfo.Port);
        onStatusUpdate?.Invoke(null, false, "Handshaking...");

        var session = CreateSocketSession(socket, false, (s, ss) => 
        { 
            onStatusUpdate?.Invoke(ss, false, "Handshaking...");
        });
        
        session.StartAsInitiator(deviceInfo.PublicKey);
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
