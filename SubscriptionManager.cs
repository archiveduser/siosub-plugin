using System.Collections.Concurrent;
using System.Text.Json;
using Dalamud.Game.Text;
using SocketIOClient;

namespace SioSub;

public sealed class SubscriptionManager : IDisposable
{
    private const int MaxMessageRecords = 200;
    private readonly PluginConfiguration configuration;
    private readonly Func<string, string> resolveVariables;
    private readonly Action<RoomConfiguration, string> printMessage;
    private readonly Action saveConfiguration;
    private readonly object syncRoot = new();
    private readonly ConcurrentDictionary<Guid, SubscriptionStatus> statuses = new();
    private readonly List<MessageRecord> messageRecords = [];
    private readonly List<SocketConnection> connections = [];

    public SubscriptionManager(
        PluginConfiguration configuration,
        Func<string, string> resolveVariables,
        Action<RoomConfiguration, string> printMessage,
        Action saveConfiguration)
    {
        this.configuration = configuration;
        this.resolveVariables = resolveVariables;
        this.printMessage = printMessage;
        this.saveConfiguration = saveConfiguration;
    }

    public IReadOnlyCollection<SubscriptionStatus> Statuses => this.statuses.Values.OrderBy(status => status.Name).ToArray();

    public IReadOnlyList<MessageRecord> MessageRecords
    {
        get
        {
            lock (this.syncRoot)
            {
                return this.messageRecords.ToArray();
            }
        }
    }

    public async Task ReloadAsync()
    {
        List<SocketConnection> oldConnections;
        lock (this.syncRoot)
        {
            oldConnections = [.. this.connections];
            this.connections.Clear();
            this.statuses.Clear();
        }

        foreach (var connection in oldConnections)
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }

        foreach (var subscription in this.configuration.Subscriptions)
        {
            subscription.EnsureValid();
            var status = new SubscriptionStatus
            {
                SubscriptionId = subscription.Id,
                Name = subscription.Name,
                ServerUrl = subscription.ServerUrl,
                State = subscription.Enabled ? ConnectionState.Disconnected : ConnectionState.Disabled,
                Detail = subscription.Enabled ? "等待连接" : "已禁用",
                Rooms = subscription.Rooms.Where(room => room.Enabled).Select(room => this.resolveVariables(room.Room)).ToArray(),
                UpdatedAt = DateTimeOffset.Now,
            };
            this.statuses[subscription.Id] = status;

            if (!subscription.Enabled || string.IsNullOrWhiteSpace(subscription.ServerUrl))
            {
                continue;
            }

            var enabledRooms = subscription.Rooms
                .Where(room => room.Enabled && !string.IsNullOrWhiteSpace(room.Room))
                .ToArray();
            if (enabledRooms.Length == 0)
            {
                status.State = ConnectionState.Disabled;
                status.Detail = "没有启用的 room";
                continue;
            }

            var socketConnection = new SocketConnection(
                subscription,
                enabledRooms,
                status,
                this.resolveVariables,
                this.HandleMessage);

            lock (this.syncRoot)
            {
                this.connections.Add(socketConnection);
            }

            _ = socketConnection.ConnectAsync();
        }

