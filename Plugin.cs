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
            this.SaveConfiguration);

        PluginInterface.UiBuilder.Draw += this.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += this.OpenConfig;
        PluginInterface.UiBuilder.OpenMainUi += this.OpenConfig;
        CommandManager.AddHandler(CommandName, new CommandInfo(this.OnCommand)
        {
            HelpMessage = "打开 SioSub 设置窗口。",
        });

        _ = this.subscriptionManager.ReloadAsync();
    }

    internal PluginConfiguration Configuration { get; }

    public void Dispose()
    {
        CommandManager.RemoveHandler(CommandName);
        PluginInterface.UiBuilder.Draw -= this.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= this.OpenConfig;
        PluginInterface.UiBuilder.OpenMainUi -= this.OpenConfig;
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
                () => _ = this.subscriptionManager.ReloadAsync(),
                this.ResolveVariables);
        }
    }

    private void FlushChatQueue()
    {
        while (this.chatQueue.TryDequeue(out var request))
        {
            ChatGui.Print(new XivChatEntry
            {
                Type = request.ChatType,
                Message = new SeString(new TextPayload(request.Text)),
                Name = new SeString(),
                Silent = request.Silent,
            });
        }
    }

    private void EnqueueChatMessage(RoomConfiguration room, string text)
    {
        var tag = string.IsNullOrWhiteSpace(room.Tag) ? string.Empty : $"[{this.ResolveVariables(room.Tag)}]";
        var message = $"{tag}{text}";
        this.chatQueue.Enqueue(new ChatPrintRequest(room.ChatType, message, this.Configuration.SilentChatMessages));
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

    private readonly record struct ChatPrintRequest(XivChatType ChatType, string Text, bool Silent);
}
