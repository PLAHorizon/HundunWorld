using FlaxEngine;
using FlaxEngine.GUI;
using Game.Character.Attributes;
using HundunWorld.Game.UI.Ink;
using HundunWorld.Game.UI.StyleSystem;

namespace HundunWorld.Game.UI.GameMain
{
    /// <summary>
    /// 名牌宿主：由 <see cref="MainUIManager"/> 在水墨 UICanvas（InkWashUICanvas）根下创建的
    /// 全屏容器，绘制在最上层。所有角色的名牌控件都挂在其下；其 Visible 由 MainUIManager
    /// 按当前页面（仅战斗 HUD 现场页显示）统一开关，从而一次隐藏全部名牌。
    /// </summary>
    public static class NameplateHost
    {
        /// <summary>当前宿主容器；未进入游戏世界时为 null。</summary>
        public static ContainerControl Current;
    }

    /// <summary>
    /// 角色头顶名牌组件：挂在每个角色 Actor 上（本地玩家 + 远程玩家），每帧把自身头顶世界坐标
    /// 投影到屏幕，在共享宿主 <see cref="NameplateHost.Current"/> 内定位并绘制血条/名称/等级/称号。
    /// 控件随 Actor 销毁（OnDestroy）自动回收。
    /// 注意：Flax 世界单位是厘米，HeadOffset / MaxDistance 均为厘米。
    /// </summary>
    public class NameplateUI : Script
    {
        // 头顶高度（厘米）：角色约 180cm 高，名牌上浮到 215cm 处
        private const float HeadOffset = 215f;
        // 超过此距离隐藏名牌（厘米），避免远处密集名牌堆叠
        private const float MaxDistance = 6000f;

        private NameplateWidget _widget;

        // 尸体拾取提示（CORPSE-LOOT-PATCH）：死怪**不挂名牌**，但没有提示玩家只看到一具模型，
        // 不知道还能按 G。这里单独画一句"按 G 拾取"，可见性由快照的 CorpseInteractable 位驱动。
        private const float LootPromptWidth = 116f;
        private const float LootPromptHeight = 22f;
        private RoundedPanel _lootPrompt;
        private Label _lootPromptText;
        private bool _lootPromptShown;

        /// <summary>
        /// 本地观察者的等级（0 = 还不知道）。
        /// <para>怪物名牌的让分色要看**看的人**是多少级，所以这是全工程唯一一份"我几级"的缓存点。
        /// 记不到时（刚进场/属性包还没到）名牌退回绝对等级配色，不做"猜一个等级"。</para>
        /// </summary>
        private static int _localViewerLevel;

