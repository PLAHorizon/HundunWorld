using System;
using System.Collections.Generic;
using FlaxEngine;
using FlaxEngine.GUI;

namespace PetEditor
{
    /// <summary>宠物编辑器的配色（与任务/怪物编辑器同一族深色，保证几个窗口并排看不刺眼）。</summary>
    internal static class PetColors
    {
        public static readonly Color Canvas = new Color(0.09f, 0.095f, 0.105f);
        public static readonly Color Grid = new Color(0.14f, 0.15f, 0.16f);
        public static readonly Color Card = new Color(0.15f, 0.16f, 0.175f);
        public static readonly Color Border = new Color(0.24f, 0.25f, 0.27f);
        public static readonly Color Text = new Color(0.90f, 0.91f, 0.93f);
        public static readonly Color TextDim = new Color(0.62f, 0.64f, 0.67f);
        public static readonly Color Edge = new Color(0.42f, 0.46f, 0.50f);
        public static readonly Color Selection = new Color(0.96f, 0.78f, 0.38f);
        public static readonly Color Danger = new Color(0.86f, 0.32f, 0.28f);
        public static readonly Color Ok = new Color(0.36f, 0.72f, 0.48f);
        public static readonly Color Info = new Color(0.36f, 0.68f, 0.92f);
        public static readonly Color MissingNote = new Color(0.95f, 0.75f, 0.30f);
        public static readonly Color SlotInnate = new Color(0.42f, 0.62f, 0.90f);
        public static readonly Color SlotLocked = new Color(0.92f, 0.72f, 0.30f);
        public static readonly Color SlotEpiphany = new Color(0.62f, 0.44f, 0.86f);
    }

    /// <summary>
    /// 通用线段/圆绘制助手：用一串小方块近似线段。
    /// <para>不用 <c>Render2D.DrawLine</c> 是因为它的重载签名在本工程里没有既有用法可依据；
    /// 近似写法代价可忽略，好处是"只用已在工程里编译验证过的 API"。</para>
    /// </summary>
    internal static class PetViewLines
    {
        public static void DrawSegment(Float2 a, Float2 b, Color color)
        {
            var dx = b.X - a.X;
            var dy = b.Y - a.Y;
            var len = (float)Math.Sqrt(dx * dx + dy * dy);
            if (len < 0.5f)
            {
                Render2D.FillRectangle(new Rectangle(a.X, a.Y, 2f, 2f), color);
                return;
            }
            var steps = Math.Min(64, Math.Max(2, (int)(len / 3f)));
            for (var i = 0; i <= steps; i++)
            {
                var t = i / (float)steps;
                Render2D.FillRectangle(new Rectangle(a.X + dx * t - 1f, a.Y + dy * t - 1f, 2f, 2f), color);
            }
        }

        public static void DrawCircleOutline(float cx, float cy, float radius, Color color, int segments)
        {
            if (radius <= 0f) return;
            if (segments < 8) segments = 8;
            var stepAngle = Math.PI * 2.0 / segments;
            for (var i = 0; i < segments; i++)
            {
                var a0 = i * stepAngle;
                var a1 = (i + 1) * stepAngle;
                var p0 = new Float2(cx + (float)Math.Cos(a0) * radius, cy + (float)Math.Sin(a0) * radius);
                var p1 = new Float2(cx + (float)Math.Cos(a1) * radius, cy + (float)Math.Sin(a1) * radius);
                DrawSegment(p0, p1, color);
            }
        }
    }

    /// <summary>一格技能槽的显示数据。</summary>
    public struct PetSkillSlotView
    {
        /// <summary>技能编号；0 = 空格。</summary>
        public int SkillId;

        /// <summary>0空 1天生 2打书 3领悟 4锁定。</summary>
        public int Kind;

        /// <summary>该格是否已开放（未开放画成虚位）。</summary>
        public bool Opened;
    }

    /// <summary>
    /// 技能格矩阵：把"格子里现在是什么"画成一排格子，天生/锁定/领悟/打书四种形态四色。
    /// <para>这个视图存在的意义是把<b>顶书的后果</b>变成看得见的东西：策划调"顶替概率"时，
    /// 需要一眼看出"我这一本书会把哪个格子顶掉"，而不是一行 outcome 文本。</para>
    /// </summary>
    public sealed class SkillSlotView : Control
    {
        private readonly List<PetSkillSlotView> _slots = new List<PetSkillSlotView>();
        private int _openCap;
        private int _maxCap;
        private int _selected = -1;
        private string _outcome = string.Empty;
        private string _note = string.Empty;

