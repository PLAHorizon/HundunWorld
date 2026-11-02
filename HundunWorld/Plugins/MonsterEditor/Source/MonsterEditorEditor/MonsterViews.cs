using System;
using System.Collections.Generic;
using FlaxEngine;
using FlaxEngine.GUI;

namespace MonsterEditor
{
    /// <summary>怪物编辑器的配色（与任务编辑器同一族深色，保证两个窗口并排看不刺眼）。</summary>
    internal static class MonsterColors
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
    }

    /// <summary>
    /// 通用线段/圆绘制助手：用一串小方块近似线段。
    /// <para>不用 <c>Render2D.DrawLine</c> 是因为它的重载签名在本工程里没有既有用法可依据；
    /// 近似写法代价可忽略，好处是"只用已在工程里编译验证过的 API"。</para>
    /// </summary>
    internal static class MonsterViewLines
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
            var steps = Math.Min(24, Math.Max(2, (int)(len / 3f)));
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

    /// <summary>
    /// 经验衰减曲线视图：横轴"怪等级 − 我等级"，纵轴"相对同级基准的倍率"，背景按难度色分带。
    /// <para>这就是目标里"随玩家等级变化反比例衰减"的直接可视化。分带颜色与经验数字共用
    /// <see cref="MonsterPreviewMath"/> 的同一个判据——否则会出现"名字灰了却还有经验"。</para>
    /// </summary>
    public sealed class XpCurveView : Control
    {
        private int _monsterLevel = 20;
        private int _playerLevel = 20;
        private int _rank;
        private int _xpBase = 45;
        private int _xpPerLevel = 5;
        private int _xpModBps = 10000;
        private int _healthModBps = 10000;
        private string _note = string.Empty;
        private int _hoverDiff = int.MinValue;

        private const int DiffMin = -30;
        private const int DiffMax = 15;
        private const float YMin = -0.05f;
        private const float YMax = 1.30f;

        public XpCurveView()
        {
            // 构造期不设 Location：非 TopLeft 锚点会在父尺寸 0 时被重算到看不见的地方。
            AnchorPreset = AnchorPresets.TopLeft;
        }

        public void SetData(int monsterLevel, int playerLevel, int rank, int xpBase, int xpPerLevel, string note,
                            int xpModBps = 10000, int healthModBps = 10000)
        {
            _monsterLevel = monsterLevel;
            _playerLevel = playerLevel;
            _rank = rank;
            _xpBase = xpBase;
            _xpPerLevel = xpPerLevel;
            _xpModBps = xpModBps;
            _healthModBps = healthModBps;
            _note = note ?? string.Empty;
        }

        public override bool OnMouseDown(Float2 location, MouseButton button)
        {
            // 点曲线区 = 挪"我等级"锚点的读数游标：把"换个号看看"做成一个动作。
            var plot = PlotBounds();
            if (plot.Width > 20f && location.X >= plot.X && location.X <= plot.Right
                && location.Y >= plot.Y && location.Y <= plot.Bottom)
            {
                var t = (location.X - plot.X) / plot.Width;
                _hoverDiff = (int)Math.Round(DiffMin + t * (DiffMax - DiffMin));
                return true;
            }
            return base.OnMouseDown(location, button);
        }

        private Rectangle PlotBounds()
        {
            return new Rectangle(44f, 22f, Math.Max(0f, Width - 60f), Math.Max(0f, Height - 66f));
        }

        private float XOf(int diff, Rectangle plot)
        {
            return plot.X + (diff - DiffMin) / (float)(DiffMax - DiffMin) * plot.Width;
        }

        private float YOf(float value, Rectangle plot)
        {
            return plot.Bottom - (value - YMin) / (YMax - YMin) * plot.Height;
        }

        /// <summary>
        /// 难度色（曲线分带用）。放在**编辑器模块**而不是 <c>MonsterPreviewMath</c>：
        /// 那个文件是内核的纯数学镜像，离线闸门要能把它编进 .NET 工程逐点真跑，
        /// 而 <c>Color</c> 是引擎类型——一份返回值就能让整份镜像脱离可执行范围。
        /// </summary>
        private static Color DifficultyColor(MonsterPreviewMath.Difficulty d)
        {
            switch (d)
            {
                case MonsterPreviewMath.Difficulty.Gray: return new Color(0.42f, 0.42f, 0.42f);
                case MonsterPreviewMath.Difficulty.Green: return new Color(0.28f, 0.78f, 0.30f);
                case MonsterPreviewMath.Difficulty.Yellow: return new Color(0.92f, 0.82f, 0.22f);
                case MonsterPreviewMath.Difficulty.Orange: return new Color(0.90f, 0.55f, 0.16f);
                case MonsterPreviewMath.Difficulty.Red: return new Color(0.86f, 0.24f, 0.20f);
                default: return new Color(0.78f, 0.20f, 0.62f); // 骷髅/首领：紫红（WoW 里骷髅是"?? "加红框）
            }
        }

        public override void Draw()
        {
            base.Draw();
            Render2D.FillRectangle(new Rectangle(0f, 0f, Width, Height), MonsterColors.Canvas);
            var plot = PlotBounds();
            if (plot.Width < 20f || plot.Height < 20f) return;

            var font = Style.Current.FontSmall;

            // 难度色分带：边界由 MonsterPreviewMath 判出，**不在此重写判据**
            for (var d = DiffMin; d <= DiffMax; d++)
            {
                var x0 = XOf(d, plot);
                var x1 = XOf(d + 1, plot);
                var color = DifficultyColor(MonsterPreviewMath.Of(_playerLevel + d, _playerLevel, _rank));
                Render2D.FillRectangle(new Rectangle(x0, plot.Y, Math.Max(1f, x1 - x0), plot.Height),
                    new Color(color.R, color.G, color.B, 0.16f));
            }

            for (var g = 0; g <= 4; g++)
            {
                var v = YMin + (YMax - YMin) * g / 4f;
                var y = YOf(v, plot);
                Render2D.FillRectangle(new Rectangle(plot.X, y, plot.Width, 1f), MonsterColors.Grid);
                Render2D.DrawText(font, v.ToString("0.00"), new Rectangle(2f, y - 8f, 40f, 16f), MonsterColors.TextDim);
            }
            for (var d = DiffMin; d <= DiffMax; d += 5)
            {
                var x = XOf(d, plot);
                Render2D.FillRectangle(new Rectangle(x, plot.Y, 1f, plot.Height), MonsterColors.Grid);
                Render2D.DrawText(font, (d > 0 ? "+" : string.Empty) + d, new Rectangle(x - 14f, plot.Bottom + 2f, 34f, 14f), MonsterColors.TextDim);
            }
            Render2D.DrawRectangle(plot, MonsterColors.Border, 1f);

            var prevX = float.NaN;
            var prevY = float.NaN;
            for (var d = DiffMin; d <= DiffMax; d++)
            {
                var f = (float)MonsterPreviewMath.Concession(_playerLevel + d, _playerLevel, _rank);
                var x = XOf(d, plot);
                var y = YOf(Mathf.Clamp(f, YMin, YMax), plot);
                if (!float.IsNaN(prevY)) MonsterViewLines.DrawSegment(new Float2(prevX, prevY), new Float2(x, y), MonsterColors.Selection);
                prevX = x;
                prevY = y;
            }

            var anchorDiff = _monsterLevel - _playerLevel;
            if (anchorDiff >= DiffMin && anchorDiff <= DiffMax)
            {
                var ax = XOf(anchorDiff, plot);
                var ay = YOf((float)Mathf.Clamp(MonsterPreviewMath.Concession(_monsterLevel, _playerLevel, _rank), YMin, YMax), plot);
                Render2D.FillRectangle(new Rectangle(ax - 3f, ay - 3f, 7f, 7f), MonsterColors.Ok);
                Render2D.FillRectangle(new Rectangle(ax, plot.Y, 1f, plot.Height), new Color(1f, 1f, 1f, 0.22f));
            }

            var final = MonsterPreviewMath.Xp(_monsterLevel, _playerLevel, _rank, _xpBase, _xpPerLevel, _xpModBps, _healthModBps);
            var difficulty = MonsterPreviewMath.Of(_monsterLevel, _playerLevel, _rank);
            Render2D.DrawText(Style.Current.FontMedium,
                string.Format("我 Lv{0} 打 Lv{1}（{2}）→ {3} 经验｜灰名 ≤ Lv{4}｜ZD={5}",
                    _playerLevel, _monsterLevel, difficulty, final,
                    MonsterPreviewMath.GrayLevel(_playerLevel), MonsterPreviewMath.Zd(_playerLevel)),
                new Rectangle(6f, 2f, Math.Max(0f, Width - 12f), 18f), MonsterColors.Text);

            var bottomText = _note;
            var bottomColor = MonsterColors.TextDim;
            if (_hoverDiff != int.MinValue)
            {
                var ml = _playerLevel + _hoverDiff;
                bottomText = string.Format("差 {0}：Lv{1} → ×{2:0.000} → {3} 经验",
                    _hoverDiff, ml, MonsterPreviewMath.Concession(ml, _playerLevel, _rank),
                    MonsterPreviewMath.Xp(ml, _playerLevel, _rank, _xpBase, _xpPerLevel, _xpModBps, _healthModBps));
                bottomColor = MonsterColors.Selection;
            }
            if (!string.IsNullOrEmpty(bottomText))
                Render2D.DrawText(font, bottomText, new Rectangle(6f, plot.Bottom + 18f, Math.Max(0f, Width - 12f), 14f), bottomColor);
        }
    }

    /// <summary>
    /// 巡逻与半径的俯视网格：索敌圈 / 脱战圈 / 踱步圈 / waypoint 折线 / 刷怪槽位同图。
    /// <para>这些半径**互相之间**的比例关系是纯数字框里看不出来的（leash 小于 aggro 会被校验判成错误，
    /// 但"aggro 10 米、leash 30 米顺不顺眼"必须画出来才知道）。</para>
    /// </summary>
    public sealed class PatrolMapView : Control
    {
        private readonly List<MonsterPointModel> _points = new List<MonsterPointModel>();
        private float _spawnX;
        private float _spawnY;
        private float _aggro = 10f;
        private float _leash = 30f;
        private float _wander = 6f;
        private int _maxAlive = 3;
        private float _slotSpacing = 8f;
        private int _mode = 1;
        private bool _useNav;
        private string _note = string.Empty;

        private float _scale = 4f;
        private Float2 _pan = new Float2(220f, 180f);
        private bool _panning;
        private Float2 _lastMouse;

        /// <summary>左键点到某个路径点（下标）时通知窗口，用于删点/改停留秒。</summary>
        public event Action<int> PointPicked;

        public PatrolMapView()
        {
            AnchorPreset = AnchorPresets.TopLeft;
        }

        public void SetData(
            IEnumerable<MonsterPointModel> points,
            float spawnX, float spawnY,
            float aggro, float leash, float wander,
            int maxAlive, float slotSpacing, int mode, bool useNav, string note)
        {
            _points.Clear();
            if (points != null) _points.AddRange(points);
            _spawnX = spawnX;
            _spawnY = spawnY;
            _aggro = aggro;
            _leash = leash;
            _wander = wander;
            _maxAlive = maxAlive;
            _slotSpacing = slotSpacing;
            _mode = mode;
            _useNav = useNav;
            _note = note ?? string.Empty;
        }

        private Float2 WorldToView(float x, float y)
        {
            return new Float2(_pan.X + (x - _spawnX) * _scale, _pan.Y + (y - _spawnY) * _scale);
        }

        public override bool OnMouseDown(Float2 location, MouseButton button)
        {
            if (button == MouseButton.Right)
            {
                _panning = true;
                _lastMouse = location;
                return true;
            }

            for (var i = 0; i < _points.Count; i++)
            {
                var p = WorldToView((float)_points[i].X, (float)_points[i].Y);
                var dx = p.X - location.X;
                var dy = p.Y - location.Y;
                if (dx * dx + dy * dy <= 64f)
                {
                    if (PointPicked != null) PointPicked(i);
                    return true;
                }
            }
            return base.OnMouseDown(location, button);
        }

        public override void OnMouseMove(Float2 location)
        {
            if (_panning)
            {
                _pan += location - _lastMouse;
                _lastMouse = location;
            }
            base.OnMouseMove(location);
        }

        public override bool OnMouseUp(Float2 location, MouseButton button)
        {
            if (button == MouseButton.Right && _panning)
            {
                _panning = false;
                return true;
            }
            return base.OnMouseUp(location, button);
        }

        public override bool OnMouseWheel(Float2 location, float delta)
        {
            var next = _scale * (delta > 0f ? 1.15f : 1f / 1.15f);
            if (next >= 0.35f && next <= 40f) _scale = next;
            return true;
        }

        public override void Draw()
        {
            base.Draw();
            Render2D.FillRectangle(new Rectangle(0f, 0f, Width, Height), MonsterColors.Canvas);
            if (Width < 40f || Height < 40f) return;

            var font = Style.Current.FontSmall;

            // 每 10 米一格（服务端单位口径 = 米）
            var step = _scale * 10f;
            if (step > 6f)
            {
                for (var x = _pan.X % step; x < Width; x += step)
                    Render2D.FillRectangle(new Rectangle(x, 0f, 1f, Height), MonsterColors.Grid);
                for (var y = _pan.Y % step; y < Height; y += step)
                    Render2D.FillRectangle(new Rectangle(0f, y, Width, 1f), MonsterColors.Grid);
            }

            DrawRadius(_leash, new Color(MonsterColors.Danger.R, MonsterColors.Danger.G, MonsterColors.Danger.B, 0.5f), "脱战");
            DrawRadius(_aggro, new Color(MonsterColors.Info.R, MonsterColors.Info.G, MonsterColors.Info.B, 0.6f), "索敌");
            DrawRadius(_wander, new Color(MonsterColors.Ok.R, MonsterColors.Ok.G, MonsterColors.Ok.B, 0.6f), "踱步");

            // 槽位沿 X 对称展开（与 SpawnDirectorCore 的出生偏移口径一致）
            for (var i = 0; i < _maxAlive; i++)
            {
                var offset = (i - (_maxAlive - 1) * 0.5f) * _slotSpacing;
                var p = WorldToView(_spawnX + offset, _spawnY);
                Render2D.FillRectangle(new Rectangle(p.X - 3f, p.Y - 3f, 7f, 7f), MonsterColors.TextDim);
            }

            var segmentColor = _useNav ? MonsterColors.Ok : MonsterColors.Selection;
            for (var i = 0; i + 1 < _points.Count; i++)
            {
                MonsterViewLines.DrawSegment(
                    WorldToView((float)_points[i].X, (float)_points[i].Y),
                    WorldToView((float)_points[i + 1].X, (float)_points[i + 1].Y),
                    segmentColor);
            }
            if (_mode == 1 && _points.Count > 2)
            {
                // 循环模式补回环；不画就看不出"终点接回第一点"这件事
                MonsterViewLines.DrawSegment(
                    WorldToView((float)_points[_points.Count - 1].X, (float)_points[_points.Count - 1].Y),
                    WorldToView((float)_points[0].X, (float)_points[0].Y),
                    MonsterColors.Edge);
            }

            for (var i = 0; i < _points.Count; i++)
            {
                var p = WorldToView((float)_points[i].X, (float)_points[i].Y);
                Render2D.FillRectangle(new Rectangle(p.X - 4f, p.Y - 4f, 9f, 9f), MonsterColors.Selection);
                Render2D.DrawText(font, (i + 1).ToString(), new Rectangle(p.X + 6f, p.Y - 8f, 30f, 14f), MonsterColors.Text);
            }

            var center = WorldToView(_spawnX, _spawnY);
            Render2D.FillRectangle(new Rectangle(center.X - 5f, center.Y - 5f, 11f, 11f), MonsterColors.Text);

            Render2D.DrawText(Style.Current.FontMedium, string.Format("俯视：点 {0}｜{1}｜{2}",
                _points.Count,
                _mode == 0 ? "往返" : (_mode == 1 ? "循环" : "随机"),
                _useNav ? "走图连段" : "直线连段"),
                new Rectangle(6f, 2f, Math.Max(0f, Width - 12f), 18f), MonsterColors.Text);
            Render2D.DrawText(font, "右键拖拽平移｜滚轮缩放｜左键点选路径点",
                new Rectangle(6f, 20f, Math.Max(0f, Width - 12f), 14f), MonsterColors.TextDim);
            if (!string.IsNullOrEmpty(_note))
                Render2D.DrawText(font, _note, new Rectangle(6f, Height - 18f, Math.Max(0f, Width - 12f), 14f), MonsterColors.MissingNote);
        }

        private void DrawRadius(float radius, Color color, string label)
        {
            var r = radius * _scale;
            if (r < 3f) return;
            var center = WorldToView(_spawnX, _spawnY);
            MonsterViewLines.DrawCircleOutline(center.X, center.Y, r, color, 48);
            if (r > 26f)
                Render2D.DrawText(Style.Current.FontSmall, label,
                    new Rectangle(center.X + r * 0.7f, center.Y - r * 0.7f, 60f, 14f), color);
        }
    }

    /// <summary>
    /// AI 状态机视图：把"哪些转移在当前能力位下真的可达"画出来。
    /// <para>价值不在好看——档位改了以后要立刻看到 Casting 那条支路是不是死的
    /// （挂了技能但技能位关着 = 走判定然后什么都不放）。</para>
    /// </summary>
    public sealed class BehaviorGraphView : Control
    {
        private bool _threat;
        private bool _skills;
        private bool _nav;
        private bool _assist;
        private bool _tactics;
        private bool _route;
        private int _skillRows;
        private string _note = string.Empty;

        private const float NodeWidth = 150f;
        private const float NodeHeight = 46f;

        private sealed class Node
        {
            public string Name;
            public string Sub;
            public float Nx;
            public float Ny;
            public bool Live;
            public Color Accent;
            public float Left;
            public float Top;
        }

        private readonly List<Node> _nodes = new List<Node>();

        public BehaviorGraphView()
        {
            AnchorPreset = AnchorPresets.TopLeft;
        }

        public void SetData(bool threat, bool skills, bool nav, bool assist, bool tactics, bool route, int skillRows, string note)
        {
            _threat = threat;
            _skills = skills;
            _nav = nav;
            _assist = assist;
            _tactics = tactics;
            _route = route;
            _skillRows = skillRows;
            _note = note ?? string.Empty;
        }

        private Float2 Center(int index)
        {
            var n = _nodes[index];
            return new Float2(n.Left + NodeWidth * 0.5f, n.Top + NodeHeight * 0.5f);
        }

        public override void Draw()
        {
            base.Draw();
            Render2D.FillRectangle(new Rectangle(0f, 0f, Width, Height), MonsterColors.Canvas);
            if (Width < 60f || Height < 60f) return;

            _nodes.Clear();
            _nodes.Add(new Node { Name = "Idle", Sub = "待机", Nx = 0.03f, Ny = 0.14f, Live = true, Accent = MonsterColors.Ok });
            _nodes.Add(new Node { Name = "Patrol", Sub = _route ? "巡逻（沿路线）" : "踱步（无路线）", Nx = 0.30f, Ny = 0.02f, Live = true, Accent = MonsterColors.Ok });
            _nodes.Add(new Node { Name = "Chase", Sub = _nav ? "追击（走图）" : "追击（直线）", Nx = 0.62f, Ny = 0.14f, Live = true, Accent = MonsterColors.Info });
            _nodes.Add(new Node { Name = "Attack", Sub = "近战/普攻", Nx = 0.78f, Ny = 0.44f, Live = true, Accent = MonsterColors.Selection });
            _nodes.Add(new Node
            {
                Name = "Casting",
                Sub = _skills ? (_skillRows > 0 ? "施法（" + _skillRows + " 个挂载）" : "施法（无挂载→空转）") : "施法（能力位关）",
                Nx = 0.34f,
                Ny = 0.44f,
                Live = _skills && _skillRows > 0,
                Accent = MonsterColors.Danger,
            });
            _nodes.Add(new Node { Name = "Evade", Sub = "回位·清仇恨·满血", Nx = 0.30f, Ny = 0.76f, Live = true, Accent = MonsterColors.Info });
            _nodes.Add(new Node { Name = "Dead", Sub = "尸体→重生", Nx = 0.03f, Ny = 0.76f, Live = true, Accent = MonsterColors.Edge });

            var areaW = Math.Max(1f, Width - NodeWidth);
            var areaH = Math.Max(1f, Height - NodeHeight - 44f);
            foreach (var n in _nodes)
            {
                n.Left = n.Nx * areaW;
                n.Top = n.Ny * areaH + 26f;
            }

            var castLive = _skills && _skillRows > 0;
            DrawEdge(0, 1, "超时转巡逻", true);
            DrawEdge(0, 2, _threat ? "索敌（威胁表）" : "索敌（最近）", true);
            DrawEdge(1, 2, "索敌", true);
            DrawEdge(2, 3, "进射程", true);
            DrawEdge(3, 2, "离射程", true);
            DrawEdge(3, 4, "概率+冷却+射程", castLive);
            DrawEdge(4, 3, "完成/被打断", castLive);
            DrawEdge(2, 5, "超脱战半径", true);
            DrawEdge(3, 5, _tactics ? "超脱战/残血逃" : "超脱战半径", true);
            DrawEdge(5, 0, "回位→清表→免疫窗", true);
            DrawEdge(3, 6, "血量归零", true);
            DrawEdge(6, 0, "重生 tick 到", true);
            if (_assist) DrawEdge(1, 4, "同族求援", castLive);

            foreach (var n in _nodes)
            {
                var rect = new Rectangle(n.Left, n.Top, NodeWidth, NodeHeight);
                var fill = n.Live ? Color.Lerp(MonsterColors.Card, n.Accent, 0.14f) : MonsterColors.Card;
                Render2D.FillRectangle(rect, fill);
                Render2D.FillRectangle(new Rectangle(rect.X, rect.Y, 4f, rect.Height), n.Live ? n.Accent : MonsterColors.Border);
                Render2D.DrawRectangle(rect, MonsterColors.Border, 1f);
                Render2D.DrawText(Style.Current.FontMedium, n.Name,
                    new Rectangle(rect.X + 10f, rect.Y + 4f, rect.Width - 16f, 18f),
                    n.Live ? MonsterColors.Text : MonsterColors.TextDim);
                Render2D.DrawText(Style.Current.FontSmall, n.Sub,
                    new Rectangle(rect.X + 10f, rect.Y + 24f, rect.Width - 16f, 16f),
                    n.Live ? MonsterColors.TextDim : MonsterColors.Border);
            }

            Render2D.DrawText(Style.Current.FontMedium,
                "AI 状态机｜威胁表 " + On(_threat) + " 技能 " + On(_skills) + " 走图 " + On(_nav) + " 求援 " + On(_assist) + " 战术 " + On(_tactics),
                new Rectangle(6f, 2f, Math.Max(0f, Width - 12f), 20f), MonsterColors.Text);

            if (!string.IsNullOrEmpty(_note))
                Render2D.DrawText(Style.Current.FontSmall, _note,
                    new Rectangle(6f, Height - 20f, Math.Max(0f, Width - 12f), 16f), MonsterColors.MissingNote);
        }

        private void DrawEdge(int from, int to, string label, bool live)
        {
            var color = live ? MonsterColors.Edge : MonsterColors.Border;
            var a = Center(from);
            var b = Center(to);
            MonsterViewLines.DrawSegment(a, b, color);
            Render2D.DrawText(Style.Current.FontSmall, label,
                new Rectangle((a.X + b.X) * 0.5f - 62f, (a.Y + b.Y) * 0.5f - 7f, 130f, 14f),
                live ? MonsterColors.TextDim : MonsterColors.Border);
        }

        private static string On(bool value)
        {
            return value ? "开" : "关";
        }
    }

    /// <summary>
    /// 施法条预览：把技能行里那几个字段（吟唱毫秒 / 系别 / 可打断 / 可否移动 / 目标数 / AOE）
    /// 画成玩家在游戏里会看到的那根条子。
    /// <para>
    /// <b>为什么要它</b>：这几项的判据分散在服务端内核、下发协议和客户端名牌三处，
    /// 策划在编辑器里改 <c>castMs</c> 或勾"可打断"时**看不到任何后果**。
    /// 预览不新增判据——颜色、取位规则、时长都走 <see cref="MonsterPreviewMath"/> 的镜像表，
    /// 而那张表与游戏内的真值由 <c>MonsterEditorContractTests</c> 逐值对账。
    /// </para>
    /// <para>
    /// <b>循环动画的时钟</b>用 <c>FlaxEngine.Time.TimeSinceStartup</c> 而不是帧计数：
    /// 编辑器空闲时不刷新，一有刷新就按真实经过的时间走，看到的节奏与游戏内插值口径一致。
    /// </para>
    /// </summary>
    public sealed class MonsterCastBarPreview : Control
    {
        private int _castMs = 1200;
        private int _schoolMask;
        private bool _interruptible = true;
        private bool _allowMovement;
        private int _targetCount = 1;
        private float _aoeRadius;
        private string _note = string.Empty;

        /// <summary>预览里一根条子的最长视觉时长（秒）：真实吟唱 8 秒不会让人等 8 秒才看完一轮。</summary>
        private const float LoopSecondsCap = 2.4f;

        public MonsterCastBarPreview()
        {
            // 构造期不设 Location：非 TopLeft 锚点会在父尺寸 0 时被重算到看不见的地方。
            AnchorPreset = AnchorPresets.TopLeft;
        }

        public void SetData(int castMs, int schoolMask, bool interruptible, bool allowMovement,
                            int targetCount, float aoeRadius, string note)
        {
            _castMs = castMs < 0 ? 0 : castMs;
            _schoolMask = schoolMask;
            _interruptible = interruptible;
            _allowMovement = allowMovement;
            _targetCount = targetCount < 1 ? 1 : targetCount;
            _aoeRadius = aoeRadius < 0f ? 0f : aoeRadius;
            _note = note ?? string.Empty;
        }

        private Rectangle BarBounds()
        {
            return new Rectangle(10f, 34f, Math.Max(0f, Width - 20f), 14f);
        }

        public override void Draw()
        {
            Render2D.FillRectangle(new Rectangle(0f, 0f, Width, Height), MonsterColors.Canvas);
            Render2D.DrawRectangle(new Rectangle(0f, 0f, Width - 1f, Height - 1f), MonsterColors.Border, 1f);

            var schoolName = MonsterPreviewMath.SchoolLabel(_schoolMask);

            Render2D.DrawText(Style.Current.FontMedium,
                string.Format("施法条预览：{0:0.##} 秒｜{1}｜{2}｜{3}",
                    _castMs / 1000f,
                    schoolName,
                    _interruptible ? "可打断" : "不可打断",
                    _allowMovement ? "可移动施法" : "站桩"),
                new Rectangle(8f, 6f, Math.Max(0f, Width - 16f), 20f), MonsterColors.Text);

            var bar = BarBounds();
            // 描边就是"能不能打断"的读法：金边=可打断，暗边=不可——与客户端 NameplateUI 同一口径。
            Render2D.DrawRectangle(bar, _interruptible ? MonsterColors.Selection : MonsterColors.Border, 2f);
            Render2D.FillRectangle(bar, new Color(0.10f, 0.10f, 0.12f, 0.9f));

            var rgb = MonsterPreviewMath.SchoolColor(_schoolMask);
            var seconds = _castMs / 1000f;
            var loop = seconds <= 0f ? 0f : Math.Min(seconds, LoopSecondsCap);
            var phase = loop <= 0f ? 1f : (float)((FlaxEngine.Time.TimeSinceStartup % loop) / loop);
            var fillWidth = bar.Width * phase;
            Render2D.FillRectangle(new Rectangle(bar.X, bar.Y, fillWidth, bar.Height), new Color(rgb[0], rgb[1], rgb[2], 0.95f));

            // 瞬发（0 秒）必须一眼看出来"这里根本没有条子"，否则空条子会被读成"吟唱极快"。
            var rightText = seconds <= 0f
                ? "瞬发：不显示条子"
                : string.Format("真实 {0:0.##}s{1}{2}",
                    seconds,
                    _targetCount > 1 ? "｜多目标 ×" + _targetCount : string.Empty,
                    _aoeRadius > 0f ? "｜AOE " + _aoeRadius.ToString("0.#") + "m" : string.Empty);
            Render2D.DrawText(Style.Current.FontSmall, rightText,
                new Rectangle(10f, Height - 24f, Math.Max(0f, Width - 20f), 18f), MonsterColors.TextDim);

            if (!string.IsNullOrEmpty(_note))
            {
                Render2D.DrawText(Style.Current.FontSmall, _note,
                    new Rectangle(10f, bar.Bottom + 4f, Math.Max(0f, Width - 20f), 16f), MonsterColors.MissingNote);
            }
        }
    }
}
