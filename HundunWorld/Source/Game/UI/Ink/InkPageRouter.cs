using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.StyleSystem;
using System;
using System.Collections.Generic;

namespace HundunWorld.Game.UI.Ink
{
    /// <summary>
    /// 水墨页面 dom-id 导航契约常量。
    /// 定义所有合法的 data-dom-id 字符串，供 <see cref="InkPageRouter"/> 注册与导航使用。
    /// 共 23 个 dom-id，覆盖战斗 HUD、加载页、章节过场、菜单、弹窗、奖励、设置、
    /// 以及特殊导航动作（返回 HUD、进入世界）。
    /// </summary>
    public static class InkPageDomIds
    {
        /// <summary>战斗 HUD（枢纽页面，不显示返回按钮）</summary>
        public const string CombatHud = "combat-hud";

        /// <summary>加载页 1</summary>
        public const string Loading1 = "loading-1";

        /// <summary>加载页 2</summary>
        public const string Loading2 = "loading-2";

        /// <summary>章节过场</summary>
        public const string ChapterTransition = "chapter-transition";

        /// <summary>角色属性菜单</summary>
        public const string NavCharacter = "nav-character";

        /// <summary>任务菜单</summary>
        public const string NavQuests = "nav-quests";

        /// <summary>商店菜单</summary>
        public const string NavShop = "nav-shop";

        /// <summary>物品获得弹窗</summary>
        public const string PopupItemAcquired = "popup-item-acquired";

        /// <summary>留言弹窗</summary>
        public const string PopupMessage = "popup-message";

        /// <summary>成就解锁奖励</summary>
        public const string RewardAchievement = "reward-achievement";

        /// <summary>任务完成奖励</summary>
        public const string RewardQuestComplete = "reward-quest-complete";

        /// <summary>设置页</summary>
        public const string NavSettings = "nav-settings";

        /// <summary>
        /// 系统菜单（Esc 在战斗 HUD 上打开的模态层：继续游戏 / 设置 / 退出到角色选择）。
        /// 走 Modal 框架＝外壳叠加层，不进返回栈，因此再按一次 Esc 就是关掉它。
        /// </summary>
        public const string SystemMenu = "system-menu";

        /// <summary>返回战斗 HUD（特殊 dom-id，由返回按钮触发，调用 <see cref="InkPageRouter.NavigateToHud"/>）</summary>
        public const string BackHud = "back-hud";

        /// <summary>
        /// 返回上一层：走到来源页，返回栈空时回枢纽。页面自带的返回按钮、弹层关闭都发这个，
        /// 与外壳左上返回按钮、Esc 完全同一语义（<see cref="InkPageRouter.Back"/>）。
        /// <para>
        /// 与 <see cref="BackHud"/> 的区别：后者是"直接跳回枢纽"，会跳过返回栈——从"罗盘→世界地图"这类
        /// 二级链路里按它就会丢掉上一层，因此除"确实要回枢纽"的入口外都应用本常量。
        /// </para>
        /// </summary>
        public const string BackOneLevel = "back-one-level";

        /// <summary>进入世界（从章节过场跳转战斗 HUD，调用 <c>NavigateTo("combat-hud")</c>）</summary>
        public const string CtaEnterWorld = "cta-enter-world";

        /// <summary>进入秘境（DungeonEntryPage「进入秘境」按钮）— 战斗入场动作，解析为返回战斗 HUD（战斗现场）。
        /// 星型拓扑下无独立秘境场景，故终点恒为枢纽 HUD（见 ui-architecture-spec §1 星型辐射）。</summary>
        public const string EnterDungeon = "enter-dungeon";

        /// <summary>出战（MenuBattlePrepPage「出战」按钮）— 战斗入场动作，解析为返回战斗 HUD（战斗现场）。</summary>
        public const string NavBattleStart = "nav-battle-start";

        /// <summary>装备已变更（MenuEquipmentPage 装备后发出的 mock 通知）— 非导航目标，
        /// 由 NavigateToPage 识别为无操作通知，避免误触发页面跳转与"未注册 domId"告警。</summary>
        public const string EquipmentChanged = "equipment-changed";

        /// <summary>战斗 HUD v2（含小地图/技能槽网格/队伍卡）</summary>
        public const string CombatHudV2 = "combat-hud-v2";

        /// <summary>角色属性菜单 v2（战力/基础/进阶/装备/武学摘要）</summary>
        public const string NavCharacterV2 = "nav-character-v2";

        /// <summary>装备管理菜单（背包/纸娃娃/属性对比）</summary>
        public const string NavEquipment = "nav-equipment";

