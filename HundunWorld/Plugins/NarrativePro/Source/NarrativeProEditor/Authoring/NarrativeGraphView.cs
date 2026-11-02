#if FLAX_EDITOR
using System;
using System.Collections.Generic;
using System.Text.Json;
using FlaxEngine;
using FlaxEngine.GUI;
using NarrativePro.Tales.Dialogue;
using NarrativePro.Tales.Quest;
using NarrativePro.Core;

namespace NarrativePro.EditorTools
{
    public enum NarrativeSelectionKind
    {
        None,
        QuestState,
        QuestBranch,
        DialogueNpc,
        DialoguePlayer,
        Speaker
    }

    public struct NarrativeSelection
    {
        public NarrativeSelectionKind Kind;
        public string Id;

        public NarrativeSelection(NarrativeSelectionKind kind, string id)
        {
            Kind = kind;
            Id = id ?? "";
        }

        public bool IsNone => Kind == NarrativeSelectionKind.None;

        public bool SameAs(NarrativeSelection other)
        {
            return Kind == other.Kind && string.Equals(Id ?? "", other.Id ?? "", StringComparison.OrdinalIgnoreCase);
        }

        public static readonly NarrativeSelection None = new NarrativeSelection(NarrativeSelectionKind.None, "");
    }

    /// <summary>
    /// 叙事图视图（对应 UE5 NarrativePro 的任务图 / 对话树）。
    /// <para>
    /// 任务：状态是卡片，分支是中继节点——与运行时数据里的 <c>position</c> 一一对应；
    /// 对话：NPC / 玩家节点按 <c>nodePosX/Y</c> 摆放，连线来自 NPCReplies / PlayerReplies 邻接表。
    /// 拖动节点直接写回 DTO 位置（保存时落盘），滚轮缩放、空白处拖拽平移。
    /// </para>
    /// </summary>
    internal sealed class NarrativeGraphView : Control
    {
        private sealed class SceneNode
        {
            public NarrativeSelectionKind Kind;
            public string Id;
            public string Title;
            public string Description;
            public string Badge;
            public Color Accent;
            public Color BadgeColor;
            public Rectangle World;
        }

        private const float ScrollStep = 1.1f;

        private QuestData _quest;
        private DialogueData _dialogue;
        private string _title = "";
        private string _subtitle = "";

        private readonly List<SceneNode> _nodes = new List<SceneNode>();
        private readonly Dictionary<string, SceneNode> _byKey = new Dictionary<string, SceneNode>(StringComparer.OrdinalIgnoreCase);

        private float _zoom = 1f;
        private Float2 _pan = new Float2(24f, 48f);

        private SceneNode _dragNode;
        private Float2 _dragOffset;
        private bool _dragMoved;
        private bool _panning;
        private Float2 _lastMouse;

        // --- 连线拖拽（Shift+左键从节点拖到节点）---
        private SceneNode _linkFrom;
        private Float2 _linkCursor;
        private bool _linkActive;

        private NarrativeSelection _selection = NarrativeSelection.None;

        /// <summary>选中项变化（点击节点 / 点空白清空 / 外部 Select）。</summary>
        public event Action<NarrativeSelection> SelectionChanged;

        /// <summary>节点被拖动过（位置已写进 DTO，调用方负责标记未保存并刷新）。</summary>
        public event Action NodeMoved;

        /// <summary>在图里按 Delete（选中项的删除由调用方执行）。</summary>
        public event Action DeleteRequested;

        /// <summary>右键点节点或空白处：把选中项与菜单弹出位置交给窗口，由它决定菜单项。</summary>
        public event Action<NarrativeSelection, Float2> ContextMenuRequested;

        /// <summary>Shift+拖连线的落地事件：两个节点的选中身份；任务=state→state，对话=npc/player 任意方向。</summary>
        public event Action<NarrativeSelection, NarrativeSelection> LinkRequested;

        public NarrativeGraphView()
        {
        }

        public float Zoom => _zoom;

        /// <summary>空图时的提示文案（内嵌面板会换成"选中模板后显示叙事图"）。</summary>
        public string EmptyHint
        {
            get => _emptyHint;
            set => _emptyHint = string.IsNullOrWhiteSpace(value) ? DefaultEmptyHint : value;
        }

        private const string DefaultEmptyHint = "从左侧资产列表打开一个任务或对话，图会出现在这里。";
        private string _emptyHint = DefaultEmptyHint;


        public bool HasContent => _quest != null || _dialogue != null;

        /// <summary>窗口刚打开时尺寸还没定下来，等第一次布局完再做一次适配。</summary>
        public bool PendingFit { get; private set; }