        public SkillSlotView()
        {
            AnchorPreset = AnchorPresets.TopLeft;
        }

        public void SetData(PetSkillSlotView[] slots, int openCap, int maxCap, string outcome, string note)
        {
            _slots.Clear();
            if (slots != null) _slots.AddRange(slots);
            _openCap = openCap;
            _maxCap = maxCap;
            _outcome = outcome ?? string.Empty;
            _note = note ?? string.Empty;
        }

        public override bool OnMouseDown(Float2 location, MouseButton button)
        {
            var cell = CellIndexAt(location);
            if (cell >= 0)
            {
                _selected = cell;
                return true;
            }
            return base.OnMouseDown(location, button);
        }

        private int CellIndexAt(Float2 location)
        {
            var area = CellArea();
            if (_slots.Count == 0 || area.Width <= 0f) return -1;
            var w = area.Width / _slots.Count;
            if (w < 8f) return -1;
            if (location.Y < area.Y || location.Y > area.Bottom) return -1;
            if (location.X < area.X || location.X > area.Right) return -1;
            var idx = (int)((location.X - area.X) / w);
            return idx >= 0 && idx < _slots.Count ? idx : -1;
        }

        private Rectangle CellArea()
        {
            return new Rectangle(10f, 40f, Math.Max(0f, Width - 20f), Math.Max(0f, Height - 76f));
        }

        public override void Draw()
        {
            var bounds = Bounds;
            var font = Style.Current.FontSmall;
            Render2D.FillRectangle(bounds, PetColors.Canvas);
            Render2D.DrawRectangle(bounds, PetColors.Border);

            var top = string.Format("技能格：开放 {0}／上限 {1}｜{2}", _openCap, _maxCap,
                string.IsNullOrEmpty(_outcome) ? "未试算" : _outcome);
            Render2D.DrawText(Style.Current.FontMedium, top, new Rectangle(10f, 8f, Math.Max(0f, Width - 20f), 18f),
                PetColors.Text);

            var area = CellArea();
            if (_slots.Count == 0 || area.Width <= 0f)
            {
                Render2D.DrawText(font, "（还没有槽表——先读取草稿）",
                    new Rectangle(10f, area.Y + 10f, Math.Max(0f, Width - 20f), 16f), PetColors.TextDim);
                return;
            }

            var w = area.Width / _slots.Count;
            for (var i = 0; i < _slots.Count; i++)
            {
                var slot = _slots[i];
                var x = area.X + i * w;
                var box = new Rectangle(x + 2f, area.Y + 4f, w - 6f, area.Height - 10f);
                var color = SlotColor(slot, i >= _openCap);
                Render2D.FillRectangle(box, color);
                Render2D.DrawRectangle(box, i == _selected ? PetColors.Selection : PetColors.Border);

                var label = slot.SkillId == 0 ? "空" : slot.SkillId.ToString();
                Render2D.DrawText(font, label, new Rectangle(box.X + 2f, box.Y + box.Height * 0.35f,
                    Math.Max(0f, box.Width - 4f), 16f), PetColors.Text);
                Render2D.DrawText(font, KindLabel(slot.Kind), new Rectangle(box.X + 2f, box.Y + 2f,
                    Math.Max(0f, box.Width - 4f), 14f), PetColors.TextDim);
                Render2D.DrawText(font, (i + 1).ToString(), new Rectangle(box.X + 2f, box.Bottom - 16f,
                    Math.Max(0f, box.Width - 4f), 14f), PetColors.TextDim);
            }

            var bottom = string.IsNullOrEmpty(_note) ? "左键点格可高亮；未开放的格子是虚位" : _note;
            Render2D.DrawText(font, bottom, new Rectangle(10f, area.Bottom + 4f, Math.Max(0f, Width - 20f), 16f),
                PetColors.MissingNote);
        }

        private static Color SlotColor(PetSkillSlotView slot, bool unopened)
        {
            if (unopened) return new Color(PetColors.Card.R, PetColors.Card.G, PetColors.Card.B, 0.45f);
            if (slot.SkillId == 0) return PetColors.Grid;
            switch (slot.Kind)
            {
                case 1: return PetColors.SlotInnate;
                case 3: return PetColors.SlotEpiphany;
                case 4: return PetColors.SlotLocked;
                default: return PetColors.Ok;
            }
        }

