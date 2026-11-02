using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using FlaxEngine;
using FlaxEngine.GUI;

namespace QuestEditor
{
    /// <summary>
    /// 任务流程画布：把"任务编辑器"里的一条 DB 任务模板按叙事资产那种图的形式画出来——
    /// 接取 → 各完成条件 → 交还 → 奖励。节点可拖（位置存 Saved/QuestEditorFlow/&lt;id&gt;.json），
    /// 空白拖动平移、滚轮缩放；点节点通知窗口去聚焦检查器对应段落。
    /// <para>
    /// 这里只做"看与选"，不碰数据：所有编辑仍在右侧检查器里（沿用原有控件与保存/发布链路）。
    /// </para>
    /// </summary>
    public sealed class QuestFlowView : Control
    {
        private sealed class Node
        {
            public string Key = "";
            public string Title = "";
            public string Subtitle = "";
            public string Badge = "";
            public Color Accent = Color.White;
            public Rectangle World;
        }

        // 卡片与链距：宽一点、间距大一点，读起来不挤
        private const float CardWidth = 240f;
        private const float CardHeight = 92f;
        private const float ColumnGap = 340f;
        private const float RowGap = 108f;

        private readonly List<Node> _nodes = new List<Node>();
        private int _questId;

        private float _zoom = 1f;
        private Float2 _pan = new Float2(24f, 48f);

        private Node _dragNode;
        private Float2 _dragOffset;
        private bool _dragging;
        private bool _panning;
        private Float2 _lastMouse;
        private string _selection = "";

        private string _title = "";
        private string _subtitle = "";

        public event Action<string> NodeSelected;

        public QuestFlowView()
        {
            NarrativeColors.Apply(this);
        }

        public string Selection => _selection;

        /// <summary>用一串"已格式化好的文案"重画流程；窗口负责把 draft 翻成这些行。</summary>
        public void ShowFlow(int questId, string title, string subtitle,
            string acceptTitle, string acceptSubtitle, string acceptBadge,
            IReadOnlyList<(string title, string subtitle, bool missingTarget, Color accent)> objectives,
            string turnInTitle, string turnInSubtitle,
            string rewardTitle, string rewardSubtitle)
        {
            bool hadContent = _nodes.Count > 0;
            _questId = questId;
            _title = title ?? "";
            _subtitle = subtitle ?? "";
            _nodes.Clear();

            var positions = LoadLayout(questId);

            Add("accept", acceptTitle, acceptSubtitle, acceptBadge, NarrativeColors.Accept, positions);

            int index = 0;
            foreach (var objective in objectives ?? Array.Empty<(string, string, bool, Color)>())
            {
                // 没填目标的条件下用红色喊出来：运行时不会被放行，作者得先补对象 id
                var accent = objective.missingTarget ? NarrativeColors.Missing : objective.accent;
                var line = objective.missingTarget ? "⚠ 未填目标 id（不会放行）" : objective.subtitle;
                Add("obj:" + index, objective.title, line, "条件 " + (index + 1), accent, positions);
                index++;
            }

            Add("turnin", turnInTitle, turnInSubtitle, "交还", NarrativeColors.TurnIn, positions);
            Add("reward", rewardTitle, rewardSubtitle, "奖励", NarrativeColors.Reward, positions);

            // 只要**任意**节点没有位置（新增条件/物品时常发生）就整体重排：
            // 否则它会被摆在 -99999，FitToView 把缩放压到最小、边线拉成满屏对角线。
            bool anyUnplaced = false;
            foreach (var node in _nodes)
            {
                if (node.World.X <= -90000f) { anyUnplaced = true; break; }
            }

            if (anyUnplaced)
            {
                AutoLayout();
                FitToView();
            }
            else if (!hadContent)
            {
                // 首次载入才自动取景；之后编辑（改条件/描述）保持作者当前的缩放与平移
                FitToView();
            }
        }

        private void Add(string key, string title, string subtitle, string badge, Color accent, Dictionary<string, Float2> positions)
        {
            var position = positions != null && positions.TryGetValue(key, out var saved)
                ? saved
                : new Float2(-99999f, -99999f);

            _nodes.Add(new Node
            {
                Key = key,
                Title = title ?? "",
                Subtitle = subtitle ?? "",
                Badge = badge ?? "",
                Accent = accent,
                World = new Rectangle(position, new Float2(CardWidth, CardHeight))
            });
        }