        public void ConsumePendingFit()
        {
            if (!PendingFit) return;
            PendingFit = false;
            FitToView();
        }

        /// <summary>窗口尺寸还要变（首次摆放/停靠）时预约一次适配：等布局稳定后由 ConsumePendingFit 执行。</summary>
        public void RequestFit()
        {
            PendingFit = true;
        }

        // -------------------------------------------------------------------
        // 数据
        // -------------------------------------------------------------------

        public void ShowQuest(QuestData data, string title, string subtitle)
        {
            _quest = data;
            _dialogue = null;
            _title = title ?? "";
            _subtitle = subtitle ?? "";
            _selection = NarrativeSelection.None;
            RebuildNodes();
            PendingFit = true;
            FitToView();
        }

        public void ShowDialogue(DialogueData data, string title, string subtitle)
        {
            _dialogue = data;
            _quest = null;
            _title = title ?? "";
            _subtitle = subtitle ?? "";
            _selection = NarrativeSelection.None;
            RebuildNodes();
            PendingFit = true;
            FitToView();
        }

        public void Clear()
        {
            _quest = null;
            _dialogue = null;
            _title = "";
            _subtitle = "";
            _selection = NarrativeSelection.None;
            _nodes.Clear();
            _byKey.Clear();
        }

        /// <summary>
        /// 撤销/重做换了另一份 DTO：重建卡片，但保留当前缩放与平移，
        /// 免得撤一次节点拖动就把视角弹走。
        /// </summary>
        public void RebindQuest(QuestData data, string subtitle)
        {
            _quest = data;
            _dialogue = null;
            if (!string.IsNullOrEmpty(subtitle)) _subtitle = subtitle;
            RebuildNodes();
        }

        /// <summary>同 <see cref="RebindQuest"/>，对话版。</summary>
        public void RebindDialogue(DialogueData data, string subtitle)
        {
            _dialogue = data;
            _quest = null;
            if (!string.IsNullOrEmpty(subtitle)) _subtitle = subtitle;
            RebuildNodes();
        }

        /// <summary>数据被增删改后重建卡片（位置从 DTO 重读）。</summary>
        public void RefreshNodes()
        {
            RebuildNodes();
        }

        /// <summary>只更新表头那行统计（"5 状态 · 4 分支"），不重建卡片。</summary>
        public void SetSubtitle(string subtitle)
        {
            _subtitle = subtitle ?? "";
        }