        private static string KindLabel(int kind)
        {
            switch (kind)
            {
                case 1: return "天生";
                case 2: return "打书";
                case 3: return "领悟";
                case 4: return "锁定";
                default: return "—";
            }
        }
    }

    /// <summary>
    /// 成长曲线视图：横轴等级，纵轴战斗属性（气血/攻击/防御三条）。
    /// <para>数值全部走 <see cref="PetPreviewMath"/>，与内核公式逐字同序；
    /// 点图放读数游标，游标读数与 CLI 的 <c>preview-stats --sweep</c> 必须一致（窗口里会交叉核对）。</para>
    /// </summary>
    public sealed class GrowthCurveView : Control
    {
        private int _level = 50;
        private int _growthBps = 11_000;
        private int _atkQ = 1200, _defQ = 1200, _staQ = 1200, _manaQ = 1200, _spdQ = 1200;
        private int _str, _end, _con, _spi, _agi;
        private int _hoverLevel = -1;
        private string _note = string.Empty;

        public GrowthCurveView()
        {
            AnchorPreset = AnchorPresets.TopLeft;
        }

        public void SetData(int level, int growthBps, int atkQuality, int defQuality, int staQuality,
            int manaQuality, int spdQuality, int strength, int endurance, int constitution, int spirit,
            int agility, string note)
        {
            _level = level;
            _growthBps = growthBps;
            _atkQ = atkQuality;
            _defQ = defQuality;
            _staQ = staQuality;
            _manaQ = manaQuality;
            _spdQ = spdQuality;
            _str = strength;
            _end = endurance;
            _con = constitution;
            _spi = spirit;
            _agi = agility;
            _note = note ?? string.Empty;
        }

        public override bool OnMouseDown(Float2 location, MouseButton button)
        {
            var plot = PlotBounds();
            if (plot.Width > 20f && location.X >= plot.X && location.X <= plot.Right
                && location.Y >= plot.Y && location.Y <= plot.Bottom)
            {
                var t = (location.X - plot.X) / plot.Width;
                _hoverLevel = (int)Math.Round(1 + t * (PetPreviewMath.MaxPetLevel - 1));
                return true;
            }
            return base.OnMouseDown(location, button);
        }

        private Rectangle PlotBounds()
        {
            return new Rectangle(52f, 26f, Math.Max(0f, Width - 66f), Math.Max(0f, Height - 70f));
        }