        /// <summary>链式布局：接取在最左，条件竖排在中间，交还与奖励在右。</summary>
        public void AutoLayout()
        {
            int objectiveCount = 0;
            foreach (var node in _nodes) if (node.Key.StartsWith("obj:")) objectiveCount++;

            float middleY = objectiveCount > 1 ? -(objectiveCount - 1) * 0.5f * RowGap : 0f;
            int seen = 0;

            foreach (var node in _nodes)
            {
                if (node.Key == "accept")
                {
                    node.World = new Rectangle(0f, 0f, CardWidth, CardHeight);
                }
                else if (node.Key.StartsWith("obj:"))
                {
                    node.World = new Rectangle(ColumnGap, middleY + seen * RowGap, CardWidth, CardHeight);
                    seen++;
                }
                else if (node.Key == "turnin")
                {
                    node.World = new Rectangle(ColumnGap * 2f, 0f, CardWidth, CardHeight);
                }
                else if (node.Key == "reward")
                {
                    node.World = new Rectangle(ColumnGap * 3f, 0f, CardWidth, CardHeight);
                }
            }

            SaveLayout();
        }

        public void FitToView()
        {
            if (_nodes.Count == 0)
            {
                _zoom = 1f;
                _pan = new Float2(24f, 48f);
                return;
            }

            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var node in _nodes)
            {
                if (node.World.X <= -90000f) continue; // 兜底：未放置的节点不参与取景，免得把缩放压到最小
                minX = Math.Min(minX, node.World.X);
                minY = Math.Min(minY, node.World.Y);
                maxX = Math.Max(maxX, node.World.Right);
                maxY = Math.Max(maxY, node.World.Bottom);
            }

            float worldWidth = Math.Max(1f, maxX - minX);
            float worldHeight = Math.Max(1f, maxY - minY);
            float availableWidth = Math.Max(160f, Width - 64f);
            float availableHeight = Math.Max(120f, Height - 140f);

            _zoom = Mathf.Clamp(Math.Min(availableWidth / worldWidth, availableHeight / worldHeight), 0.4f, 1.1f);
            _pan = new Float2(
                (Width - worldWidth * _zoom) * 0.5f - minX * _zoom,
                (Height - worldHeight * _zoom) * 0.5f + 26f - minY * _zoom);
        }

        public void SelectNode(string key)
        {
            _selection = key ?? "";
        }

        // ── 输入 ────────────────────────────────────────────────

        public override bool OnMouseDown(Float2 location, MouseButton button)
        {
            if (button != MouseButton.Left) return base.OnMouseDown(location, button);

            var hit = HitTest(ToWorld(location));
            if (hit != null)
            {
                _selection = hit.Key;
                NodeSelected?.Invoke(hit.Key);
                _dragNode = hit;
                _dragOffset = ToWorld(location) - hit.World.Location;
                _dragging = false;
                StartMouseCapture(false);
                return true;
            }

            _selection = "";
            _panning = true;
            _lastMouse = location;
            StartMouseCapture(false);
            return true;
        }

        public override void OnMouseMove(Float2 location)
        {
            if (_dragNode != null)
            {
                _dragNode.World.Location = ToWorld(location) - _dragOffset;
                _dragging = true;
                return;
            }

            if (_panning)
            {
                _pan += location - _lastMouse;
                _lastMouse = location;
                return;
            }

            base.OnMouseMove(location);
        }

        public override bool OnMouseUp(Float2 location, MouseButton button)
        {
            if (_dragNode != null)
            {
                bool moved = _dragging;
                _dragNode = null;
                _dragging = false;
                EndMouseCapture();
                if (moved) SaveLayout();
                return true;
            }

            if (_panning)
            {
                _panning = false;
                EndMouseCapture();
                return true;
            }

            return base.OnMouseUp(location, button);
        }

        public override bool OnMouseWheel(Float2 location, float delta)
        {
            if (delta == 0f || _nodes.Count == 0) return false;

            var worldUnderCursor = ToWorld(location);
            _zoom = Mathf.Clamp(_zoom * (delta > 0f ? 1.1f : 1f / 1.1f), 0.3f, 2.4f);
            _pan = location - worldUnderCursor * _zoom;
            return true;
        }

        private Float2 ToView(Float2 world) => world * _zoom + _pan;
        private Float2 ToWorld(Float2 view) => (view - _pan) / _zoom;
        private Rectangle ToView(Rectangle world) => new Rectangle(world.Location * _zoom + _pan, world.Size * _zoom);

        private Node HitTest(Float2 world)
        {
            for (int i = _nodes.Count - 1; i >= 0; i--)
            {
                if (_nodes[i].World.Contains(world)) return _nodes[i];
            }
            return null;
        }

        // ── 绘制 ────────────────────────────────────────────────