        private void RebuildNodes()
        {
            _nodes.Clear();
            _byKey.Clear();
            _linkActive = false;
            _linkFrom = null;

            if (_quest != null)
            {
                foreach (var state in _quest.States ?? new List<QuestStateData>())
                {
                    var position = state.Position ?? new NodePositionData();
                    var accent = StateColor(state.StateType);
                    bool isStart = string.Equals(_quest.StartStateId, state.Id, StringComparison.OrdinalIgnoreCase);
                    AddNode(new SceneNode
                    {
                        Kind = NarrativeSelectionKind.QuestState,
                        Id = state.Id ?? "",
                        Title = string.IsNullOrEmpty(state.Id) ? "(未命名状态)" : state.Id,
                        Description = (state.Description ?? "") + EventTagSuffix(state.Events),
                        Badge = isStart ? "起始" : StateBadge(state.StateType),
                        BadgeColor = isStart ? NarrativeEditorTheme.Selection : accent,
                        Accent = accent,
                        World = new Rectangle(position.X, position.Y, 210f, 72f)
                    });
                }

                foreach (var branch in _quest.Branches ?? new List<QuestBranchData>())
                {
                    var position = branch.Position ?? new NodePositionData();
                    string route = (branch.FromStateId ?? "?") + " → " + (branch.ToStateId ?? "?");
                    AddNode(new SceneNode
                    {
                        Kind = NarrativeSelectionKind.QuestBranch,
                        Id = branch.Id ?? "",
                        Title = string.IsNullOrEmpty(branch.Id) ? "(未命名分支)" : branch.Id,
                        Description = (string.IsNullOrWhiteSpace(branch.Description) ? route : branch.Description + " · " + route)
                                      + EventTagSuffix(branch.Events),
                        Badge = branch.Hidden ? "隐藏" : "分支",
                        BadgeColor = branch.Hidden ? NarrativeEditorTheme.TextDim : NarrativeEditorTheme.Accent,
                        Accent = branch.Hidden ? NarrativeEditorTheme.TextDim : NarrativeEditorTheme.Accent,
                        World = new Rectangle(position.X, position.Y, 168f, 56f)
                    });
                }
            }
            else if (_dialogue != null)
            {
                foreach (var npc in _dialogue.NpcReplies ?? new List<NpcReplyData>())
                {
                    var speaker = NarrativeAssetStore.FindDialogueSpeaker(_dialogue, npc.SpeakerId);
                    string speakerLabel = speaker != null && !string.IsNullOrWhiteSpace(speaker.DisplayName)
                        ? speaker.DisplayName
                        : npc.SpeakerId ?? "";
                    var speakerColor = SpeakerColor(npc.SpeakerId);

                    AddNode(new SceneNode
                    {
                        Kind = NarrativeSelectionKind.DialogueNpc,
                        Id = npc.Id ?? "",
                        Title = string.IsNullOrEmpty(npc.Id) ? "(未命名 NPC)" : npc.Id,
                        Description = (npc.Line?.Text ?? "") + EventTagSuffix(npc.Events),
                        Badge = npc.IsRoot ? "ROOT" : NarrativeEditorTheme.Fit(speakerLabel, 88f, NarrativeEditorTheme.FontSmall),
                        BadgeColor = npc.IsRoot ? NarrativeEditorTheme.Selection : speakerColor,
                        Accent = npc.IsRoot ? NarrativeEditorTheme.Selection : speakerColor,
                        World = new Rectangle(npc.NodePosX, npc.NodePosY, 232f, 84f)
                    });
                }

                foreach (var player in _dialogue.PlayerReplies ?? new List<PlayerReplyData>())
                {
                    AddNode(new SceneNode
                    {
                        Kind = NarrativeSelectionKind.DialoguePlayer,
                        Id = player.Id ?? "",
                        Title = string.IsNullOrEmpty(player.Id) ? "(未命名选项)" : player.Id,
                        Description = (string.IsNullOrWhiteSpace(player.OptionText) ? player.Line?.Text ?? "" : player.OptionText)
                                      + EventTagSuffix(player.Events),
                        Badge = "玩家",
                        BadgeColor = NarrativeEditorTheme.Player,
                        Accent = NarrativeEditorTheme.Player,
                        World = new Rectangle(player.NodePosX, player.NodePosY, 216f, 84f)
                    });
                }
            }

            // 分支卡片就是"连线"，压到状态/节点上面会抢标题与点击；让它先画（沉底），命中测试仍然后取=节点优先
            _nodes.Sort((a, b) => NodeZOrder(a).CompareTo(NodeZOrder(b)));
        }

        private static int NodeZOrder(SceneNode node)
        {
            return node.Kind == NarrativeSelectionKind.QuestBranch ? 0 : 1;
        }

        private void AddNode(SceneNode node)
        {
            _nodes.Add(node);
            _byKey[KeyOf(node.Kind, node.Id)] = node;
        }

        private static string KeyOf(NarrativeSelectionKind kind, string id) => (int)kind + "|" + (id ?? "");

        private static Color StateColor(EStateNodeType type)
        {
            switch (type)
            {
                case EStateNodeType.Success: return NarrativeEditorTheme.Success;
                case EStateNodeType.Failure: return NarrativeEditorTheme.Failure;
                default: return NarrativeEditorTheme.Regular;
            }
        }

        private static readonly Color[] SpeakerPalette =
        {
            new Color(0.33f, 0.74f, 0.80f),
            new Color(0.52f, 0.70f, 0.92f),
            new Color(0.66f, 0.58f, 0.90f),
            new Color(0.38f, 0.80f, 0.62f),
            new Color(0.92f, 0.66f, 0.40f),
            new Color(0.86f, 0.52f, 0.62f)
        };

        /// <summary>按说话者 id 稳定取色，让同一说话者的 NPC 节点在图上一眼能聚在一起。</summary>
        private static Color SpeakerColor(string speakerId)
        {
            if (string.IsNullOrEmpty(speakerId)) return NarrativeEditorTheme.Accent;
            int hash = 0;
            foreach (char c in speakerId) hash = (hash * 31 + c) & 0x7FFFFFFF;
            return SpeakerPalette[hash % SpeakerPalette.Length];
        }

        private static string StateBadge(EStateNodeType type)
        {
            switch (type)
            {
                case EStateNodeType.Success: return "成功";
                case EStateNodeType.Failure: return "失败";
                default: return "";
            }
        }

        // -------------------------------------------------------------------
        // 选中
        // -------------------------------------------------------------------

        public NarrativeSelection Selection => _selection;

        public void Select(NarrativeSelection selection)
        {
            _selection = selection;
            SelectionChanged?.Invoke(_selection);
        }

