using FlaxEngine;
using FlaxEngine.GUI;

namespace HundunWorld.Game.UI.Components
{
    /// <summary>
    /// 圆角面板组件
    /// 继承 Panel，使用基类 BackgroundColor 渲染纯色背景
    /// （圆角效果后续通过纹理实现）
    /// </summary>
    public class RoundedPanel : Panel
    {
        /// <summary>
        /// 圆角半径（当前版本使用纯色矩形，圆角效果待实现）
        /// </summary>
        public float CornerRadius { get; set; } = UIStyleTokens.RadiusCard;

        /// <summary>
        /// 构造函数：默认裁剪子控件，防止内容溢出面板边界
        /// </summary>
        public RoundedPanel()
        {
            ClipChildren = true;
        }
    }
}