        public override void Draw()
        {
            var bounds = new Rectangle(Float2.Zero, Size);
            Render2D.FillRectangle(bounds, NarrativeColors.Canvas);
            DrawGrid(bounds);

            if (_nodes.Count == 0)
            {
                Render2D.DrawText(Style.Current.FontMedium,
                    "选中左侧一条任务：这里会按 接取 → 完成条件 → 交还 → 奖励 画出它的流程。",
                    bounds, NarrativeColors.TextDim, TextAlignment.Center, TextAlignment.Center);
                DrawHeader();
                return;
            }

            DrawEdges();
            foreach (var node in _nodes) DrawNode(node);
            DrawHeader();
        }

        private void DrawGrid(Rectangle bounds)
        {
            float step = 32f * _zoom;
            if (step < 8f) step *= Mathf.Ceil(8f / step);

            float startX = _pan.X % step;
            if (startX > 0) startX -= step;
            float startY = _pan.Y % step;
            if (startY > 0) startY -= step;

            for (float x = startX; x < bounds.Width; x += step)
            {
                for (float y = startY; y < bounds.Height; y += step)
                {
                    Render2D.FillRectangle(new Rectangle(x, y, 1f, 1f), NarrativeColors.Grid);
                }
            }
        }

        private void DrawHeader()
        {
            // 标题与副标题分两行（原来都挤在 34px 里，字会压在一起）
            const float headerHeight = 44f;
            var rect = new Rectangle(0f, 0f, Width, headerHeight);
            Render2D.FillRectangle(rect, new Color(0.11f, 0.115f, 0.125f, 0.96f));
            Render2D.FillRectangle(new Rectangle(0f, headerHeight - 1f, Width, 1f), NarrativeColors.Border);

            Render2D.DrawText(Style.Current.FontLarge, _title,
                new Rectangle(12f, 3f, Width - 220f, 24f),
                NarrativeColors.Text, TextAlignment.Near, TextAlignment.Center);
            if (!string.IsNullOrEmpty(_subtitle))
            {
                Render2D.DrawText(Style.Current.FontSmall, _subtitle,
                    new Rectangle(12f, 26f, Width - 220f, 15f),
                    NarrativeColors.TextDim, TextAlignment.Near, TextAlignment.Near);
            }

            Render2D.DrawText(Style.Current.FontSmall, "缩放 " + (int)(_zoom * 100f) + "%",
                new Rectangle(Width - 70f, Height - 22f, 60f, 18f), NarrativeColors.TextDim,
                TextAlignment.Far, TextAlignment.Center);

            if (Width > 760f)
            {
                Render2D.DrawText(Style.Current.FontSmall,
                    "滚轮缩放 · 拖节点排位置 · 点节点看检查器 · 空白拖动平移",
                    new Rectangle(12f, Height - 22f, Width - 100f, 18f), NarrativeColors.TextDim,
                    TextAlignment.Near, TextAlignment.Center);
            }
        }

        private void DrawEdges()
        {
            // 接取 → 每个条件；每个条件 → 交还；交还 → 奖励
            Node accept = null, turnIn = null, reward = null;
            var objectives = new List<Node>();
            foreach (var node in _nodes)
            {
                if (node.Key == "accept") accept = node;
                else if (node.Key == "turnin") turnIn = node;
                else if (node.Key == "reward") reward = node;
                else if (node.Key.StartsWith("obj:")) objectives.Add(node);
            }

            if (accept != null)
                foreach (var objective in objectives) DrawEdge(accept, objective);
            foreach (var objective in objectives)
                if (turnIn != null) DrawEdge(objective, turnIn);
            if (turnIn != null && reward != null) DrawEdge(turnIn, reward);
            if (objectives.Count == 0 && accept != null && turnIn != null) DrawEdge(accept, turnIn);
        }

        private void DrawEdge(Node from, Node to)
        {
            if (from.World.X <= -90000f || to.World.X <= -90000f) return; // 兜底：有节点还没定位时不连线

            var a = ToView(new Float2(from.World.Right, from.World.Y + from.World.Height * 0.5f));
            var b = ToView(new Float2(to.World.X, to.World.Y + to.World.Height * 0.5f));

            float dx = Math.Max(30f, Math.Abs(b.X - a.X) * 0.45f);
            Render2D.DrawBezier(a, new Float2(a.X + dx, a.Y), new Float2(b.X - dx, b.Y), b,
                NarrativeColors.Edge, 1.6f);

            var direction = b - new Float2(b.X - dx, b.Y);
            float length = Mathf.Sqrt(direction.X * direction.X + direction.Y * direction.Y);
            if (length < 0.001f) return;
            direction /= length;
            var normal = new Float2(-direction.Y, direction.X);
            var basePoint = b - direction * 9f;
            Render2D.FillTriangle(b, basePoint + normal * 4.5f, basePoint - normal * 4.5f, NarrativeColors.Edge);
        }

