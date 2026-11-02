using System;
using System.Collections.Generic;
using FlaxEngine;
using FlaxEngine.GUI;
using Game.Character.Attributes;
using HundunWorld.Game.Combat.Skills;
using HundunWorld.Game.Services;
using HundunWorld.Game.UI.Ink.ActionBar;
using HundunWorld.Game.UI.StyleSystem;
using Hud = global::Game.UI.HUD;

namespace HundunWorld.Game.UI.Ink.Pages.Character
{
    /// <summary>
    /// 五行技能树页（docs/SKILL_WUXING_TREE_DESIGN.md 的数据驱动视图）。
    /// <para>
    /// 纵轴五行（金木水火土），横轴七列（白黄玄地天 + 道·相生 + 混沌），每格三枚境章（初/中/极）。
    /// 已学 / 未学 / 已封存（被相生演化融合掉的源技，画斜杠）/ 暂不可学四种状态一目了然；
    /// 已学且未封存的式可直接拖到底部技能栏。数据全部来自服务端下行目录 + skills.json，页面不写死任何招式。
    /// </para>
    /// </summary>
    public class SkillTreePage : ContainerControl, IInkPage, IInkNavigationSource
    {
        private static readonly Float2 PanelSize = new Float2(1400f, 900f);
        private const float TopBarH = 56f;
        private const float ColHeaderH = 26f;
        private const float RowHeaderW = 76f;
        private const float GridPad = 24f;
        private const float DetailW = 380f;
        private const int Columns = 7;
        private const int Rows = 5;

        // PanelSize 是 static readonly（非编译期常量），派生尺寸只能用 readonly 字段承载
        private static readonly float CellW = (PanelSize.X - DetailW - GridPad * 2f - RowHeaderW) / Columns;
        private static readonly float ChipW = CellW / 3f - 4f;
        private const float ChipH = 62f;
        private static readonly float RowH = ChipH + 10f;

        private static readonly string[] ElementNames = { "金", "木", "水", "火", "土" };
        private static readonly string[] ColumnNames = { "白阶", "黄阶", "玄阶", "地阶", "天阶", "道阶", "混沌" };


        private InkPanelElevated _panel;
        private TreeGrid _grid;
        private DetailPanel _detail;
        private LiteButton _learnButton;
        private LiteButton _refineButton;
        private readonly List<Cell> _cells = new();
        private readonly Dictionary<int, SkillConfig> _byId = new();
        private readonly List<SkillConfig> _tree = new();
        private readonly HashSet<int> _learned = new();
        private readonly HashSet<int> _retired = new();

        private CharacterAttributesComponent _boundCharacter;
        private SkillConfig _selected;
        private bool _dataDirty = true;

        /// <summary>已经念过"师承"的那个训练师实体（0 = 当前不在训练师面前）。</summary>
        private ulong _announcedTrainerEntity;

        public event Action<string> NavigationRequested;
        public InkParticleSystem ParticleSystem { get; set; }

        public SkillTreePage()
        {
            AnchorPreset = AnchorPresets.TopLeft;
            BackgroundColor = Color.Transparent;
            ClipChildren = false;
            AutoFocus = false;
            Location = Float2.Zero;

            _panel = new InkPanelElevated
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Size = PanelSize,
            };
            AddChild(_panel);

            BuildTopBar();

            _grid = new TreeGrid(this)
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(GridPad, TopBarH),
                Size = new Float2(PanelSize.X - DetailW - GridPad * 2f, PanelSize.Y - TopBarH - GridPad),
                BackgroundColor = Color.Transparent,
                AutoFocus = false,
            };
            _panel.AddChild(_grid);

