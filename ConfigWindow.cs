using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.Text;

namespace SioSub;

internal static class ConfigWindow
{
    private static readonly XivChatType[] ChatTypes =
    [
        XivChatType.Notice,
        XivChatType.Urgent,
        XivChatType.Debug,
        XivChatType.Echo,
        XivChatType.SystemMessage,
        XivChatType.NoviceNetworkSystem,
        XivChatType.Party,
        XivChatType.Alliance,
        XivChatType.FreeCompany,
        XivChatType.Ls1,
        XivChatType.CrossLinkShell1,
    ];

    private static readonly string[] ChatTypeNames = ChatTypes.Select(type => type.ToString()).ToArray();

    private static readonly TagColorPreset[] TagColorPresets =
    [
        new("白色", 1, new Vector4(1.00f, 1.00f, 1.00f, 1.00f)),
        new("黄色", 45, new Vector4(1.00f, 0.86f, 0.22f, 1.00f)),
        new("橙色", 500, new Vector4(1.00f, 0.55f, 0.18f, 1.00f)),
        new("红色", 17, new Vector4(1.00f, 0.28f, 0.28f, 1.00f)),
        new("绿色", 43, new Vector4(0.36f, 0.90f, 0.42f, 1.00f)),
        new("青色", 37, new Vector4(0.30f, 0.82f, 1.00f, 1.00f)),
        new("蓝色", 34, new Vector4(0.45f, 0.62f, 1.00f, 1.00f)),
        new("紫色", 39, new Vector4(0.82f, 0.55f, 1.00f, 1.00f)),
        new("Dalamud", 540, new Vector4(0.82f, 0.55f, 1.00f, 1.00f)),
    ];

    private static readonly string[] TagColorNames = TagColorPresets
        .Select(preset => $"{preset.Name} ({preset.ColorKey})")
        .Concat(["自定义"])
        .ToArray();

