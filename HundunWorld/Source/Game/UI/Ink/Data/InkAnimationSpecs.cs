using System.Collections.Generic;

namespace HundunWorld.Game.UI.Ink.Data
{
    /// <summary>
    /// 水墨动画种类。
    /// 对应设计稿 <c>ink-anim-ref-1.html</c>（晕染 Spread）与 <c>ink-anim-ref-2.html</c>（笔触 Brush）。
    /// </summary>
    public enum InkAnimationKind
    {
        /// <summary>水墨晕染（Spread）— 触发场景：章节切换，总时长 2.4s</summary>
        Spread,

        /// <summary>水墨笔触（Brush）— 触发场景：技能释放，总时长 2.0s</summary>
        Brush,
    }

    /// <summary>
    /// 动画分镜的一个时间轴阶段。
    /// 对应设计稿底部时间轴中的一张阶段卡（时间区间 / 阶段名 / 阶段说明）。
    /// </summary>
    public sealed class InkAnimationPhase
    {
        /// <summary>时间区间文案，如 "0.0s — 0.4s"</summary>
        public string TimeRange { get; set; }

        /// <summary>阶段名，如 "墨点初现"</summary>
        public string Name { get; set; }

        /// <summary>阶段说明，如 "浓墨骤现 · opacity 0→0.9"</summary>
        public string Description { get; set; }

        /// <summary>阶段起始时间（秒），用于点击阶段卡跳转预览进度</summary>
        public float Start { get; set; }

        /// <summary>阶段结束时间（秒）</summary>
        public float End { get; set; }
    }

    /// <summary>
    /// 技术参数面板的一行（标签 + 取值）。
    /// </summary>
    public sealed class InkAnimationParam
    {
        /// <summary>参数名，如 "动画类型"</summary>
        public string Label { get; set; }

        /// <summary>参数值，如 "Spread"</summary>
        public string Value { get; set; }

        /// <summary>
        /// 构造函数。
        /// </summary>
        /// <param name="label">参数名</param>
        /// <param name="value">参数值</param>
        public InkAnimationParam(string label, string value)
        {
            Label = label;
            Value = value;
        }
    }

    /// <summary>
    /// 分镜标注卡（画面上的编号 + 标题 + 说明）。
    /// </summary>
    public sealed class InkAnimationAnnotation
    {
        /// <summary>标注编号，如 "1"</summary>
        public string Number { get; set; }

        /// <summary>标注标题，如 "墨点初现"</summary>
        public string Title { get; set; }

        /// <summary>标注说明</summary>
        public string Description { get; set; }

        /// <summary>水平位置（0~1）。<see cref="AnchorRight"/> 为 true 时表示距右边缘的比例</summary>
        public float X { get; set; }

        /// <summary>垂直位置（0~1）</summary>
        public float Y { get; set; }

        /// <summary>是否以右边缘为基准定位（对应设计稿的 right:xx%）</summary>
        public bool AnchorRight { get; set; }
    }

    /// <summary>
    /// 水墨动画分镜规格。
    /// 设计稿 <c>ink-anim-ref-1/2</c> 中「标题 + 标签 + 标注 + 技术参数 + 时间轴」的完整数据描述，
    /// 同时驱动动画预览控件的播放时序（<see cref="Duration"/> 与 <see cref="Phases"/>）。
    /// </summary>
    public sealed class InkAnimationSpec
    {
        /// <summary>动画种类</summary>
        public InkAnimationKind Kind { get; set; }

        /// <summary>分镜编号，如 "01"</summary>
        public string Number { get; set; }

        /// <summary>分镜主标题，如 "水墨晕染"</summary>
        public string Title { get; set; }

        /// <summary>分镜副标题（英文 · 分镜参考 n/n），如 "INK SPREAD · 分镜参考 01/02"</summary>
        public string Subtitle { get; set; }

        /// <summary>标题右侧标签文案（首个为主标签）</summary>
        public List<string> Tags { get; set; } = new List<string>();

        /// <summary>技术参数面板标题（设计稿固定为 "技术参数"）</summary>
        public string PanelTitle { get; set; } = "技术参数";

        /// <summary>时间轴区块标题，如 "动画时间轴 · TIMELINE"</summary>
        public string TimelineTitle { get; set; }

        /// <summary>动画总时长（秒）</summary>
        public float Duration { get; set; }

