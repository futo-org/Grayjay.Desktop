using Grayjay.ClientServer.Serializers;
using Grayjay.Desktop.POC;
using System.Text;
using static Grayjay.ClientServer.Sync.Internal.SyncSocketSession;

namespace Grayjay.ClientServer.Sync.Internal;

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
        Action<SyncSession, bool> onConnectedChanged, Action<SyncSession> onClose)
    {
        RemotePublicKey = remotePublicKey;
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

    public void Authorize(SyncSocketSession? socketSession = null)
    {
        if (socketSession == null)
        {
            lock (_socketSessions)
            {
                socketSession = _socketSessions.First();
            }
        }

        Logger.i<SyncSession>($"Sent AUTHORIZED with session id {_id}");
        var str = _id.ToString();
        socketSession.Send(Opcode.NOTIFY_AUTHORIZED, 0, Encoding.UTF8.GetBytes(str));
        _authorized = true;
        CheckAuthorized();
    }

    public void Unauthorize(SyncSocketSession? socketSession = null)
    {
        if (socketSession == null)
        {
            lock (_socketSessions)
            {
                socketSession = _socketSessions.First();
            }
        }

        socketSession.Send(Opcode.NOTIFY_UNAUTHORIZED);
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
        Console.WriteLine($"Handle packet (opcode: {opcode}, subOpcode: {subOpcode}, data length: {data.Length})");

        switch (opcode)
        {
            case Opcode.NOTIFY_AUTHORIZED:
                _remoteId = data.Length >= 16 ? Guid.Parse(Encoding.UTF8.GetString(data)) : Guid.Empty;
                _remoteAuthorized = true;
                Logger.i<SyncSession>($"Received AUTHORIZED with session id {_remoteId}");
                CheckAuthorized();
                return;
            case Opcode.NOTIFY_UNAUTHORIZED:
                _remoteAuthorized = false;
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

        Console.WriteLine($"Received (opcode = {opcode}, subOpcode = {subOpcode}) ({data.Length} bytes)");
        Handlers?.Handle(this, socketSession, (byte)opcode, subOpcode, data);
    }


    public void Send(byte opcode, byte subOpcode, byte[] data)
    {
        var connection = _socketSessions.FirstOrDefault();
        if (connection != null)
            connection.Send(opcode, subOpcode, data);
        else
            throw new InvalidOperationException("No connection");
    }

    public void Send(byte opcode, byte subOpcode, string data) => Send(opcode, subOpcode, Encoding.UTF8.GetBytes(data));
    public void SendJsonData(byte subOpcode, object data) => Send((byte)Opcode.DATA, subOpcode, Encoding.UTF8.GetBytes(GJsonSerializer.AndroidCompatible.SerializeObj(data)));

    public void Dispose()
    {
        Close();
    }
}