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
                Rooms =
                [
                    new RoomConfiguration
                    {
                        Room = "room-${SERVER}",
                        Tag = "SioSub",
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
                InputText("Join 事件", subscription.JoinEvent, value => subscription.JoinEvent = value, 128, save);
                InputText("消息事件", subscription.MessageEvent, value => subscription.MessageEvent = value, 128, save);

                if (ImGui.Button("添加 Room", new Vector2(100, 0)))
                {
                    subscription.Rooms.Add(new RoomConfiguration
                    {
                        Room = "room-${SERVER}",
                        Tag = "SioSub",
                        ChatType = XivChatType.Notice,
                    });
                    save();
                }

                var removeRoom = -1;
                for (var roomIndex = 0; roomIndex < subscription.Rooms.Count; roomIndex++)
                {
                    var room = subscription.Rooms[roomIndex];
                    ImGui.PushID(roomIndex);
                    ImGui.Separator();

                    var roomEnabled = room.Enabled;
                    if (ImGui.Checkbox("启用 Room", ref roomEnabled))
                    {
                        room.Enabled = roomEnabled;
                        save();
                    }

                    ImGui.SameLine();
                    if (ImGui.Button("删除 Room", new Vector2(100, 0)))
                    {
                        removeRoom = roomIndex;
                    }

                    InputText("Room", room.Room, value => room.Room = value, 256, save);
                    ImGui.TextUnformatted($"实际 Room: {resolveVariables(room.Room)}");
                    InputText("Tag", room.Tag, value => room.Tag = value, 128, save);
                    DrawChatTypeCombo(room, save);

                    ImGui.PopID();
                }

                if (removeRoom >= 0)
                {
                    subscription.Rooms.RemoveAt(removeRoom);
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
            ImGui.TableSetupColumn("Room", ImGuiTableColumnFlags.None, 0, 0);
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
                ImGui.TextUnformatted(string.Join(", ", status.Rooms));
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
            ImGui.TableSetupColumn("Room", ImGuiTableColumnFlags.WidthFixed, 150, 0);
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
                ImGui.TextUnformatted(record.Room);
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

    private static void DrawChatTypeCombo(RoomConfiguration room, Action save)
    {
        var currentIndex = Array.IndexOf(ChatTypes, room.ChatType);
        if (currentIndex < 0)
        {
            currentIndex = 0;
        }

        ImGui.SetNextItemWidth(220);
        if (ImGui.Combo("输出频道", ref currentIndex, ChatTypeNames, ChatTypeNames.Length))
        {
            room.ChatType = ChatTypes[currentIndex];
            save();
        }
    }
}
