using Grayjay.Desktop.POC;
using Microsoft.AspNetCore.DataProtection;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Xml;

namespace Grayjay.ClientServer.WebSockets
{
    public class WebSocketEndpoint: IDisposable
    {
        public string Url { get; set; }

        public Dictionary<string, WebSocketClient> _clients = new Dictionary<string, WebSocketClient>();

        public event Action<WebSocketClient> OnNewClient;
        public event Action<WebSocketClient, WebSocketPacket> OnPacketAll;

        public WebSocketEndpoint(string url)
        {
            Url = url;
        }

        public async Task HandleClient(WebSocket socket)
        {
            var client = new WebSocketClient(socket);
            var onPacket = (WebSocketClient client, WebSocketPacket packet) =>
            {
                OnPacketAll?.Invoke(client, packet);
            };
            client.OnPacket += onPacket;
            client.OnClose += (_) => 
            {
                lock (_clients)
                {
                    Logger.i(nameof(WebSocketEndpoint), "Removed disconnected client " + client.ID);
                    _clients.Remove(client.ID);
                    client.OnPacket -= onPacket;
                }
            };

            lock (_clients)
            {
                _clients[client.ID] = client;
            }
            OnNewClient?.Invoke(client);

            await client.Handle();
        }

        public async Task Broadcast(object? message, string? type = null, string? id = null)
        {
            List<WebSocketClient> clients;
            lock (_clients)
            {
                clients = _clients.Values.ToList();
            }
            
            var obj = new WebSocketPacket()
            {
                ID = id,
                Type = type,
                Payload = message
            };

            byte[] toSend = obj.ToPacket();
            foreach (WebSocketClient client in clients)
            {
                try
                {
                    await client.SendRaw(toSend);
                }
                catch(Exception ex)
                {
                    Logger.e(nameof(WebSocketEndpoint), "Failed to send message to client, removing client " + client.ID, ex);
                    lock (_clients)
                    {
                        _clients.Remove(client.ID);
                    }
                }
            }
        }


        public void Dispose()
        {
            List<WebSocketClient> clients;
            lock (_clients)
            {
                clients = _clients.Values.ToList();
                _clients.Clear();
            }

            foreach (var client in clients)
                client.Dispose();
        }
    }

    public class WebSocketClient: IDisposable
    {
        public string ID { get; private set; } = Guid.NewGuid().ToString();
        public WebSocket Socket { get; private set; }
        public bool Active { get; private set; } = true;
        public event Action<WebSocketClient> OnClose;
        public event Action<WebSocketClient, WebSocketPacket> OnPacket;

        public WebSocketClient(WebSocket socket)
        {
            Socket = socket;
        }

        public async Task Handle()
        {
            byte[] buffer = new byte[64 * 1024];
            Send("Connected", "Status");

            while (Socket.State == WebSocketState.Open && Active)
            {
                var result = await Socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }
            }

            try
            {
                await Socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
            }
            catch (Exception ex)
            {
                Logger.e(nameof(WebSocketEndpoint), "Failed to close socket", ex);
            }
            
            OnClose?.Invoke(this);
        }

        public async void Send(object data, string? type = null, string? id = null)
        {
            var obj = new WebSocketPacket()
            {
                Payload = data,
                ID = id,
                Type = type
            };
            await Send(obj);
        }
        public async Task Send(WebSocketPacket packet)
        {
            await SendRaw(packet.ToPacket());
        }
        public async Task SendRaw(byte[] bytes)
        {
            await Socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
        }

        public void Dispose()
        {
            Active = false;
            Socket.Dispose();
        }
    }

    public class WebSocketPacket
    {
        public string? ID { get; set; }
        public string? Type { get; set; }
        public object? Payload { get; set; }


        private static JsonSerializerOptions _options = new JsonSerializerOptions()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        public byte[] ToPacket()
        {
            return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(this, _options));
        }
    }
}