        public override void Draw()
        {
            var bounds = Bounds;
            var font = Style.Current.FontSmall;
            Render2D.FillRectangle(bounds, PetColors.Canvas);
            Render2D.DrawRectangle(bounds, PetColors.Border);

            var plot = PlotBounds();

            // 逐等级算出三条序列；上限取满级的气血，三条线共用一个纵轴便于比较形状
            var maxLevel = PetPreviewMath.MaxPetLevel;
            var hp = new float[maxLevel + 1];
            var atk = new float[maxLevel + 1];
            var def = new float[maxLevel + 1];
            var peak = 1f;
            for (var lv = 1; lv <= maxLevel; lv++)
            {
                var s = PetPreviewMath.ComputeStats(_atkQ, _defQ, _staQ, _manaQ, _spdQ, lv, _growthBps,
                    _str, _end, _con, _spi, _agi);
                hp[lv] = s.Hp;
                atk[lv] = s.Attack;
                def[lv] = s.Defense;
                if (s.Hp > peak) peak = s.Hp;
                if (s.Attack > peak) peak = s.Attack;
                if (s.Defense > peak) peak = s.Defense;
            }

            // 网格
            for (var g = 0; g <= 4; g++)
            {
                var y = plot.Y + plot.Height * g / 4f;
                Render2D.FillRectangle(new Rectangle(plot.X, y, plot.Width, 1f), PetColors.Grid);
                Render2D.DrawText(font, ((peak * (4 - g) / 4f)).ToString("0"),
                    new Rectangle(2f, y - 8f, 46f, 16f), PetColors.TextDim);
            }
            for (var lv = 10; lv <= maxLevel; lv += 25)
            {
                var x = plot.X + plot.Width * (lv - 1) / (maxLevel - 1);
                Render2D.FillRectangle(new Rectangle(x, plot.Y, 1f, plot.Height), PetColors.Grid);
                Render2D.DrawText(font, lv.ToString(), new Rectangle(x - 12f, plot.Bottom + 2f, 30f, 14f),
                    PetColors.TextDim);
            }

            DrawSeries(hp, plot, peak, PetColors.Info);
            DrawSeries(atk, plot, peak, PetColors.Ok);
            DrawSeries(def, plot, peak, PetColors.Selection);

            if (_hoverLevel >= 1 && _hoverLevel <= maxLevel)
            {
                var x = plot.X + plot.Width * (_hoverLevel - 1) / (maxLevel - 1);
                Render2D.FillRectangle(new Rectangle(x, plot.Y, 1f, plot.Height), PetColors.Edge);
                var s = PetPreviewMath.ComputeStats(_atkQ, _defQ, _staQ, _manaQ, _spdQ, _hoverLevel, _growthBps,
                    _str, _end, _con, _spi, _agi);
                var readout = string.Format("Lv{0}：气血 {1}｜攻击 {2}｜防御 {3}｜速度 {4}｜灵力 {5}",
                    _hoverLevel, s.Hp, s.Attack, s.Defense, s.Speed, s.SpiritPower);
                Render2D.DrawText(font, readout,
                    new Rectangle(6f, plot.Bottom + 18f, Math.Max(0f, Width - 12f), 14f), PetColors.Text);
            }
            else
            {
                Render2D.DrawText(font, "点图放读数游标｜蓝=气血 绿=攻击 黄=防御",
                    new Rectangle(6f, plot.Bottom + 18f, Math.Max(0f, Width - 12f), 14f), PetColors.TextDim);
            }

            var title = string.Format("成长曲线（成长 {0}／潜能合计 {1}）",
                (_growthBps / 10000f).ToString("0.00"), PetPreviewMath.PotentialTotal(_level));
            Render2D.DrawText(Style.Current.FontMedium, title,
                new Rectangle(8f, 6f, Math.Max(0f, Width - 16f), 18f), PetColors.Text);

            if (!string.IsNullOrEmpty(_note))
            {
                Render2D.DrawText(font, _note,
                    new Rectangle(6f, Height - 18f, Math.Max(0f, Width - 12f), 14f), PetColors.MissingNote);
            }
        }

        private static void DrawSeries(float[] series, Rectangle plot, float peak, Color color)
        {
            if (plot.Width <= 0f || plot.Height <= 0f) return;
            var maxLevel = PetPreviewMath.MaxPetLevel;
            for (var lv = 1; lv <= maxLevel; lv++)
            {
                var x0 = plot.X + plot.Width * (lv - 1) / (maxLevel - 1);
                var x1 = plot.X + plot.Width * lv / (maxLevel - 1);
                var y0 = plot.Bottom - plot.Height * (series[lv - 1] / peak);
                var y1 = plot.Bottom - plot.Height * (series[lv] / peak);
                PetViewLines.DrawSegment(new Float2(x0, y0), new Float2(x1, y1), color);
            }
        }
    }

    /// <summary>
    /// 资质雷达：五维各一根轴，内圈是裸资、外圈是加了悟性/灵性/变异之后的有效资质。
    /// <para>两张圈叠着画是为了让"提悟性到底带来多少"这件事可看：梦幻/天龙玩家判断一只宠，
    /// 看的从来不是裸资本身而是"养到顶之后能到多少"。</para>
    /// </summary>
    public sealed class StatRadarView : Control
    {
        private static readonly string[] AxisNames = { "攻", "防", "体", "法", "速" };

        private readonly int[] _raw = new int[5];
        private readonly int[] _effective = new int[5];
        private int _comprehension;
        private int _spirit;
        private bool _superSpirit;
        private int _mutationCapBps = 10_000;
        private string _note = string.Empty;

        public StatRadarView()
        {
            AnchorPreset = AnchorPresets.TopLeft;
            for (var i = 0; i < 5; i++)
            {
                _raw[i] = 1000;
                _effective[i] = 1000;
            }
        }

        public void SetData(int attack, int defense, int stamina, int mana, int speed,
            int comprehension, int spirit, bool superSpirit, int mutationCapBps, string note)
        {
            _raw[0] = attack;
            _raw[1] = defense;
            _raw[2] = stamina;
            _raw[3] = mana;
            _raw[4] = speed;
            _comprehension = comprehension;
            _spirit = spirit;
            _superSpirit = superSpirit;
            _mutationCapBps = mutationCapBps;
            _note = note ?? string.Empty;

            for (var i = 0; i < 5; i++)
            {
                _effective[i] = PetPreviewMath.EffectiveQuality(_raw[i], comprehension, spirit,
                    superSpirit, mutationCapBps);
            }
        }

