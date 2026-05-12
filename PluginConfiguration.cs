using Dalamud.Configuration;
using Dalamud.Game.Text;

namespace SioSub;

[Serializable]
public sealed class PluginConfiguration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public bool SilentChatMessages { get; set; } = true;

    public List<SubscriptionConfiguration> Subscriptions { get; set; } = [];

    public static PluginConfiguration CreateDefault()
    {
        return new PluginConfiguration
        {
            Subscriptions =
            [
                new SubscriptionConfiguration
                {
                    Name = "本地测试服务器",
                    ServerUrl = "http://127.0.0.1:3000",
                    Listeners =
                    [
                        new ListenerConfiguration
                        {
                            EventName = "siosub-test",
                            Tag = "LOCAL",
                            ChatType = XivChatType.Notice,
                        },
                    ],
                },
            ],
        };
    }

    public void EnsureValid()
    {
        this.Subscriptions ??= [];
        foreach (var subscription in this.Subscriptions)
        {
            subscription.EnsureValid();
        }
    }
}

[Serializable]
public sealed class SubscriptionConfiguration
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public bool Enabled { get; set; } = true;

    public string Name { get; set; } = "订阅";

    public string ServerUrl { get; set; } = string.Empty;

    public string Path { get; set; } = "/socket.io";

    public List<ListenerConfiguration> Listeners { get; set; } = [];

    public void EnsureValid()
    {
        if (this.Id == Guid.Empty)
        {
            this.Id = Guid.NewGuid();
        }

        this.Name ??= string.Empty;
        this.ServerUrl ??= string.Empty;
        this.Path = string.IsNullOrWhiteSpace(this.Path) ? "/socket.io" : this.Path;
        this.Listeners ??= [];

        foreach (var listener in this.Listeners)
        {
            listener.EnsureValid();
        }
    }
}

[Serializable]
public sealed class ListenerConfiguration
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public bool Enabled { get; set; } = true;

    public string EventName { get; set; } = string.Empty;

    public XivChatType ChatType { get; set; } = XivChatType.Notice;

    public string Tag { get; set; } = "SioSub";

    public void EnsureValid()
    {
        if (this.Id == Guid.Empty)
        {
            this.Id = Guid.NewGuid();
        }

        this.EventName ??= string.Empty;
        this.Tag ??= string.Empty;
    }
}