        /// <summary>阵亡界面（破招/返回）</summary>
        public const string DeathScreen = "death-screen";

        /// <summary>NPC 对话确认（纸色卷轴对话框）</summary>
        public const string DialogueConfirm = "dialogue-confirm";

        /// <summary>战前备战菜单（装备/武学/战力/药品）</summary>
        public const string NavBattlePrep = "nav-battle-prep";

        /// <summary>点穴系统（人体穴位图 + 详情）</summary>
        public const string Acupoint = "acupoint";

        /// <summary>QTE 千钧一发（圆环计时器）</summary>
        public const string Qte = "qte";

        /// <summary>等级提升奖励（属性对比）</summary>
        public const string RewardLevelUp = "reward-level-up";

        /// <summary>任务验证完成弹窗</summary>
        public const string PopupVerification = "popup-verification";

        /// <summary>奇术详情弹窗</summary>
        public const string PopupMartialArts = "popup-martial-arts";

        /// <summary>心法领悟弹窗</summary>
        public const string PopupSkillRealization = "popup-skill-realization";

        /// <summary>武学详情弹窗</summary>
        public const string PopupMartialDetail = "popup-martial-detail";

        /// <summary>引导侧边栏弹窗</summary>
        public const string PopupGuideSide = "popup-guide-side";

        /// <summary>图鉴侧边栏弹窗</summary>
        public const string PopupBestiarySide = "popup-bestiary-side";

        /// <summary>外观菜单（发型/面容/服饰/武器/坐骑/挂件）</summary>
        public const string NavAppearance = "nav-appearance";

        /// <summary>活动菜单（限时活动列表+详情）</summary>
        public const string NavActivities = "nav-activities";

        /// <summary>邮件菜单（邮件列表+详情）</summary>
        public const string NavMail = "nav-mail";

        /// <summary>门派菜单（门派列表+详情）</summary>
        public const string NavSect = "nav-sect";

        /// <summary>组队菜单（队伍成员+邀请面板）</summary>
        public const string NavTeam = "nav-team";

        /// <summary>博物志菜单（收集品网格+详情）</summary>
        public const string NavBestiary = "nav-bestiary";

        /// <summary>通行证菜单（战令进度+奖励列表）</summary>
        public const string NavBattlePass = "nav-battle-pass";

        /// <summary>抽卡菜单（祈愿池+抽卡结果）</summary>
        public const string NavGacha = "nav-gacha";

        /// <summary>生活菜单（采集/制作/钓鱼等）</summary>
        public const string NavLivelihood = "nav-livelihood";

        /// <summary>个人信息菜单（角色卡+统计信息）</summary>
        public const string NavPersonalInfo = "nav-personal-info";

        /// <summary>武学记录菜单（已学武学列表）</summary>
        public const string NavMartialRecord = "nav-martial-record";

        /// <summary>休闲模式菜单（活动卡片网格）</summary>
        public const string NavCasualMode = "nav-casual-mode";

        /// <summary>时间菜单（时辰系统）</summary>
        public const string NavTime = "nav-time";

        /// <summary>奇珍阁菜单（稀有商品）</summary>
        public const string NavShopRare = "nav-shop-rare";

        /// <summary>多人模式菜单（房间列表）</summary>
        public const string NavMultiplayer = "nav-multiplayer";

        /// <summary>拍照模式菜单（取景+滤镜）</summary>
        public const string NavPhotoMode = "nav-photo-mode";

        /// <summary>拍照相册页（从拍照页「相册」按钮进入）。</summary>
        public const string NavPhotoAlbum = "nav-photo-album";

        /// <summary>元素视野页面（高亮元素标记）</summary>
        public const string NavElementVision = "nav-element-vision";

        /// <summary>角色选择页（登录后的行囊 / 角色列表，也是创角流程入口）</summary>
        public const string CcRoleList = "cc-role-list";

        /// <summary>创角第一步：性别（水墨笔触双档「男 / 女」）</summary>
        public const string CcGender = "cc-gender";

        /// <summary>创角第二步：面容风格预设（风雅 / 写实竖版水墨卡）</summary>
        public const string CcFacePreset = "cc-face-preset";

        /// <summary>创角第三步：捏脸（捏脸 / 智能捏脸页签 + 预设列表 + 更多面容）</summary>
        public const string CcFaceSculpt = "cc-face-sculpt";

        /// <summary>创角最后一步：命名（不提供门派选择，门派由游戏内加入后再显示）</summary>
        public const string CcName = "cc-name";

        /// <summary>音频设置子页面</summary>
        public const string NavSettingsAudio = "nav-settings-audio";