        public override void Draw()
        {
            var bounds = Bounds;
            Render2D.FillRectangle(bounds, PetColors.Canvas);
            Render2D.DrawRectangle(bounds, PetColors.Border);

            var font = Style.Current.FontSmall;
            var cx = bounds.X + bounds.Width * 0.5f;
            var cy = bounds.Y + bounds.Height * 0.55f;
            var radius = Math.Max(20f, Math.Min(bounds.Width * 0.30f, bounds.Height * 0.32f));

            // 刻度环：上限取"有效资格里最大的那个"再留 10%，否则满悟性的圈会画到框外
            var peak = 1;
            for (var i = 0; i < 5; i++)
            {
                if (_effective[i] > peak) peak = _effective[i];
                if (_raw[i] > peak) peak = _raw[i];
            }
            var scale = peak * 1.1f;

            for (var g = 1; g <= 4; g++)
            {
                PetViewLines.DrawCircleOutline(cx, cy, radius * g / 4f, PetColors.Grid, 40);
            }

            var angles = new float[5];
            for (var i = 0; i < 5; i++)
            {
                angles[i] = (float)(-Math.PI * 0.5 + Math.PI * 2.0 * i / 5.0);
                PetViewLines.DrawSegment(new Float2(cx, cy),
                    new Float2(cx + (float)Math.Cos(angles[i]) * radius, cy + (float)Math.Sin(angles[i]) * radius),
                    PetColors.Edge);

                var lx = cx + (float)Math.Cos(angles[i]) * (radius + 14f) - 8f;
                var ly = cy + (float)Math.Sin(angles[i]) * (radius + 14f) - 8f;
                Render2D.DrawText(font, AxisNames[i], new Rectangle(lx, ly, 26f, 14f), PetColors.Text);
            }

            DrawPolygon(cx, cy, angles, _raw, radius, scale, PetColors.Info);
            DrawPolygon(cx, cy, angles, _effective, radius, scale, PetColors.Selection);

            var title = string.Format("资质雷达｜悟性 {0} 灵性 {1}{2}｜变异上限 ×{3}",
                _comprehension, _spirit, _superSpirit ? "（超灵）" : string.Empty,
                (_mutationCapBps / 10000f).ToString("0.00"));
            Render2D.DrawText(Style.Current.FontMedium, title,
                new Rectangle(8f, 6f, Math.Max(0f, bounds.Width - 16f), 18f), PetColors.Text);

            var legend = string.Format("蓝=裸资 {0}/{1}/{2}/{3}/{4}｜黄=有效 {5}/{6}/{7}/{8}/{9}",
                _raw[0], _raw[1], _raw[2], _raw[3], _raw[4],
                _effective[0], _effective[1], _effective[2], _effective[3], _effective[4]);
            Render2D.DrawText(font, legend,
                new Rectangle(8f, bounds.Bottom - 34f, Math.Max(0f, bounds.Width - 16f), 14f), PetColors.TextDim);

            if (!string.IsNullOrEmpty(_note))
            {
                Render2D.DrawText(font, _note,
                    new Rectangle(8f, bounds.Bottom - 18f, Math.Max(0f, bounds.Width - 16f), 14f),
                    PetColors.MissingNote);
            }
        }

        private static void DrawPolygon(float cx, float cy, float[] angles, int[] values, float radius,
            float scale, Color color)
        {
            var count = angles.Length;
            for (var i = 0; i < count; i++)
            {
                var j = (i + 1) % count;
                var r0 = radius * Math.Min(1f, values[i] / scale);
                var r1 = radius * Math.Min(1f, values[j] / scale);
                var p0 = new Float2(cx + (float)Math.Cos(angles[i]) * r0, cy + (float)Math.Sin(angles[i]) * r0);
                var p1 = new Float2(cx + (float)Math.Cos(angles[j]) * r1, cy + (float)Math.Sin(angles[j]) * r1);
                PetViewLines.DrawSegment(p0, p1, color);
            }
        }
    }