        public override void OnUpdate()
        {
            var host = NameplateHost.Current;
            if (host == null || !host.Visible)
            {
                _widget?.SetVisible(false);
                HideLootPrompt();
                return;
            }

            if (_widget == null)
                _widget = new NameplateWidget(host);

            var camera = Camera.MainCamera;
            if (camera == null)
            {
                _widget.SetVisible(false);
                return;
            }

            var attrs = Actor.GetScript<CharacterAttributesComponent>();
            var tag = Actor.GetScript<HundunWorld.Game.Selection.SelectableEntityTag>();
            bool isPlayerLike = tag == null ||
                                tag.Kind == HundunWorld.Game.Selection.SelectableKind.Player ||
                                tag.Kind == HundunWorld.Game.Selection.SelectableKind.Npc;

            // 记录"我"的等级：怪物体名的让分色必须由**观看者**的等级决定（换号看同一只怪，颜色会变）。
            // 记不到就退回 0，让 SetContent 用绝对等级色，绝不会因此抛异常或整块名牌不显示。
            if (isPlayerLike && attrs != null && attrs.Level > 0 && IsLocalPlayerActor())
                _localViewerLevel = attrs.Level;

            // 尸体不挂名牌（WoW 的怪尸既没有名字也没有血条，只有躺着的模型）。
            // 服务端在 corpse_seconds 窗口内仍然把这只怪下发进快照，所以必须在这里收掉，
            // 否则玩家看到的是一只"头顶还挂着满名牌"的死怪。
            // **但可拾取的尸体要给一句提示**：否则玩家只看到地上一个模型，不知道按 G 能取。
            if (attrs != null && attrs.IsDead && !isPlayerLike)
            {
                UpdateLootPrompt(host, camera, tag);
                _widget.SetVisible(false);
                return;
            }
            HideLootPrompt();

            string name = attrs != null && !string.IsNullOrEmpty(attrs.Nickname)
                ? attrs.Nickname
                : (!string.IsNullOrEmpty(tag?.DisplayName)
                    ? tag.DisplayName
                    : (Actor.GetScript<RemotePlayerActor>()?.PlayerName ?? Actor.Name));
            int level = attrs != null && attrs.Level > 0 ? attrs.Level : (tag?.Level ?? 0);
            float curHp = attrs != null ? attrs.CurrentHealth : 1f;
            float maxHp = attrs != null && attrs.MaxHealth > 0f ? attrs.MaxHealth : 1f;

            // NPC 的名字颜色由**立场**决定（绿/黄/红），与怪物那条"让分色"是正交的两条轴。
            // 解析不到（不是 NPC / 表里没有）就留 −1，让 SetContent 走原逻辑。
            int npcStance = -1;
            if (tag != null && tag.Kind == HundunWorld.Game.Selection.SelectableKind.Npc)
            {
                if (HundunWorld.Game.Selection.NpcStanceCatalog.TryResolve(tag.TemplateId, tag.ModelKey, out var resolvedStance))
                    npcStance = resolvedStance;
            }

            // 头顶 !/?：按**这只 NPC 自己的**清单算（清单由同步系统在它出现时各拉一份）。
            // 以前只有"刚交互过的那个 NPC"能出符号——因为客户端只缓存了一份清单；
            // 现在按实体缓存，走到谁附近谁就带符号，交互与否都不影响。
            string questMark = null;
            Color? questMarkColor = null;
            if (tag != null && tag.Kind == HundunWorld.Game.Selection.SelectableKind.Npc)
            {
                var quests = HundunWorld.Game.Network.QuestClientService.Instance;
                var offers = quests.GetOffersFor(tag.EntityId);
                // 交付者集合与"条件不足条数"都是**按这只 NPC** 的服务端结论：
                // 身上的任务清单是全局的，直接读它会把 A 的交付结论画到 B 头上。
                var (markKind, markCount) = HundunWorld.Game.Selection.NpcQuestMark.Resolve(
                    offers, quests.GetQuests(),
                    quests.GetLockedOfferCountFor(tag.EntityId),
                    quests.GetTurnInQuestIdsFor(tag.EntityId));

                if (markKind != HundunWorld.Game.Selection.NpcQuestMarkKind.None)
                {
                    var glyph = HundunWorld.Game.Selection.NpcQuestMark.GlyphOf(markKind);
                    questMark = markCount > 1 ? glyph + markCount : glyph;
                    var grey = markKind == HundunWorld.Game.Selection.NpcQuestMarkKind.InProgress
                               || markKind == HundunWorld.Game.Selection.NpcQuestMarkKind.Locked;
                    questMarkColor = grey
                        ? NameplateWidget.UnattackableNameColor   // 复用"此刻不可用"的灰，避免再造一种色
                        : NameplateWidget.QuestGoldColor;
                }
            }

            // 头顶世界坐标 → 屏幕坐标（世界单位：厘米）
            Vector3 head = Actor.Position + Vector3.Up * HeadOffset;
            var camT = camera.Transform;
            float dot = Vector3.Dot(head - camT.Translation, camT.Forward);
            float dist = Vector3.Distance(head, camT.Translation);
            camera.ProjectPoint(head, out var screen);

            // 视口边界用宿主画布的像素尺寸判定：Flax 的 Camera.Viewport 是归一化 [0..1]，
            // 拿它和 ProjectPoint 返回的像素坐标比较会把所有名牌误判为“屏幕外”而永不显示。
            bool inBounds = screen.X >= 0f && screen.X <= host.Width && screen.Y >= 0f && screen.Y <= host.Height;

            if (dot <= 0f || dist > MaxDistance || !inBounds)
            {
                _widget.SetVisible(false);
                return;
            }

            _widget.SetVisible(true);
            _widget.Location = new Float2(screen.X - NameplateWidget.PlateWidth * 0.5f,
                                          screen.Y - NameplateWidget.PlateHeight);
            _widget.SetContent(name, level, curHp / maxHp, attrs?.Title, isPlayerLike, _localViewerLevel, tag?.Rank ?? 0,
                              attrs != null && attrs.Unattackable, npcStance, questMark, questMarkColor);

            // 施法条：状态由服务端的起手/收条事件写进标记（见 HundunWorldGame.TryApplyMonsterCast），
            // 这里只负责"每帧推进本地时钟 + 画出来"。吟唱秒数在本地累加而不是取引擎绝对时钟，
            // 这样暂停/切后台回来不会出现一根已经跑完的条子。
            if (tag != null && tag.IsCasting)
                tag.AdvanceCast(Time.DeltaTime);
            _widget.SetCast(tag != null && tag.IsCasting, tag?.CastProgress ?? 0f,
                            tag?.CastSchoolMask ?? 0, tag != null && tag.CastInterruptible);

            // 喊话气泡：文案与倒计时都在标记里（服务端只发一次号，收的动作本地判）。
            // 逐帧只在文本真的变了时才写 Label——每帧写 Text 会让这层重排文本。
            if (tag != null) tag.AdvanceShout(Time.DeltaTime);
            _widget.SetShout(tag != null && tag.HasShout ? tag.ShoutText : string.Empty);
        }