        this.saveConfiguration();
    }

    public void ClearMessages()
    {
        lock (this.syncRoot)
        {
            this.messageRecords.Clear();
        }
    }

    public void Dispose()
    {
        List<SocketConnection> oldConnections;
        lock (this.syncRoot)
        {
            oldConnections = [.. this.connections];
            this.connections.Clear();
        }

        foreach (var connection in oldConnections)
        {
            connection.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private void HandleMessage(SubscriptionConfiguration subscription, RoomConfiguration room, string resolvedRoom, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        this.printMessage(room, text);
        var record = new MessageRecord(
            DateTimeOffset.Now,
            subscription.Name,
            resolvedRoom,
            room.ChatType,
            room.Tag,
            text);

        lock (this.syncRoot)
        {
            this.messageRecords.Add(record);
            if (this.messageRecords.Count > MaxMessageRecords)
            {
                this.messageRecords.RemoveRange(0, this.messageRecords.Count - MaxMessageRecords);
            }
        }
    }

    private sealed class SocketConnection : IAsyncDisposable
    {
        private readonly SubscriptionConfiguration subscription;
        private readonly IReadOnlyList<RoomConfiguration> rooms;
        private readonly SubscriptionStatus status;
        private readonly Func<string, string> resolveVariables;
        private readonly Action<SubscriptionConfiguration, RoomConfiguration, string, string> handleMessage;
        private readonly Dictionary<string, RoomConfiguration> resolvedRooms;
        private readonly SocketIOClient.SocketIO socket;

        public SocketConnection(
            SubscriptionConfiguration subscription,
            IReadOnlyList<RoomConfiguration> rooms,
            SubscriptionStatus status,
            Func<string, string> resolveVariables,
            Action<SubscriptionConfiguration, RoomConfiguration, string, string> handleMessage)
        {
            this.subscription = subscription;
            this.rooms = rooms;
            this.status = status;
            this.resolveVariables = resolveVariables;
            this.handleMessage = handleMessage;
            this.resolvedRooms = rooms
                .Select(room => new { Config = room, Name = resolveVariables(room.Room) })
                .Where(item => !string.IsNullOrWhiteSpace(item.Name))
                .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First().Config, StringComparer.OrdinalIgnoreCase);

            this.socket = new SocketIOClient.SocketIO(subscription.ServerUrl, new SocketIOOptions
            {
                Path = subscription.Path,
                Reconnection = true,
                ReconnectionAttempts = int.MaxValue,
                ReconnectionDelay = 2000,
            });

            this.socket.OnConnected += async (_, _) =>
            {
                this.SetStatus(ConnectionState.Connected, "已连接");
                await this.JoinRoomsAsync().ConfigureAwait(false);
            };
            this.socket.OnDisconnected += (_, reason) => this.SetStatus(ConnectionState.Disconnected, reason);
            this.socket.OnError += (_, error) => this.SetStatus(ConnectionState.Error, error);

            this.RegisterMessageHandlers();
        }

        public async Task ConnectAsync()
        {
            try
            {
                this.SetStatus(ConnectionState.Connecting, "连接中");
                await this.socket.ConnectAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                this.SetStatus(ConnectionState.Error, ex.Message);
            }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await this.socket.DisconnectAsync().ConfigureAwait(false);
                this.socket.Dispose();
            }
            catch
            {
                // Plugin unload should continue even if a socket is already gone.
            }
        }

        private void RegisterMessageHandlers()
        {
            this.socket.On(this.subscription.MessageEvent, response =>
            {
                var payload = MessagePayload.FromResponse(response);
                var targetRoom = payload.Room;

                if (!string.IsNullOrWhiteSpace(targetRoom)
                    && this.resolvedRooms.TryGetValue(targetRoom, out var room))
                {
                    this.handleMessage(this.subscription, room, targetRoom, payload.Text);
                    return;
                }

                if (this.resolvedRooms.Count == 1)
                {
                    var only = this.resolvedRooms.First();
                    this.handleMessage(this.subscription, only.Value, only.Key, payload.Text);
                }
            });

            foreach (var item in this.resolvedRooms)
            {
                var resolvedRoom = item.Key;
                var room = item.Value;
                this.socket.On(resolvedRoom, response =>
                {
                    var payload = MessagePayload.FromResponse(response);
                    this.handleMessage(this.subscription, room, resolvedRoom, payload.Text);
                });
            }
        }

        private async Task JoinRoomsAsync()
        {
            foreach (var resolvedRoom in this.resolvedRooms.Keys)
            {
                await this.socket.EmitAsync(this.subscription.JoinEvent, resolvedRoom).ConfigureAwait(false);
            }

            this.status.Rooms = this.resolvedRooms.Keys.ToArray();
            this.status.UpdatedAt = DateTimeOffset.Now;
        }

        private void SetStatus(ConnectionState state, string detail)
        {
            this.status.State = state;
            this.status.Detail = detail;
            this.status.UpdatedAt = DateTimeOffset.Now;
            this.status.Rooms = this.resolvedRooms.Keys.ToArray();
        }
    }

    private sealed class MessagePayload
    {
        public string Text { get; init; } = string.Empty;

        public string? Room { get; init; }

        public static MessagePayload FromResponse(SocketIOResponse response)
        {
            try
            {
                var first = response.GetValue<JsonElement>(0);
                return FromJson(first);
            }
            catch
            {
                return new MessagePayload { Text = response.ToString() ?? string.Empty };
            }
        }

        private static MessagePayload FromJson(JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => new MessagePayload { Text = element.GetString() ?? string.Empty },
                JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => new MessagePayload { Text = element.ToString() },
                JsonValueKind.Object => FromObject(element),
                _ => new MessagePayload { Text = element.ToString() },
            };
        }

        private static MessagePayload FromObject(JsonElement element)
        {
            var text = TryGetString(element, "text")
                ?? TryGetString(element, "message")
                ?? TryGetString(element, "content")
                ?? element.ToString();
            var room = TryGetString(element, "room")
                ?? TryGetString(element, "roomName")
                ?? TryGetString(element, "channel");

            return new MessagePayload
            {
                Text = text,
                Room = room,
            };
        }

        private static string? TryGetString(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var property))
            {
                return null;
            }

            return property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : property.ToString();
        }
    }
}