    /// <summary>
    /// 跟随半径视图：主人固定在中心，画出跟随距离 / leash / 硬传送三个圈，宠物点按给定坐标落位。
    /// <para>这三个圈就是"宠物在玩家眼里的活动边界"。调 leash 与传送半径时，
    /// 光看数字判断不了"会不会看起来像被抛弃"，看图可以。</para>
    /// </summary>
    public sealed class FollowRadiusView : Control
    {
        private float _followDistance = 3f;
        private float _leashRadius = 12f;
        private float _teleportBeyond = 40f;
        private float _petX = 6f;
        private float _petY = 0f;
        private int _state;
        private string _note = string.Empty;

        public FollowRadiusView()
        {
            AnchorPreset = AnchorPresets.TopLeft;
        }

        /// <summary>状态码：0跟随 1交战 2待命 3归位 4已传送 5尸体 6复活中。</summary>
        public void SetData(float followDistance, float leashRadius, float teleportBeyond,
            float petX, float petY, int state, string note)
        {
            _followDistance = followDistance;
            _leashRadius = leashRadius;
            _teleportBeyond = teleportBeyond;
            _petX = petX;
            _petY = petY;
            _state = state;
            _note = note ?? string.Empty;
        }

        public override void Draw()
        {
            var bounds = Bounds;
            var font = Style.Current.FontSmall;
            Render2D.FillRectangle(bounds, PetColors.Canvas);
            Render2D.DrawRectangle(bounds, PetColors.Border);

            var cx = bounds.X + bounds.Width * 0.5f;
            var cy = bounds.Y + bounds.Height * 0.52f;

            // 米 → 像素的比例：以最大圈（传送半径）刚好画进框为准
            var maxRadius = Math.Max(_teleportBeyond, Math.Max(_leashRadius, _followDistance));
            if (maxRadius < 1f) maxRadius = 1f;
            var px = Math.Max(8f, Math.Min(bounds.Width, bounds.Height) * 0.42f) / maxRadius;

            PetViewLines.DrawCircleOutline(cx, cy, _followDistance * px, PetColors.Ok, 48);
            PetViewLines.DrawCircleOutline(cx, cy, _leashRadius * px, PetColors.Info, 60);
            PetViewLines.DrawCircleOutline(cx, cy, _teleportBeyond * px, PetColors.Danger, 72);

            // 主人
            Render2D.FillRectangle(new Rectangle(cx - 3f, cy - 3f, 6f, 6f), PetColors.Selection);
            Render2D.DrawText(font, "主", new Rectangle(cx - 6f, cy - 20f, 20f, 14f), PetColors.Selection);

            // 宠物：按真实相对位置落位（这里只画平面，Y/Z 换轴由服务端与客户端各自负责）
            var dotX = cx + _petX * px;
            var dotY = cy + _petY * px;
            var dotColor = _state == 4 ? PetColors.Danger : PetColors.Text;
            Render2D.FillRectangle(new Rectangle(dotX - 3f, dotY - 3f, 6f, 6f), dotColor);
            Render2D.DrawText(font, StateLabel(_state), new Rectangle(dotX + 6f, dotY - 8f, 90f, 14f), dotColor);

            var title = string.Format("跟随半径｜跟随 {0:0.#}m｜leash {1:0.#}m｜传送 >{2:0.#}m",
                _followDistance, _leashRadius, _teleportBeyond);
            Render2D.DrawText(Style.Current.FontMedium, title,
                new Rectangle(8f, 6f, Math.Max(0f, bounds.Width - 16f), 18f), PetColors.Text);

            Render2D.DrawText(font, "绿=理想跟随圈｜蓝=超出即归位｜红=超出直接传送到主人身边",
                new Rectangle(8f, bounds.Bottom - 34f, Math.Max(0f, bounds.Width - 16f), 14f), PetColors.TextDim);

            if (!string.IsNullOrEmpty(_note))
            {
                Render2D.DrawText(font, _note,
                    new Rectangle(8f, bounds.Bottom - 18f, Math.Max(0f, bounds.Width - 16f), 14f),
                    PetColors.MissingNote);
            }
        }

        private static string StateLabel(int state)
        {
            switch (state)
            {
                case 0: return "跟随";
                case 1: return "交战";
                case 2: return "待命";
                case 3: return "归位中";
                case 4: return "已传送";
                case 5: return "尸体";
                case 6: return "复活中";
                default: return "—";
            }
        }
    }

