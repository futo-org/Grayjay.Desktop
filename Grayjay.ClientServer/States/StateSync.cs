using System.Diagnostics;
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
using SyncClient;

using Opcode = SyncShared.Opcode;
using Logger = Grayjay.Desktop.POC.Logger;
using SyncShared;

namespace Grayjay.ClientServer.States;

public class StateSync : IDisposable
{
    private readonly DictionaryStore<string, SyncSessionData> _syncSessionData = new DictionaryStore<string, SyncSessionData>("syncSessionData", new Dictionary<string, SyncSessionData>())
        .Load();

    public SyncService? SyncService { get; private set; }
    public event Action<string>? DeviceRemoved;
    public event Action<string, SyncSession>? DeviceUpdatedOrAdded;
    public const string RelayServer = "relay.grayjay.app";
    public const string RelayPublicKey = "xGbHRzDOvE6plRbQaFgSen82eijF+gxS0yeUaeEErkw=";
    public const string ServiceName = "_gsync._tcp.local";

    private static readonly SyncHandlers _handlers = new GrayjaySyncHandlers();

    public StateSync()
    {

    }

    public async Task StartAsync()
    {
        if (SyncService != null)
            throw new Exception("Already started.");

        SyncService = new SyncService(ServiceName, RelayServer, RelayPublicKey, APP_ID, new StoreBasedSyncDatabaseProvider(), new SyncServiceSettings
        {
            MdnsBroadcast = GrayjaySettings.Instance.Synchronization.Broadcast,
            MdnsConnectDiscovered = GrayjaySettings.Instance.Synchronization.ConnectDiscovered,
            BindListener = GrayjaySettings.Instance.Synchronization.LocalConnections,
            ConnectLastKnown = GrayjaySettings.Instance.Synchronization.ConnectLast,
            RelayHandshakeAllowed = GrayjaySettings.Instance.Synchronization.ConnectThroughRelay,
            RelayPairAllowed = GrayjaySettings.Instance.Synchronization.PairThroughRelay,
            RelayEnabled = GrayjaySettings.Instance.Synchronization.DiscoverThroughRelay,
            RelayConnectDirect = GrayjaySettings.Instance.Synchronization.ConnectLocalDirectThroughRelay,
            RelayConnectRelayed = GrayjaySettings.Instance.Synchronization.ConnectThroughRelay
        });

        SyncService.OnAuthorized = async (sess, isNewlyAuthorized, isNewSession) =>
        {
            if (isNewSession)
            {
                StateWebsocket.SyncDevicesChanged();
                DeviceUpdatedOrAdded?.Invoke(sess.RemotePublicKey, sess);
                await CheckForSyncAsync(sess);
            }
        };

        SyncService.OnUnauthorized = (sess) =>
        {
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
                            Logger.Info<SyncService>($"{sess.RemotePublicKey} unauthorized received");
                            SyncService.RemoveAuthorizedDevice(sess.RemotePublicKey);
                            StateWebsocket.SyncDevicesChanged();
                            DeviceRemoved?.Invoke(sess.RemotePublicKey);
                        }
                    }
                }
            });
        };

        SyncService.OnConnectedChanged = (sess, _) =>
        {
            StateWebsocket.SyncDevicesChanged();
            DeviceUpdatedOrAdded?.Invoke(sess.RemotePublicKey, sess);
        };

        SyncService.OnClose = (sess) =>
        {
            DeviceRemoved?.Invoke(sess.RemotePublicKey);
        };

        SyncService.OnData = (sess, opcode, subOpcode, data) =>
        {
            var dataCopy = data.ToArray();
            StateApp.ThreadPool.Run(() =>
            {
                try
                {
                    _handlers.Handle(sess, opcode, subOpcode, dataCopy);
                }
                catch (Exception e)
                {
                    Logger.Error<SyncService>("Failed to handle data packet, closing connection.", e);
                    sess.Dispose();
                }
            });
        };

        SyncService.AuthorizePrompt = (remotePublicKey, callback) =>
        {
            var dialog = new SyncConfirmDialog(remotePublicKey, callback);
            _ = dialog.Show();
        };

        await SyncService.StartAsync();
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

    public SyncDeviceInfo GetSyncDeviceInfo()
    {
        var publicKey = SyncService?.PublicKey;
        var pairingCode = SyncService?.PairingCode;
        if (publicKey == null || pairingCode == null)
            throw new Exception("StateSync was not started, make sure Sync is enabled in the settings.");

        return new SyncDeviceInfo(
            publicKey: publicKey, 
            addresses: Utilities.GetIPs().Select(x => x.ToString()).ToArray(),
            port: PORT,
            pairingCode: pairingCode);
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

    public string? GetCachedName(string publicKey)
    {
        return SyncService?.GetCachedName(publicKey);
    }

    public Task BroadcastJsonAsync(byte subOpcode, object data, CancellationToken cancellationToken = default) => BroadcastAsync(Opcode.DATA, subOpcode, GJsonSerializer.AndroidCompatible.SerializeObj(data), cancellationToken);
    public Task BroadcastAsync(Opcode opcode, byte subOpcode, string data, CancellationToken cancellationToken = default) => BroadcastAsync(opcode, subOpcode, Encoding.UTF8.GetBytes(data), cancellationToken);
    public async Task BroadcastAsync(Opcode opcode, byte subOpcode, byte[] data, CancellationToken cancellationToken = default)
    {
        var sessions = SyncService?.GetSessions();
        if (sessions == null)
            return;

        //TODO: Should be done in parallel
        foreach (var session in sessions)
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

    public bool HasAtLeastOneDevice()
    {
        return (SyncService?.GetAuthorizedDeviceCount() ?? 0) > 0;
    }

    public string[] GetAllDevices()
    {
        return SyncService?.GetAllAuthorizedDevices() ?? [];
    }

    public async Task DeleteDeviceAsync(string publicKey)
    {
        await Task.Run(async () =>
        {
            try
            {
                var session = SyncService!.GetSession(publicKey);
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

                SyncService.RemoveAuthorizedDevice(publicKey);
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
        SyncService?.Dispose();
        SyncService = null;
    }

    private const int PORT = 12315;
    private const uint APP_ID = 0x534A5247; //GRayJaySync (GRJS)

    private static StateSync? _instance;
    public static StateSync Instance => _instance ?? (_instance = new StateSync());
    public static void Cleanup()
    {
        _instance?.Dispose();
        _instance = null;
    }
}

public class StoreBasedSyncDatabaseProvider : ISyncDatabaseProvider
{
    private readonly StringArrayStore _authorizedDevices;
    private readonly StringStore _syncKeyPair;
    private readonly DictionaryStore<string, string> _lastAddressStorage;
    private readonly DictionaryStore<string, string> _nameStorage;
    private readonly DictionaryStore<string, SyncSessionData> _syncSessionData;

    public StoreBasedSyncDatabaseProvider()
    {
        _authorizedDevices = new StringArrayStore("authorizedDevices", Array.Empty<string>()).Load();
        _syncKeyPair = new StringStore("syncKeyPair").Load();
        _lastAddressStorage = new DictionaryStore<string, string>("lastAddressStorage").Load();
        _nameStorage = new DictionaryStore<string, string>("rememberedNameStorage").Load();
        _syncSessionData = new DictionaryStore<string, SyncSessionData>("syncSessionData", new Dictionary<string, SyncSessionData>()).Load();
    }

    public bool IsAuthorized(string publicKey)
    {
        lock (_authorizedDevices)
            return _authorizedDevices.Value.Contains(publicKey);
    }

    public void AddAuthorizedDevice(string publicKey)
    {
        lock (_authorizedDevices)
        {
            if (!_authorizedDevices.Value.Contains(publicKey))
                _authorizedDevices.Save(_authorizedDevices.Value.Concat([publicKey]).ToArray());
        }
    }

    public void RemoveAuthorizedDevice(string publicKey)
    {
        lock (_authorizedDevices)
        {
            _authorizedDevices.Save(_authorizedDevices.Value.Where(pk => pk != publicKey).ToArray());
        }
    }

    public string[]? GetAllAuthorizedDevices()
    {
        lock (_authorizedDevices)
        {
            return _authorizedDevices.Value?.ToArray();
        }
    }

    public int GetAuthorizedDeviceCount()
    {
        lock (_authorizedDevices)
        {
            return _authorizedDevices.Value?.Length ?? 0;
        }
    }

    public SyncKeyPair? GetSyncKeyPair()
    {
        return JsonSerializer.Deserialize<SyncKeyPair>(EncryptionProvider.Instance.Decrypt(_syncKeyPair.Value!));
    }

    public void SetSyncKeyPair(SyncKeyPair value)
    {
        _syncKeyPair.Save(EncryptionProvider.Instance.Encrypt(JsonSerializer.Serialize(value)));
    }

    public string? GetLastAddress(string publicKey)
    {
        lock (_lastAddressStorage)
            return _lastAddressStorage.GetValue(publicKey, null);
    }

    public void SetLastAddress(string publicKey, string address)
    {
        lock (_lastAddressStorage)
        {
            _lastAddressStorage.Value[publicKey] = address;
            _lastAddressStorage.SaveThis();
        }
    }

    public string? GetDeviceName(string publicKey)
    {
        return _nameStorage.GetValue(publicKey, null);
    }

    public void SetDeviceName(string publicKey, string name)
    {
        _nameStorage.SetAndSave(publicKey, name);
    }

    public SyncSessionData? GetSyncSessionData(string publicKey)
    {
        return _syncSessionData.GetValue(publicKey, null);
    }

    public void SetSyncSessionData(string publicKey, SyncSessionData data)
    {
        _syncSessionData.SetAndSave(publicKey, data);
    }
}
