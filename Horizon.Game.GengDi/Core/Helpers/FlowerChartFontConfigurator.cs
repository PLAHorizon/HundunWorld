using SkiaSharp;

namespace Horizon.Game.GengDi.Core.Helpers
{
    /// <summary>
    /// LiveCharts2 全局字体配置器。
    /// 在 App 启动时调用 Configure()，预先加载中文字体 SKTypeface。
    /// FlowerChartHelper.CjkTypeface 会在所有 Paint 中引用此字体。
    /// </summary>
    public static class FlowerChartFontConfigurator
    {
        private static bool _configured;

        /// <summary>预加载中文字体，确保 SkiaSharp 字体缓存就绪</summary>
        public static void Configure()
        {
            if (_configured) return;
            _configured = true;

            // 触发 FlowerChartHelper 的静态构造函数，预加载 CjkTypeface
            _ = FlowerChartHelper.CjkTypeface;
        }
    }
}