        /// <summary>技术参数行</summary>
        public List<InkAnimationParam> Parameters { get; set; } = new List<InkAnimationParam>();

        /// <summary>分镜标注</summary>
        public List<InkAnimationAnnotation> Annotations { get; set; } = new List<InkAnimationAnnotation>();

        /// <summary>时间轴阶段（按时序排列）</summary>
        public List<InkAnimationPhase> Phases { get; set; } = new List<InkAnimationPhase>();
    }

    /// <summary>
    /// 水墨动画规格表（设计稿数据落地）。
    /// <para>
    /// 数值与文案严格取自 <c>hundun-yy-ui/pages/ink-anim-ref-1.html</c> 与
    /// <c>ink-anim-ref-2.html</c> 的分镜标注、技术参数面板与时间轴。
    /// </para>
    /// </summary>
    public static class InkAnimationSpecs
    {
        /// <summary>水墨晕染分镜（ink-anim-ref-1.html）</summary>
        public static readonly InkAnimationSpec Spread = BuildSpread();

        /// <summary>水墨笔触分镜（ink-anim-ref-2.html）</summary>
        public static readonly InkAnimationSpec Brush = BuildBrush();

        /// <summary>
        /// 按种类取规格。
        /// </summary>
        /// <param name="kind">动画种类</param>
        /// <returns>对应的分镜规格</returns>
        public static InkAnimationSpec Get(InkAnimationKind kind)
        {
            return kind == InkAnimationKind.Brush ? Brush : Spread;
        }

        /// <summary>
        /// 构建「水墨晕染」分镜：动画类型 Spread / 总时长 2.4s / 缓动 ease-out /
        /// 墨色 3 层 / 混合 multiply / 最大模糊 24px / 触发场景 章节切换。
        /// </summary>
        private static InkAnimationSpec BuildSpread()
        {
            var spec = new InkAnimationSpec
            {
                Kind = InkAnimationKind.Spread,
                Number = "01",
                Title = "水墨晕染",
                Subtitle = "INK SPREAD · 分镜参考 01/02",
                TimelineTitle = "动画时间轴 · TIMELINE",
                Duration = 2.4f,
            };

            spec.Tags.Add("晕染");
            spec.Tags.Add("分镜");
            spec.Tags.Add("视觉参考");

            spec.Parameters.Add(new InkAnimationParam("动画类型", "Spread"));
            spec.Parameters.Add(new InkAnimationParam("总时长", "2.4s"));
            spec.Parameters.Add(new InkAnimationParam("缓动函数", "ease-out"));
            spec.Parameters.Add(new InkAnimationParam("墨色层级", "3 层"));
            spec.Parameters.Add(new InkAnimationParam("混合模式", "multiply"));
            spec.Parameters.Add(new InkAnimationParam("最大模糊", "24px"));
            spec.Parameters.Add(new InkAnimationParam("触发场景", "章节切换"));

            spec.Annotations.Add(new InkAnimationAnnotation
            {
                Number = "1", Title = "墨点初现", X = 0.15f, Y = 0.28f, AnchorRight = false,
                Description = "中心浓墨点骤然浮现，半径由 0 扩散至 40px，透明度 0.9 渐入，持续 0.4s",
            });
            spec.Annotations.Add(new InkAnimationAnnotation
            {
                Number = "2", Title = "晕染扩散", X = 0.42f, Y = 0.45f, AnchorRight = false,
                Description = "墨色以不规则边缘向外晕开，blur(8px) → blur(24px) 渐变，模拟宣纸吸墨效果，持续 0.8s",
            });
            spec.Annotations.Add(new InkAnimationAnnotation
            {
                Number = "3", Title = "淡墨层叠", X = 0.30f, Y = 0.22f, AnchorRight = true,
                Description = "第二层淡墨叠加，透明度 0.3，混合模式 multiply，边缘呈现飞白质感，持续 0.6s",
            });

            spec.Phases.Add(new InkAnimationPhase
            {
                TimeRange = "0.0s — 0.4s", Name = "墨点初现",
                Description = "浓墨骤现 · opacity 0→0.9", Start = 0.0f, End = 0.4f,
            });
            spec.Phases.Add(new InkAnimationPhase
            {
                TimeRange = "0.4s — 1.2s", Name = "晕染扩散",
                Description = "blur 8→24px · 边缘不规则", Start = 0.4f, End = 1.2f,
            });
            spec.Phases.Add(new InkAnimationPhase
            {
                TimeRange = "1.2s — 1.8s", Name = "淡墨层叠",
                Description = "二次叠加 · multiply 混合", Start = 1.2f, End = 1.8f,
            });
            spec.Phases.Add(new InkAnimationPhase
            {
                TimeRange = "1.8s — 2.4s", Name = "渐隐消散",
                Description = "opacity 0.9→0 · 收束", Start = 1.8f, End = 2.4f,
            });

            return spec;
        }