        private void DrawNode(Node node)
        {
            var rect = ToView(node.World);
            bool selected = node.Key == _selection;

            Render2D.FillRectangle(rect, Color.Lerp(NarrativeColors.Card, node.Accent, 0.16f));
            Render2D.FillRectangle(new Rectangle(rect.X, rect.Y, 4f, rect.Height), node.Accent);
            Render2D.DrawRectangle(rect, selected ? NarrativeColors.Selection : NarrativeColors.Border, selected ? 2f : 1f);

            float scale = Mathf.Clamp(_zoom, 0.7f, 1f);
            var font = Style.Current.FontMedium;
            var small = Style.Current.FontSmall;

            Render2D.DrawText(font, node.Title, new Rectangle(rect.X + 12f, rect.Y + 4f, rect.Width - 24f, 22f),
                NarrativeColors.Text, TextAlignment.Near, TextAlignment.Center, TextWrapping.NoWrap, scale);

            if (!string.IsNullOrEmpty(node.Badge) && rect.Width > 90f)
            {
                Render2D.DrawText(small, node.Badge, new Rectangle(rect.Right - 84f, rect.Y + 5f, 74f, 18f),
                    node.Accent, TextAlignment.Far, TextAlignment.Center, TextWrapping.NoWrap, scale);
            }

            if (rect.Height > 44f && !string.IsNullOrEmpty(node.Subtitle))
            {
                Render2D.DrawText(small, node.Subtitle,
                    new Rectangle(rect.X + 12f, rect.Y + 28f * scale, rect.Width - 24f, rect.Height - 30f * scale),
                    NarrativeColors.TextDim, TextAlignment.Near, TextAlignment.Near, TextWrapping.WrapWords, scale);
            }
        }

        // ── 布局持久化（纯界面，不动 DB）────────────────────────

        private static string LayoutDirectory => Path.Combine(Globals.ProjectFolder, "Saved", "QuestEditorFlow");

        private Dictionary<string, Float2> LoadLayout(int questId)
        {
            var positions = new Dictionary<string, Float2>();
            if (questId <= 0) return positions;

            string path = Path.Combine(LayoutDirectory, questId + ".json");
            try
            {
                if (!File.Exists(path)) return positions;

                using var document = JsonDocument.Parse(File.ReadAllText(path));
                foreach (var property in document.RootElement.EnumerateObject())
                {
                    if (property.Value.ValueKind != JsonValueKind.Array) continue;
                    var items = property.Value;
                    if (items.GetArrayLength() < 2) continue;
                    positions[property.Name] = new Float2(items[0].GetSingle(), items[1].GetSingle());
                }
            }
            catch
            {
                // 布局坏了就按自动布局来，不影响任务数据
            }

            return positions;
        }

        private void SaveLayout()
        {
            if (_questId <= 0) return;

            try
            {
                Directory.CreateDirectory(LayoutDirectory);
                var map = new Dictionary<string, float[]>();
                foreach (var node in _nodes) map[node.Key] = new[] { node.World.X, node.World.Y };

                File.WriteAllText(Path.Combine(LayoutDirectory, _questId + ".json"),
                    JsonSerializer.Serialize(map, new JsonSerializerOptions { WriteIndented = false }));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[QuestEditor] 流程图布局保存失败：" + ex.Message);
            }
        }
    }

    /// <summary>流程图配色（与编辑器暗色主题一致，独立于 NarrativePro 的 theme，避免跨插件依赖）。</summary>
    internal static class NarrativeColors
    {
        public static readonly Color Canvas = new Color(0.09f, 0.095f, 0.105f);
        public static readonly Color Grid = new Color(0.14f, 0.15f, 0.16f);
        public static readonly Color Card = new Color(0.15f, 0.16f, 0.175f);
        public static readonly Color Border = new Color(0.24f, 0.25f, 0.27f);
        public static readonly Color Text = new Color(0.90f, 0.91f, 0.93f);
        public static readonly Color TextDim = new Color(0.62f, 0.64f, 0.67f);
        public static readonly Color Edge = new Color(0.42f, 0.46f, 0.50f);
        public static readonly Color Selection = new Color(0.96f, 0.78f, 0.38f);
        public static readonly Color Accept = new Color(0.36f, 0.68f, 0.92f);
        public static readonly Color Objective = new Color(0.45f, 0.78f, 0.55f);
        public static readonly Color TurnIn = new Color(0.72f, 0.60f, 0.92f);
        public static readonly Color Reward = new Color(0.92f, 0.72f, 0.40f);
        public static readonly Color Missing = new Color(0.95f, 0.75f, 0.30f);

        public static void Apply(Control control)
        {
            // 预留：视图本身用上面的颜色自绘，不需要控件级主题
        }
    }
}
