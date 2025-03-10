using Grayjay.ClientServer.Serializers;
using Grayjay.Desktop.POC;
using System.Text;
using static Grayjay.ClientServer.Sync.Internal.SyncSocketSession;

namespace Grayjay.ClientServer.Sync.Internal;

using LogLevel = Grayjay.Desktop.POC.LogLevel;

public interface IAuthorizable
{
    bool IsAuthorized { get; }
}

public class SyncSession : IAuthorizable, IDisposable
{
    private readonly List<SyncSocketSession> _socketSessions = new List<SyncSocketSession>();
    private bool _authorized;
    private bool _remoteAuthorized;
    private readonly Action<SyncSession, bool, bool> _onAuthorized;
    private readonly Action<SyncSession> _onUnauthorized;
    private readonly Action<SyncSession> _onClose;
    private readonly Action<SyncSession, bool> _onConnectedChanged;
    public string RemotePublicKey { get; }
    public bool IsAuthorized => _authorized && _remoteAuthorized;
    public bool _wasAuthorized = false;
    private Guid _id = Guid.NewGuid();
    private Guid? _remoteId = null;
    public string? RemoteDeviceName { get; private set; } = null;
    public string DisplayName => RemoteDeviceName ?? RemotePublicKey;
    private Guid? _lastAuthorizedRemoteId = null;

    private bool _connected;
    public bool Connected
    {
        get => _connected;
        private set
        {
            if (_connected != value)
            {
                _connected = value;
                _onConnectedChanged(this, _connected);
            }
        }
    }

    public static SyncHandlers Handlers = new GrayjaySyncHandlers();

    public SyncSession(string remotePublicKey, Action<SyncSession, bool, bool> onAuthorized, Action<SyncSession> onUnauthorized,
        Action<SyncSession, bool> onConnectedChanged, Action<SyncSession> onClose, string? remoteDeviceName)
    {
        RemotePublicKey = remotePublicKey;
        RemoteDeviceName = remoteDeviceName;
        _onAuthorized = onAuthorized;
        _onUnauthorized = onUnauthorized;
        _onConnectedChanged = onConnectedChanged;
        _onClose = onClose;
    }

    public void AddSocketSession(SyncSocketSession socketSession)
    {
        if (socketSession.RemotePublicKey != RemotePublicKey)
            throw new Exception("Public key of session must match public key of socket session");

        lock (_socketSessions)
        {
            _socketSessions.Add(socketSession);
            Connected = _socketSessions.Any();
        }

        socketSession.Authorizable = this;
    }

    public async Task AuthorizeAsync(SyncSocketSession? socketSession = null, CancellationToken cancellationToken = default)
    {
        if (socketSession == null)
        {
            lock (_socketSessions)
            {
                socketSession = _socketSessions.First();
            }
        }

        Logger.i<SyncSession>($"Sent AUTHORIZED with session id {_id}");

        if (socketSession.RemoteVersion >= 3)
        {
            var idBytes = Encoding.UTF8.GetBytes(_id.ToString());
            var nameBytes = Encoding.UTF8.GetBytes(OSHelper.GetComputerName());
            var data = new byte[sizeof(byte) + idBytes.Length + sizeof(byte) + nameBytes.Length];
            using (var stream = new MemoryStream(data))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write((byte)idBytes.Length);
                writer.Write(idBytes);
                writer.Write((byte)nameBytes.Length);
                writer.Write(nameBytes);
            }

            await socketSession.SendAsync(Opcode.NOTIFY_AUTHORIZED, 0, data, cancellationToken: cancellationToken);
        }
        else
        {
            var str = _id.ToString();
            await socketSession.SendAsync(Opcode.NOTIFY_AUTHORIZED, 0, Encoding.UTF8.GetBytes(str), cancellationToken: cancellationToken);
        }