        /// <summary>
        /// 当前这个 Actor 是不是"我"（本地玩家）。
        /// <para>判据用 <c>PlayerController</c>：它只挂在本地玩家上，远程玩家走
        /// <c>RemotePlayerActor</c>（DS 通道），二者互斥。不用实体 ID 对照——
        /// <c>NetworkEntityIdComponent</c> 是 Arch ECS 的结构而非 Flax 脚本，取不到。</para>
        /// </summary>
        private bool IsLocalPlayerActor() => Actor.GetScript<HundunWorld.Game.PlayerController>() != null;

        /// <summary>
        /// 尸体拾取提示（CORPSE-LOOT-PATCH）：只在"快照状态位说可拾取"时出现，并把尸体头顶投影到屏幕。
        /// 日志是**边沿触发**（第一次显示/收起各一行）：像素验收靠客户端日志核对它到底有没有出现，
        /// 逐帧打会把日志刷满且看不出"什么时候出现的"。
        /// </summary>
        private void UpdateLootPrompt(ContainerControl host, Camera camera, HundunWorld.Game.Selection.SelectableEntityTag tag)
        {
            if (tag == null || !tag.CorpseInteractable)
            {
                HideLootPrompt();
                return;
            }
            if (_lootPrompt == null)
            {
                _lootPrompt = new RoundedPanel
                {
                    Parent = host,
                    AnchorPreset = AnchorPresets.TopLeft,
                    Size = new Float2(LootPromptWidth, LootPromptHeight),
                    BackgroundColor = new Color(0.06f, 0.06f, 0.08f, 0.55f),
                    CornerRadius = 4f,
                    Visible = false,
                };
                _lootPromptText = new Label
                {
                    Parent = _lootPrompt,
                    Bounds = new Rectangle(0, 0, LootPromptWidth, LootPromptHeight),
                    Text = "按 G 拾取",
                    TextColor = new Color(0.97f, 0.84f, 0.36f),
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                    HorizontalAlignment = TextAlignment.Center,
                    VerticalAlignment = TextAlignment.Center,
                };
            }

            Vector3 head = Actor.Position + Vector3.Up * HeadOffset;
            var camT = camera.Transform;
            float dot = Vector3.Dot(head - camT.Translation, camT.Forward);
            float dist = Vector3.Distance(head, camT.Translation);
            camera.ProjectPoint(head, out var screen);
            bool inBounds = screen.X >= 0f && screen.X <= host.Width && screen.Y >= 0f && screen.Y <= host.Height;
            bool show = dot > 0f && dist <= MaxDistance && inBounds;
            _lootPrompt.Visible = show;
            if (show)
                _lootPrompt.Location = new Float2(screen.X - LootPromptWidth * 0.5f, screen.Y - LootPromptHeight * 0.5f);
            if (show != _lootPromptShown)
            {
                _lootPromptShown = show;
                FlaxEngine.Debug.Log($"[CorpsePrompt] entity={tag.EntityId} visible={(show ? 1 : 0)} dist={(int)dist}");
            }
        }