        public void SelectNode(NarrativeSelectionKind kind, string id)
        {
            if (_byKey.TryGetValue(KeyOf(kind, id ?? ""), out var node))
            {
                Select(new NarrativeSelection(node.Kind, node.Id));
                FocusNode(node);
                return;
            }
            Select(new NarrativeSelection(kind, id));
        }

        /// <summary>把某个节点滚进可视区域中央（校验面板点击定位时用）。</summary>
        private void FocusNode(SceneNode node)
        {
            var center = node.World.Location + node.World.Size * 0.5f;
            _pan = new Float2(Width * 0.5f - center.X * _zoom, Height * 0.5f - center.Y * _zoom);
        }

        // -------------------------------------------------------------------
        // 视图
        // -------------------------------------------------------------------

        public void AutoLayout()
        {
            if (_quest != null)
            {
                NarrativeAssetStore.LayoutQuest(_quest);
                RebuildNodes();
                FitToView();
            }
            else if (_dialogue != null)
            {
                NarrativeAssetStore.LayoutDialogue(_dialogue);
                RebuildNodes();
                FitToView();
            }
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
                minX = Math.Min(minX, node.World.X);
                minY = Math.Min(minY, node.World.Y);
                maxX = Math.Max(maxX, node.World.Right);
                maxY = Math.Max(maxY, node.World.Bottom);
            }

            float worldWidth = Math.Max(1f, maxX - minX);
            float worldHeight = Math.Max(1f, maxY - minY);
            float availableWidth = Math.Max(160f, Width - 64f);
            float availableHeight = Math.Max(120f, Height - 120f);

            _zoom = Mathf.Clamp(Math.Min(availableWidth / worldWidth, availableHeight / worldHeight), 0.5f, 1.15f);
            float viewWidth = worldWidth * _zoom;
            float viewHeight = worldHeight * _zoom;
            _pan = new Float2(
                (Width - viewWidth) * 0.5f - minX * _zoom,
                (Height - viewHeight) * 0.5f + 26f - minY * _zoom);
        }

        private Float2 ToView(Float2 world) => world * _zoom + _pan;

        private Float2 ToWorld(Float2 view) => (view - _pan) / _zoom;

        private Rectangle ToView(Rectangle world) => new Rectangle(world.Location * _zoom + _pan, world.Size * _zoom);

        private SceneNode HitTest(Float2 world)
        {
            for (int i = _nodes.Count - 1; i >= 0; i--)
            {
                if (_nodes[i].World.Contains(world)) return _nodes[i];
            }
            return null;
        }

        /// <summary>连线拖拽只从"能发起连线的节点"开始（分支节点本身就是连线，不能再被连）。</summary>
        private static bool CanLinkFrom(SceneNode node)
        {
            return node.Kind == NarrativeSelectionKind.QuestState ||
                   node.Kind == NarrativeSelectionKind.DialogueNpc ||
                   node.Kind == NarrativeSelectionKind.DialoguePlayer;
        }

        /// <summary>同一批节点能当连线终点；同种的合法性由调用方（存储层）裁决。</summary>
        private static bool CanLinkTo(SceneNode node) => CanLinkFrom(node);

        // -------------------------------------------------------------------
        // 绘制
        // -------------------------------------------------------------------