        /// <summary>
        /// 构建「水墨笔触」分镜：动画类型 Brush / 总时长 2.0s / 路径 Bezier /
        /// 笔触 4 段 / 宽度 2-6px / 飞白纹理 noise / 触发场景 技能释放。
        /// </summary>
        private static InkAnimationSpec BuildBrush()
        {
            var spec = new InkAnimationSpec
            {
                Kind = InkAnimationKind.Brush,
                Number = "02",
                Title = "水墨笔触",
                Subtitle = "INK BRUSH STROKE · 分镜参考 02/02",
                TimelineTitle = "笔触时间轴 · BRUSH TIMELINE",
                Duration = 2.0f,
            };

            spec.Tags.Add("笔触");
            spec.Tags.Add("分镜");
            spec.Tags.Add("视觉参考");

            spec.Parameters.Add(new InkAnimationParam("动画类型", "Brush"));
            spec.Parameters.Add(new InkAnimationParam("总时长", "2.0s"));
            spec.Parameters.Add(new InkAnimationParam("路径方式", "Bezier"));
            spec.Parameters.Add(new InkAnimationParam("笔触段数", "4 段"));
            spec.Parameters.Add(new InkAnimationParam("宽度变化", "2-6px"));
            spec.Parameters.Add(new InkAnimationParam("飞白纹理", "noise"));
            spec.Parameters.Add(new InkAnimationParam("触发场景", "技能释放"));

            spec.Annotations.Add(new InkAnimationAnnotation
            {
                Number = "1", Title = "落笔 · 蓄势", X = 0.12f, Y = 0.30f, AnchorRight = false,
                Description = "笔锋触纸瞬间，浓墨凝聚一点，scale 0→1.2 回弹，持续 0.3s。模拟笔尖压纸的力度感",
            });
            spec.Annotations.Add(new InkAnimationAnnotation
            {
                Number = "2", Title = "行笔 · 游走", X = 0.38f, Y = 0.48f, AnchorRight = false,
                Description = "笔触沿贝塞尔曲线游走，stroke-dashoffset 动画展开路径，宽度随速度变化 2px→6px→3px，持续 0.9s",
            });
            spec.Annotations.Add(new InkAnimationAnnotation
            {
                Number = "3", Title = "飞白 · 枯笔", X = 0.32f, Y = 0.25f, AnchorRight = true,
                Description = "行笔末段速度加快，墨色渐枯，以 filter:contrast() + noise texture 模拟飞白效果，持续 0.5s",
            });
            spec.Annotations.Add(new InkAnimationAnnotation
            {
                Number = "4", Title = "收笔 · 回锋", X = 0.12f, Y = 0.55f, AnchorRight = true,
                Description = "笔锋回转收束，末点 opacity 渐隐 0.8→0，半径缩小至 2px，持续 0.3s。模拟回锋藏锋之韵",
            });

            spec.Phases.Add(new InkAnimationPhase
            {
                TimeRange = "0.0s — 0.3s", Name = "落笔蓄势",
                Description = "浓墨凝聚 · scale 0→1.2", Start = 0.0f, End = 0.3f,
            });
            spec.Phases.Add(new InkAnimationPhase
            {
                TimeRange = "0.3s — 1.2s", Name = "行笔游走",
                Description = "Bezier 路径 · 宽度 2→6→3px", Start = 0.3f, End = 1.2f,
            });
            spec.Phases.Add(new InkAnimationPhase
            {
                TimeRange = "1.2s — 1.7s", Name = "飞白枯笔",
                Description = "contrast + noise · 速度加快", Start = 1.2f, End = 1.7f,
            });
            spec.Phases.Add(new InkAnimationPhase
            {
                TimeRange = "1.7s — 2.0s", Name = "收笔回锋",
                Description = "opacity 0.8→0 · 半径收束", Start = 1.7f, End = 2.0f,
            });

            return spec;
        }
    }
}
