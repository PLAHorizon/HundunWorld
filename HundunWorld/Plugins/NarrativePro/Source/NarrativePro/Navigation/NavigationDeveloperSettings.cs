using FlaxEngine;

namespace NarrativePro.Navigation
{
    /// <summary>
    /// 导航系统开发者设置。适配 UE5 UNavigationDeveloperSettings。
    /// Flax 中使用静态类作为配置中心。
    /// </summary>
    public static class NavigationDeveloperSettings
    {
        /// <summary>本地玩家标记颜色</summary>
        public static Color PlayerColor { get; set; } = new Color(0.2f, 0.8f, 1.0f, 1.0f);

        /// <summary>友方标记颜色</summary>
        public static Color FriendlyColor { get; set; } = new Color(0.2f, 1.0f, 0.2f, 1.0f);

        /// <summary>中立标记颜色</summary>
        public static Color NeutralColor { get; set; } = new Color(1.0f, 1.0f, 0.2f, 1.0f);

        /// <summary>敌方标记颜色</summary>
        public static Color HostileColor { get; set; } = new Color(1.0f, 0.2f, 0.2f, 1.0f);
    }
}