        _authorized = true;
        CheckAuthorized();
    }

    public async Task UnauthorizeAsync(SyncSocketSession? socketSession = null, CancellationToken cancellationToken = default)
    {
        if (socketSession == null)
        {
            lock (_socketSessions)
            {
                socketSession = _socketSessions.First();
            }
        }

        await socketSession.SendAsync(Opcode.NOTIFY_UNAUTHORIZED, cancellationToken: cancellationToken);
    }

    private void CheckAuthorized()
    {
        if (IsAuthorized)
        {
            var isNewlyAuthorized = !_wasAuthorized;
            var isNewSession = _lastAuthorizedRemoteId != _remoteId;
            Logger.i<SyncSession>($"onAuthorized (isNewlyAuthorized = {isNewlyAuthorized}, isNewSession = {isNewSession})");
            _onAuthorized(this, isNewlyAuthorized, isNewSession);
            _wasAuthorized = true;
            _lastAuthorizedRemoteId = _remoteId;
        }
    }

    public void RemoveSocketSession(SyncSocketSession socketSession)
    {
        lock (_socketSessions)
        {
            _socketSessions.Remove(socketSession);
            Connected = _socketSessions.Any();
        }
    }

    public void Close()
    {
        lock (_socketSessions)
        {
            var socketSessionsToClose = _socketSessions.ToList();
            foreach (var socketSession in socketSessionsToClose)
            {
                socketSession.Stop();
            }
            _socketSessions.Clear();
        }

        _onClose(this);
    }

    public void HandlePacket(SyncSocketSession socketSession, Opcode opcode, byte subOpcode, byte[] data)
    {
        if (Logger.WillLog(LogLevel.Debug))
            Logger.Debug<SyncSession>($"Handle packet (opcode: {opcode}, subOpcode: {subOpcode}, data length: {data.Length})");

        switch (opcode)
        {
            case Opcode.NOTIFY_AUTHORIZED:

                if (socketSession.RemoteVersion >= 3)
                {
                    using var stream = new MemoryStream(data);
                    using var reader = new BinaryReader(stream);
                    
                    var idStringLength = reader.ReadByte();
                    if (idStringLength > 64)
                        throw new Exception("Id string must be less than 64 bytes.");
                    var idString = Encoding.UTF8.GetString(reader.ReadBytes(idStringLength));
                    var nameLength = reader.ReadByte();
                    if (nameLength > 64)
                        throw new Exception("Name string must be less than 64 bytes.");
                    _remoteId = data.Length >= 16 ? Guid.Parse(idString) : Guid.Empty;
                    RemoteDeviceName = Encoding.UTF8.GetString(reader.ReadBytes(nameLength));
                }
                else
                {
                    _remoteId = data.Length >= 16 ? Guid.Parse(Encoding.UTF8.GetString(data)) : Guid.Empty;
                    RemoteDeviceName = null;
                }

                _remoteAuthorized = true;
                Logger.i<SyncSession>($"Received AUTHORIZED with session id {_remoteId} (device name: '{RemoteDeviceName ?? "not set"}')");
                CheckAuthorized();
                return;
            case Opcode.NOTIFY_UNAUTHORIZED:
                _remoteAuthorized = false;
                _remoteId = null;
                RemoteDeviceName = null;
                _onUnauthorized(this);
                return;
                // Handle other potentially unauthorized packet types...
        }

        if (!IsAuthorized)
            return;

        if (opcode != Opcode.DATA)
        {
            Logger.w<SyncSession>($"Unknown opcode received: {opcode}");
            return;
        }

        if (Logger.WillLog(LogLevel.Debug))
            Logger.Debug<SyncSession>($"Received (opcode = {opcode}, subOpcode = {subOpcode}) ({data.Length} bytes)");

        Task.Run(() =>
        {
            try
            {
                Handlers?.HandleAsync(this, socketSession, (byte)opcode, subOpcode, data);
            }
            catch (Exception e)
            {
                //TODO: Should be disconnected? socketSession.Dispose();
                Logger.w<SyncSession>("Failed to handle packet", e);
            }
        });
    }


    public async Task SendAsync(byte opcode, byte subOpcode, byte[] data, CancellationToken cancellationToken = default)
    {
        List<SyncSocketSession> socketSessions;
        lock (_socketSessions)
        {
            socketSessions = _socketSessions.ToList();
        }

        if (socketSessions.Count == 0)
        {
            Logger.v<SyncSession>($"Packet was not sent (opcode = {opcode}, subOpcode = {subOpcode}) due to no connected sockets");
            return;
        }

        var sent = false;
        foreach (var socketSession in socketSessions) 
        {
            try
            {
                await socketSession.SendAsync(opcode, subOpcode, data, cancellationToken: cancellationToken);
                sent = true;
                break;
            }
            catch (Exception e)
            {
                Logger.w<SyncSession>($"Packet failed to send (opcode = {opcode}, subOpcode = {subOpcode}) due to no connected sockets", e);
            }            
        }

        if (!sent)
            throw new Exception($"Packet was not sent (opcode = {opcode}, subOpcode = {subOpcode}) due to send errors and no remaining candidates");
    }

    public Task SendAsync(byte opcode, byte subOpcode, string data, CancellationToken cancellationToken = default) => SendAsync(opcode, subOpcode, Encoding.UTF8.GetBytes(data), cancellationToken);
    public Task SendJsonDataAsync(byte subOpcode, object data, CancellationToken cancellationToken = default) => SendAsync((byte)Opcode.DATA, subOpcode, Encoding.UTF8.GetBytes(GJsonSerializer.AndroidCompatible.SerializeObj(data)), cancellationToken);

    public void Dispose()
    {
        Close();
    }
}