        private void HideLootPrompt()
        {
            if (_lootPrompt == null) return;
            _lootPrompt.Visible = false;
            if (!_lootPromptShown) return;
            _lootPromptShown = false;
            var tag = Actor.GetScript<HundunWorld.Game.Selection.SelectableEntityTag>();
            FlaxEngine.Debug.Log($"[CorpsePrompt] entity={(tag != null ? tag.EntityId : 0UL)} visible=0");
        }

        public override void OnDestroy()
        {
            _widget?.Dispose();
            _widget = null;
            _lootPrompt?.Dispose();
            _lootPrompt = null;
        }
    }

    /// <summary>单个角色名牌控件：称号 + 名称/等级 + 血条。</summary>
    internal class NameplateWidget
    {
        internal const float PlateWidth = 160f;
        internal const float PlateHeight = 58f;
        private const float BarWidth = 132f;
        private const float BarHeight = 8f;

        /// <summary>敌对实体（怪物）的名字配色，与玩家的白字区分。</summary>
        private static readonly Color HostileNameColor = new Color(0.92f, 0.45f, 0.38f);

        /// <summary>任务符号的黄色（可接/可交付）。</summary>
        internal static readonly Color QuestGoldColor = new Color(0.97f, 0.84f, 0.36f);

        /// <summary>NPC 友方绿名（WoW 观测：可交易/可交任务的 NPC 是一眼可辨的绿）。</summary>
        private static readonly Color FriendlyNpcNameColor = new Color(0.42f, 0.85f, 0.45f);

        /// <summary>NPC 中立黄名（两侧都能打，打了谁就得罪谁）。</summary>
        private static readonly Color NeutralNpcNameColor = new Color(0.95f, 0.84f, 0.35f);

        /// <summary>此刻打不到的实体（正在回位的怪）：名字转灰，与"灰名=打不动了"同一族视觉语言。</summary>
        internal static readonly Color UnattackableNameColor = new Color(0.55f, 0.58f, 0.62f);

        /// <summary>同上，血条底色转灰——血量颜色讲的是"还剩多少"，这里要说的是"这一手不会落"。</summary>
        private static readonly Color UnattackableBarColor = new Color(0.42f, 0.44f, 0.48f, 0.95f);

        private readonly Panel _root;
        private readonly Label _title;
        private readonly Label _name;
        private readonly RoundedPanel _barFill;

        /// <summary>头顶任务符号标签（`!` / `?`，可见性由 <see cref="SetContent"/> 控制）。</summary>
        private readonly Label _questMark;
        private readonly RoundedPanel _castBg;
        private readonly RoundedPanel _castFill;
        private float _lastRatio = -1f;
        private bool _lastUnattackable;
        private float _lastCastProgress = -1f;
        private int _lastCastSchoolMask = -1;
        private bool? _lastCastInterruptible;
        private bool _lastCastVisible;
        private Label _shout;
        private string _lastShoutText = string.Empty;

        /// <summary>
        /// 挂/收一句喊话。只在文本真的变了时动 Label——
        /// 逐帧写 <c>Text</c> 会让文本层每帧重排，而一句话 60 帧里 59 帧是同一句。
        /// </summary>
        public void SetShout(string text)
        {
            var next = text ?? string.Empty;
            if (next == _lastShoutText) return;
            _lastShoutText = next;
            _shout.Text = next;
            _shout.Visible = next.Length > 0;
        }