        public override void Draw()
        {
            var bounds = new Rectangle(Float2.Zero, Size);
            NarrativeEditorTheme.Fill(bounds, NarrativeEditorTheme.Canvas);
            DrawGrid(bounds);

            if (_nodes.Count == 0)
            {
                DrawHeader();
                NarrativeEditorTheme.DrawText(_emptyHint, bounds,
                    NarrativeEditorTheme.TextDim, NarrativeEditorTheme.FontMedium, TextAlignment.Center, TextAlignment.Center);
                return;
            }

            DrawEdges();
            foreach (var node in _nodes) DrawNode(node);
            DrawLinkPreview();
            DrawHeader();
            DrawLegend();
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
                    NarrativeEditorTheme.Fill(new Rectangle(x, y, 1f, 1f), NarrativeEditorTheme.CanvasGrid);
                }
            }
        }

        private void DrawHeader()
        {
            var rect = new Rectangle(0f, 0f, Width, 34f);
            NarrativeEditorTheme.Fill(rect, new Color(0.11f, 0.115f, 0.125f, 0.96f));
            NarrativeEditorTheme.Fill(new Rectangle(0f, 33f, Width, 1f), NarrativeEditorTheme.Border);

            string title = NarrativeEditorTheme.Fit(_title, Width - 24f, NarrativeEditorTheme.FontLarge);
            NarrativeEditorTheme.DrawText(title, new Rectangle(12f, 0f, Width - 24f, 34f),
                NarrativeEditorTheme.Text, NarrativeEditorTheme.FontLarge);

            if (!string.IsNullOrEmpty(_subtitle))
            {
                NarrativeEditorTheme.DrawText(_subtitle,
                    new Rectangle(12f, Height - 24f, Math.Max(60f, Width * 0.55f), 18f),
                    NarrativeEditorTheme.TextDim, NarrativeEditorTheme.FontSmall);
            }

            string zoom = Mathf.RoundToInt(_zoom * 100f) + "%";
            NarrativeEditorTheme.DrawText(zoom, new Rectangle(Width - 66f, Height - 24f, 54f, 18f),
                NarrativeEditorTheme.TextDim, NarrativeEditorTheme.FontSmall, TextAlignment.Far);

            if (Width > 760f)
            {
                NarrativeEditorTheme.DrawText("滚轮缩放 · 拖节点改位置 · Shift+拖 连线 · 右键 菜单 · Delete 删选中 · F 适配",
                    new Rectangle(Width - 400f, Height - 24f, 320f, 18f),
                    NarrativeEditorTheme.TextDim, NarrativeEditorTheme.FontSmall, TextAlignment.Far);
            }
        }

        private void DrawLegend()
        {
            if (_quest == null && _dialogue == null) return;

            var entries = new List<(Color color, string label)>();
            if (_quest != null)
            {
                entries.Add((NarrativeEditorTheme.Regular, "常规"));
                entries.Add((NarrativeEditorTheme.Success, "成功"));
                entries.Add((NarrativeEditorTheme.Failure, "失败"));
                entries.Add((NarrativeEditorTheme.Accent, "分支"));
            }
            else
            {
                // 对话图按说话者列图例：颜色与节点一致，一眼能分出谁在说话。
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var speaker in _dialogue.Speakers ?? new List<SpeakerData>())
                {
                    if (string.IsNullOrEmpty(speaker.SpeakerId) || !seen.Add(speaker.SpeakerId)) continue;
                    string label = string.IsNullOrWhiteSpace(speaker.DisplayName) ? speaker.SpeakerId : speaker.DisplayName;
                    entries.Add((SpeakerColor(speaker.SpeakerId),
                        NarrativeEditorTheme.Fit(label, 86f, NarrativeEditorTheme.FontSmall)));
                    if (entries.Count >= 5) break;
                }
                entries.Add((NarrativeEditorTheme.Player, "玩家选项"));
            }

            float width = 118f;
            float height = 12f + entries.Count * 18f;
            var rect = new Rectangle(Width - width - 14f, 44f, width, height);
            NarrativeEditorTheme.Fill(rect, new Color(0.11f, 0.115f, 0.125f, 0.9f));
            NarrativeEditorTheme.Outline(rect, NarrativeEditorTheme.Border, 1f);

            float y = rect.Y + 6f;
            foreach (var entry in entries)
            {
                NarrativeEditorTheme.Fill(new Rectangle(rect.X + 8f, y + 5f, 8f, 8f), entry.color);
                NarrativeEditorTheme.DrawText(entry.label, new Rectangle(rect.X + 22f, y, width - 28f, 18f),
                    NarrativeEditorTheme.TextDim, NarrativeEditorTheme.FontSmall);
                y += 18f;
            }
        }

        private void DrawEdges()
        {
            if (_quest != null)
            {
                foreach (var branch in _quest.Branches ?? new List<QuestBranchData>())
                {
                    var branchNode = FindNode(NarrativeSelectionKind.QuestBranch, branch.Id);
                    var fromNode = FindNode(NarrativeSelectionKind.QuestState, branch.FromStateId);
                    var toNode = FindNode(NarrativeSelectionKind.QuestState, branch.ToStateId);

                    bool hot = IsSelected(NarrativeSelectionKind.QuestBranch, branch.Id);
                    var color = hot ? NarrativeEditorTheme.Selection : new Color(0.34f, 0.36f, 0.40f);

                    if (fromNode != null && branchNode != null)
                        DrawEdge(RightCenter(fromNode.World), LeftCenter(branchNode.World), color);
                    if (branchNode != null && toNode != null)
                        DrawEdge(RightCenter(branchNode.World), LeftCenter(toNode.World), color);
                    else if (fromNode != null && toNode != null)
                        DrawEdge(RightCenter(fromNode.World), LeftCenter(toNode.World), color);
                }
                return;
            }

            if (_dialogue == null) return;

            foreach (var node in _nodes)
            {
                IEnumerable<string> npcTargets = null;
                IEnumerable<string> playerTargets = null;

                if (node.Kind == NarrativeSelectionKind.DialogueNpc)
                {
                    var npc = NarrativeAssetStore.FindNpcNode(_dialogue, node.Id);
                    if (npc != null)
                    {
                        npcTargets = npc.NpcReplies;
                        playerTargets = npc.PlayerReplies;
                    }
                }
                else if (node.Kind == NarrativeSelectionKind.DialoguePlayer)
                {
                    var player = NarrativeAssetStore.FindPlayerNode(_dialogue, node.Id);
                    if (player != null)
                    {
                        npcTargets = player.NpcReplies;
                    }
                }

                bool hot = IsSelected(node.Kind, node.Id);
                var color = hot ? NarrativeEditorTheme.Selection : new Color(0.34f, 0.36f, 0.40f);

                if (npcTargets != null)
                {
                    foreach (var targetId in npcTargets)
                    {
                        var target = FindNode(NarrativeSelectionKind.DialogueNpc, targetId);
                        if (target != null) DrawEdge(RightCenter(node.World), LeftCenter(target.World), color);
                    }
                }

                if (playerTargets != null)
                {
                    foreach (var targetId in playerTargets)
                    {
                        var target = FindNode(NarrativeSelectionKind.DialoguePlayer, targetId);
                        if (target != null) DrawEdge(RightCenter(node.World), LeftCenter(target.World), color);
                    }
                }
            }
        }

        private void DrawLinkPreview()
        {
            if (!_linkActive || _linkFrom == null) return;

            var fromRect = ToView(_linkFrom.World);
            var from = new Float2(fromRect.X + fromRect.Width, fromRect.Y + fromRect.Height * 0.5f);
            var to = _linkCursor;

            float dx = Math.Max(36f, Math.Abs(to.X - from.X) * 0.45f);
            var c1 = new Float2(from.X + dx, from.Y);
            var c2 = new Float2(to.X - dx, to.Y);
            Render2D.DrawBezier(from, c1, c2, to, NarrativeEditorTheme.Selection, 2f);

            var direction = to - c2;
            float length = Mathf.Sqrt(direction.X * direction.X + direction.Y * direction.Y);
            if (length > 0.001f)
            {
                direction /= length;
                var normal = new Float2(-direction.Y, direction.X);
                var basePoint = to - direction * 10f;
                Render2D.FillTriangle(to, basePoint + normal * 5f, basePoint - normal * 5f, NarrativeEditorTheme.Selection);
            }

            // 悬停在合法目标上时把它描亮
            var hover = HitTest(ToWorld(_linkCursor));
            if (hover != null && hover != _linkFrom && CanLinkTo(hover))
            {
                NarrativeEditorTheme.Outline(ToView(hover.World), NarrativeEditorTheme.Selection, 2f);
            }

            NarrativeEditorTheme.DrawText("松开连线 · Esc 取消",
                new Rectangle(_linkCursor.X + 12f, _linkCursor.Y - 26f, 220f, 20f),
                NarrativeEditorTheme.TextDim, NarrativeEditorTheme.FontSmall);
        }

        private void DrawEdge(Float2 fromWorld, Float2 toWorld, Color color)
        {
            var from = ToView(fromWorld);
            var to = ToView(toWorld);

            float dx = Math.Max(36f, Math.Abs(to.X - from.X) * 0.45f);
            var c1 = new Float2(from.X + dx, from.Y);
            var c2 = new Float2(to.X - dx, to.Y);

            Render2D.DrawBezier(from, c1, c2, to, color, 1.6f);

            Float2 direction = new Float2(to.X - c2.X, to.Y - c2.Y);
            float length = Mathf.Sqrt(direction.X * direction.X + direction.Y * direction.Y);
            if (length < 0.001f) return;

            direction /= length;
            var normal = new Float2(-direction.Y, direction.X);
            var basePoint = to - direction * 9f;
            Render2D.FillTriangle(to, basePoint + normal * 4.5f, basePoint - normal * 4.5f, color);
        }

        private void DrawNode(SceneNode node)
        {
            var rect = ToView(node.World);
            bool selected = IsSelected(node.Kind, node.Id);
            float scale = Mathf.Clamp(_zoom, 0.72f, 1f);

            NarrativeEditorTheme.Fill(rect, NarrativeEditorTheme.Tint(node.Accent, 0.16f));
            NarrativeEditorTheme.Fill(new Rectangle(rect.X, rect.Y, 4f, rect.Height), node.Accent);
            NarrativeEditorTheme.Outline(rect, selected ? NarrativeEditorTheme.Selection : NarrativeEditorTheme.Border,
                selected ? 2f : 1f);

            float padding = 12f;
            float textWidth = rect.Width - padding * 2f;
            float fitWidth = textWidth / scale;

            // badgeWidth 是屏幕像素，而 fitWidth 是"逻辑像素"（DrawText 会按 scale 缩放绘制），
            // 两者直接相减会在缩小时多扣掉 1/scale 倍宽度：实测 0.72 缩放下分支标题被挤成 "b_..."。
            float badgeWidth = string.IsNullOrEmpty(node.Badge) ? 0f : Mathf.Min(96f, Mathf.Max(54f, rect.Width * 0.4f));
            float badgeReserve = (badgeWidth + 8f) / scale;
            bool drawBadge = badgeWidth > 0f && fitWidth - badgeReserve >= 56f;

            NarrativeEditorTheme.DrawText(
                NarrativeEditorTheme.Fit(node.Title, drawBadge ? fitWidth - badgeReserve : fitWidth, NarrativeEditorTheme.FontMedium),
                new Rectangle(rect.X + padding, rect.Y + 4f, textWidth, 22f),
                NarrativeEditorTheme.Text, NarrativeEditorTheme.FontMedium, TextAlignment.Near, TextAlignment.Center,
                TextWrapping.NoWrap, scale);

            if (drawBadge)
            {
                NarrativeEditorTheme.DrawText(node.Badge,
                    new Rectangle(rect.Right - badgeWidth - 6f, rect.Y + 5f, badgeWidth, 18f),
                    node.BadgeColor, NarrativeEditorTheme.FontSmall, TextAlignment.Far, TextAlignment.Center,
                    TextWrapping.NoWrap, scale);
            }

            if (rect.Height > 42f)
            {
                NarrativeEditorTheme.DrawText(node.Description,
                    new Rectangle(rect.X + padding, rect.Y + 26f * scale + 2f, textWidth, rect.Height - 28f * scale - 4f),
                    NarrativeEditorTheme.TextDim, NarrativeEditorTheme.FontSmall,
                    TextAlignment.Near, TextAlignment.Near, TextWrapping.WrapWords, scale);
            }
        }

        private SceneNode FindNode(NarrativeSelectionKind kind, string id)
        {
            return _byKey.TryGetValue(KeyOf(kind, id ?? ""), out var node) ? node : null;
        }

        /// <summary>
        /// 图内事件提示：状态/分支/节点上挂了任务与对话事件时，在描述后补一句「任务×N 对话×N」。
        /// 作者不必逐个点开检查器才知道这条叙事在哪里穿插了任务。
        /// </summary>
        /// <summary>自动化验收：把图节点的标题与描述写到回执文件（日志有缓冲，读文件更可靠）。</summary>
        internal void DumpNodes(string path)
        {
            try
            {
                var lines = new List<string>();
                foreach (var node in _nodes) lines.Add(node.Id + " | " + node.Title + " | " + node.Description);
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                System.IO.File.WriteAllText(path, string.Join("\n", lines));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[NarrativePro] 图节点回执写入失败: " + ex.Message);
            }
        }

        internal static string EventTagSuffix(List<JsonElement> events)
        {
            if (events == null || events.Count == 0) return "";

            int quest = 0, dialogue = 0, other = 0;
            foreach (var element in events)
            {
                if (element.ValueKind != JsonValueKind.Object) { other++; continue; }

                string type = "";
                foreach (var prop in element.EnumerateObject())
                {
                    if (!string.Equals(prop.Name, "type", StringComparison.OrdinalIgnoreCase)) continue;
                    type = prop.Value.ValueKind == JsonValueKind.String ? (prop.Value.GetString() ?? "") : "";
                    break;
                }

                type = type.Trim();
                if (type.EndsWith("Event", StringComparison.OrdinalIgnoreCase)) type = type.Substring(0, type.Length - 5);

                if (string.Equals(type, "StartQuest", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(type, "RestartQuest", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(type, "CompleteQuest", StringComparison.OrdinalIgnoreCase)) quest++;
                else if (string.Equals(type, "BeginDialogue", StringComparison.OrdinalIgnoreCase)) dialogue++;
                else other++;
            }

            var parts = new List<string>();
            if (quest > 0) parts.Add("任务×" + quest);
            if (dialogue > 0) parts.Add("对话×" + dialogue);
            if (other > 0) parts.Add("事件×" + other);
            return parts.Count == 0 ? "" : " ｜ " + string.Join(" ", parts);
        }

        private bool IsSelected(NarrativeSelectionKind kind, string id)
        {
            return _selection.Kind == kind && string.Equals(_selection.Id ?? "", id ?? "", StringComparison.OrdinalIgnoreCase);
        }

        private static Float2 RightCenter(Rectangle rect) => new Float2(rect.Right, rect.Y + rect.Height * 0.5f);

        private static Float2 LeftCenter(Rectangle rect) => new Float2(rect.X, rect.Y + rect.Height * 0.5f);

        // -------------------------------------------------------------------
        // 输入
        // -------------------------------------------------------------------

        public override bool OnMouseDown(Float2 location, MouseButton button)
        {
            if (button == MouseButton.Left)
            {
                var node = HitTest(ToWorld(location));
                if (node != null)
                {
                    // Shift+左键=连线拖拽：不移动节点，只画一条预览线，松开时落到哪个节点就连到哪个节点。
                    if (Input.GetKey(KeyboardKeys.Shift) && CanLinkFrom(node))
                    {
                        Select(new NarrativeSelection(node.Kind, node.Id));
                        _linkFrom = node;
                        _linkCursor = location;
                        _linkActive = true;
                        StartMouseCapture(false);
                        return true;
                    }

                    Select(new NarrativeSelection(node.Kind, node.Id));
                    _dragNode = node;
                    _dragOffset = ToWorld(location) - node.World.Location;
                    _dragMoved = false;
                    StartMouseCapture(false);
                    return true;
                }

                Select(NarrativeSelection.None);
                _panning = true;
                _lastMouse = location;
                StartMouseCapture(false);
                return true;
            }

            if (button == MouseButton.Right)
            {
                var hit = HitTest(ToWorld(location));
                if (hit != null && !(hit.Kind == _selection.Kind && string.Equals(hit.Id, _selection.Id, StringComparison.OrdinalIgnoreCase)))
                    Select(new NarrativeSelection(hit.Kind, hit.Id));

                ContextMenuRequested?.Invoke(_selection, location);
                return true;
            }

            if (button == MouseButton.Middle)
            {
                _panning = true;
                _lastMouse = location;
                StartMouseCapture(false);
                return true;
            }

            return base.OnMouseDown(location, button);
        }

        public override void OnMouseMove(Float2 location)
        {
            if (_linkActive)
            {
                _linkCursor = location;
                return;
            }

            if (_dragNode != null)
            {
                var world = ToWorld(location) - _dragOffset;
                _dragNode.World.Location = world;
                WriteNodePosition(_dragNode, world);
                _dragMoved = true;
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
            if (_linkActive)
            {
                _linkActive = false;
                EndMouseCapture();

                var from = _linkFrom;
                _linkFrom = null;
                var target = HitTest(ToWorld(location));
                if (target != null && target != from && CanLinkTo(target))
                {
                    LinkRequested?.Invoke(new NarrativeSelection(from.Kind, from.Id), new NarrativeSelection(target.Kind, target.Id));
                }
                return true;
            }

            if (_dragNode != null)
            {
                bool moved = _dragMoved;
                _dragNode = null;
                _dragMoved = false;
                EndMouseCapture();
                if (moved) NodeMoved?.Invoke();
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
            _zoom = Mathf.Clamp(_zoom * (delta > 0f ? ScrollStep : 1f / ScrollStep), 0.3f, 2.4f);
            _pan = location - worldUnderCursor * _zoom;
            return true;
        }

        /// <summary>图拿到焦点时的快捷键：Delete 删选中项、F 适配视图、L 自动布局。文本框里的键不会走到这里。</summary>
        public override bool OnKeyDown(KeyboardKeys key)
        {
            if (key == KeyboardKeys.Escape && _linkActive)
            {
                _linkActive = false;
                _linkFrom = null;
                EndMouseCapture();
                return true;
            }

            if (!HasContent) return base.OnKeyDown(key);

            switch (key)
            {
                case KeyboardKeys.Delete:
                    if (_selection.IsNone) return false;
                    DeleteRequested?.Invoke();
                    return true;
                case KeyboardKeys.F:
                    FitToView();
                    return true;
                case KeyboardKeys.L:
                    AutoLayout();
                    NodeMoved?.Invoke();
                    return true;
                case KeyboardKeys.Escape:
                    if (_selection.IsNone) return false;
                    Select(NarrativeSelection.None);
                    return true;
                default:
                    return base.OnKeyDown(key);
            }
        }

        private void WriteNodePosition(SceneNode node, Float2 world)
        {
            if (_quest != null)
            {
                NarrativeAssetStore.SetQuestNodePosition(_quest, node.Id, world.X, world.Y);
            }
            else if (_dialogue != null)
            {
                NarrativeAssetStore.SetDialogueNodePosition(_dialogue, node.Id, world.X, world.Y);
            }
        }
    }
}
#endif
