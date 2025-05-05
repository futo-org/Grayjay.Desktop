using Grayjay.ClientServer.Serializers;
using SyncClient;
using SyncShared;
using System.Text;

namespace Grayjay.ClientServer.Sync.Internal;

using LogLevel = Grayjay.Desktop.POC.LogLevel;
using Logger = Grayjay.Desktop.POC.Logger;

public class SyncSession : IDisposable, IAuthorizable
{
    private readonly List<IChannel> _channels = new List<IChannel>();
    private bool _authorized;
    private bool _remoteAuthorized;
    private readonly Action<SyncSession, bool, bool> _onAuthorized;
    private readonly Action<SyncSession> _onUnauthorized;
    private readonly Action<SyncSession> _onClose;
    private readonly Action<SyncSession, bool> _onConnectedChanged;
    private readonly Action<SyncSession, Opcode, byte, ReadOnlySpan<byte>> _dataHandler;
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

    public LinkType LinkType
    {
        get
        {
            var linkType = LinkType.None;

            lock (_channels)
            {
                foreach (var channel in _channels)
                {
                    if (channel.LinkType == LinkType.Direct)
                        return LinkType.Direct;
                    if (channel.LinkType == LinkType.Relayed)
                        linkType = LinkType.Relayed;
                }
            }

            return linkType;
        }
    }

    public SyncSession(string remotePublicKey, Action<SyncSession, bool, bool> onAuthorized, Action<SyncSession> onUnauthorized,
        Action<SyncSession, bool> onConnectedChanged, Action<SyncSession> onClose, Action<SyncSession, Opcode, byte, ReadOnlySpan<byte>> dataHandler, string? remoteDeviceName)
    {
        RemotePublicKey = remotePublicKey.DecodeBase64().EncodeBase64();
        RemoteDeviceName = remoteDeviceName;
        _onAuthorized = onAuthorized;
        _onUnauthorized = onUnauthorized;
        _onConnectedChanged = onConnectedChanged;
        _onClose = onClose;
        _dataHandler = dataHandler;
    }

    public void AddChannel(IChannel channel)
    {
        if (channel.RemotePublicKey != RemotePublicKey)
            throw new Exception("Public key of session must match public key of socket session");

        channel.Authorizable = this;
        channel.SyncSession = this;
        lock (_channels)
        {
            _channels.Add(channel);
            Connected = _channels.Any();
        }
    }

    public async Task AuthorizeAsync(CancellationToken cancellationToken = default)
    {
        Logger.i<SyncSession>($"Sent AUTHORIZED with session id {_id}");

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

        await SendAsync(Opcode.NOTIFY, (byte)NotifyOpcode.AUTHORIZED, data, cancellationToken: cancellationToken);

        _authorized = true;
        CheckAuthorized();
    }

    public async Task UnauthorizeAsync(CancellationToken cancellationToken = default)
    {
        await SendAsync(Opcode.NOTIFY, (byte)NotifyOpcode.UNAUTHORIZED, cancellationToken: cancellationToken);
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

    public void RemoveChannel(IChannel channel)
    {
        lock (_channels)
        {
            _channels.Remove(channel);
            Connected = _channels.Any();
        }
    }

    public void Close()
    {
        lock (_channels)
        {
            var channelsToClose = _channels.ToList();
            foreach (var channel in channelsToClose)
                channel.Dispose();
            _channels.Clear();
        }

        _onClose(this);
    }

    public void HandlePacket(Opcode opcode, byte subOpcode, ReadOnlySpan<byte> data)
    {
        if (Logger.WillLog(LogLevel.Debug))
            Logger.Debug<SyncSession>($"Handle packet (opcode: {opcode}, subOpcode: {subOpcode}, data length: {data.Length})");

        switch (opcode)
        {
            case Opcode.NOTIFY:
                switch ((NotifyOpcode)subOpcode)
                {
                    case NotifyOpcode.AUTHORIZED:
                        var offset = 0;
                        var idStringLength = data[offset];
                        offset++;
                        if (idStringLength > 64)
                            throw new Exception("Id string must be less than 64 bytes.");

                        var idString = Encoding.UTF8.GetString(data.Slice(offset, idStringLength));
                        offset += idStringLength;

                        var nameLength = data[offset];
                        offset++;
                        if (nameLength > 64)
                            throw new Exception("Name string must be less than 64 bytes.");

                        _remoteId = data.Length >= 16 ? Guid.Parse(idString) : Guid.Empty;
                        RemoteDeviceName = Encoding.UTF8.GetString(data.Slice(offset, nameLength));
                        offset += nameLength;

                        _remoteAuthorized = true;
                        Logger.i<SyncSession>($"Received AUTHORIZED with session id {_remoteId} (device name: '{RemoteDeviceName ?? "not set"}')");
                        CheckAuthorized();
                        return;
                    case NotifyOpcode.UNAUTHORIZED:
                        _remoteAuthorized = false;
                        _remoteId = null;
                        RemoteDeviceName = null;
                        _onUnauthorized(this);
                        return;
                }
                break;
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

        try
        {
            _dataHandler.Invoke(this, opcode, subOpcode, data);
        }
        catch (Exception e)
        {
            //TODO: Should be disconnected? socketSession.Dispose();
            Logger.w<SyncSession>("Failed to handle packet", e);
        }
    }

    //TODO: local connections should be used before udp and udp before relayed
    public async Task SendAsync(Opcode opcode, byte subOpcode, byte[]? data = null, int offset = 0, int count = -1, ContentEncoding contentEncoding = ContentEncoding.Raw, CancellationToken cancellationToken = default)
    {
        int c = count;
        if (c == -1)
            c = data?.Length ?? 0;

        //TODO: Make this more efficient
        //TODO: Prefer local over remote, etc
        List<IChannel> channels;
        lock (_channels)
        {
            channels = _channels.OrderBy(c => (int)c.LinkType).ToList();
        }

        if (channels.Count == 0)
        {
            Logger.v<SyncSession>($"Packet was not sent (opcode = {opcode}, subOpcode = {subOpcode}) due to no connected sockets");
            return;
        }

        var sent = false;
        foreach (var channel in channels) 
        {
            try
            {
                await channel.SendAsync(opcode, subOpcode, data, offset, count, contentEncoding: contentEncoding, cancellationToken: cancellationToken);
                sent = true;
                break;
            }
            catch (Exception e)
            {
                Logger.w<SyncSession>($"Packet failed to send (opcode = {opcode}, subOpcode = {subOpcode}) due to no connected sockets, closing channel", e);
                channel.Dispose();
                RemoveChannel(channel);
            }            
        }

        if (!sent)
            throw new Exception($"Packet was not sent (opcode = {opcode}, subOpcode = {subOpcode}) due to send errors and no remaining candidates");
    }

    public Task SendAsync(Opcode opcode, byte subOpcode, string data, CancellationToken cancellationToken = default) => SendAsync(opcode, subOpcode, Encoding.UTF8.GetBytes(data), contentEncoding: ContentEncoding.Gzip, cancellationToken: cancellationToken);
    public Task SendJsonDataAsync(byte subOpcode, object data, CancellationToken cancellationToken = default) => SendAsync(Opcode.DATA, subOpcode, Encoding.UTF8.GetBytes(GJsonSerializer.AndroidCompatible.SerializeObj(data)), cancellationToken: cancellationToken);

    public void Dispose()
    {
        Close();
    }
}