        internal NameplateWidget(ContainerControl parent)
        {
            _root = new Panel
            {
                Parent = parent,
                AnchorPreset = AnchorPresets.TopLeft,
                Size = new Float2(PlateWidth, PlateHeight),
                BackgroundColor = Color.Transparent,
                ClipChildren = false,
            };

            _title = new Label
            {
                Parent = _root,
                Bounds = new Rectangle(0, 0, PlateWidth, 16),
                Text = string.Empty,
                TextColor = Color.White,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 11f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };

            _name = new Label
            {
                Parent = _root,
                Bounds = new Rectangle(0, 17, PlateWidth, 18),
                Text = string.Empty,
                TextColor = Color.White,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };

            // 头顶任务符号（WoW 的 ! / ?）：浮在名牌右上角，不占名字与血条的位置。
            _questMark = new Label
            {
                Parent = _root,
                Bounds = new Rectangle(PlateWidth - 22, -20, 22, 20),
                Text = string.Empty,
                TextColor = QuestGoldColor,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 16f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                Visible = false,
            };

            float barX = (PlateWidth - BarWidth) * 0.5f;
            var barBg = new RoundedPanel
            {
                Parent = _root,
                Bounds = new Rectangle(barX, 38, BarWidth, BarHeight),
                BackgroundColor = new Color(0.1f, 0.05f, 0.05f, 0.85f),
                CornerRadius = 3f,
            };

            _barFill = new RoundedPanel
            {
                Parent = barBg,
                Bounds = new Rectangle(0, 0, BarWidth, BarHeight),
                BackgroundColor = new Color(0.2f, 0.8f, 0.25f, 0.95f),
                CornerRadius = 3f,
            };

            // 施法条贴在血条下方（y=49，6 高，仍在 PlateHeight=58 内）。
            _castBg = new RoundedPanel
            {
                Parent = _root,
                Bounds = new Rectangle(barX, CastBarY, BarWidth, CastBarHeight),
                BackgroundColor = InterruptibleCastFrame,
                CornerRadius = 2f,
                Visible = false,
            };
            _castFill = new RoundedPanel
            {
                Parent = _castBg,
                Bounds = new Rectangle(0, 0, 0f, CastBarHeight),
                BackgroundColor = NeutralCastColor,
                CornerRadius = 2f,
            };

            // 进战喊话：浮在名牌上方的世界气泡（WoW 的怪物喊话是"看得见的一句话"，
            // 不是聊天框里的一行——近处几只怪同时开口时聊天框会被刷满，而气泡各归各的怪）。
            _shout = new Label
            {
                Parent = _root,
                Bounds = new Rectangle(-70, ShoutY, PlateWidth + 140, ShoutHeight),
                Text = string.Empty,
                TextColor = ShoutColor,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 13f),
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                Visible = false,
            };
        }

        /// <summary>喊话气泡所在行（名牌上方）。</summary>
        private const float ShoutY = -34f;
        private const float ShoutHeight = 18f;
        private static readonly Color ShoutColor = new Color(0.96f, 0.86f, 0.55f, 0.98f);

        /// <summary>施法条所在行（血条下方）。</summary>
        private const float CastBarY = 49f;
        private const float CastBarHeight = 6f;

        /// <summary>可打断时的描底色（WoW 里这条金边就是"现在有打断窗口"的信号）。</summary>
        private static readonly Color InterruptibleCastFrame = new Color(0.78f, 0.62f, 0.22f, 0.95f);

        /// <summary>不可打断时的底色：与金边一眼分得开，玩家才知道"这个窗口打断不了"。</summary>
        private static readonly Color UninterruptibleCastFrame = new Color(0.16f, 0.1f, 0.1f, 0.9f);

        /// <summary>系别未定/纯物理时的条子色（中性，不假装是某个五行颜色）。</summary>
        private static readonly Color NeutralCastColor = new Color(0.72f, 0.74f, 0.78f, 0.95f);

        internal Float2 Location
        {
            get => _root.Location;
            set => _root.Location = value;
        }

        internal void SetVisible(bool visible) => _root.Visible = visible;