        /// <summary>路牌可见性设置子页（设置 → 路牌）</summary>
        public const string NavSettingsRoadSign = "nav-settings-roadsign";

        /// <summary>UI 浏览器（Debug 菜单，用于查看所有 UI）</summary>
        public const string UIGallery = "ui-gallery";

        /// <summary>引导 HUD 页（步骤引导覆盖层，含聚光高亮/水墨箭头/操作提示）</summary>
        public const string GuideHud = "guide-hud";

        /// <summary>动作引导页（招式演示引导）</summary>
        public const string GuideAction = "guide-action";

        /// <summary>武学引导页（武学修习引导）</summary>
        public const string GuideMartial = "guide-martial";

        /// <summary>角色属性菜单 V1（menu-char-attributes.html，与 V2 并存）</summary>
        public const string MenuCharAttributes = "menu-char-attributes";

        /// <summary>小游戏完成页（奖励结算 + 继续到引导动作）</summary>
        public const string MiniGameComplete = "mini-game-complete";

        /// <summary>传世恭喜奖励页（reward-congratulations.html）</summary>
        public const string RewardCongratulations = "reward-congratulations";

        /// <summary>水墨动画分镜参考 01 · 水墨晕染（ink-anim-ref-1.html）</summary>
        public const string InkAnimRefSpread = "ink-anim-ref-1";

        /// <summary>水墨动画分镜参考 02 · 水墨笔触（ink-anim-ref-2.html）</summary>
        public const string InkAnimRefBrush = "ink-anim-ref-2";

        /// <summary>地图解锁奖励页（reward-map-unlock.html）</summary>
        public const string RewardMapUnlock = "reward-map-unlock";

        /// <summary>传送点解锁奖励页（reward-teleport-unlock.html）</summary>
        public const string RewardTeleportUnlock = "reward-teleport-unlock";

        // ===================================================================
        // game-ui-system 设计方案要求的 19 个页面 dom-id
        // 按 4 大子系统分组：① 核心战斗 HUD ② 角色与背包 ③ 任务与技能 ④ 社交与商城
        // 与现有 dom-id 并存，由 InkPageRouter 注册工厂后即可导航。
        // =======================================================================

        // --- ① 核心战斗 HUD ---

        /// <summary>传统模式战斗 HUD — 对应 combat-hud-traditional.html（沉浸式 HUD 的传统布局变体）</summary>
        public const string CombatHudTraditional = "combat-hud-traditional";

        /// <summary>战斗模式切换按钮（沉浸式 ⇄ 传统式）— 触发 toggle-traditional 动作</summary>
        public const string ToggleTraditional = "toggle-traditional";

        /// <summary>战斗模式切换按钮（传统式 → 沉浸式）— 触发 toggle-immersive 动作。
        /// 对应 combat-hud-traditional.html 的「切换沉浸模式」按钮；与 <see cref="ToggleTraditional"/> 互为反向，
        /// 二者均由 MainUIManager.NavigateToPage 解析为在 CombatHudV2 ⇄ CombatHudTraditional 间往返。</summary>
        public const string ToggleImmersive = "toggle-immersive";

        // --- ② 角色与背包子系统 ---

        /// <summary>角色面板 — 对应 character-panel.html，nav-character-v2 的别名（功能等价）</summary>
        public const string NavCharacterPanel = "nav-character-panel";

        /// <summary>五行技能树 — 数据驱动的 五行×五阶×三境 阶梯与相生演化视图（docs/SKILL_WUXING_TREE_DESIGN.md）</summary>
        public const string NavSkillTree = "nav-skill-tree";

        /// <summary>背包行囊 — 对应 inventory.html，物品网格 + 分类 + 详情</summary>
        public const string NavInventory = "nav-inventory";

        /// <summary>装备强化 — 对应 equipment-enhance.html，强化/镶嵌/精炼</summary>
        public const string NavEquipmentEnhance = "nav-equipment-enhance";

        /// <summary>制造技艺 — 对应 crafting.html，配方/材料/产出</summary>
        public const string NavCrafting = "nav-crafting";

        /// <summary>坐骑灵兽 — 对应 mount-pet.html，坐骑/宠物列表 + 详情</summary>
        public const string NavMountPet = "nav-mount-pet";

        // --- ③ 任务与技能子系统 ---

        /// <summary>江湖任务志 — 对应 quest-log.html，主线/支线/日常/师门任务列表 + 详情</summary>
        public const string NavQuestLog = "nav-quest-log";

        /// <summary>世界地图 — 对应 world-map.html，区域/界碑/传送点</summary>
        public const string NavWorldMap = "nav-world-map";

