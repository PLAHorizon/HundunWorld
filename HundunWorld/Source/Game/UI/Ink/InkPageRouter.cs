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

        /// <summary>返回战斗 HUD（特殊 dom-id，由返回按钮触发，调用 <see cref="InkPageRouter.NavigateToHud"/>）</summary>
        public const string BackHud = "back-hud";

        /// <summary>进入世界（从章节过场跳转战斗 HUD，调用 <c>NavigateTo("combat-hud")</c>）</summary>
        public const string CtaEnterWorld = "cta-enter-world";

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

        /// <summary>页面缓存表：dom-id → 已创建的页面控件（可选缓存，预留扩展）</summary>
        private readonly Dictionary<string, Control> _pageCache = new();

        /// <summary>关联的页面外壳</summary>
        private InkPageShell _shell;

        /// <summary>当前活动页面 dom-id</summary>
        private string _currentPageDomId;

        /// <summary>战斗 HUD 的 dom-id，作为导航枢纽</summary>
        private string _hudDomId;

        /// <summary>
        /// 当前页面 dom-id。
        /// </summary>
        public string CurrentPageDomId => _currentPageDomId;

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
        public void RegisterPage(string domId, Func<Control> factory)
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
        /// <returns>导航是否成功</returns>
        public bool NavigateTo(string domId)
        {
            try
            {
                if (string.IsNullOrEmpty(domId))
                {
                    FlaxEngine.Debug.LogWarning("[InkPageRouter] NavigateTo 收到空 domId");
                    return false;
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

                // 通过 Shell 挂载页面（Shell 负责生命周期管理，UnloadPage 时销毁旧页面）
                _shell.LoadPage(page);

                // 更新当前页面 dom-id
                _currentPageDomId = domId;

                // 根据 dom-id 控制页面外壳视觉层：
                // - 战斗 HUD：不显示返回按钮、隐藏背景层/暗角层，避免遮挡游戏场景
                // - 非 HUD 菜单页：显示返回按钮、背景层、暗角层
                bool isHud = domId == _hudDomId;
                _shell.ShowBackButton(!isHud);
                _shell.ShowBackgroundLayer(!isHud);
                _shell.ShowVignette(!isHud);

                FlaxEngine.Debug.Log($"[InkPageRouter] 导航到页面: {domId}, isHud={isHud}, bg={!isHud}, vignette={!isHud}");
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
        private void OnBackButtonClicked()
        {
            try
            {
                NavigateToHud();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[InkPageRouter] OnBackButtonClicked 失败: {ex.Message}");
            }
        }
    }
}