        internal void SetContent(string name, int level, float hpRatio, string title = null, bool showCultivationTitle = true, int viewerLevel = 0, int rank = 0,
                                bool unattackable = false, int npcStance = -1, string questMark = null, Color? questMarkColor = null)
        {
            hpRatio = Mathf.Clamp(hpRatio, 0f, 1f);
            // rank≥3（首领/世界BOSS）恒为骷髅档：WoW 里这类怪**不显示数字等级**，
            // 硬显示一个数字会给出一个比"看不到"更误导的信息。
            var skullRanked = rank >= 3;

            if (showCultivationTitle)
            {
                // 服务端下发的称号优先；未佩戴/旧服务端无此字段时回退按等级推导的阶梯称号。
                _title.Text = string.IsNullOrEmpty(title) ? TitleForLevel(level) : title;
                _title.TextColor = TitleColorForLevel(level);
                _name.TextColor = Color.White;
            }
            else
            {
                // 怪物/采集点没有修炼境界，硬套阶梯称号会把"墨麒麟 Lv.12"标成"外门弟子"。
                _title.Text = string.Empty;
                // 让分色：**直接调服务端同一份内核**（MonsterDifficultyCore 物理上在客户端装的
                // Horizon.Game.Sim.Contracts 里，命名空间仍是 Horizon.Game.Sim.Monsters），
                // 不在客户端重写判据 —— 否则"名字灰了却还有经验"这种不一致没有任何地方能发现。
                // viewerLevel 未知（刚进场/属性包未到）时退回原有的敌对红，不做猜测。
                _name.TextColor = viewerLevel > 0 && level > 0
                    ? ConcessionColor(Horizon.Game.Sim.Monsters.MonsterDifficultyCore.Of(level, viewerLevel,
                        (Horizon.Game.Sim.Monsters.MonsterRank)Mathf.Clamp(rank, 0, 4)))
                    : HostileNameColor;
            }
            // 首领级不显示数字（骷髅标记已表达"我打不动/等级未知"这层信息）
            _name.Text = skullRanked ? $"{name} ??" : (level > 0 ? $"{name} Lv.{level}" : name);

            // NPC 的立场色：与上面两条分支正交（它答的是"我该把它当友方还是敌人"，不是"它多难打"）。
            // 必须在让分色/白色之后覆盖：NPC 一律 isPlayerLike=true，会先被刷成白色。
            if (npcStance == HundunWorld.Game.Selection.NpcStanceCatalog.StanceHostile)
                _name.TextColor = HostileNameColor;
            else if (npcStance == HundunWorld.Game.Selection.NpcStanceCatalog.StanceNeutral)
                _name.TextColor = NeutralNpcNameColor;
            else if (npcStance == HundunWorld.Game.Selection.NpcStanceCatalog.StanceFriendly)
                _name.TextColor = FriendlyNpcNameColor;

            // "此刻打不到"（正在回位的怪，服务端状态位）要把名字和血条一起转灰：
            // 光靠"按了没反应"分辨不出"打不动"与"卡了"，而让分色/敌对红继续留着会承诺一个做不到的事。
            // 放在两个分支之后：它覆盖的是"能不能打"，与"这只怪对我有多难"是两条正交的信息。
            if (unattackable) _name.TextColor = UnattackableNameColor;

            // 任务符号：空串 = 不显示。颜色由调用方给（可交付/可接=金，进行中=灰）。
            if (_questMark != null)
            {
                _questMark.Visible = !string.IsNullOrEmpty(questMark);
                _questMark.Text = questMark ?? string.Empty;
                _questMark.TextColor = questMarkColor ?? QuestGoldColor;
            }

            if (Mathf.Abs(hpRatio - _lastRatio) > 0.001f || unattackable != _lastUnattackable)
            {
                _lastRatio = hpRatio;
                _lastUnattackable = unattackable;
                var b = _barFill.Bounds;
                b.Size.X = BarWidth * hpRatio;
                _barFill.Bounds = b;

                _barFill.BackgroundColor = unattackable
                    ? UnattackableBarColor
                    : hpRatio > 0.5f
                        ? new Color(0.2f, 0.8f, 0.25f, 0.95f)
                        : hpRatio > 0.25f
                            ? new Color(0.9f, 0.8f, 0.2f, 0.95f)
                            : new Color(0.85f, 0.2f, 0.2f, 0.95f);
            }
        }

        /// <summary>
        /// 画施法条。与血条同一套"值没变就不重画"的纪律：名牌每帧都跑，
        /// 而一根 1.2 秒的条子里绝大多数帧只有宽度在变，底色/描边只在起手时变一次。
        /// </summary>
        /// <param name="active">该实体是否正在吟唱。</param>
        /// <param name="progress">0..1 进度（由标记按本地累加秒数算出）。</param>
        /// <param name="schoolMask">伤害系别位（<c>MonsterSchoolBits</c> 口径）。</param>
        /// <param name="interruptible">可打断（决定描底色，玩家据此判断现在打断有没有用）。</param>
        internal void SetCast(bool active, float progress, int schoolMask, bool interruptible)
        {
            if (active != _lastCastVisible)
            {
                _lastCastVisible = active;
                _castBg.Visible = active;
            }
            if (!active) return;

            if (schoolMask != _lastCastSchoolMask)
            {
                _lastCastSchoolMask = schoolMask;
                _castFill.BackgroundColor = SchoolColorFor(schoolMask);
            }

            if (_lastCastInterruptible != interruptible)
            {
                _lastCastInterruptible = interruptible;
                _castBg.BackgroundColor = interruptible ? InterruptibleCastFrame : UninterruptibleCastFrame;
            }

            var clamped = progress < 0f ? 0f : (progress > 1f ? 1f : progress);
            if (Mathf.Abs(clamped - _lastCastProgress) > 0.001f)
            {
                _lastCastProgress = clamped;
                var b = _castFill.Bounds;
                b.Size.X = BarWidth * clamped;
                _castFill.Bounds = b;
            }
        }