    public static void Draw(
        ref bool visible,
        PluginConfiguration configuration,
        SubscriptionManager manager,
        Action save,
        Action reload,
        Func<string, string> resolveVariables)
    {
        ImGui.SetNextWindowSize(new Vector2(900, 620), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("SioSub 设置", ref visible, ImGuiWindowFlags.None))
        {
            ImGui.End();
            return;
        }

        if (ImGui.BeginTabBar("SioSubTabs", ImGuiTabBarFlags.None))
        {
            if (ImGui.BeginTabItem("订阅", ImGuiTabItemFlags.None))
            {
                DrawSubscriptions(configuration, save, reload, resolveVariables);
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("状态", ImGuiTabItemFlags.None))
            {
                DrawStatuses(manager);
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("消息记录", ImGuiTabItemFlags.None))
            {
                DrawMessages(manager);
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }

        ImGui.End();
    }

    private static void DrawSubscriptions(
        PluginConfiguration configuration,
        Action save,
        Action reload,
        Func<string, string> resolveVariables)
    {
        var silent = configuration.SilentChatMessages;
        if (ImGui.Checkbox("聊天消息静音", ref silent))
        {
            configuration.SilentChatMessages = silent;
            save();
        }

        ImGui.SameLine();
        if (ImGui.Button("应用并重连", new Vector2(120, 0)))
        {
            save();
            reload();
        }

        ImGui.SameLine();
        if (ImGui.Button("添加订阅", new Vector2(110, 0)))
        {
            configuration.Subscriptions.Add(new SubscriptionConfiguration
            {
                Name = $"订阅 {configuration.Subscriptions.Count + 1}",
                ServerUrl = "http://127.0.0.1:3000",
                Listeners =
                [
                    new ListenerConfiguration
                    {
                        EventName = "siosub-test",
                        Tag = "SioSub",
                        TagColorEnabled = true,
                        TagColorKey = 540,
                        ChatType = XivChatType.Notice,
                    },
                ],
            });
            save();
        }

        ImGui.Separator();
        ImGui.TextUnformatted("可用变量: ${DATACENTER}, ${SERVER}");

        var removeSubscription = -1;
        for (var i = 0; i < configuration.Subscriptions.Count; i++)
        {
            var subscription = configuration.Subscriptions[i];
            ImGui.PushID(i);

            if (ImGui.CollapsingHeader($"{subscription.Name}##subscription", ImGuiTreeNodeFlags.DefaultOpen))
            {
                var enabled = subscription.Enabled;
                if (ImGui.Checkbox("启用", ref enabled))
                {
                    subscription.Enabled = enabled;
                    save();
                }

                ImGui.SameLine();
                if (ImGui.Button("删除订阅", new Vector2(100, 0)))
                {
                    removeSubscription = i;
                }

                InputText("名称", subscription.Name, value => subscription.Name = value, 128, save);
                InputText("服务器 URL", subscription.ServerUrl, value => subscription.ServerUrl = value, 512, save);
                InputText("Socket.IO Path", subscription.Path, value => subscription.Path = value, 128, save);

                if (ImGui.Button("添加监听事件", new Vector2(120, 0)))
                {
                    subscription.Listeners.Add(new ListenerConfiguration
                    {
                        EventName = "siosub-test",
                        Tag = "SioSub",
                        TagColorEnabled = true,
                        TagColorKey = 540,
                        ChatType = XivChatType.Notice,
                    });
                    save();
                }

                var removeListener = -1;
                for (var listenerIndex = 0; listenerIndex < subscription.Listeners.Count; listenerIndex++)
                {
                    var listener = subscription.Listeners[listenerIndex];
                    ImGui.PushID(listenerIndex);
                    ImGui.Separator();

                    var listenerEnabled = listener.Enabled;
                    if (ImGui.Checkbox("启用监听", ref listenerEnabled))
                    {
                        listener.Enabled = listenerEnabled;
                        save();
                    }

                    ImGui.SameLine();
                    if (ImGui.Button("删除监听", new Vector2(100, 0)))
                    {
                        removeListener = listenerIndex;
                    }

                    InputText("监听事件", listener.EventName, value => listener.EventName = value, 256, save);
                    ImGui.TextUnformatted($"实际事件: {resolveVariables(listener.EventName)}");
                    InputText("Tag", listener.Tag, value => listener.Tag = value, 128, save);
                    DrawTagColorControls(listener, save);
                    DrawChatTypeCombo(listener, save);

                    ImGui.PopID();
                }

                if (removeListener >= 0)
                {
                    subscription.Listeners.RemoveAt(removeListener);
                    save();
                }
            }

            ImGui.PopID();
        }

        if (removeSubscription >= 0)
        {
            configuration.Subscriptions.RemoveAt(removeSubscription);
            save();
        }
    }

    private static void DrawStatuses(SubscriptionManager manager)
    {
        if (ImGui.Button("刷新连接", new Vector2(110, 0)))
        {
            _ = manager.ReloadAsync();
        }

        ImGui.Separator();
        if (ImGui.BeginTable("StatusTable", 5, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg, Vector2.Zero, 0))
        {
            ImGui.TableSetupColumn("名称", ImGuiTableColumnFlags.None, 0, 0);
            ImGui.TableSetupColumn("服务器", ImGuiTableColumnFlags.None, 0, 0);
            ImGui.TableSetupColumn("状态", ImGuiTableColumnFlags.None, 0, 0);
            ImGui.TableSetupColumn("监听事件", ImGuiTableColumnFlags.None, 0, 0);
            ImGui.TableSetupColumn("更新时间", ImGuiTableColumnFlags.None, 0, 0);
            ImGui.TableHeadersRow();

            foreach (var status in manager.Statuses)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(status.Name);
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(status.ServerUrl);
                ImGui.TableNextColumn();
                ImGui.TextUnformatted($"{status.State}: {status.Detail}");
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(string.Join(", ", status.Events));
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(status.UpdatedAt.ToLocalTime().ToString("HH:mm:ss"));
            }

            ImGui.EndTable();
        }
    }

