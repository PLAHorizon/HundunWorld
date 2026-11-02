using FlaxEngine;
using HundunWorld.Game.UI.StyleSystem;
using ChatChannel = Horizon.Game.Message.Network.ChatChannel;

namespace HundunWorld.Game.UI.Ink.Components
{
    /// <summary>
    /// 聊天频道展示口径的唯一来源：Tab 顺序、中文标签、行内前缀与颜色。
    /// 主界面左下聊天框与「结伴江湖」页共用，避免同一频道在两处叫法/配色不一致。
    /// </summary>
    public static class InkChatChannelView
    {
        /// <summary>频道 Tab 顺序（与聊天框一致）。</summary>
        public static readonly ChatChannel[] TabChannels =
        {
            ChatChannel.Nearby, ChatChannel.World, ChatChannel.Sect, ChatChannel.Team,
            ChatChannel.Raid, ChatChannel.Private, ChatChannel.System,
        };

        public static string Label(ChatChannel c) => c switch
        {
            ChatChannel.Nearby => "附近",
            ChatChannel.World => "世界",
            ChatChannel.Sect => "门派",
            ChatChannel.Team => "队伍",
            ChatChannel.Raid => "团队",
            ChatChannel.Private => "密聊",
            ChatChannel.System => "系统",
            _ => "聊天",
        };

        public static (string Prefix, Color Color) Style(ChatChannel c) => c switch
        {
            ChatChannel.Nearby => ("[附近]", InkWashTheme.TextSecondary),
            ChatChannel.World => ("[世界]", InkWashTheme.GoldBright),
            ChatChannel.Sect => ("[门派]", InkWashTheme.JadeBright),
            ChatChannel.Team => ("[队伍]", InkWashTheme.BloodBright),
            ChatChannel.Raid => ("[团队]", InkWashTheme.QualityEpic),
            ChatChannel.Private => ("[密聊]", InkWashTheme.QualityEpic),
            ChatChannel.System => ("[系统]", InkWashTheme.TextTertiary),
            _ => ("[聊天]", InkWashTheme.TextSecondary),
        };
    }
}
