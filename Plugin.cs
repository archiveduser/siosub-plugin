using System.Collections.Concurrent;
using Dalamud.Configuration;
using Dalamud.Game.Command;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace SioSub;

public sealed class Plugin : IDalamudPlugin
{
    private const string CommandName = "/siosub";

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    private readonly ConcurrentQueue<ChatPrintRequest> chatQueue = new();
    private readonly SubscriptionManager subscriptionManager;
    private bool configVisible;

    public Plugin()
    {
        this.Configuration = PluginInterface.GetPluginConfig() as PluginConfiguration ?? PluginConfiguration.CreateDefault();
        this.Configuration.EnsureValid();

        this.subscriptionManager = new SubscriptionManager(
            this.Configuration,
            this.ResolveVariables,
            this.EnqueueChatMessage,
            this.EnqueueConnectionStatus,
            this.SaveConfiguration);

        PluginInterface.UiBuilder.Draw += this.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += this.OpenConfig;
        PluginInterface.UiBuilder.OpenMainUi += this.OpenConfig;
        CommandManager.AddHandler(CommandName, new CommandInfo(this.OnCommand)
        {
            HelpMessage = "打开 SioSub 设置窗口。使用 /siosub status 查看连接状态。",
        });

        ClientState.Login += this.OnLogin;
        ClientState.Logout += this.OnLogout;

        if (ClientState.IsLoggedIn)
        {
            this.ReloadSubscriptionsForCurrentCharacter();
        }
    }

    internal PluginConfiguration Configuration { get; }

    public void Dispose()
    {
        CommandManager.RemoveHandler(CommandName);
        PluginInterface.UiBuilder.Draw -= this.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= this.OpenConfig;
        PluginInterface.UiBuilder.OpenMainUi -= this.OpenConfig;
        ClientState.Login -= this.OnLogin;
        ClientState.Logout -= this.OnLogout;
        this.subscriptionManager.Dispose();
    }

    internal void SaveConfiguration()
    {
        this.Configuration.EnsureValid();
        PluginInterface.SavePluginConfig(this.Configuration);
    }

    internal Task ReloadSubscriptionsAsync() => this.subscriptionManager.ReloadAsync();

    private void OnCommand(string command, string args)
    {
        if (args.Trim().Equals("status", StringComparison.OrdinalIgnoreCase))
        {
            this.PrintConnectionStatus();
            return;
        }

        this.configVisible = true;
    }

    private void OpenConfig()
    {
        this.configVisible = true;
    }

    private void Draw()
    {
        this.FlushChatQueue();

        if (this.configVisible)
        {
            ConfigWindow.Draw(
                ref this.configVisible,
                this.Configuration,
                this.subscriptionManager,
                this.SaveConfiguration,
                this.ReloadSubscriptionsForCurrentCharacter,
                this.ResolveVariables);
        }
    }

    private void OnLogin()
    {
        Log.Info("[SioSub] Character login detected. Connecting subscriptions.");
        this.ReloadSubscriptionsForCurrentCharacter();
    }

    private void OnLogout(int type, int code)
    {
        Log.Info("[SioSub] Character logout detected. Disconnecting subscriptions. Type={Type}, Code={Code}", type, code);
        _ = this.subscriptionManager.DisconnectAsync();
    }

    private void ReloadSubscriptionsForCurrentCharacter()
    {
        if (!ClientState.IsLoggedIn)
        {
            Log.Info("[SioSub] Not logged in. Subscriptions will connect after character login.");
            return;
        }

        _ = this.subscriptionManager.ReloadAsync();
    }

    private void FlushChatQueue()
    {
        while (this.chatQueue.TryDequeue(out var request))
        {
            ChatGui.Print(new XivChatEntry
            {
                Type = request.ChatType,
                Message = request.Message,
                Name = new SeString(),
                Silent = request.Silent,
            });
        }
    }

    private void EnqueueChatMessage(ListenerConfiguration listener, string text)
    {
        var builder = new SeStringBuilder();
        var tag = this.ResolveVariables(listener.Tag);
        if (!string.IsNullOrWhiteSpace(tag))
        {
            if (listener.TagColorEnabled)
            {
                builder.AddUiForeground(listener.TagColorKey);
            }

            builder.AddText($"[{tag}]");

            if (listener.TagColorEnabled)
            {
                builder.AddUiForegroundOff();
            }
        }

        builder.AddText(text);
        this.chatQueue.Enqueue(new ChatPrintRequest(listener.ChatType, builder.BuiltString, this.Configuration.SilentChatMessages));
    }

    private void EnqueueConnectionStatus(ConnectionState state, string subscriptionName, string detail)
    {
        var message = $"[SioSub] {subscriptionName}: {StateToText(state)}";
        if (!string.IsNullOrWhiteSpace(detail))
        {
            message += $" - {detail}";
        }

        switch (state)
        {
            case ConnectionState.Error:
                Log.Error(message);
                break;
            case ConnectionState.Disconnected:
                Log.Warning(message);
                break;
            default:
                Log.Info(message);
                break;
        }
    }

    private static string StateToText(ConnectionState state)
    {
        return state switch
        {
            ConnectionState.Disabled => "已禁用",
            ConnectionState.Disconnected => "已断开",
            ConnectionState.Connecting => "连接中",
            ConnectionState.Connected => "已连接",
            ConnectionState.Error => "连接错误",
            _ => state.ToString(),
        };
    }

    private void PrintConnectionStatus()
    {
        if (!ClientState.IsLoggedIn)
        {
            this.EnqueueSystemMessage("[SioSub] 当前未登录角色，订阅未连接。");
            return;
        }

        var statuses = this.subscriptionManager.Statuses.ToArray();
        if (statuses.Length == 0)
        {
            this.EnqueueSystemMessage("[SioSub] 当前没有订阅状态。");
            return;
        }

        foreach (var status in statuses)
        {
            var events = status.Events.Count == 0 ? "无监听事件" : string.Join(", ", status.Events);
            this.EnqueueSystemMessage($"[SioSub] {status.Name}: {StateToText(status.State)} - {status.Detail}; {status.ServerUrl}; 监听: {events}");
        }
    }

    private void EnqueueSystemMessage(string message)
    {
        this.chatQueue.Enqueue(new ChatPrintRequest(XivChatType.SystemMessage, new SeString(new TextPayload(message)), true));
    }

    private string ResolveVariables(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var server = string.Empty;
        var dataCenter = string.Empty;

        try
        {
            if (PlayerState.IsLoaded)
            {
                var currentWorld = PlayerState.CurrentWorld.ValueNullable;
                if (currentWorld is not null)
                {
                    server = currentWorld.Value.Name.ToString();
                    var dc = currentWorld.Value.DataCenter.ValueNullable;
                    if (dc is not null)
                    {
                        dataCenter = dc.Value.Name.ToString();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Failed to resolve player world variables.");
        }

        return value
            .Replace("${SERVER}", server, StringComparison.OrdinalIgnoreCase)
            .Replace("${DATACENTER}", dataCenter, StringComparison.OrdinalIgnoreCase);
    }

    private readonly record struct ChatPrintRequest(XivChatType ChatType, SeString Message, bool Silent);
}
