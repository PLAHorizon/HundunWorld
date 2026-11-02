// PCG 插件 - 参数列的**纯几何**（不碰任何控件，只算矩形）
// 属于 FlaxEngine PCG 插件。
//
// 为什么单独抽出来：参数列里"输入框右端伸出面板、露出去的那截点不到"是一类**只会被眼睛发现**的缺陷，
// 而离屏门禁看不见编辑器。把摆放规则变成纯函数之后，"每个矩形都在面板之内"就能被断言钉住，
// 而不是靠对着截图猜。窗口那边只负责把算出来的矩形赋给控件——两边用的是同一份规则，不会走偏。

using FlaxEngine;

namespace PCGEditor
{
    /// <summary>一行的摆放结果：标签、暴露勾选框与若干个输入框。</summary>
    public struct PCGParameterRowLayout
    {
        /// <summary>参数名标签。</summary>
        public Rectangle Label;

        /// <summary>「暴露」这两个字。</summary>
        public Rectangle ExposeLabel;

        /// <summary>暴露勾选框。</summary>
        public Rectangle ExposeBox;

        /// <summary>每个输入框的宽度。</summary>
        public float FieldWidth;

        /// <summary>第一个输入框的左边界。</summary>
        public float FieldLeft;

        /// <summary>输入框那一行的上边界。</summary>
        public float FieldTop;

        /// <summary>输入框个数。</summary>
        public int FieldCount;

        /// <summary>这一行总共占掉的高度（标签行 + 输入框行 + 行间空隙）。</summary>
        public float RowAdvance;

        /// <summary>第 <paramref name="index"/> 个输入框的矩形。</summary>
        public Rectangle FieldRect(int index) => new Rectangle(
            FieldLeft + index * (FieldWidth + FieldGap), FieldTop, FieldWidth, FieldHeight);

        /// <summary>相邻两个输入框之间的空隙。</summary>
        public const float FieldGap = 4.0f;

        /// <summary>输入框的高度。</summary>
        public const float FieldHeight = 18.0f;

        /// <summary>这一行里最靠右的那个边界。</summary>
        public float RightEdge
        {
            get
            {
                float right = ExposeBox.Right;

                for (int i = 0; i < FieldCount; i++)
                    right = Mathf.Max(right, FieldRect(i).Right);

                return right;
            }
        }
    }

    /// <summary>参数列的摆放规则（纯函数，可离线断言）。</summary>
    public static class PCGParameterLayout
    {
        /// <summary>参数列自身的最小可用宽度。</summary>
        public const float MinPanelWidth = 180.0f;

        /// <summary>右边留给滚动条/边框的余量：输入框不许贴着面板边缘。</summary>
        public const float RightMargin = 26.0f;

        /// <summary>输入框的宽度下限。</summary>
        public const float MinFieldWidth = 26.0f;

        /// <summary>暴露勾选框那一块的宽度（「暴露」两字 + 勾选框）。</summary>
        public const float ExposeWidth = 62.0f;

        /// <summary>参数名标签与暴露那一块之间的空隙。</summary>
        public const float LabelGap = 6.0f;

        /// <summary>左边距。</summary>
        public const float LeftMargin = 6.0f;

        /// <summary>
        /// 参数列顶部那条标题（节点名或"参数"）。
        /// </summary>
        /// <remarks>
        /// 这一条是**必须的**：没有它，一列在没选中节点时什么也不画，而其背景与空画布几乎同色——于是"没有属性
        /// 编辑 UI"与"属性列是空的"在屏幕上看不出区别。标题把它变成一块看得见的面板。
        /// </remarks>
        public static Rectangle Header(float panelWidth)
        {
            float panel = Mathf.Max(MinPanelWidth, panelWidth);
            return new Rectangle(LeftMargin, 4.0f, panel - 2.0f * LeftMargin, 18.0f);
        }

        /// <summary>没选中节点（或节点没有参数）时给的一句话，替那一列解释它为什么是空的。</summary>
        public static Rectangle Hint(float panelWidth)
        {
            float panel = Mathf.Max(MinPanelWidth, panelWidth);
            return new Rectangle(LeftMargin, 26.0f, panel - 2.0f * LeftMargin, 18.0f);
        }

        /// <summary>算出某一行在给定面板宽度下的摆放。</summary>
        /// <param name="panelWidth">参数列的宽度（应当 ≥ <see cref="MinPanelWidth"/>）。</param>
        /// <param name="componentCount">这一行有几个输入框（1 个标量，3 个向量，4 个颜色）。</param>
        public static PCGParameterRowLayout Row(float panelWidth, int componentCount)
        {
            int count = Mathf.Max(1, componentCount);
            float panel = Mathf.Max(MinPanelWidth, panelWidth);

            // 标签让位给「暴露」那一块：两者同一行，所以标签的右边界必须停在暴露块的左边再往前一点。
            float exposeLeft = panel - RightMargin - ExposeWidth;

            var layout = new PCGParameterRowLayout
            {
                Label = new Rectangle(LeftMargin, 0.0f, Mathf.Max(24.0f, exposeLeft - LabelGap - LeftMargin), 16.0f),
                ExposeLabel = new Rectangle(exposeLeft, 0.0f, 38.0f, 16.0f),
                ExposeBox = new Rectangle(panel - RightMargin - 18.0f, -1.0f, 18.0f, 18.0f),
                FieldCount = count,
                FieldLeft = LeftMargin,
                FieldTop = 16.0f,
                RowAdvance = 39.0f,
            };

            // 宽度按面板宽度摊分，但**不小于下限**：面板窄到摊不开时，输入框会一起撑出面板——
            // 那种情况由"面板本身不许比 MinPanelWidth 更窄"来挡住（窗口那边给面板定了下限）。
            float available = panel - RightMargin - LeftMargin - (count - 1) * PCGParameterRowLayout.FieldGap;
            layout.FieldWidth = Mathf.Max(MinFieldWidth, available / count);

            return layout;
        }

        /// <summary>
        /// 给定面板宽度下**装得下**的最大输入框个数。
        /// </summary>
        /// <remarks>
        /// 宽度有下限（<see cref="MinFieldWidth"/>），所以个数多到一定程度必然撑不下面板。插件目前最多的是颜色
        /// （4 个），离屏断言会断言"最窄的面板也至少装得下 4 个"——哪天有人加一个 5 个分量的类型，
        /// 那条会先红，而不是等到屏幕上发现输入框又伸出去了。
        /// </remarks>
        public static int MaxFittingComponents(float panelWidth)
        {
            float panel = Mathf.Max(MinPanelWidth, panelWidth);
            int fit = 0;

            for (int count = 1; count <= 16; count++)
            {
                if (Row(panel, count).RightEdge <= panel + 0.01f)
                    fit = count;
            }

            return fit;
        }
    }
}
