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
    private readonly Action<ListenerConfiguration, string> printMessage;
    private readonly Action<ConnectionState, string, string> logStatus;
    private readonly Action saveConfiguration;
    private readonly object syncRoot = new();
    private readonly ConcurrentDictionary<Guid, SubscriptionStatus> statuses = new();
    private readonly List<MessageRecord> messageRecords = [];
    private readonly List<SocketConnection> connections = [];

    public SubscriptionManager(
        PluginConfiguration configuration,
        Func<string, string> resolveVariables,
        Action<ListenerConfiguration, string> printMessage,
        Action<ConnectionState, string, string> logStatus,
        Action saveConfiguration)
    {
        this.configuration = configuration;
        this.resolveVariables = resolveVariables;
        this.printMessage = printMessage;
        this.logStatus = logStatus;
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
                Events = subscription.Listeners.Where(listener => listener.Enabled).Select(listener => this.resolveVariables(listener.EventName)).ToArray(),
                UpdatedAt = DateTimeOffset.Now,
            };
            this.statuses[subscription.Id] = status;

            if (!subscription.Enabled || string.IsNullOrWhiteSpace(subscription.ServerUrl))
            {
                continue;
            }

            var enabledListeners = subscription.Listeners
                .Where(listener => listener.Enabled && !string.IsNullOrWhiteSpace(listener.EventName))
                .ToArray();
            if (enabledListeners.Length == 0)
            {
                status.State = ConnectionState.Disabled;
                status.Detail = "没有启用的监听事件";
                continue;
            }

            var socketConnection = new SocketConnection(
                subscription,
                enabledListeners,
                status,
                this.resolveVariables,
                this.logStatus,
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

    public async Task DisconnectAsync()
    {
        var oldConnections = this.TakeConnections();

        foreach (var connection in oldConnections)
        {
            await connection.DisposeAsync().ConfigureAwait(false);
        }

        foreach (var status in this.statuses.Values)
        {
            status.State = ConnectionState.Disconnected;
            status.Detail = "未登录角色";
            status.UpdatedAt = DateTimeOffset.Now;
        }
    }

    public void Dispose()
    {
        var oldConnections = this.TakeConnections();

        foreach (var connection in oldConnections)
        {
            connection.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }

    private List<SocketConnection> TakeConnections()
    {
        lock (this.syncRoot)
        {
            var oldConnections = new List<SocketConnection>(this.connections);
            this.connections.Clear();
            return oldConnections;
        }
    }

    private void HandleMessage(SubscriptionConfiguration subscription, ListenerConfiguration listener, string resolvedEvent, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        this.printMessage(listener, text);
        var record = new MessageRecord(
            DateTimeOffset.Now,
            subscription.Name,
            resolvedEvent,
            listener.ChatType,
            listener.Tag,
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
        private readonly SubscriptionStatus status;
        private readonly Action<ConnectionState, string, string> logStatus;
        private readonly Action<SubscriptionConfiguration, ListenerConfiguration, string, string> handleMessage;
        private readonly Dictionary<string, ListenerConfiguration> resolvedEvents;
        private readonly SocketIOClient.SocketIO socket;
        private bool disposing;

        public SocketConnection(
            SubscriptionConfiguration subscription,
            IReadOnlyList<ListenerConfiguration> listeners,
            SubscriptionStatus status,
            Func<string, string> resolveVariables,
            Action<ConnectionState, string, string> logStatus,
            Action<SubscriptionConfiguration, ListenerConfiguration, string, string> handleMessage)
        {
            this.subscription = subscription;
            this.status = status;
            this.logStatus = logStatus;
            this.handleMessage = handleMessage;
            this.resolvedEvents = listeners
                .Select(listener => new { Config = listener, Name = resolveVariables(listener.EventName) })
                .Where(item => !string.IsNullOrWhiteSpace(item.Name))
                .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First().Config, StringComparer.OrdinalIgnoreCase);

            this.socket = new SocketIOClient.SocketIO(subscription.ServerUrl, new SocketIOOptions
            {
                Path = subscription.Path,
                ConnectionTimeout = TimeSpan.FromSeconds(10),
                EIO = SocketIO.Core.EngineIO.V4,
                AutoUpgrade = true,
                Reconnection = true,
                ReconnectionAttempts = int.MaxValue,
                ReconnectionDelay = 2000,
            });

            this.socket.OnConnected += (_, _) => this.SetStatus(ConnectionState.Connected, $"已连接; {subscription.ServerUrl}{subscription.Path}; 监听: {string.Join(", ", this.resolvedEvents.Keys)}");
            this.socket.OnDisconnected += (_, reason) =>
            {
                if (!this.disposing)
                {
                    this.SetStatus(ConnectionState.Disconnected, reason);
                }
            };
            this.socket.OnError += (_, error) => this.SetStatus(ConnectionState.Error, $"{subscription.ServerUrl}{subscription.Path}; {error}");

            this.RegisterEventHandlers();
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
                this.SetStatus(ConnectionState.Error, $"{this.subscription.ServerUrl}{this.subscription.Path}; {ex.Message}");
            }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                this.disposing = true;
                await this.socket.DisconnectAsync().ConfigureAwait(false);
                this.socket.Dispose();
            }
            catch
            {
                // Plugin unload should continue even if a socket is already gone.
            }
        }

        private void RegisterEventHandlers()
        {
            foreach (var item in this.resolvedEvents)
            {
                var resolvedEvent = item.Key;
                var listener = item.Value;
                this.socket.On(resolvedEvent, response =>
                {
                    var payload = MessagePayload.FromResponse(response);
                    this.handleMessage(this.subscription, listener, resolvedEvent, payload.Text);
                });
            }
        }

        private void SetStatus(ConnectionState state, string detail)
        {
            var shouldLog = this.status.State != state || !string.Equals(this.status.Detail, detail, StringComparison.Ordinal);
            this.status.State = state;
            this.status.Detail = detail;
            this.status.UpdatedAt = DateTimeOffset.Now;
            this.status.Events = this.resolvedEvents.Keys.ToArray();

            if (shouldLog)
            {
                this.logStatus(state, this.subscription.Name, detail);
            }
        }
    }

    private sealed class MessagePayload
    {
        public string Text { get; init; } = string.Empty;

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

            return new MessagePayload
            {
                Text = text,
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
