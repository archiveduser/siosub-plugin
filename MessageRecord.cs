using Dalamud.Game.Text;

namespace SioSub;

public sealed record MessageRecord(
    DateTimeOffset Time,
    string SubscriptionName,
    string Room,
    XivChatType ChatType,
    string Tag,
    string Text);