            _detail = new DetailPanel(this)
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(PanelSize.X - DetailW - GridPad, TopBarH + GridPad * 0.5f),
                Size = new Float2(DetailW, PanelSize.Y - TopBarH - GridPad * 1.5f),
                BackgroundColor = Color.Transparent,
                AutoFocus = false,
            };
            _panel.AddChild(_detail);

            SkillCatalogService.Instance.Changed += OnCatalogChanged;
            RebuildData();
            RefreshLayout();
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            SkillCatalogService.Instance.Changed -= OnCatalogChanged;
        }

        private void OnCatalogChanged()
        {
            _dataDirty = true;
        }

        /// <summary>绑定本地玩家：亲和度决定领悟门槛是否达标，等级决定阶位是否解锁。</summary>
        public void BindCharacter(CharacterAttributesComponent component)
        {
            _boundCharacter = component;
            _dataDirty = true;
        }

        public void RefreshLayout()
        {
            if (Width <= 0f || Height <= 0f) return;
            _panel.Location = new Float2((Width - PanelSize.X) * 0.5f, (Height - PanelSize.Y) * 0.5f);
        }

        public override void OnParentResized()
        {
            base.OnParentResized();
            RefreshLayout();
        }

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);
            if (_dataDirty) RebuildData();
            AnnounceTrainerIfChanged();
        }

        /// <summary>
        /// 训练师语境变化时把"这位教什么、多少钱"念一遍。
        /// <para>
        /// 为什么要念：技能页本身是五行树，玩家看不出**站在谁面前**、这位 NPC 教的是哪几式。
        /// 不念的话，"点开训练师＝打开一张全门派技能树"——教与不教长得一模一样，
        /// 点错行只会在服务端被拒（还看不出是被谁拒的）。
        /// </para>
        /// <para>这份清单只是导出快照，**能不能学与学费仍由服务端裁决**；这里只做预告。</para>
        /// </summary>
        private void AnnounceTrainerIfChanged()
        {
            var trainer = HundunWorld.Game.Network.TrainerClientService.Instance;
            var entityId = trainer.EntityId;
            if (entityId == _announcedTrainerEntity) return;

            _announcedTrainerEntity = entityId;
            if (entityId == 0UL) return;

            var rows = HundunWorld.Game.Selection.NpcStanceCatalog.GetTrainerRows(trainer.NpcId);
            if (rows.Length == 0)
            {
                Hud.ShowNotification("这位前辈此处不授业", 2.6f, InkWashTheme.GoldBright);
                return;
            }

            var text = new System.Text.StringBuilder("师承：");
            for (var i = 0; i < rows.Length; i++)
            {
                if (i > 0) text.Append('、');
                var name = rows[i].Kind == 1 && _byId.TryGetValue(rows[i].SkillId, out var cfg) && cfg != null
                    ? cfg.SkillName
                    : (rows[i].Kind == 2
                        ? (string.IsNullOrEmpty(rows[i].Label) ? "配方#" + rows[i].SkillId : rows[i].Label)
                        : "技能#" + rows[i].SkillId);
                text.Append(name).Append('（').Append(rows[i].Gold).Append("金）");
            }

            Hud.ShowNotification(text.ToString(), 4.2f, InkWashTheme.GoldBright);
        }

        // ===================================================================
        // 数据装配
        // ===================================================================

        private void BuildTopBar()
        {
            var title = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(GridPad, 14f),
                Size = new Float2(760f, 30f),
                Text = "五行技能树 · 天 地 玄 黄 白 与 相 生 演 化",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 21f),
                TextColor = InkWashTheme.GoldBright,
                HorizontalAlignment = TextAlignment.Near,
                VerticalAlignment = TextAlignment.Center,
            };
            _panel.AddChild(title);

            var close = new LiteButton("返回")
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(PanelSize.X - GridPad - 64f, 14f),
                Size = new Float2(64f, 28f),
            };
            close.Clicked += () => NavigationRequested?.Invoke(InkPageDomIds.BackOneLevel);
            _panel.AddChild(close);

            // 白阶这一档由门派传授直接习得；其余阶位点了只会得到一句"去读书"，
            // 真正的拦断在服务端——LearnMethod 是客户端上报的，客户端的规矩只算 UX。
            _learnButton = new LiteButton("习得")
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(PanelSize.X - DetailW - GridPad + 24f, PanelSize.Y - 78f),
                Size = new Float2(120f, 30f),
                Visible = false,
            };
            _learnButton.Clicked += OnLearnClicked;
            _panel.AddChild(_learnButton);

            // 精研：把已习得的一式往圆满推进。演化式要求源式练满（§6.2），
            // 而没有这个入口时"练满"整条前置在客户端是死路——服务端早就允许升级。
            _refineButton = new LiteButton("精研")
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(PanelSize.X - DetailW - GridPad + 24f + 132f, PanelSize.Y - 78f),
                Size = new Float2(120f, 30f),
                Visible = false,
            };
            _refineButton.Clicked += OnRefineClicked;
            _panel.AddChild(_refineButton);
        }

        private void OnRefineClicked()
        {
            var cfg = _selected;
            if (cfg == null || !IsLearned(cfg) || IsRetired(cfg)) return;
            if (HundunWorld.Game.Network.MessageHandlers.SkillUpgradeHandler.InFlight) return;

            // 满级与"服务端会拒绝"是两回事，网关回包不区分，所以在这里先按权威快照说清楚。
            if (IsMaxed(cfg))
            {
                Hud.ShowNotification($"《{cfg.SkillName}》已臻圆满（第 {MaxLevelOf(cfg)} 重），无从再进",
                    2.2f, InkWashTheme.JadeBright);
                return;
            }

            var player = HundunWorldGame.Instance;
            if (player == null || player.PlayerId == 0UL) return;
            _ = HundunWorld.Game.Network.MessageHandlers.SkillUpgradeHandler.RequestAsync(player.PlayerId, cfg.SkillId);
        }

        /// <summary>权威等级；服务端还没回过目录时返回 0（此时按钮文案不说"第 0 重"，见 LevelNote）。</summary>
        private static int LevelOf(SkillConfig cfg) =>
            cfg == null ? 0 : SkillCatalogService.Instance.LevelOf(cfg.SkillId);

        /// <summary>权威上限；没拿到权威数据时为 0，含义与服务端一致——不可再进。</summary>
        private static int MaxLevelOf(SkillConfig cfg)
        {
            if (cfg == null) return 0;
            return SkillCatalogService.Instance.Learned.TryGetValue(cfg.SkillId, out var s) ? s.MaxLevel : 0;
        }

        /// <summary>与服务端口径一致：MaxLevel&lt;=0 视为不可再进（服务端此时同样拒绝升级）。</summary>
        private static bool IsMaxed(SkillConfig cfg)
        {
            int max = MaxLevelOf(cfg);
            return max <= 0 || LevelOf(cfg) >= max;
        }

        /// <summary>有权威等级才写"第 x/y 重"，否则干脆不提，别把 0/0 当成进度。</summary>
        private static string LevelNote(SkillConfig cfg)
        {
            if (cfg == null || !SkillCatalogService.Instance.HasServerData) return "";
            int max = MaxLevelOf(cfg);
            if (max <= 0) return "";
            return IsMaxed(cfg) ? $" · 已臻圆满（第 {max} 重）" : $" · 第 {LevelOf(cfg)}/{max} 重";
        }

        private void OnLearnClicked()
        {
            var cfg = _selected;
            if (cfg == null || IsLearned(cfg)) return;

            if (cfg.Grade != 1)
            {
                Hud.ShowNotification("此式需研读对应秘籍方可习得（在背包里使用技能书）", 2.4f, InkWashTheme.GoldBright);
                return;
            }

            var blocker = WuxingSkillTree.DescribeBlockers(cfg, _boundCharacter, _learned);
            if (blocker != null)
            {
                Hud.ShowNotification($"条件未具：{blocker}", 2.4f, InkWashTheme.GoldBright);
                return;
            }

            var player = HundunWorldGame.Instance;
            if (player == null || player.PlayerId == 0UL) return;
            // 在训练师面前打开时带实体（服务端按训练师结算学费）；不在训练师面前则为 0，走免费老路。
            var trainer = HundunWorld.Game.Network.TrainerClientService.Instance;
            var trainerEntity = trainer.EntityId;

            // 站在训练师面前时，先看这位到底教不教这一式：不教就当场说明，
            // 而不是发一枪过去等服务端按"没有这条教学行"拒掉（那句拒绝里没有"是谁不教"）。
            // 这份清单是导出快照；真正的收费与门槛仍由服务端判。
            if (trainerEntity != 0UL)
            {
                if (!HundunWorld.Game.Selection.NpcStanceCatalog.TryGetTrainerRow(trainer.NpcId, cfg.SkillId, out var row))
                {
                    Hud.ShowNotification("这位前辈不传授此式", 2.4f, InkWashTheme.GoldBright);
                    return;
                }

                if (row.Gold > 0)
                    Hud.ShowNotification($"向师承请教 {cfg.SkillName}（学费 {row.Gold} 金）", 2.4f, InkWashTheme.GoldBright);
            }

            _ = HundunWorld.Game.Network.MessageHandlers.SkillLearnHandler
                .LearnByTransmissionAsync(player.PlayerId, cfg.SkillId, trainerEntity);
        }

        private void RebuildData()
        {
            _byId.Clear();
            _tree.Clear();

            var all = SkillDatabase.GetAllSkills();
            for (int i = 0; i < all.Count; i++)
            {
                var cfg = all[i];
                if (cfg == null) continue;
                _byId[cfg.SkillId] = cfg;
                if (cfg.Grade >= 1 && cfg.Grade <= Columns) _tree.Add(cfg);
            }

            _learned.Clear();
            foreach (var id in WuxingSkillTree.LearnedIds()) _learned.Add(id);

            // 已封存 = 被某个"已学"演化式融合掉的源技（口径见 WuxingSkillTree）
            _retired.Clear();
            foreach (var id in WuxingSkillTree.ComputeRetired(_learned)) _retired.Add(id);

            LayoutCells();
            UpdateButtonVisibility();
            _dataDirty = false;
        }

        /// <summary>
        /// 两个按钮的可见性：未学→「习得」，已学未封存→「精研」。
        /// 等级每次重拉权威目录都会走到这里（精研回包后就是这样），所以"已臻圆满"能自己变色。
        /// </summary>
        private void UpdateButtonVisibility()
        {
            var cfg = _selected;
            bool learnedUsable = cfg != null && IsLearned(cfg) && !IsRetired(cfg);
            if (_learnButton != null)
                _learnButton.Visible = cfg != null && !IsLearned(cfg) && !IsRetired(cfg);
            if (_refineButton != null)
            {
                _refineButton.Visible = learnedUsable;
                _refineButton.SetLabel(learnedUsable && IsMaxed(cfg) ? "已圆满" : "精研");
            }
        }

        // 排布与解锁规则统一问 WuxingSkillTree，页面只负责画
        private int RowOf(SkillConfig cfg) => WuxingSkillTree.RowOf(cfg);

        private int ColumnOf(SkillConfig cfg) => WuxingSkillTree.ColumnOf(cfg);

        private void LayoutCells()
        {
            _cells.Clear();
            foreach (var cfg in _tree)
            {
                int row = RowOf(cfg);
                int col = ColumnOf(cfg);
                if (row < 0 || row >= Rows || col < 0 || col >= Columns) continue;

                int slot = Math.Max(0, Math.Min(2, cfg.GradeStage - 1));
                float x = RowHeaderW + col * CellW + slot * (CellW / 3f);
                float y = ColHeaderH + row * RowH;
                _cells.Add(new Cell { Skill = cfg, Rect = new Rectangle(x, y, ChipW, ChipH) });
            }
        }

        internal IReadOnlyList<Cell> Cells => _cells;
        internal static string[] ElementRowNames => ElementNames;
        internal static string[] ColumnTitles => ColumnNames;
        internal static string StageGlyph(int stage) => WuxingSkillTree.StageName(stage);
        internal static Color RowColor(int row) => row switch
        {
            0 => InkWashTheme.ElementMetal,
            1 => InkWashTheme.ElementWood,
            2 => InkWashTheme.ElementWater,
            3 => InkWashTheme.ElementFire,
            _ => InkWashTheme.ElementEarth,
        };

        internal bool IsLearned(SkillConfig cfg) => cfg != null && _learned.Contains(cfg.SkillId);
        internal bool IsRetired(SkillConfig cfg) => cfg != null && _retired.Contains(cfg.SkillId);
        internal SkillConfig Selected => _selected;

        internal class Cell
        {
            public SkillConfig Skill;
            public Rectangle Rect;
        }

        // ===================================================================
        // 状态判定与交互
        // ===================================================================

        /// <summary>还差什么才能研读此式；null 表示条件已具。</summary>
        private string UnlockBlocker(SkillConfig cfg) =>
            WuxingSkillTree.DescribeBlockers(cfg, _boundCharacter, _learned);

        internal string StatusText(SkillConfig cfg)
        {
            if (cfg == null) return "";
            if (IsRetired(cfg)) return "已融于演化式（散功可解封）";
            if (IsLearned(cfg)) return $"已习得{LevelNote(cfg)} · 可拖入技能栏";
            string missing = UnlockBlocker(cfg);
            return missing == null ? "条件已具，待研读秘籍" : $"暂不可学：{missing}";
        }

        internal string DetailLine(SkillConfig cfg, int line)
        {
            if (cfg == null) return line == 0 ? "未选中招式" : "";
            switch (line)
            {
                case 0: return cfg.SkillName;
                case 1: return $"{WuxingSkillTree.GradeName(cfg.Grade)} · {StageGlyph(cfg.GradeStage)}境 · 系 {cfg.SeriesId}";
                case 2: return $"倍率 {cfg.DamageMultiplier:0.##}　耗气 {cfg.EnergyCost:0.#}";
                case 3: return $"冷却 {cfg.Cooldown:0.#}s　射程 {cfg.Range:0.#}";
                case 4: return $"作用 {cfg.RangeType}{(cfg.AoeRadius > 0f ? $" · AOE {cfg.AoeRadius:0.#}" : "")}";
                case 5: return $"门槛 等级 {cfg.RequiredLevel} · 领悟 {cfg.ComprehensionRequire}";
                case 6: return StatusText(cfg);
                default: return WuxingSkillTree.DescribeEffects(cfg);
            }
        }


        internal void OnCellClicked(SkillConfig cfg)
        {
            _selected = cfg;
            UpdateButtonVisibility();
            if (cfg == null) return;

            if (IsRetired(cfg))
            {
                Hud.ShowNotification("该式已融进演化式，需先散功才能重新施放", 2.2f, InkWashTheme.JadeBright);
                return;
            }
            if (IsLearned(cfg)) return;

            string blocker = UnlockBlocker(cfg);
            Hud.ShowNotification(blocker == null
                    ? $"{cfg.SkillName}：条件已具，白阶可点右下「习得」由门派传授，其余阶位请在背包里研读对应秘籍"
                    : $"{cfg.SkillName} 暂不可学，缺 {blocker}",
                2.6f, InkWashTheme.GoldBright);
        }

        /// <summary>已学且未封存的式才允许拖上技能栏。</summary>
        internal bool TryBeginDrag(SkillConfig cfg, Float2 screenPos)
        {
            if (!IsLearned(cfg) || IsRetired(cfg)) return false;
            return ActionBarDrag.Begin(ActionBarSlotContent.OfSkill(cfg.SkillId), -1, screenPos);
        }

        // ===================================================================
        // 网格自绘
        // ===================================================================

        private class TreeGrid : Control
        {
            private readonly SkillTreePage _page;
            private SkillConfig _hover;

            public TreeGrid(SkillTreePage page)
            {
                _page = page;
                BackgroundColor = Color.Transparent;
                AutoFocus = false;
            }

            public override void Draw()
            {
                if (!Visible || Width <= 0f) return;

                var headFont = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f).GetFont();
                if (headFont != null)
                {
                    for (int c = 0; c < Columns; c++)
                    {
                        var box = new Rectangle(RowHeaderW + c * CellW, 0f, CellW, ColHeaderH);
                        Render2D.DrawText(headFont, ColumnTitles[c], box, InkWashTheme.TextSecondary,
                            TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
                    }
                    for (int r = 0; r < Rows; r++)
                    {
                        var box = new Rectangle(0f, ColHeaderH + r * RowH, RowHeaderW - 6f, ChipH);
                        Render2D.DrawText(headFont, ElementRowNames[r], box, SkillTreePage.RowColor(r),
                            TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
                    }
                }

                foreach (var cell in _page._cells) DrawCell(cell);

                var noteFont = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f).GetFont();
                if (noteFont != null)
                {
                    var note = new Rectangle(0f, ColHeaderH + Rows * RowH + 10f, Width - 8f, 62f);
                    Render2D.DrawText(noteFont,
                        "相生演化：母行与子行的天阶三式各自圆满后，两式融合为道阶一式；被融合的源技就此封存（画斜杠的格子），不再可施放、不占技能栏，散功可原路解封。横看一行是登阶，竖看一列是同阶五行，绕完五段相生即一次周天。",
                        note, InkWashTheme.TextTertiary, TextAlignment.Near, TextAlignment.Near, TextWrapping.WrapWords);
                }
            }

            private void DrawCell(Cell cell)
            {
                var cfg = cell.Skill;
                bool learned = _page.IsLearned(cfg);
                bool retired = _page.IsRetired(cfg);
                bool hover = _hover == cfg;
                int row = _page.RowOf(cfg);
                var tint = SkillTreePage.RowColor(Math.Max(0, Math.Min(Rows - 1, row)));

                var bg = learned
                    ? new Color(tint.R, tint.G, tint.B, 0.24f)
                    : new Color(InkWashTheme.BaseTertiary.R, InkWashTheme.BaseTertiary.G, InkWashTheme.BaseTertiary.B, 0.5f);
                InkRenderHelper.FillRoundedRectangle(cell.Rect, InkWashTheme.RadiusSm, bg);

                var border = retired ? InkWashTheme.TextDisabled
                    : hover ? InkWashTheme.GoldBright
                    : learned ? tint : InkWashTheme.BorderGoldSubtle;
                InkRenderHelper.DrawRoundedRectangle(cell.Rect, InkWashTheme.RadiusSm, border, hover ? 2f : 1f);

                var font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 17f).GetFont();
                if (font != null)
                {
                    var color = retired ? InkWashTheme.TextDisabled
                        : learned ? InkWashTheme.PaperBright : InkWashTheme.TextTertiary;
                    Render2D.DrawText(font, SkillTreePage.StageGlyph(cfg.GradeStage), cell.Rect, color,
                        TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
                }

                if (retired)
                {
                    // 斜杠比单纯变灰更能说明"被融合掉了"而不是"还没学"
                    Render2D.DrawLine(new Float2(cell.Rect.X + 5f, cell.Rect.Bottom - 5f),
                        new Float2(cell.Rect.Right - 5f, cell.Rect.Y + 5f), InkWashTheme.TextDisabled, 1.5f);
                }

                if (_page._selected == cfg)
                {
                    var halo = new Rectangle(cell.Rect.X - 2f, cell.Rect.Y - 2f, cell.Rect.Width + 4f, cell.Rect.Height + 4f);
                    InkRenderHelper.DrawRoundedRectangle(halo, InkWashTheme.RadiusSm, InkWashTheme.GoldPrimary, 2f);
                }
            }

            private SkillConfig HitTest(Float2 location)
            {
                var cells = _page._cells;
                for (int i = 0; i < cells.Count; i++)
                {
                    var r = cells[i].Rect;
                    if (location.X >= r.X && location.X <= r.Right && location.Y >= r.Y && location.Y <= r.Bottom)
                        return cells[i].Skill;
                }
                return null;
            }

            public override void OnMouseMove(Float2 location)
            {
                base.OnMouseMove(location);
                _hover = HitTest(location);
            }

            public override void OnMouseLeave()
            {
                base.OnMouseLeave();
                _hover = null;
            }

            public override bool OnMouseDown(Float2 location, MouseButton button)
            {
                var cfg = HitTest(location);
                if (cfg == null) return base.OnMouseDown(location, button);

                // 左键按下先当"起拖"；拖不起来（未学/已封存）才落回点击说明
                if (button == MouseButton.Left && _page.TryBeginDrag(cfg, PointToScreen(location)))
                    return true;

                _page.OnCellClicked(cfg);
                return true;
            }
        }

        /// <summary>右侧详情列：自绘，免得一堆 Label 的尺寸与换行到处对齐。</summary>
        private class DetailPanel : Control
        {
            private readonly SkillTreePage _page;

            public DetailPanel(SkillTreePage page)
            {
                _page = page;
                BackgroundColor = Color.Transparent;
                AutoFocus = false;
            }

            public override void Draw()
            {
                if (!Visible || Width <= 0f) return;

                var rect = new Rectangle(0f, 0f, Width, Height);
                InkRenderHelper.FillRoundedRectangle(rect, InkWashTheme.RadiusMd, new Color(
                    InkWashTheme.BaseSecondary.R, InkWashTheme.BaseSecondary.G, InkWashTheme.BaseSecondary.B, 0.6f));
                InkRenderHelper.DrawRoundedRectangle(rect, InkWashTheme.RadiusMd, InkWashTheme.BorderGoldSubtle, 1f);

                var sel = _page._selected;
                var titleFont = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 18f).GetFont();
                var bodyFont = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f).GetFont();
                if (titleFont == null || bodyFont == null) return;

                float y = 14f;
                Render2D.DrawText(titleFont, _page.DetailLine(sel, 0),
                    new Rectangle(16f, y, Width - 32f, 26f), InkWashTheme.GoldBright,
                    TextAlignment.Near, TextAlignment.Center, TextWrapping.NoWrap);
                y += 34f;

                for (int line = 1; line <= 6; line++)
                {
                    var color = line == 6 && sel != null && !_page.IsLearned(sel) && !_page.IsRetired(sel)
                        ? InkWashTheme.JadeBright : InkWashTheme.TextSecondary;
                    Render2D.DrawText(bodyFont, _page.DetailLine(sel, line),
                        new Rectangle(16f, y, Width - 32f, 20f), color,
                        TextAlignment.Near, TextAlignment.Center, TextWrapping.NoWrap);
                    y += 22f;
                }

                y += 6f;
                Render2D.DrawLine(new Float2(16f, y), new Float2(Width - 16f, y), InkWashTheme.BorderGoldSubtle, 1f);
                y += 10f;

                Render2D.DrawText(bodyFont, _page.DetailLine(sel, 7),
                    new Rectangle(16f, y, Width - 32f, Height - y - 60f), InkWashTheme.TextTertiary,
                    TextAlignment.Near, TextAlignment.Near, TextWrapping.WrapWords);

                if (sel != null && _page.IsLearned(sel) && !_page.IsRetired(sel))
                {
                    string hint = SkillTreePage.IsMaxed(sel)
                        ? "按住此格可拖到底部技能栏；此式已臻圆满，演化门槛中的「源式圆满」已满足"
                        : "按住此格可拖到底部技能栏；点右下「精研」往圆满推进一重（演化式要求源式练满）";
                    Render2D.DrawText(bodyFont, hint,
                        new Rectangle(16f, Height - 48f, Width - 32f, 40f), InkWashTheme.TextJade,
                        TextAlignment.Near, TextAlignment.Near, TextWrapping.WrapWords);
                }
            }
        }

        /// <summary>顶栏返回钮（不复用战斗 HUD 的私有 InkNavButton，避免跨页耦合）。</summary>
        private class LiteButton : Control
        {
            private string _text;
            private bool _hover;

            public event Action Clicked;

            public LiteButton(string text)
            {
                _text = text;
                BackgroundColor = Color.Transparent;
                AutoFocus = false;
            }

            /// <summary>改文案（自绘按钮没有现成的 Label 可写）。</summary>
            public void SetLabel(string text) => _text = text;

            public override void Draw()
            {
                var rect = new Rectangle(0f, 0f, Width, Height);
                InkRenderHelper.FillRoundedRectangle(rect, InkWashTheme.RadiusSm, new Color(
                    InkWashTheme.BaseTertiary.R, InkWashTheme.BaseTertiary.G, InkWashTheme.BaseTertiary.B, 0.7f));
                InkRenderHelper.DrawRoundedRectangle(rect, InkWashTheme.RadiusSm,
                    _hover ? InkWashTheme.GoldBright : InkWashTheme.BorderGoldSubtle, 1f);

                var font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f).GetFont();
                if (font != null)
                    Render2D.DrawText(font, _text, rect, InkWashTheme.TextSecondary,
                        TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
            }

            public override void OnMouseEnter(Float2 location) { _hover = true; base.OnMouseEnter(location); }
            public override void OnMouseLeave() { _hover = false; base.OnMouseLeave(); }

            public override bool OnMouseUp(Float2 location, MouseButton button)
            {
                if (button == MouseButton.Left && location.X >= 0f && location.Y >= 0f && location.X <= Width && location.Y <= Height)
                    Clicked?.Invoke();
                return base.OnMouseUp(location, button);
            }
        }
    }
}