    /// <summary>
    /// 谱系视图：两个亲本框 → 产物框，标出产数、领取时限与继承技能数。
    /// <para>炼妖与繁殖在数据上是同一族操作，但玩家看到的后果完全不同
    /// （一个吃材料立刻出、一个留父母等 48 小时领）。把两者画在同一张图上是为了让这个区别可见。</para>
    /// </summary>
    public sealed class PedigreeView : Control
    {
        private string _modeLabel = string.Empty;
        private string _parentA = string.Empty;
        private string _parentB = string.Empty;
        private string _offspring = string.Empty;
        private int _offspringCount;
        private int _inheritedCount;
        private int _claimSeconds;
        private bool _parentsConsumed;
        private string _note = string.Empty;

        public PedigreeView()
        {
            AnchorPreset = AnchorPresets.TopLeft;
        }

        public void SetData(string modeLabel, string parentA, string parentB, string offspring,
            int offspringCount, int inheritedCount, int claimSeconds, bool parentsConsumed, string note)
        {
            _modeLabel = modeLabel ?? string.Empty;
            _parentA = parentA ?? string.Empty;
            _parentB = parentB ?? string.Empty;
            _offspring = offspring ?? string.Empty;
            _offspringCount = offspringCount;
            _inheritedCount = inheritedCount;
            _claimSeconds = claimSeconds;
            _parentsConsumed = parentsConsumed;
            _note = note ?? string.Empty;
        }

        public override void Draw()
        {
            var bounds = Bounds;
            var font = Style.Current.FontSmall;
            Render2D.FillRectangle(bounds, PetColors.Canvas);
            Render2D.DrawRectangle(bounds, PetColors.Border);

            var title = string.IsNullOrEmpty(_modeLabel) ? "谱系（先做一次合成/繁殖试算）" : _modeLabel;
            Render2D.DrawText(Style.Current.FontMedium, title,
                new Rectangle(8f, 6f, Math.Max(0f, bounds.Width - 16f), 18f), PetColors.Text);

            var boxW = Math.Max(90f, (bounds.Width - 60f) / 3f);
            var boxH = Math.Max(46f, bounds.Height * 0.34f);
            var top = bounds.Y + 30f;
            var midY = top + boxH * 0.5f;

            var a = new Rectangle(bounds.X + 14f, top, boxW, boxH);
            var b = new Rectangle(bounds.X + 14f, bounds.Bottom - 34f - boxH, boxW, boxH);
            var child = new Rectangle(bounds.Right - 14f - boxW, midY - boxH * 0.5f, boxW, boxH);

            DrawBox(a, _parentA, PetColors.Card);
            DrawBox(b, _parentB, PetColors.Card);
            DrawBox(child, _offspring, _offspringCount > 0 ? PetColors.Ok : PetColors.Danger);

            var aCenter = new Float2(a.Right, a.Y + a.Height * 0.5f);
            var bCenter = new Float2(b.Right, b.Y + b.Height * 0.5f);
            var cCenter = new Float2(child.X, child.Y + child.Height * 0.5f);
            PetViewLines.DrawSegment(aCenter, cCenter, PetColors.Edge);
            PetViewLines.DrawSegment(bCenter, cCenter, PetColors.Edge);

            var summary = string.Format("产 {0} 只｜继承技能 {1} 个｜领取 {2}｜{3}",
                _offspringCount, _inheritedCount, ClaimText(_claimSeconds),
                _parentsConsumed ? "亲本已被消耗" : "亲本保留");
            Render2D.DrawText(font, summary,
                new Rectangle(8f, bounds.Bottom - 20f, Math.Max(0f, bounds.Width - 16f), 14f), PetColors.TextDim);

            if (!string.IsNullOrEmpty(_note))
            {
                Render2D.DrawText(font, _note,
                    new Rectangle(child.X - boxW * 0.1f, child.Bottom + 2f,
                        Math.Max(0f, bounds.Width - child.X), 14f), PetColors.MissingNote);
            }
        }

        private static string ClaimText(int seconds)
        {
            if (seconds <= 0) return "即时";
            var hours = seconds / 3600.0;
            return hours >= 1 ? hours.ToString("0.0") + " 小时" : seconds + " 秒";
        }

        private static void DrawBox(Rectangle r, string text, Color fill)
        {
            Render2D.FillRectangle(r, fill);
            Render2D.DrawRectangle(r, PetColors.Border);
            Render2D.DrawText(Style.Current.FontSmall, text,
                new Rectangle(r.X + 4f, r.Y + 4f, Math.Max(0f, r.Width - 8f), Math.Max(0f, r.Height - 8f)),
                PetColors.Text);
        }
    }
}