        /// <summary>
        /// 系别位 → 五行色（复用工程既有的水墨五行色板，不在这里另调一套配色）。
        /// <para>多系技能取<b>最低位</b>那一系：一根条子只有一个颜色，规则必须稳定，
        /// 见 <c>MonsterSchoolBits.FirstDomainOf</c>。位号与全工程的域号同源（1金 2木 3水 4火 5土），
        /// 所以"墨麒麟（水）的技能"和"条子是水的颜色"不会是两件事。</para>
        /// </summary>
        private static Color SchoolColorFor(int schoolMask)
        {
            switch (Horizon.Game.Message.Sync.MonsterSchoolBits.FirstDomainOf(schoolMask))
            {
                case 1: return InkWashTheme.ElementColor(InkWashTheme.InkElement.Metal);
                case 2: return InkWashTheme.ElementColor(InkWashTheme.InkElement.Wood);
                case 3: return InkWashTheme.ElementColor(InkWashTheme.InkElement.Water);
                case 4: return InkWashTheme.ElementColor(InkWashTheme.InkElement.Fire);
                case 5: return InkWashTheme.ElementColor(InkWashTheme.InkElement.Earth);
                default: return NeutralCastColor;   // 纯物理或未定系别：不给假颜色
            }
        }

        internal void Dispose()
        {
            if (_root != null && _root.Parent != null)
                _root.Parent.RemoveChild(_root);
        }

        /// <summary>修炼称号阶梯：按等级映射称号与配色（低阶灰、高阶鎏金）。</summary>
        /// <summary>
        /// 让分档 → 名牌文字色（WoW 的灰/绿/黄/橙/红/骷髅六档）。
        /// <para>与编辑器的曲线分带、服务端的经验断供共用同一个枚举；这里只负责"给个颜色"，
        /// 判据一律留在 <c>MonsterDifficultyCore</c>。</para>
        /// </summary>
        private static Color ConcessionColor(Horizon.Game.Sim.Monsters.MonsterDifficulty difficulty)
        {
            switch (difficulty)
            {
                case Horizon.Game.Sim.Monsters.MonsterDifficulty.Gray: return new Color(0.55f, 0.55f, 0.55f);
                case Horizon.Game.Sim.Monsters.MonsterDifficulty.Green: return new Color(0.35f, 0.78f, 0.4f);
                case Horizon.Game.Sim.Monsters.MonsterDifficulty.Yellow: return new Color(0.93f, 0.84f, 0.28f);
                case Horizon.Game.Sim.Monsters.MonsterDifficulty.Orange: return new Color(0.92f, 0.58f, 0.18f);
                case Horizon.Game.Sim.Monsters.MonsterDifficulty.Red: return new Color(0.88f, 0.26f, 0.22f);
                default: return new Color(0.8f, 0.22f, 0.6f);   // 骷髅/首领
            }
        }

        private static string TitleForLevel(int level)
        {
            if (level <= 9) return "杂役";
            if (level <= 29) return "外门弟子";
            if (level <= 59) return "内门弟子";
            if (level <= 99) return "真传弟子";
            if (level <= 149) return "长老";
            if (level <= 199) return "宗主";
            return "天骄";
        }

        private static Color TitleColorForLevel(int level)
        {
            if (level <= 9) return new Color(0.65f, 0.65f, 0.65f);       // 灰
            if (level <= 29) return new Color(0.45f, 0.75f, 0.45f);       // 绿
            if (level <= 59) return new Color(0.45f, 0.65f, 0.95f);       // 蓝
            if (level <= 99) return new Color(0.75f, 0.5f, 0.9f);         // 紫
            if (level <= 149) return new Color(0.95f, 0.7f, 0.25f);       // 橙金
            if (level <= 199) return new Color(0.95f, 0.35f, 0.3f);       // 赤
            return new Color(1f, 0.85f, 0.3f);                            // 鎏金（天骄）
        }
    }
}