        /// <summary>指南针 — 对应 compass.html，方位/POI/追踪</summary>
        public const string NavCompass = "nav-compass";

        /// <summary>江湖秘境入口 — 对应 dungeon-entry.html，副本列表 + 进入</summary>
        public const string NavDungeonEntry = "nav-dungeon-entry";

        // --- ④ 社交与商城子系统 ---

        /// <summary>江湖门派（社交门派） — 对应 social-guild.html，门派列表 + 成员 + 申请</summary>
        public const string NavSocialGuild = "nav-social-guild";

        /// <summary>江湖商城 — 对应 shop.html，商品分类 + 商品网格 + 购物车结算</summary>
        public const string NavSocialShop = "nav-social-shop";

        /// <summary>NPC 对话菜单（点 NPC 先选"能做什么"）。</summary>
        public const string NavNpcGossip = "nav-npc-gossip";

        /// <summary>江湖交游（好友列表） — 对应 friends.html</summary>
        public const string NavFriends = "nav-friends";

        /// <summary>飞鸽传书（社交邮件） — 对应 mail.html，邮件分类 + 列表 + 详情</summary>
        public const string NavSocialMail = "nav-social-mail";

        /// <summary>师徒传承 — 对应 mentor.html，师徒任务/奖励</summary>
        public const string NavMentor = "nav-mentor";

        /// <summary>江湖风云榜 — 对应 leaderboard.html，排名/对比</summary>
        public const string NavLeaderboard = "nav-leaderboard";

        /// <summary>江湖百艺录（成就） — 对应 achievement.html，成就分类 + 进度</summary>
        public const string NavAchievement = "nav-achievement";

        // --- 跨页快捷跳转动作（不走 HUD 中转的合法直达路径） ---
        // friends → mail / mentor → dungeon-entry / inventory → equipment-enhance
        // character-panel → skill-panel / combat-hud 小地图 → world-map
        // 这些跨页跳转触发 panel:show 自定义事件，由粒子系统接墨韵涟漪反馈。

        /// <summary>跨页直达：好友详情 → 飞鸽传书（撰写邮件）</summary>
        public const string ActionFriendsToMail = "action-friends-to-mail";

        /// <summary>跨页直达：师徒周常 → 江湖秘境</summary>
        public const string ActionMentorToDungeon = "action-mentor-to-dungeon";

        /// <summary>跨页直达：背包选中装备 → 装备强化</summary>
        public const string ActionInventoryToEnhance = "action-inventory-to-enhance";

        /// <summary>跨页直达：角色面板经脉 → 武学技能</summary>
        public const string ActionCharToSkill = "action-char-to-skill";

        /// <summary>跨页直达：HUD 小地图 → 世界地图</summary>
        public const string ActionHudToWorldMap = "action-hud-to-world-map";
    }

    // =======================================================================

    /// <summary>
    /// 水墨页面路由器。
    /// 按 data-dom-id 字符串管理页面栈，支持前进/返回/切换。
    /// <para>
    /// 通过 <see cref="RegisterPage"/> 注册页面工厂，
    /// 通过 <see cref="NavigateTo"/> 导航到指定页面，
    /// 通过 <see cref="NavigateToHud"/> 返回战斗 HUD。
    /// 返回按钮点击事件由关联的 <see cref="InkPageShell"/> 订阅并转发至本路由器。
    /// </para>
    /// <para>
    /// 导航契约 dom-id 清单参见 <see cref="InkPageDomIds"/>。
    /// </para>
    /// </summary>
    public class InkPageRouter : ContainerControl
    {
        /// <summary>页面工厂表：dom-id → 创建页面控件的工厂函数</summary>
        private readonly Dictionary<string, Func<Control>> _pageFactories = new();

        /// <summary>
        /// 页面框架描述符表：dom-id → 该页的呈现类型（铺不铺墨底/暗角、给不给外壳返回）。
        /// 这是"哪些层给哪些页"的唯一来源——取代了原先按 <c>popup-</c> 前缀猜 + 两张手写白名单。
        /// </summary>
        private readonly Dictionary<string, InkPageFrame> _pageFrames = new();

        /// <summary>返回栈：记下"从哪来"，供返回按钮/Esc 回上一层（栈空则回战斗 HUD）。</summary>
        private readonly Stack<string> _history = new();

        /// <summary>页面缓存表：dom-id → 已创建的页面控件（可选缓存，预留扩展）</summary>
        private readonly Dictionary<string, Control> _pageCache = new();

        /// <summary>关联的页面外壳</summary>
        private InkPageShell _shell;

        /// <summary>当前活动页面 dom-id</summary>
        private string _currentPageDomId;