    private static void DrawMessages(SubscriptionManager manager)
    {
        if (ImGui.Button("清空记录", new Vector2(100, 0)))
        {
            manager.ClearMessages();
        }

        ImGui.Separator();
        if (ImGui.BeginTable("MessageTable", 6, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY, new Vector2(0, 460), 0))
        {
            ImGui.TableSetupColumn("时间", ImGuiTableColumnFlags.WidthFixed, 82, 0);
            ImGui.TableSetupColumn("订阅", ImGuiTableColumnFlags.WidthFixed, 120, 0);
            ImGui.TableSetupColumn("监听事件", ImGuiTableColumnFlags.WidthFixed, 150, 0);
            ImGui.TableSetupColumn("频道", ImGuiTableColumnFlags.WidthFixed, 110, 0);
            ImGui.TableSetupColumn("Tag", ImGuiTableColumnFlags.WidthFixed, 100, 0);
            ImGui.TableSetupColumn("文本", ImGuiTableColumnFlags.None, 0, 0);
            ImGui.TableHeadersRow();

            foreach (var record in manager.MessageRecords.Reverse())
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(record.Time.ToLocalTime().ToString("HH:mm:ss"));
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(record.SubscriptionName);
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(record.EventName);
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(record.ChatType.ToString());
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(record.Tag);
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(record.Text);
            }

            ImGui.EndTable();
        }
    }

    private static void InputText(string label, string value, Action<string> setValue, int maxLength, Action save)
    {
        var copy = value ?? string.Empty;
        ImGui.SetNextItemWidth(420);
        if (ImGui.InputText(label, ref copy, maxLength, ImGuiInputTextFlags.None, (ImGui.ImGuiInputTextCallbackDelegate?)null))
        {
            setValue(copy);
            save();
        }
    }

    private static void DrawChatTypeCombo(ListenerConfiguration listener, Action save)
    {
        var currentIndex = Array.IndexOf(ChatTypes, listener.ChatType);
        if (currentIndex < 0)
        {
            currentIndex = 0;
        }

        ImGui.SetNextItemWidth(220);
        if (ImGui.Combo("输出频道", ref currentIndex, ChatTypeNames, ChatTypeNames.Length))
        {
            listener.ChatType = ChatTypes[currentIndex];
            save();
        }
    }

    private static void DrawTagColorControls(ListenerConfiguration listener, Action save)
    {
        var tagColorEnabled = listener.TagColorEnabled;
        if (ImGui.Checkbox("Tag 上色", ref tagColorEnabled))
        {
            listener.TagColorEnabled = tagColorEnabled;
            save();
        }

        ImGui.SameLine();
        var previewColor = TagColorPresets.FirstOrDefault(preset => preset.ColorKey == listener.TagColorKey).PreviewColor;
        if (previewColor == Vector4.Zero)
        {
            previewColor = new Vector4(1, 1, 1, 1);
        }

        ImGui.ColorButton("Tag颜色预览", in previewColor, ImGuiColorEditFlags.NoTooltip, new Vector2(24, 24));

        var currentIndex = Array.FindIndex(TagColorPresets, preset => preset.ColorKey == listener.TagColorKey);
        if (currentIndex < 0)
        {
            currentIndex = TagColorPresets.Length;
        }

        ImGui.SetNextItemWidth(220);
        if (ImGui.Combo("Tag 颜色", ref currentIndex, TagColorNames, TagColorNames.Length))
        {
            if (currentIndex >= 0 && currentIndex < TagColorPresets.Length)
            {
                listener.TagColorKey = TagColorPresets[currentIndex].ColorKey;
            }

            save();
        }

        var colorKeyInt = (int)listener.TagColorKey;
        ImGui.SetNextItemWidth(120);
        if (ImGui.InputInt("UIColor ID", ref colorKeyInt, 1, 10, "%d", ImGuiInputTextFlags.None))
        {
            listener.TagColorKey = (ushort)Math.Clamp(colorKeyInt, 0, (int)ushort.MaxValue);
            save();
        }
    }

    private readonly record struct TagColorPreset(string Name, ushort ColorKey, Vector4 PreviewColor);
}