        /// <summary>当前弹层 dom-id（无弹层时为 null）。弹层叠在页面之上，不占用页面槽位。</summary>
        private string _currentOverlayDomId;

        /// <summary>战斗 HUD 的 dom-id，作为导航枢纽</summary>
        private string _hudDomId;

        /// <summary>
        /// 当前页面 dom-id。
        /// </summary>
        public string CurrentPageDomId => _currentPageDomId;

        /// <summary>返回栈是否非空（Esc/返回按钮会回上一层而非直接回 HUD）。</summary>
        public bool CanGoBack => _history.Count > 0;

        /// <summary>返回栈深度（诊断用）。</summary>
        public int HistoryDepth => _history.Count;

        /// <summary>当前是否有模态弹层盖在页面上。</summary>
        public bool HasOverlay => _shell != null && _shell.HasOverlay;

        /// <summary>当前弹层 dom-id（无弹层时为 null）。</summary>
        public string CurrentOverlayDomId => _currentOverlayDomId;

        /// <summary>当前生效的枢纽 HUD dom-id（沉浸式/传统式切换后会跟着变）。</summary>
        public string HudDomId => _hudDomId;

        /// <summary>
        /// 当前页面是否自带返回入口。自带返回的页面（如创角阶段流）同时自己处理 Esc，
        /// 中央 Esc 不再改派——否则同一次按键会被页面与路由器各处理一次，多退一层。
        /// </summary>
        public bool CurrentPageOwnsBackKey
        {
            get
            {
                if (string.IsNullOrEmpty(_currentPageDomId)) return false;
                return _pageFrames.TryGetValue(_currentPageDomId, out var frame) && frame.OwnBack;
            }
        }

        /// <summary>取某页登记的框架描述符；未登记时按沉浸式全屏页处理（并告警，便于补登记）。</summary>
        public InkPageFrame GetFrame(string domId)
        {
            if (!string.IsNullOrEmpty(domId) && _pageFrames.TryGetValue(domId, out var frame))
                return frame;

            FlaxEngine.Debug.LogWarning($"[InkPageRouter] 页面 {domId} 未登记 Frame，按沉浸式全屏页处理（请在 RegisterPage 补登记）");
            return InkPageFrame.Immersive;
        }

        /// <summary>
        /// 构造函数：初始化为透明、不裁剪的路由器控件。
        /// 路由器本身不渲染内容，仅作为导航逻辑控制器。
        /// </summary>
        public InkPageRouter()
        {
            try
            {
                AnchorPreset = AnchorPresets.StretchAll;
                BackgroundColor = Color.Transparent;
                ClipChildren = false;
                AutoFocus = false;
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[InkPageRouter] 初始化失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 关联外壳与战斗 HUD dom-id，并一次性订阅返回按钮事件。
        /// </summary>
        /// <param name="shell">页面外壳实例</param>
        /// <param name="hudDomId">战斗 HUD 的 dom-id（通常为 <see cref="InkPageDomIds.CombatHud"/>）</param>
        public void Initialize(InkPageShell shell, string hudDomId)
        {
            try
            {
                _shell = shell;
                _hudDomId = hudDomId;

                if (_shell != null)
                {
                    // 一次性订阅返回按钮事件
                    _shell.BackButtonClicked += OnBackButtonClicked;
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[InkPageRouter] Initialize 失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 注册页面工厂。
        /// 同一 dom-id 重复注册将覆盖旧工厂。
        /// </summary>
        /// <param name="domId">页面 dom-id</param>
        /// <param name="factory">创建页面控件的工厂函数</param>
        /// <param name="frame">页面框架描述符（铺不铺墨底/暗角、给不给外壳返回）</param>
        public void RegisterPage(string domId, Func<Control> factory, InkPageFrame frame)
        {
            try
            {
                if (string.IsNullOrEmpty(domId))
                {
                    FlaxEngine.Debug.LogWarning("[InkPageRouter] RegisterPage 收到空 domId，已忽略");
                    return;
                }
                if (factory == null)
                {
                    FlaxEngine.Debug.LogWarning($"[InkPageRouter] RegisterPage 收到 null 工厂 (domId={domId})，已忽略");
                    return;
                }

                _pageFactories[domId] = factory;
                _pageFrames[domId] = frame;
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[InkPageRouter] RegisterPage 失败 (domId={domId}): {ex.Message}");
            }
        }

        /// <summary>
        /// 导航到指定页面。
        /// <para>
        /// 流程：
        /// <list type="number">
        ///   <item>若 domId 未注册，记录警告日志并返回 false</item>
        ///   <item>调用工厂创建页面控件</item>
        ///   <item>通过 <see cref="InkPageShell.LoadPage"/> 挂载到内容层</item>
        ///   <item>更新 <see cref="CurrentPageDomId"/></item>
        ///   <item>若 domId 为战斗 HUD，隐藏返回按钮；否则显示返回按钮</item>
        /// </list>
        /// </para>
        /// </summary>
        /// <param name="domId">目标页面 dom-id</param>
        /// <param name="pushHistory">是否把当前页压入返回栈（Back() 用 false 弹栈，避免来回震荡）</param>
        /// <returns>导航是否成功</returns>
        public bool NavigateTo(string domId, bool pushHistory = true)
        {
            try
            {
                if (string.IsNullOrEmpty(domId))
                {
                    FlaxEngine.Debug.LogWarning("[InkPageRouter] NavigateTo 收到空 domId");
                    return false;
                }

                // 战斗 HUD 别名：枢纽可在 v1/v2 间切换（见 Initialize 的 hudDomId）——
                // 将 "combat-hud" 请求解析为当前激活的枢纽 HUD，使所有页面出口/回退自动指向生效版本。
                if (domId == InkPageDomIds.CombatHud && !string.IsNullOrEmpty(_hudDomId) && domId != _hudDomId)
                {
                    domId = _hudDomId;
                }
                // 枢纽随激活变体走：切到 沉浸式(V2) 之后，别名解析、返回栈根判定、返回枢纽都必须指向 V2，
                // 否则按 Esc/返回/back-hud 会被静默送回传统式（等于悄悄改回用户的选择）。
                else if (domId == InkPageDomIds.CombatHudV2 || domId == InkPageDomIds.CombatHudTraditional)
                {
                    _hudDomId = domId;
                }

                if (!_pageFactories.TryGetValue(domId, out var factory))
                {
                    FlaxEngine.Debug.LogWarning($"[InkPageRouter] 未注册的页面 domId: {domId}");
                    return false;
                }

                if (_shell == null)
                {
                    FlaxEngine.Debug.LogWarning("[InkPageRouter] 未关联 InkPageShell，请先调用 Initialize");
                    return false;
                }

                // 调用工厂创建页面控件（工厂异常单独捕获，避免影响路由器稳定性）
                Control page;
                try
                {
                    page = factory();
                }
                catch (Exception factoryEx)
                {
                    FlaxEngine.Debug.LogError($"[InkPageRouter] 页面工厂抛出异常 (domId={domId}): {factoryEx.Message}");
                    return false;
                }

                if (page == null)
                {
                    FlaxEngine.Debug.LogWarning($"[InkPageRouter] 页面工厂返回 null (domId={domId})");
                    return false;
                }

                // 根据页面登记的框架描述符控制外壳视觉层（唯一来源，不再按 dom-id 前缀猜 + 白名单）：
                // - Hud：不显示返回按钮、隐藏背景层/暗角层，避免遮挡游戏场景
                // - Immersive：显示返回按钮、背景层、暗角层（自铺浅色舞台者不叠暗角）
                // - Modal：由叠加层承载（叠在底页之上、底页不销毁），不铺全屏墨底/暗角
                InkPageFrame frame = GetFrame(domId);
                bool isHud = frame.Kind == InkPageKind.Hud;
                bool isModal = frame.Kind == InkPageKind.Modal;

                if (isModal)
                {
                    // 弹层不占页面槽位：不改 CurrentPageDomId、不进返回栈、不动外壳的铺底/返回按钮
                    // （那些属于底页）。关闭走 Back()/CloseOverlay()，底页原样还在。
                    _shell.ShowOverlay(page);
                    _currentOverlayDomId = domId;
                }
                else
                {
                    // 任何非弹层导航都先把弹层收掉：弹层关闭的语义就是回到它下面那一页。
                    if (_shell.HasOverlay)
                    {
                        _shell.HideOverlay();
                        _currentOverlayDomId = null;
                    }

                    // 战斗 HUD 内容层顶部对齐：非 16:9 窗口下设计空间默认上下居中会整体下沉，
                    // 顶部对齐后设计坐标 y=0 贴合窗口上边缘（罗盘带/屏幕边缘边距才是真实像素距离）。
                    _shell.TopAnchoredContent = isHud;

                    // 通过 Shell 挂载页面（Shell 负责生命周期管理，UnloadPage 时销毁旧页面）
                    _shell.LoadPage(page);

                    // 记录来源页供 Back() 弹栈：目标页与来源页相同、或目标是枢纽 HUD（回根）时不入栈。
                    bool isRootHud = domId == _hudDomId;
                    if (isRootHud || domId == InkPageDomIds.CombatHud)
                    {
                        _history.Clear();
                    }
                    else if (pushHistory
                             && !string.IsNullOrEmpty(_currentPageDomId)
                             && _currentPageDomId != domId
                             && _currentPageDomId != InkPageDomIds.CombatHud
                             && _currentPageDomId != _hudDomId)
                    {
                        _history.Push(_currentPageDomId);
                    }

                    // 更新当前页面 dom-id
                    _currentPageDomId = domId;

                    // 自带返回/侧栏的页面隐藏外壳返回，避免与页面自身控件重叠遮挡。
                    _shell.ShowBackButton(!isHud && !frame.OwnBack);
                    // 外壳背景层**不**为自铺页面关闭：内容层是居中缩放的 1920×1080 且 ClipChildren=true，
                    // 页面自己的雾白底铺不到信箱区；关掉这层会让非 16:9 窗口的上下/左右黑边露出 3D 场景。
                    // 16:9 全屏时该层被页面完全盖住，视觉无差别；暗角层则确实要关（会压在浅色舞台上留黑边）。
                    _shell.ShowBackgroundLayer(!frame.IsBare && !frame.NoBackdrop);
                    _shell.ShowVignette(!frame.IsBare && !frame.SelfLitStage && !frame.NoBackdrop);
                }

                // 触发 PanelShow 事件：通知粒子系统在屏幕中心绘制墨韵涟漪。
                // 屏幕中心坐标由粒子系统在事件回调中通过 PointFromScreen 转换为控件局部坐标，
                // 此处传入 Zero 表示触发点未知，由粒子系统回退到自身中心。
                try
                {
                    PanelShow?.Invoke(domId, Float2.Zero);
                }
                catch (Exception panelEx)
                {
                    FlaxEngine.Debug.LogWarning($"[InkPageRouter] PanelShow 事件订阅者抛出异常 (domId={domId}): {panelEx.Message}");
                }

                FlaxEngine.Debug.Log($"[InkPageRouter] 导航到页面: {domId}, frame={frame}, 叠加层={isModal}, 当前页={_currentPageDomId}");
                return true;
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[InkPageRouter] NavigateTo 失败 (domId={domId}): {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 返回战斗 HUD（调用 <see cref="NavigateTo"/> 传入 <see cref="_hudDomId"/>）。
        /// </summary>
        /// <returns>导航是否成功</returns>
        public bool NavigateToHud()
        {
            try
            {
                if (string.IsNullOrEmpty(_hudDomId))
                {
                    FlaxEngine.Debug.LogWarning("[InkPageRouter] 未设置战斗 HUD dom-id，请先调用 Initialize");
                    return false;
                }

                return NavigateTo(_hudDomId);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[InkPageRouter] NavigateToHud 失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 返回按钮点击事件处理：调用 <see cref="NavigateToHud"/> 返回战斗 HUD。
        /// 此方法由 <see cref="InkPageShell.BackButtonClicked"/> 事件触发。
        /// </summary>
        /// <summary>
        /// 返回上一层：弹出返回栈回到来源页；栈空则回战斗 HUD。
        /// 外壳返回按钮与 Esc 都走这里——星型枢纽仍是总出口，只是子页间跳转后能回上一层。
        /// </summary>
        /// <returns>是否发生了导航</returns>
        public bool Back()
        {
            try
            {
                // 有弹层时先关弹层（弹层是叠加层，不占返回栈）。
                if (HasOverlay)
                {
                    CloseOverlay();
                    return true;
                }

                while (_history.Count > 0)
                {
                    string target = _history.Pop();
                    if (string.IsNullOrEmpty(target) || target == _currentPageDomId) continue;
                    if (target == InkPageDomIds.CombatHud || target == _hudDomId) break;

                    if (NavigateTo(target, pushHistory: false))
                    {
                        return true;
                    }
                }

                return NavigateToHud();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[InkPageRouter] Back 失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>把一个模态弹层挂到外壳叠加层（底页保持挂载，关闭后回来）。</summary>
        public bool ShowOverlay(Control overlay)
        {
            if (_shell == null || overlay == null) return false;
            _shell.ShowOverlay(overlay);
            return true;
        }

        /// <summary>关闭当前弹层（底页保持不变）。</summary>
        public void CloseOverlay()
        {
            _shell?.HideOverlay();
            _currentOverlayDomId = null;
        }

        /// <summary>
        /// 返回按钮点击事件处理：调用 <see cref="Back"/> 回上一层（栈空则回战斗 HUD）。
        /// 此方法由 <see cref="InkPageShell.BackButtonClicked"/> 事件触发。
        /// </summary>
        private void OnBackButtonClicked()
        {
            try
            {
                Back();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[InkPageRouter] OnBackButtonClicked 失败: {ex.Message}");
            }
        }

        // ===================================================================
        // 跨页快捷跳转支持（game-ui-system §1.2 跨页跳转约定）
        // 不走 HUD 中转的合法直达路径，触发 PanelShow 事件供粒子系统接墨韵涟漪反馈。
        // =======================================================================

        /// <summary>
        /// 面板显示事件。
        /// <para>
        /// 任何 <see cref="NavigateTo"/> / <see cref="NavigateToHud"/> / <see cref="NavigateToAction"/>
        /// 成功后触发，供 <c>InkParticleSystem</c> 订阅并在目标位置绘制墨韵涟漪。
        /// 参数：目标 dom-id，目标屏幕坐标（若未知则传 <see cref="Float2.Zero"/>）。
        /// </para>
        /// </summary>
        public event Action<string, Float2> PanelShow;

        /// <summary>
        /// 跨页直达动作。
        /// <para>
        /// 合法直达路径（详见 <see cref="InkPageDomIds.Action*"/> 常量）：
        /// <list type="bullet">
        ///   <item><see cref="InkPageDomIds.ActionFriendsToMail"/>：好友 → 飞鸽传书</item>
        ///   <item><see cref="InkPageDomIds.ActionMentorToDungeon"/>：师徒 → 江湖秘境</item>
        ///   <item><see cref="InkPageDomIds.ActionInventoryToEnhance"/>：背包 → 装备强化</item>
        ///   <item><see cref="InkPageDomIds.ActionCharToSkill"/>：角色面板 → 武学技能</item>
        ///   <item><see cref="InkPageDomIds.ActionHudToWorldMap"/>：HUD 小地图 → 世界地图</item>
        /// </list>
        /// 其余跨页跳转一律视为违规，记日志返回 false。
        /// </para>
        /// </summary>
        /// <param name="actionDomId">动作 dom-id（必须为 Action* 常量）</param>
        /// <param name="triggerScreenPos">触发点屏幕坐标（用于粒子涟漪定位）</param>
        /// <returns>跳转是否成功</returns>
        public bool NavigateToAction(string actionDomId, Float2 triggerScreenPos = default)
        {
            try
            {
                string targetDomId = actionDomId switch
                {
                    InkPageDomIds.ActionFriendsToMail => InkPageDomIds.NavMail,
                    InkPageDomIds.ActionMentorToDungeon => InkPageDomIds.NavDungeonEntry,
                    InkPageDomIds.ActionInventoryToEnhance => InkPageDomIds.NavEquipmentEnhance,
                    InkPageDomIds.ActionCharToSkill => InkPageDomIds.NavSkillTree,
                    InkPageDomIds.ActionHudToWorldMap => InkPageDomIds.NavWorldMap,
                    _ => null
                };

                if (targetDomId == null)
                {
                    FlaxEngine.Debug.LogWarning($"[InkPageRouter] 非法的跨页跳转动作: {actionDomId}（其余跨页跳转必须经 HUD 中转）");
                    return false;
                }

                // NavigateTo 内部已触发 PanelShow 事件（默认 Zero 触发点）。
                // 若调用方提供了 triggerScreenPos（非 Zero），则覆盖触发一次带具体坐标的涟漪，
                // 让粒子系统在按钮点击位置而非屏幕中心绘制涟漪，更贴合设计意图。
                bool ok = NavigateTo(targetDomId);
                if (ok && triggerScreenPos != Float2.Zero)
                {
                    try
                    {
                        PanelShow?.Invoke(targetDomId, triggerScreenPos);
                    }
                    catch (Exception panelEx)
                    {
                        FlaxEngine.Debug.LogWarning($"[InkPageRouter] NavigateToAction PanelShow 覆盖触发异常: {panelEx.Message}");
                    }
                }
                return ok;
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[InkPageRouter] NavigateToAction 失败 (action={actionDomId}): {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 在 NavigateTo 成功后触发 PanelShow 事件。
        /// 重写版本：导航完成后向订阅者广播目标 dom-id 与触发点坐标。
        /// </summary>
        /// <param name="domId">目标 dom-id</param>
        /// <param name="triggerPos">触发点屏幕坐标（默认 Zero 表示未知）</param>
        public void RaisePanelShow(string domId, Float2 triggerPos = default)
        {
            try
            {
                PanelShow?.Invoke(domId, triggerPos);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[InkPageRouter] RaisePanelShow 失败: {ex.Message}");
            }
        }
    }
}
