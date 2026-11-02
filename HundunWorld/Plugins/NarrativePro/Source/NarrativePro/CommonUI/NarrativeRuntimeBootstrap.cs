// 模块关系说明：
// 本文件是 NarrativePro 端口缺失的「运行期引导」。
//
// UE5 侧不需要引导：ANarrativeCharacter 的类构造器用 CreateDefaultSubobject 给出
// ASC/属性集/背包/交互/移动/视觉/脚步 foley，GameMode 生成 HUD 与 CommonUI 子系统，
// 玩家控制器由引擎自动 Possess。Flax 没有类构造器、GameMode、HUD、PlayerController，
// 因此这些对象在端口里一个都没被创建过 —— 所有 GetScript 解析结果都是 null，
// 整套条件/事件/ASC/UI 系统静默空转。
//
// 本文件把 UE5 的那套隐式装配搬到显式运行期时序上（与 SaveSystem/NarrativeSaveBootstrap 同构）：
//   NarrativeProPlugin.Initialize → NarrativeRuntimeBootstrap.Install →
//   订阅 Level.SceneLoaded/SceneUnloading → 玩法场景启动时生成宿主 EmptyActor 并挂载
//   NarrativeCharacterSubsystem / NarrativeCommonUISubsystem / NarrativeCommonHUD /
//   NarrativeGameplayHUD / NarrativeRuntimePump → 泵脚本在首帧之后（GUI 只能主线程创建）
//   驱动 HUD 建根控件、注册 UI 层、为本地玩家角色补 NarrativePlayerController、
//   创建对话/任务面板并接入 TalesComponent、把对话文本接到字幕通道、
//   把任务事件接到通知通道，并提供角色创建器的可达入口 OpenCreator/CloseCreator。
//
// Flax GUI 口径（本仓库既有约定）：一律 TopLeft 锚 + 显式 Location/Size 重排，只在主线程构建。
//
// 「本地玩家宿主装配」模式（本文件第二职责）：
// 本游戏从不实例化插件角色 —— Content/Maps/*.scene 里只有 NarrativePro.Navigation.* 与
// NarrativeCameraComponent，Source/Game/GameSceneInitializer.cs:46-73 把游戏自己的 PlayerController
// 绑到场景里名为 "Player" 的 Actor 上。于是 Level.FindScript<NarrativePlayerCharacter>() 恒为 null，
// ASC/背包/Tales/面板/暂停菜单/任务持久化/网络 Tick 整条链路都不可达。
// 因此引导按「角色路径优先、宿主装配兜底」装配：
//   ① 解析宿主 Actor（ArsenalStatics.ResolveLocalPlayerHostActor：已登记宿主 → 插件控制器 →
//      插件玩家角色 → 名为 "Player" 的场景 Actor → 插件相机/交互组件所在 Actor → 全场景名扫描）；
//   ② 真是插件角色 → 只调 character.EnsureSubobjects()（UE5 类默认对象等价物，幂等）；
//   ③ 否则把 存档身份脚本 / ASC(+属性集) / 背包 / 交互 / TalesComponent / PlayerController
//      AddScript 到该宿主 Actor 上，并把它登记进存档子系统（身份脚本 ShouldRespawn=false，
//      记录只做状态还原），使任务进度能进档也能读档。
// 装配只在玩法场景进行（登录/启动/标题场景由场景资产路径门禁排除），并按帧有限重试直到宿主出现。

using System;
using System.Collections.Generic;
using System.Reflection;
using FlaxEngine;
using FlaxEngine.GUI;
using NarrativePro.AI;
using NarrativePro.CharacterCreator;
using NarrativePro.CharacterCreator.Options;
using NarrativePro.Core;
using NarrativePro.CommonUI.Widgets;
using NarrativePro.Items;
using NarrativePro.Tales;
using NarrativePro.Tales.Data;
using NarrativePro.Tales.Nodes;
using NarrativePro.UI;
using NarrativePro.UnrealFramework;
using NarrativePro.Widgets;
using DialogueClass = NarrativePro.Tales.Dialogue.Dialogue;
using QuestClass = NarrativePro.Tales.Quest.Quest;
// Core 与 Items 各有一个 NarrativeGameplayTags：本引导只用 Core 的标签注册表，显式别名消除歧义
using NarrativeGameplayTags = NarrativePro.Core.NarrativeGameplayTags;

namespace NarrativePro.CommonUI
{
    /// <summary>
    /// 运行期引导：HUD/CommonUI/角色子对象的实际创建者。由 <see cref="NarrativeProPlugin"/> 安装。
    /// </summary>
    public static class NarrativeRuntimeBootstrap
    {
        /// <summary>宿主 Actor 名（对应 UE5 由引擎自动生成的 GameInstance/PlayerController 宿主）。</summary>
        public const string HostActorName = "NarrativeRuntimeHost";

        /// <summary>
        /// 当前已装配的玩法场景（Teardown 后置空）。
        /// 运行期世界对象（NPC/载具/攀爬物/投射物/音效等）必须显式生成到本场景：
        /// Flax 无父级版 <see cref="FlaxEngine.Level.SpawnActor(Actor)"/> 固定落入
        /// Scenes[0]（常驻 RootScene，切场景不卸载），会把 RootScene 堆成垃圾场并跨会话滞留。
        /// </summary>
        public static Scene GameplayScene { get; private set; }

        /// <summary>
        /// 把新建 Actor 生成到当前玩法场景。玩法场景缺失时大声报错并返回失败
        /// （本插件的运行期对象只在玩法场景有意义，绝不落进 RootScene；销毁由调用方按既有模式处理）。
        /// 返回值遵循 Flax 约定：true 表示失败。
        /// </summary>
        public static bool TrySpawnToGameplayScene(Actor actor, string what)
        {
            var scene = GameplayScene;
            if (scene == null)
            {
                NarrativeLog.LogError($"[NarrativePro] 玩法场景未就绪，跳过生成 {what}（RootScene 只允许全局共享宿主）");
                return true;
            }
            return Level.SpawnActor(actor, scene);
        }

        /// <summary>
        /// 把已通过 PrefabManager.SpawnPrefab 生成的 Actor 搬进玩法场景
        /// （SpawnPrefab 与无父级 SpawnActor 同样默认落 Scenes[0]）。
        /// </summary>
        public static void MoveToGameplayScene(Actor actor, string what)
        {
            if (actor == null) return;
            var scene = GameplayScene;
            if (scene == null)
            {
                NarrativeLog.LogError($"[NarrativePro] 玩法场景未就绪，{what} 滞留在 {actor.Scene?.Name ?? "未知场景"}（RootScene 只允许全局共享宿主）");
                return;
            }
            if (actor.Scene != scene)
            {
                Level.SpawnActor(actor, scene);
            }
        }


        /// <summary>未显式给配置路径时，创建器配置的默认搜索目录。</summary>
        public const string DefaultCreatorDirectory = "Content/NarrativePro/CharacterCreator";

        private static bool _installed;
        private static Guid _initializedSceneId = Guid.Empty;

        private static Actor _hostActor;
        private static NarrativeRuntimePump _pump;

        private static NarrativeCharacterSubsystem _characterSubsystem;
        private static NarrativeCommonUISubsystem _subsystem;
        private static NarrativeCommonHUD _commonHUD;
        private static NarrativeGameplayHUD _gameplayHUD;

        // 音乐子系统（由代码持有的运行期唯一实例；Flax 无 GameInstanceSubsystem，必须挂在宿主 Actor 上）
        private static NarrativePro.Music.NarrativeMusicSubsystem _musicSubsystem;

        private static DialoguePanel _dialoguePanel;
        private static QuestLogPanel _questLogPanel;
        private static QuestTracker _questTracker;
        private static NarrativeNotification _notification;
        private static NarrativeUIBuilder _uiBuilder;

        private static TalesComponent _boundTales;
        private static NarrativePlayerController _localController;

        private static bool _layersRegistered;
        private static bool _uiBuilt;
        private static bool _rosterDone;

        // ===== 宿主装配模式（本游戏不实例化插件角色时的等价装配） =====

        /// <summary>本地玩家宿主 Actor（游戏场景里承载本地玩家的 Actor，本游戏为名为 "Player" 的 Actor）。</summary>
        private static Actor _playerHostActor;

        /// <summary>宿主组件是否已装配完成（幂等位，装配成功后不再重复扫描）。</summary>
        private static bool _bHostComponentsAttached;

        /// <summary>装配走的是真正的插件角色路径（此时不需要宿主装配）。</summary>
        private static bool _bHostIsNarrativeCharacter;

        /// <summary>宿主解析已尝试的帧数（宿主 Actor 可能晚于引导出现，需有限重试）。</summary>
        private static int _hostResolveAttempts;
        private static float _hostResolveTimer;

        private static NarrativePro.GAS.NarrativeAbilitySystemComponent _hostAsc;
        private static NarrativePro.Items.NarrativeInventoryComponent _hostInventory;
        private static NarrativePro.Items.EquipmentComponent _hostEquipment;
        private static NarrativePro.Interaction.PlayerInteractionComponent _hostInteraction;
        private static NarrativePro.Tales.TalesComponent _hostTales;
        private static NarrativePro.SaveSystem.NarrativeSavableHostActor _hostIdentity;

        /// <summary>宿主装配模式下挂在宿主上的玩家状态：本地玩家的阵营来源（UE5 由 PlayerState 持有阵营）。</summary>
        private static NarrativePro.UnrealFramework.NarrativePlayerState _hostPlayerState;

        /// <summary>
        /// 上一次成功装配时宿主 Actor 的稳定身份（<see cref="Actor.ID"/>）。
        /// 用于区分「同一场景内玩家重生 / 换角色」与「未变化」：宿主被销毁或身份改变时必须重挂组件，
        /// 而不能像旧的 <see cref="_bHostComponentsAttached"/> 那样一次性短路到场景结束。
        /// </summary>
        private static Guid _attachedHostId = Guid.Empty;

        /// <summary>玩法场景引导宿主上的 GameState（时间/阵营联盟的运行时载体）。</summary>
        private static NarrativePro.UnrealFramework.NarrativeGameState _gameState;

        // ===== 网络适配器接线重试（插件只在 Initialize 时尝试一次，那时游戏场景脚本还不存在） =====

        private static bool _bNetworkAdapterWired;
        private static float _networkRetryTimer;
        private const float NetworkRetryIntervalSeconds = 1f;
        private const int NetworkRetryMaxAttempts = 120;
        private static int _networkRetryAttempts;

        /// <summary>
        /// 宿主解析的重试节奏。解析要遍历全场景 Actor，按帧跑会白烧 CPU（原先每帧最多 10 次遍历 ×1200 帧），
        /// 改为 0.5 秒一次、上限 120 次（约 60 秒），之后放弃并只告警一次。
        /// </summary>
        private const float HostResolveIntervalSeconds = 0.5f;
        private const int HostResolveMaxAttempts = 120;

        /// <summary>不参与玩法装配的场景（登录/启动/标题/常驻根场景）资产路径或根对象名关键字。</summary>
        private static readonly string[] NonGameplaySceneMarkers =
        {
            "login", "logo", "boot", "startup", "launcher", "launch", "init", "title",
            "rootscene",
        };

        /// <summary>已知登录场景资产 ID（<c>Source/Game/GameStartup.cs:75</c> 的兜底常量），用于精确排除登录场景。</summary>
        private static readonly Guid LoginSceneId = Guid.Parse("dc72848f4b0f90ee2df186a728f14858");

        /// <summary>已订阅 OnStackChanged 的激活栈（避免重复订阅，退订时逐个清理）。</summary>
        private static readonly List<NarrativeActivatableWidgetContainer> _hookedStacks = new List<NarrativeActivatableWidgetContainer>();

        // ===== Tales 事件订阅句柄（可精确退订） =====

        private static Action<TalesComponent, QuestClass> _onQuestStarted;
        private static Action<TalesComponent, QuestClass> _onQuestSucceeded;
        private static Action<TalesComponent, QuestClass> _onQuestFailed;
        private static Action<TalesComponent, DialogueClass, DialogueNode_NPC, DialogueLine, SpeakerInfo> _onNPCLineStarted;
        private static Action<TalesComponent, DialogueClass, DialogueNode_Player, DialogueLine> _onPlayerLineStarted;
        private static Action<TalesComponent, DialogueClass, EExitDialogueReason> _onDialogueFinished;

        // ===== 角色创建器会话 =====

        private static CharacterCreatorPlayerController _creatorController;
        private static ContainerControl _creatorRoot;
        private static readonly List<Control> _creatorRows = new List<Control>();
        private static bool _bCreatorOpen;
        private static float _creatorContentBottomY;

        // 过场/宣传片视频层（NarrativeCommonVideoPlayer 的运行期宿主；对应 UE5 由 UMG 放置 CommonVideoPlayer）
        private static ContainerControl _videoRoot;
        private static NarrativeCommonVideoPlayer _videoPlayer;
        private static float _videoAutoCloseRemaining;

        // ===== 对外只读视图 =====

        /// <summary>引导是否已安装。</summary>
        public static bool IsInstalled => _installed;

        /// <summary>运行期宿主 Actor（场景未启动时为 null）。</summary>
        public static Actor HostActor => _hostActor;

        /// <summary>CommonUI 子系统实例（字幕/菜单入口）。</summary>
        public static NarrativeCommonUISubsystem Subsystem => _subsystem;

        /// <summary>通用 HUD（GUI 根容器 + 激活栈）。</summary>
        public static NarrativeCommonHUD CommonHUD => _commonHUD;

        /// <summary>玩法 HUD（分层容器/通知）。</summary>
        public static NarrativeGameplayHUD GameplayHUD => _gameplayHUD;

        /// <summary>角色子系统（NPC 注册表）。</summary>
        public static NarrativeCharacterSubsystem CharacterSubsystem => _characterSubsystem;

        /// <summary>本地玩家控制器（引导补挂的 PlayerController）。</summary>
        public static NarrativePlayerController LocalController => _localController;

        /// <summary>
        /// 本地玩家宿主 Actor：角色路径时为 <c>NarrativeCharacter</c> 所在 Actor，
        /// 宿主装配路径时为本游戏承载本地玩家的场景 Actor（名为 "Player"）。未解析时为 null。
        /// </summary>
        public static Actor LocalPlayerHostActor => _playerHostActor;

        /// <summary>宿主装配模式下挂在宿主上的 ASC（角色路径为 null —— 角色的 ASC 由角色自身提供）。</summary>
        public static NarrativePro.GAS.NarrativeAbilitySystemComponent LocalPlayerAbilitySystemComponent => _hostAsc;

        /// <summary>宿主装配模式下挂在宿主上的 TalesComponent（角色路径为 null）。</summary>
        public static NarrativePro.Tales.TalesComponent LocalPlayerTalesComponent => _hostTales;

        /// <summary>该 Actor 是否就是引导登记的本地玩家宿主。</summary>
        public static bool IsLocalPlayerHostActor(Actor actor)
        {
            return actor != null && _playerHostActor != null && ReferenceEquals(actor, _playerHostActor);
        }

        /// <summary>对话面板逻辑。</summary>
        public static DialoguePanel Dialogue => _dialoguePanel;

        /// <summary>任务日志面板逻辑。</summary>
        public static QuestLogPanel QuestLog => _questLogPanel;

        /// <summary>任务追踪面板逻辑。</summary>
        public static QuestTracker Tracker => _questTracker;

        /// <summary>通知面板逻辑。</summary>
        public static NarrativeNotification Notification => _notification;

        /// <summary>创建器会话控制器（OpenCreator 之后非空）。</summary>
        public static CharacterCreatorPlayerController CreatorController => _creatorController;

        // =====================================================================
        // 安装 / 卸载
        // =====================================================================

        /// <summary>安装运行期引导（幂等）。对应 UE5 引擎自动完成的角色/GameMode/HUD 装配。</summary>
        public static void Install()
        {
            if (_installed) return;
            _installed = true;

            // 标签注册表：UI 层标签依赖它，未初始化时 RequestTag 结果不可用
            NarrativeGameplayTags.InitializeNativeTags();

            Level.SceneLoaded += OnSceneLoaded;
            Level.SceneUnloading += OnSceneUnloading;

            NarrativeLog.Log("[NarrativePro] 运行期引导已安装（HUD/CommonUI/角色子对象装配）");

            // 插件 Initialize 时场景通常已加载完毕，补跑一次当前场景。
            // 取最后加载的场景（玩法场景是附加加载的，0 号是常驻 RootScene，不是玩法场景）。
            Scripting.InvokeOnUpdate(() => InitializeForScene(
                Level.ScenesCount > 0 ? Level.GetScene(Level.ScenesCount - 1) : null));
        }

        /// <summary>卸载引导并回收运行期对象。</summary>
        public static void Uninstall()
        {
            if (!_installed) return;

            Level.SceneLoaded -= OnSceneLoaded;
            Level.SceneUnloading -= OnSceneUnloading;

            Teardown();
            _installed = false;
            NarrativeLog.Log("[NarrativePro] 运行期引导已卸载");
        }

        private static void OnSceneLoaded(Scene scene, Guid sceneId)
        {
            if (!_installed) return;
            Scripting.InvokeOnUpdate(() => InitializeForScene(scene, sceneId));
        }

        private static void OnSceneUnloading(Scene scene, Guid sceneId)
        {
            if (!_installed) return;
            Teardown();
        }

        // =====================================================================
        // 场景装配
        // =====================================================================

        // 集成测试一次性门闩：仅当环境变量 NARRATIVEPRO_RUN_INTEGRATION_TESTS=1 时，
        // 场景装配完成后跑一次 NarrativeProIntegrationTest（该测试有真实断言但此前零调用者）。
        private static bool _bIntegrationTestsRun;

        /// <summary>
        /// 为玩法场景创建运行期宿主与各子系统脚本（幂等，仅 Play 模式）。
        /// </summary>
        /// <param name="scene">目标场景。</param>
        /// <param name="sceneAssetId">
        /// <see cref="FlaxEngine.Level.SceneLoaded"/> 给出的场景资产 ID（可为空）。
        /// 注意 <see cref="Scene.Name"/> 是「根对象名」而不是资产文件名，判定登录/启动场景
        /// 必须走资产路径（<c>FlaxEngine.Content.GetAssetInfo(Guid, out AssetInfo)</c>）。
        /// </param>
        public static void InitializeForScene(Scene scene, Guid sceneAssetId = default)
        {
            if (!_installed) return;
            if (!Engine.IsPlayMode) return;
            if (scene == null) return;

            // 场景门禁：登录/启动/标题场景不装配玩法宿主与 HUD（此前无任何过滤，登录场景也会生成宿主/HUD）
            if (!IsGameplayScene(scene, sceneAssetId))
            {
                NarrativeLog.Log($"[NarrativePro] {DescribeScene(scene, sceneAssetId)} 不是玩法场景，跳过运行期装配");
                return;
            }

            if (_hostActor != null && _initializedSceneId == scene.ID) return;

            if (_initializedSceneId != Guid.Empty && _initializedSceneId != scene.ID)
            {
                Teardown();
            }

            var host = new EmptyActor
            {
                Name = HostActorName,
            };

            // 宿主生成进玩法场景（与其"按玩法场景装配/Teardown"的语义一致，随场景卸载自然销毁；
            // 无父级版会落 Scenes[0]=RootScene 并跨会话堆积）
            if (Level.SpawnActor(host, scene))
            {
                NarrativeLog.LogError("[NarrativePro] 运行期宿主 Actor 生成失败（HUD/子系统不会创建）");
                FlaxEngine.Object.Destroy(host);
                return;
            }

            _hostActor = host;
            _initializedSceneId = scene.ID;
            GameplayScene = scene;

            // 集成测试入口（默认关闭）：NARRATIVEPRO_RUN_INTEGRATION_TESTS=1 时每进程一次，
            // 结果走 NarrativeLog。此前 NarrativeProIntegrationTest 无任何调用方。
            if (!_bIntegrationTestsRun &&
                string.Equals(Environment.GetEnvironmentVariable("NARRATIVEPRO_RUN_INTEGRATION_TESTS"), "1", StringComparison.Ordinal))
            {
                _bIntegrationTestsRun = true;
                NarrativePro.Tests.NarrativeProIntegrationTest.RunAllTests();
            }

            // 顺序敏感：CommonUI 子系统要先于 HUD 启用 —— NarrativeCommonHUD.OnEnable 会把自己
            // 登记到 NarrativeCommonUISubsystem.Instance，反过来时 HUD 只会打一条警告。
            _characterSubsystem = NarrativeCharacterSubsystem.Instance ?? host.AddScript<NarrativeCharacterSubsystem>();
            _subsystem = NarrativeCommonUISubsystem.Instance ?? host.AddScript<NarrativeCommonUISubsystem>();
            _commonHUD = NarrativeCommonHUD.Instance ?? host.AddScript<NarrativeCommonHUD>();
            _gameplayHUD = Level.FindScript<NarrativeGameplayHUD>() ?? host.AddScript<NarrativeGameplayHUD>();

            _pump = host.AddScript<NarrativeRuntimePump>();

            // 群体（NPC / 行人）运行期头：本游戏的场景资产不实例化任何插件 Actor，
            // NPC 与 MassPedSimulation 同样只能由代码起（与本地玩家的宿主装配同一模式）。
            TryAttachCrowdRuntime(host);

            // 车流/红绿灯模拟与行人系统同构：UE5 由子系统自动存在，Flax 侧必须显式挂到宿主上才会逐帧跑。
            if (NarrativePro.Vehicles.VehicleTrafficSimulation.HasConfiguration())
            {
                NarrativePro.Vehicles.VehicleTrafficSimulation.AttachToRuntimeHost(host);
            }

            // 导航子系统（标记/POI/瓦片边界的注册表）。UE5 里它是世界子系统，天然存在；
            // Flax 没有世界子系统，而本游戏场景不实例化插件 Actor，此前全仓没有任何创建点：
            // NavigationSubsystem.Instance 恒为 null → MapMarker.RegisterMarker() 注册进空表
            // （任务标记、角色标记、POI 全线不可见），POI/瓦片边界的"反向补票"分支也从不执行。
            // 必须早于导航瓦片网格与角色装配，注册表先就位。
            var navigationSubsystem = Level.FindScript<NarrativePro.Navigation.NavigationSubsystem>()
                                      ?? host.AddScript<NarrativePro.Navigation.NavigationSubsystem>();
            if (navigationSubsystem == null)
            {
                NarrativeLog.LogWarning("[NarrativePro] 导航子系统挂载失败：地图标记/POI/瓦片边界注册表不可用");
            }

            // 地图瓦片边界/网格/POI 连线的绘制件。UE5 是 AMapTileBounds 构造期
            // CreateEditorOnlyDefaultSubobject（MapTileBounds.cpp:29）——即"每个边界 Actor 自带一份编辑器预览"。
            // Flax 端口不能在编辑态往场景 Actor 上 AddScript（会污染场景资产，本工程既有约束，
            // 见 NarrativeCharacter.EnsureSubobjects 里的 Engine.IsPlayMode 分支），
            // 而这份绘制件的数据源本来就是全局的（NavigationSubsystem.Instance.MapTileBounds
            // 或场景里任一 MapTileBoundsActor），所以挂在代码宿主上等价、且不碰场景资产。
            var tileRenderer = host.GetScript<NarrativePro.Navigation.MapTileRenderingComponent>()
                               ?? host.AddScript<NarrativePro.Navigation.MapTileRenderingComponent>();
            if (tileRenderer == null)
            {
                NarrativeLog.LogWarning("[NarrativePro] 地图瓦片绘制件挂载失败：编辑器里看不到瓦片边界/网格/POI 连线");
            }

            // 导航瓦片网格 = 掩护数据的唯一生产者。UE5 里 ANarrativeRecastNavMesh 由引擎随导航构建自动实例化，
            // 插件只换掉它的类；Flax 没有这一步，而本游戏场景资产不实例化任何插件 Actor，
            // 于是此前全仓唯一的 AddScript<NarrativeRecastNavMesh> 藏在没人挂载的门面脚本
            // NarrativeTileCoverGenerator 里 → 瓦片网格从不存在 → 掩护从不生成，
            // 玩家 L 键 TryEnterCover、AI TryTakeCoverFromThreat、FindNearestCoverToPoint 全部恒假。
            // OnEnable 里 bRebuildOnEnable 会自行 RebuildAll 并把瓦片标脏，OnUpdate 逐帧推进异步生成任务，
            // 所以这里只负责"让它存在"。
            var navMesh = Level.FindScript<NarrativePro.AI.Navigation.NarrativeRecastNavMesh>()
                          ?? host.AddScript<NarrativePro.AI.Navigation.NarrativeRecastNavMesh>();
            if (navMesh != null)
            {
                NarrativeLog.Log("[NarrativePro] 导航瓦片网格已挂载（掩护生成通路打通）");
            }
            else
            {
                NarrativeLog.LogWarning("[NarrativePro] 导航瓦片网格挂载失败：掩护系统不可达");
            }

            NarrativeLog.Log($"[NarrativePro] 场景 {scene.Name} 运行期装配完成（HUD={_commonHUD != null}, GameplayHUD={_gameplayHUD != null}, 角色子系统={_characterSubsystem != null}）");
        }

        /// <summary>
        /// 当磁盘上存在群体配置时，把 <see cref="NarrativePro.Spawners.NPCCrowdSpawner"/> 挂到运行期宿主上。
        /// <para>
        /// 复用既有做法：宿主由代码创建、脚本用 <c>GetScript ?? AddScript</c> 幂等补齐
        /// （Flax 的 <c>AddScript</c> 在活跃 Actor 上会立刻执行 OnEnable/OnUpdate，本仓库已有先例）。
        /// 生成动作本身在脚本自己的 Update 里等 <see cref="NarrativeCharacterSubsystem"/> 就绪后进行，
        /// 因此这里只负责「有配置就挂脚本」，不读取定义以外的任何运行期状态。
        /// </para>
        /// </summary>
        private static void TryAttachCrowdRuntime(Actor host)
        {
            if (host == null) return;

            try
            {
                NarrativePro.Data.NarrativeNPCDatabase.Initialize();

                if (!NarrativePro.Data.NarrativeCrowdConfig.HasAny())
                {
                    NarrativeLog.Log("[NarrativePro] 未发现群体配置（Content/NarrativePro/Crowd），NPC/行人不生成");
                    return;
                }

                var spawner = host.GetScript<NarrativePro.Spawners.NPCCrowdSpawner>();
                spawner ??= host.AddScript<NarrativePro.Spawners.NPCCrowdSpawner>();

                NarrativeLog.Log($"[NarrativePro] 群体运行期已挂载（NPCCrowdSpawner={spawner != null}，" +
                                 $"NPC 定义 {NarrativePro.Data.NarrativeNPCDatabase.Count} 条）");
            }
            catch (Exception ex)
            {
                NarrativeLog.LogWarning("[NarrativePro] 群体运行期挂载失败（NPC/行人不可达）：" + ex.Message);
            }
        }

        /// <summary>
        /// 判定场景是否为「玩法场景」：登录/启动/标题一类前置场景不装配玩法宿主、HUD 与玩家组件。
        /// <para>
        /// 依据（按可靠性排序）：① 已知登录场景资产 ID；② <see cref="Scene"/> 的资产路径
        /// （<c>Content/Maps/Login.scene</c> 之类，来自 <c>Content.GetAssetInfo</c>）；③ 根对象名。
        /// 本仓库事实：<c>Scene.Name</c> 是根对象名而非资产文件名，因此单靠 Name 判定不可靠，
        /// 这里把它只作为次级信号。
        /// </para>
        /// </summary>
        private static bool IsGameplayScene(Scene scene, Guid sceneAssetId)
        {
            if (scene == null) return false;

            if (sceneAssetId != Guid.Empty && sceneAssetId == LoginSceneId) return false;
            if (scene.ID != Guid.Empty && scene.ID == LoginSceneId) return false;

            string probe = BuildSceneProbe(scene, sceneAssetId);
            if (string.IsNullOrEmpty(probe)) return true;

            for (int i = 0; i < NonGameplaySceneMarkers.Length; i++)
            {
                if (probe.IndexOf(NonGameplaySceneMarkers[i], StringComparison.OrdinalIgnoreCase) >= 0) return false;
            }
            return true;
        }

        /// <summary>解析场景资产路径（事件给出的资产 ID 优先，其次按 Scene.ID 反查），失败时返回空串。</summary>
        private static string ResolveSceneAssetPath(Scene scene, Guid sceneAssetId)
        {
            try
            {
                if (sceneAssetId != Guid.Empty && Content.GetAssetInfo(sceneAssetId, out var info))
                {
                    if (!string.IsNullOrEmpty(info.Path)) return info.Path;
                }

                if (scene != null && scene.ID != Guid.Empty && Content.GetAssetInfo(scene.ID, out var byActor))
                {
                    return byActor.Path ?? string.Empty;
                }
            }
            catch (Exception ex)
            {
                NarrativeLog.LogWarning("[NarrativePro] 场景资产路径解析失败：" + ex.Message);
            }

            return string.Empty;
        }

        /// <summary>场景判定用的探针串（资产路径 + 根对象名，统一成正斜杠路径）。</summary>
        private static string BuildSceneProbe(Scene scene, Guid sceneAssetId)
        {
            string path = ResolveSceneAssetPath(scene, sceneAssetId);
            string name = scene != null ? scene.Name : string.Empty;
            string probe = (path ?? string.Empty).Replace('\\', '/');
            if (!string.IsNullOrEmpty(name)) probe += "/" + name;
            return probe;
        }

        /// <summary>供日志使用的场景描述（资产路径优先，避免只看到根对象名而无法定位）。</summary>
        private static string DescribeScene(Scene scene, Guid sceneAssetId)
        {
            string path = ResolveSceneAssetPath(scene, sceneAssetId);
            return string.IsNullOrEmpty(path)
                ? $"场景（根对象名={scene?.Name}，ID={scene?.ID}）"
                : $"场景 {path}";
        }

        /// <summary>由 <see cref="NarrativeRuntimePump"/> 每帧驱动的分步装配。</summary>
        internal static void Tick()
        {
            if (!_installed || _hostActor == null) return;

            // 1) HUD 根控件（GUI 主线程创建，Initialize 内部会复用/新建 UICanvas）
            if (_commonHUD != null && !_commonHUD.IsInitialized)
            {
                _commonHUD.Initialize(null);
            }

            if (_commonHUD == null || _commonHUD.RootControl == null) return;

            // 2) 把子系统缓存的 HUD 补齐（子系统可能先于 HUD 创建）
            if (_subsystem != null && _subsystem.CommonHUD == null)
            {
                _subsystem.CommonHUD = _commonHUD;
            }

            // 3) 本地玩家宿主装配 + 角色子对象名册
            //    （EnsureLocalPlayerHostComponents 优先走真正的插件角色路径，
            //      没有角色时把 ASC/背包/交互/Tales/身份脚本 AddScript 到游戏的本地玩家宿主 Actor 上）
            EnsureLocalPlayerHostComponents();
            RosterExistingCharacters();
            EnsureGameState();

            // 3b) 音乐子系统：Flax 没有 GameInstanceSubsystem，NarrativeMusicSubsystem 必须被某个 Actor 持有
            //     才会被引擎驱动 OnUpdate（淡入淡出靠它），否则整套状态机是死代码。
            EnsureMusicSubsystem();

            // 4) 注册 UE5 在 GameplayHUD 里注册的三个层
            RegisterLayers();
            HookActivationStacks();

            // 5) 面板与 Tales 数据接线
            EnsurePanelsAndTales();

            // 6) 视频层的定时关闭（对应 UE5 过场播完 RemoveWidget）
            TickVideo();

            // 7) 音乐回落计时（短乐句/紧张段播放完后回常态主题）
            NarrativePro.Music.NarrativeMusicDriver.Tick();

            // 8) 网络适配器接线重试（插件只在 Initialize 时试一次，那时游戏场景脚本/NetworkManager 还不存在）
            TickNetworkAdapterRetry();
        }

        private static void TickVideo()
        {
            if (_videoAutoCloseRemaining <= 0f) return;

            _videoAutoCloseRemaining -= Time.DeltaTime;
            if (_videoAutoCloseRemaining <= 0f)
            {
                _videoAutoCloseRemaining = 0f;
                StopVideo();
            }
        }

        /// <summary>
        /// 订阅所有激活栈的 <c>OnStackChanged</c>。
        /// UE5 由 CommonUI 自己把「栈顶控件」与输入模式连起来；Flax 端口里这个事件一个订阅者都没有，
        /// 因此栈顶变化不会重算输入门禁，也不会重排新入栈控件 —— 这里补上消费者。
        /// </summary>
        private static void HookActivationStacks()
        {
            if (_commonHUD == null) return;

            HookStack(_commonHUD.WidgetStack);
            HookStack(_commonHUD.PromptStack);

            if (_gameplayHUD != null)
            {
                foreach (var container in _gameplayHUD.Layers.Values)
                {
                    HookStack(container);
                }
            }
        }

        private static void HookStack(NarrativeActivatableWidgetContainer stack)
        {
            if (stack == null || _hookedStacks.Contains(stack)) return;

            _hookedStacks.Add(stack);
            stack.OnStackChanged += HandleStackChanged;
        }

        private static void HandleStackChanged(NarrativeActivatableWidget active)
        {
            // 栈顶变化 → 控制器重新对账游戏输入门禁（菜单态阻断游戏输入）
            _localController?.RefreshGameInputBlockState();

            // 新入栈控件按当前栈尺寸重排一次（TopLeft 锚 + 显式尺寸）
            if (active != null && active.Parent is NarrativeActivatableWidgetContainer container)
            {
                container.RelayoutAll();
            }
        }

        /// <summary>
        /// 为场景里的本地玩家角色补挂 <see cref="NarrativePlayerController"/>。
        /// 对应 UE5 GameMode::StartPlay → SpawnDefaultPawn + Possess：端口里没人生成控制器，
        /// 于是 IsLocallyControlled 恒 false（脚步 foley 的本地权威门永不开）、HUD 的 OwnerPC 为空（OpenMenu 永不开）。
        /// </summary>
        private static void EnsureLocalPlayerController()
        {
            if (_localController != null && _localController.Actor != null)
            {
                if (_localController.GetNarrativeGameplayHUD() == null)
                {
                    _localController.AttachGameplayHUD(_gameplayHUD);
                }
                return;
            }

            var player = Level.FindScript<NarrativePlayerCharacter>();
            if (player == null || player.Actor == null)
            {
                // 本游戏不实例化插件角色：改走「宿主装配」路径，把控制器挂到引导解析出的本地玩家宿主上。
                // 没有控制器时 HUD.OwnerPC 为空 → 暂停菜单（NarrativeGameplayHUD.OpenMenu）永不可达，
                // 输入门禁也就没人对账，所以这一步是 UI 链路可达的前提。
                EnsureLocalPlayerControllerForHost();
                return;
            }

            var controller = player.Actor.GetScript<NarrativePlayerController>();
            controller ??= player.Actor.AddScript<NarrativePlayerController>();
            if (controller == null) return;

            controller.OnPossess(player);
            controller.AttachGameplayHUD(_gameplayHUD);

            // 控制器到位后作废脚步 foley 的本地权威缓存：否则首帧求到的 false 会把脚步声永久静音
            player.GetFootstepFoleyComponent()?.InvalidateAuthorityCache();

            if (_gameplayHUD != null && _gameplayHUD.OwnerPC == null)
            {
                _gameplayHUD.OwnerPC = controller;
            }

            _localController = controller;
            NarrativeLog.Log($"[NarrativePro] 已为本地玩家角色 {player.Actor.Name} 挂接 NarrativePlayerController");
        }

        /// <summary>
        /// 宿主装配模式下的控制器挂接：把 <see cref="NarrativePlayerController"/> 挂到
        /// 本地玩家宿主 Actor（本游戏为场景里名为 "Player" 的 Actor）并与已装配的组件对接。
        /// </summary>
        private static void EnsureLocalPlayerControllerForHost()
        {
            var host = _playerHostActor;
            if (host == null || !_bHostComponentsAttached || _bHostIsNarrativeCharacter) return;

            var controller = host.GetScript<NarrativePlayerController>();
            controller ??= host.AddScript<NarrativePlayerController>();
            if (controller == null) return;

            // 没有 NarrativeCharacter 可 Possess：改用宿主绑定，
            // 控制器内部会把自己所在 Actor 上的 ASC / 交互 / Tales / 玩家状态 / 导航组件采集进来。
            controller.BindHostActor(host);
            controller.AttachGameplayHUD(_gameplayHUD);

            // OwnerPC 无条件指向本次（重）装配出的控制器：重生/换角色后旧的已被 DetachHostComponents 置空，
            // 这里确保 HUD 的暂停菜单/输入门禁重新连到新控制器。
            if (_gameplayHUD != null)
            {
                _gameplayHUD.OwnerPC = controller;
            }

            _localController = controller;
            NarrativeLog.Log($"[NarrativePro] 宿主装配模式：已为本地玩家宿主 {host.Name} 挂接 NarrativePlayerController（HUD.OwnerPC 就绪 → 暂停菜单/输入门禁可达）");
        }

        // =====================================================================
        // 本地玩家宿主装配（本游戏不实例化插件角色时的等价装配）
        // =====================================================================

        /// <summary>
        /// 解析本地玩家宿主并装配插件组件。
        /// <para>
        /// 两条路径共存：<see cref="NarrativeCharacter"/>（含 <see cref="NarrativePlayerCharacter"/>）存在时
        /// 走角色自身的 <c>EnsureSubobjects</c>（UE5 类默认对象等价物），引导不重复 AddScript；
        /// 没有插件角色时（本游戏的实际情况）把
        /// 身份脚本 / ASC(+属性集) / 背包 / 交互 / TalesComponent 直接 <c>AddScript</c> 到游戏的宿主 Actor 上。
        /// 幂等：装配成功后由 <see cref="_bHostComponentsAttached"/> 短路；宿主可能晚于引导出现，故按帧有限重试。
        /// </para>
        /// </summary>
        /// <summary>
        /// 解析本地玩家宿主并装配插件组件；<b>按宿主身份对账</b>，而非「一场景一次」。
        /// <para>
        /// 两条路径共存：存在 <see cref="NarrativeCharacter"/> 时走角色自身的 <c>EnsureSubobjects</c>；
        /// 否则把 身份脚本 / ASC(+属性集) / 背包 / 装备 / 交互 / Tales / 玩家状态 AddScript 到宿主 Actor。
        /// </para>
        /// <para>
        /// 本游戏的本地玩家 Actor 会在<see cref="Actor.ID"/> 不变的场景内被销毁重建（重生 / 换角色，
        /// 见 <c>Source/Game/HundunWorldGame.cs:2246-2250</c>），而 <see cref="ArsenalStatics"/> 会重新解析出新宿主。
        /// 旧的 <see cref="_bHostComponentsAttached"/> 一次性短路会让组件永久黏在已销毁的旧宿主上，
        /// 于是整场游戏里交互与 UI 静默失效。这里改为：宿主被销毁或身份变化时先 <see cref="DetachHostComponents"/>
        /// 干净拆掉旧的每宿主状态，再重挂到新宿主；宿主未变时保持幂等，且解析仍按 <see cref="HostResolveIntervalSeconds"/> 节流。
        /// </para>
        /// </summary>
        private static void EnsureLocalPlayerHostComponents()
        {
            // 已装配且当前宿主仍然有效（引用未销毁、Actor.ID 未变）→ 直接返回，不每帧重解析。
            if (_bHostComponentsAttached && IsAttachedHostStillValid()) return;

            _hostResolveTimer += (float)Time.DeltaTime;
            if (_hostResolveTimer < HostResolveIntervalSeconds) return;
            _hostResolveTimer = 0f;

            // 重试上限只约束「从未装配成功」的首次解析（宿主可能晚于引导出现）；
            // 已装配过则不受上限约束——玩家重生 / 换角色必须能再次装配，只受上面的节流限制。
            if (!_bHostComponentsAttached)
            {
                if (_hostResolveAttempts++ >= HostResolveMaxAttempts)
                {
                    if (_hostResolveAttempts == HostResolveMaxAttempts)
                    {
                        NarrativeLog.LogWarning("[NarrativePro] 未解析到本地玩家宿主 Actor（游戏 LocalPlayerActor / 插件角色 / 约定命名均未命中），ASC/背包/交互/Tales 未装配");
                    }
                    return;
                }
            }

            var host = ArsenalStatics.ResolveLocalPlayerHostActor(out bool bUsesNarrativeCharacter);
            if (host == null)
            {
                // 旧宿主已被销毁且新宿主尚未出现（重生窗口内）：先干净拆掉旧的每宿主状态，等下一拍再装配。
                if (_bHostComponentsAttached) DetachHostComponents();
                return;
            }

            // 解析到的仍是当前已装配宿主（引用一致、Actor.ID 一致、装配模式一致）→ 保持，不做无谓的重挂。
            if (_bHostComponentsAttached
                && ReferenceEquals(_playerHostActor, host)
                && _attachedHostId == host.ID
                && _bHostIsNarrativeCharacter == bUsesNarrativeCharacter)
            {
                return;
            }

            // 宿主身份变化（首次装配 / 重生 / 换角色）：先拆旧，再挂新。
            if (_bHostComponentsAttached) DetachHostComponents();

            _playerHostActor = host;
            _attachedHostId = host.ID;
            _bHostIsNarrativeCharacter = bUsesNarrativeCharacter;

            if (bUsesNarrativeCharacter)
            {
                var character = host.GetScript<NarrativeCharacter>();
                character?.EnsureSubobjects();
                character?.RegisterWithCharacterSubsystem();
                _bHostComponentsAttached = true;

                // 角色路径：控制器仍由这一步补挂（Possess 语义，既有行为不变）。
                EnsureLocalPlayerController();
                NarrativeLog.Log($"[NarrativePro] 本地玩家为插件角色 {host.Name}，走角色子对象装配路径");
                return;
            }

            AttachHostComponents(host);
            _bHostComponentsAttached = true;

            // 宿主装配成功的那一帧就挂控制器：AddScript 出的控制器 OnEnable 能直接采集到
            // 同 Actor 的 ASC/背包/交互/Tales/玩家状态，省掉一次「下一帧才补控制器」的空窗。
            EnsureLocalPlayerControllerForHost();
        }

        /// <summary>
        /// 已装配宿主是否仍是同一个：引用未随销毁变 null，且 <see cref="Actor.ID"/> 未变。
        /// 只读缓存引用与 ID，不做任何场景遍历，因此可以每帧廉价调用。
        /// </summary>
        private static bool IsAttachedHostStillValid()
        {
            if (_playerHostActor == null) return false;        // Flax：宿主 Actor 被销毁后按 null 处理
            if (_attachedHostId == Guid.Empty) return false;
            if (_playerHostActor.ID != _attachedHostId) return false;  // 同一 Actor 被复用但换了身份（换角色）
            return true;
        }

        /// <summary>
        /// 干净拆掉「每宿主」状态：面板/事件解绑、身份脚本退订、名册注销、按住交互取消、
        /// 控制器与 HUD.OwnerPC 作废、各组件缓存引用清空。
        /// 脚本本体随旧宿主 Actor 一起销毁，这里只断引用与订阅，绝不销毁引导自身的宿主/HUD。
        /// </summary>
        private static void DetachHostComponents()
        {
            // 1) 面板与 Tales 事件切离旧 TalesComponent（旧组件若已随宿主销毁，退订只是清托管委托，安全）。
            //    新宿主装配后 EnsurePanelsAndTales 会看到新的 _hostTales 并重新 RebindTales。
            UnbindTales();

            // 2) 存档身份脚本：退订记录还原回调，避免闭包持有旧宿主引用。
            if (_hostIdentity != null) _hostIdentity.OnHostRecordLoaded -= HandleHostRecordLoaded;

            // 3) 角色路径：把旧宿主角色从名册注销，避免重生后名册里残留已销毁角色。
            if (_bHostIsNarrativeCharacter && _playerHostActor != null)
            {
                _characterSubsystem?.UnregisterCharacter(_playerHostActor);
            }

            // 4) 交互：显式丢弃旧宿主上的按住交互状态，避免 bInteractHeld 黏在已销毁/死亡的宿主上。
            _hostInteraction?.ForceCancelInteraction();

            // 5) 控制器缓存与 HUD.OwnerPC 指向旧宿主上的控制器，宿主销毁后作废以便在新宿主上重挂。
            _localController = null;
            if (_gameplayHUD != null) _gameplayHUD.OwnerPC = null;

            // 5b) 音乐：宿主销毁会一并带走 NarrativeMusicSubsystem 实例，驱动侧的回落计时/常态主题复位，
            //     避免把上一场景的计时状态带进新场景（新场景由 EnsureMusicSubsystem 重新起底）。
            _musicSubsystem = null;
            NarrativePro.Music.NarrativeMusicDriver.Reset();

            // 6) 清空每宿主缓存引用（脚本本体随旧宿主 Actor 一起销毁）。
            _hostAsc = null;
            _hostInventory = null;
            _hostEquipment = null;
            _hostInteraction = null;
            _hostTales = null;
            _hostIdentity = null;
            _hostPlayerState = null;
            _hostNavigation = null;
            _hostCamera = null;

            _bHostComponentsAttached = false;
            _attachedHostId = Guid.Empty;
            // 保留 _hostResolveAttempts：重挂不受首次上限约束（见 EnsureLocalPlayerHostComponents）。
        }

        // 缺陷①/②补挂组件的宿主缓存引用（AttachHostComponents 幂等装配、DetachHostComponents 置空）。
        private static NarrativePro.Navigation.NarrativeNavigationComponent _hostNavigation;
        private static NarrativePro.Camera.NarrativeCameraComponent _hostCamera;

        /// <summary>把插件玩法组件装配到宿主 Actor 上（幂等：已存在的组件只复用，不重复创建）。</summary>
        private static void AttachHostComponents(Actor host)
        {
            _attachedHostId = host.ID;

            // 1) 存档身份脚本最先挂：NarrativeSaveSubsystem 依据「Actor 上能否解析出稳定 GUID」
            //    决定要不要为它建记录（NarrativeSaveSubsystem.cs:1212-1223），顺序错了整条任务持久化就进不了档。
            if (_hostIdentity == null || !ReferenceEquals(_hostIdentity.Actor, host))
            {
                _hostIdentity = host.GetScript<NarrativePro.SaveSystem.NarrativeSavableHostActor>()
                                ?? host.AddScript<NarrativePro.SaveSystem.NarrativeSavableHostActor>();
                if (_hostIdentity != null)
                {
                    _hostIdentity.StableKey = NarrativePro.SaveSystem.NarrativeSavableHostActor.LocalPlayerHostKey;
                    _hostIdentity.OnHostRecordLoaded += HandleHostRecordLoaded;
                }
            }

            // 2) ASC + 属性集。时序对齐 NarrativeCharacter.EnsureAbilitySystemComponent：
            //    AddScript 出的脚本 OnEnable 已用空配置跑过一次初始化，不重刷会静默丢属性/启动效果；
            //    NarrativeAttributeSetBase 由 ASC 的初始化在本 Actor 上创建（见 ASC.InitializeAttributes）。
            _hostAsc = host.GetScript<NarrativePro.GAS.NarrativeAbilitySystemComponent>();
            if (_hostAsc == null)
            {
                _hostAsc = host.AddScript<NarrativePro.GAS.NarrativeAbilitySystemComponent>();
                if (_hostAsc != null)
                {
                    if (_hostAsc.AreStartupEffectsApplied) _hostAsc.ReapplyStartupEffects();
                    else _hostAsc.InitializeAttributes();
                }
            }

            // 2b) 玩家状态（阵营来源）。UE5 里 PlayerState 持有玩家阵营；本游戏宿主不是插件角色，
            //     缺 PlayerState 时 NarrativePlayerController.GetFactions 恒返回空容器，
            //     AddFaction 也无处可写 → 本地玩家对任何阵营永远无法为友军。这里补一份可持久、可读写的阵营容器，
            //     并让 GetTeamAttitudeTowards 走同一份容器（见 NarrativePlayerController.ResolveActorFactions）。
            _hostPlayerState = host.GetScript<NarrativePro.UnrealFramework.NarrativePlayerState>()
                               ?? host.AddScript<NarrativePro.UnrealFramework.NarrativePlayerState>();

            // 3) 背包 / 交互 / Tales。
            //    TalesComponent.OnEnable 会把自身注册为插件激活组件（NarrativeProPlugin.SetActiveTalesComponent），
            //    EnsurePanelsAndTales 因此不再早退 → 对话/任务面板 + NarrativeUIBuilder.BuildAll 可达；
            //    其 OnUpdate 同时驱动 SyncManager.TickSync 与任务 tick。
            _hostInventory = host.GetScript<NarrativePro.Items.NarrativeInventoryComponent>()
                             ?? host.AddScript<NarrativePro.Items.NarrativeInventoryComponent>();
            // 装备/持握组件：宿主不是 NarrativeCharacter 子类，UE5 由角色类默认对象提供的这份必须补上，
            // 否则拾取→装备→持握→授予武器能力这条链在宿主装配模式下同样静默失败。
            _hostEquipment = host.GetScript<NarrativePro.Items.EquipmentComponent>()
                             ?? host.AddScript<NarrativePro.Items.EquipmentComponent>();
            _hostInteraction = host.GetScript<NarrativePro.Interaction.PlayerInteractionComponent>()
                               ?? host.AddScript<NarrativePro.Interaction.PlayerInteractionComponent>();
            _hostTales = host.GetScript<TalesComponent>() ?? host.AddScript<TalesComponent>();

            // 3b) 导航组件（缺陷①）：此前宿主装配模式从不挂 NarrativeNavigationComponent，
            //     NarrativePlayerController.FastTravelToPOI 恒告警失败（插件导航整条死路）。
            //     补挂后调用链：FastTravelToPOI → RequestFastTravel（校验 bSupportsFastTravel/已发现）
            //     → ResolveFastTravelTransform → FindPOIActor 在场景中按 POITag 命中 World.scene 的
            //     POIActor（10 个）→ OnFastTravelRequested / ArsenalStatics.TeleportActor。
            _hostNavigation = host.GetScript<NarrativePro.Navigation.NarrativeNavigationComponent>()
                              ?? host.AddScript<NarrativePro.Navigation.NarrativeNavigationComponent>();

            // 3c) 相机组件（缺陷②(a)）：给模式栈/ADS FOV/弹簧臂/震动一个运行态归属者——
            //     对话 cinecam（NarrativeDialogueSequence.ResolveCameraComponent）用
            //     Level.FindScript<NarrativeCameraComponent> 找它，此前全场景 0 命中。
            //     注意：AddScript 出的组件 bDrivesCameraOutput 默认 false——它不写 MainCamera 的
            //     FOV/位置/朝向，游戏的 ThirdPersonCamera 继续独占相机（缺陷②(b) 的单一谓词，
            //     见 Camera/NarrativeCameraComponent.bDrivesCameraOutput）。宿主集成要交还相机
            //     控制权时显式调用 _hostCamera.ClaimCameraOwnership()。
            _hostCamera = host.GetScript<NarrativePro.Camera.NarrativeCameraComponent>()
                          ?? host.AddScript<NarrativePro.Camera.NarrativeCameraComponent>();

            // 4) 登记进存档子系统查找表；宿主由场景/引导每次重建，身份脚本的 ShouldRespawn=false，
            //    因此记录只做状态还原，不会被当成需要动态重生的 Actor。
            var subsystem = NarrativePro.SaveSystem.Subsystems.NarrativeSaveSubsystem.Instance;
            subsystem?.NotifyActorDynamicallySpawned(host);

            // 5) 世界档可能早于本次装配就已读取（当时宿主还不存在，记录没被命中）→ 补放一次记录，
            //    走的就是子系统既有通路 LoadActorFromRecord → 逐组件 INarrativeSavableComponent.Load()。
            Scripting.InvokeOnUpdate(() => ReplayHostRecord(host));

            NarrativeLog.Log($"[NarrativePro] 宿主装配完成：{host.Name}（ASC={_hostAsc != null}, 背包={_hostInventory != null}, 交互={_hostInteraction != null}, Tales={_hostTales != null}, 存档身份={_hostIdentity != null}, 玩家状态={_hostPlayerState != null}, 导航={_hostNavigation != null}, 相机(不驱动)={_hostCamera != null && !_hostCamera.bDrivesCameraOutput}）");
        }

        /// <summary>补放宿主 Actor 的存档记录（读档早于宿主装配时的补偿通路）。</summary>
        private static void ReplayHostRecord(Actor host)
        {
            if (host == null || _hostIdentity == null) return;

            var subsystem = NarrativePro.SaveSystem.Subsystems.NarrativeSaveSubsystem.Instance;
            var save = subsystem?.GetSaveObject();
            if (save == null) return;

            Guid guid = _hostIdentity.GetActorGUID();
            if (guid == Guid.Empty) return;

            if (save.RecordMap != null
                && save.RecordMap.TryGetValue(guid, out var record)
                && record != null
                && !record.bDestroyed)
            {
                subsystem.LoadActorFromRecord(host, record);
                NarrativeLog.Log($"[NarrativePro] 宿主装配后补放存档记录（GUID={guid}）：TalesComponent.Load() 已执行，任务/对话进度还原");
            }
        }

        /// <summary>宿主记录还原完成：让面板视图重新对账一次（任务列表已被还原）。</summary>
        private static void HandleHostRecordLoaded(NarrativePro.SaveSystem.NarrativeSavableHostActor identity)
        {
            _uiBuilder?.Update();
        }

        /// <summary>
        /// 保证存在一个 <see cref="NarrativePro.UnrealFramework.NarrativeGameState"/>。
        /// UE5 由 GameMode/世界设置生成 GameState；本游戏没有，缺它时游戏内时间恒为 0，
        /// 所有 TimeOfDay 条件/事件（任务与 NPC 日程）都判定不过。
        /// </summary>
        private static void EnsureGameState()
        {
            if (_gameState != null && _gameState.Actor != null) return;
            if (_hostActor == null) return;

            _gameState = NarrativePro.UnrealFramework.NarrativeGameState.GetCurrent()
                         ?? _hostActor.AddScript<NarrativePro.UnrealFramework.NarrativeGameState>();
        }

        /// <summary>
        /// 保证存在一个 <see cref="NarrativePro.Music.NarrativeMusicSubsystem"/> 并让它真正被驱动。
        /// <para>
        /// UE5 里它是 GameInstanceSubsystem（自动存在）且 SetTheme 由蓝图调用；Flax 两者都没有，
        /// 结果这套完整的状态机既没被任何场景持有、也无人调用 —— 音乐静默缺失。
        /// 这里按 <see cref="EnsureGameState"/> 的同一模式把它补挂到运行期宿主上，
        /// 主题切换由 <see cref="NarrativePro.Music.NarrativeMusicDriver"/> 接 Tales 的任务事件（见 RebindTales）。
        /// </para>
        /// </summary>
        private static void EnsureMusicSubsystem()
        {
            if (_musicSubsystem != null && _musicSubsystem.Actor != null) return;
            if (_hostActor == null) return;

            _musicSubsystem = NarrativePro.Music.NarrativeMusicDriver.AttachTo(_hostActor);
        }

        // =====================================================================
        // 网络适配器接线重试
        // =====================================================================

        /// <summary>
        /// 周期性重试插件的网络适配器接线。
        /// <para>
        /// <c>NarrativeProPlugin.Initialize</c> 只在插件初始化时用 <c>InvokeOnUpdate</c> 试一次
        /// <c>TryWireNetworkAdapter</c>，那一刻游戏场景脚本（HundunWorldGame / NetworkManager）还没创建，
        /// 反射拿不到适配器且没有重试 → 之后创建的 SyncManager 永远没有 SendCallback。
        /// 本引导的泵运行在玩法场景里（此时游戏脚本已存在），正好补上缺失的重试。
        /// 插件的 <c>TryWireNetworkAdapter</c>/<c>_networkAdapterRef</c>/<c>_syncManager</c> 是私有成员，
        /// 且插件文件不在本次可改范围内，因此这里按反射驱动（与插件自身桥接 Game 模块用的同一手法）。
        /// </para>
        /// </summary>
        private static void TickNetworkAdapterRetry()
        {
            if (_bNetworkAdapterWired) return;
            if (_networkRetryAttempts >= NetworkRetryMaxAttempts) return;

            _networkRetryTimer -= Time.DeltaTime;
            if (_networkRetryTimer > 0f) return;
            _networkRetryTimer = NetworkRetryIntervalSeconds;
            _networkRetryAttempts++;

            var plugin = NarrativeProPlugin.Instance;
            if (plugin == null) return;

            if (!RetryWireNetworkAdapter(plugin)) return;

            // 接线成功前可能已创建过没有 SendCallback 的 SyncManager（插件不会二次配置），丢弃后重建
            ResetCachedSyncManager(plugin);
            var sync = plugin.SyncManager;
            _bNetworkAdapterWired = true;
            NarrativeLog.Log($"[NarrativePro] 网络适配器接线重试成功（第 {_networkRetryAttempts} 次），SyncManager={(sync != null ? "已就绪" : "等待 ActiveTalesComponent")}");
        }

        /// <summary>反射调用插件私有的 TryWireNetworkAdapter，直到 _networkAdapterRef 非空。</summary>
        private static bool RetryWireNetworkAdapter(object plugin)
        {
            try
            {
                var type = plugin.GetType();
                var adapterField = type.GetField("_networkAdapterRef", BindingFlags.Instance | BindingFlags.NonPublic);
                if (adapterField == null) return false;

                if (adapterField.GetValue(plugin) != null) return true;

                var wire = type.GetMethod("TryWireNetworkAdapter", BindingFlags.Instance | BindingFlags.NonPublic);
                if (wire == null) return false;

                wire.Invoke(plugin, null);
                return adapterField.GetValue(plugin) != null;
            }
            catch (Exception ex)
            {
                NarrativeLog.LogWarning("[NarrativePro] 网络适配器接线重试异常：" + ex.Message);
                return false;
            }
        }

        /// <summary>丢弃接线成功之前创建的 SyncManager（它的 SendCallback 为 null 且插件不会重配）。</summary>
        private static void ResetCachedSyncManager(object plugin)
        {
            try
            {
                var type = plugin.GetType();
                var field = type.GetField("_syncManager", BindingFlags.Instance | BindingFlags.NonPublic);
                if (field == null) return;

                var manager = field.GetValue(plugin) as NarrativePro.Network.NarrativeSyncManager;
                if (manager != null && manager.SendCallback == null)
                {
                    field.SetValue(plugin, null);
                }
            }
            catch (Exception ex)
            {
                NarrativeLog.LogWarning("[NarrativePro] SyncManager 重建异常：" + ex.Message);
            }
        }

        /// <summary>把场景里已存在的角色补登记进角色子系统（角色早于引导启用时的补票）。</summary>
        private static void RosterExistingCharacters()
        {
            if (_rosterDone || _characterSubsystem == null) return;
            _rosterDone = true;

            try
            {
                var actors = Level.GetActors<Actor>(true);
                if (actors == null) return;

                int count = 0;
                for (int i = 0; i < actors.Length; i++)
                {
                    var actor = actors[i];
                    if (actor == null) continue;

                    var character = actor.GetScript<NarrativeCharacter>();
                    if (character == null) continue;

                    character.EnsureSubobjects();
                    // 角色启用时子系统可能还不存在（引导稍后建），这里补一次登记；方法自身幂等，不会重复入册
                    character.RegisterWithCharacterSubsystem();
                    count++;
                }

                if (count > 0)
                {
                    NarrativeLog.Log($"[NarrativePro] 角色子系统已登记场景内角色 {count} 个（ASC/背包/交互/foley 已装配）");
                }
            }
            catch (Exception ex)
            {
                NarrativeLog.LogWarning("[NarrativePro] 角色名册装配失败：" + ex.Message);
            }
        }

        /// <summary>注册 UE5 侧的 UI 层容器（Game / Menu / Modal），使 Layers 字典真正有内容。</summary>
        private static void RegisterLayers()
        {
            if (_layersRegistered || _gameplayHUD == null) return;

            _gameplayHUD.HudHost = _commonHUD;

            _gameplayHUD.GetOrAddLayerContainer(NarrativeGameplayTags.UI_Layer_Game);
            _gameplayHUD.GetOrAddLayerContainer(NarrativeGameplayTags.UI_Layer_Menu);
            _gameplayHUD.GetOrAddLayerContainer(NarrativeGameplayTags.UI_Layer_Modal);

            _layersRegistered = _gameplayHUD.Layers.Count > 0;
            if (_layersRegistered)
            {
                NarrativeLog.Log("[NarrativePro] UI 层已注册：Game / Menu / Modal");
            }
        }

        /// <summary>创建对话/任务/追踪/通知面板并把激活的 TalesComponent 接进来。</summary>
        private static void EnsurePanelsAndTales()
        {
            var tales = _hostTales;   // 宿主装配模式下的 TalesComponent（权威来源）
            tales ??= NarrativeProPlugin.Instance != null ? NarrativeProPlugin.Instance.ActiveTalesComponent : null;
            tales ??= Level.FindScript<TalesComponent>();
            if (tales == null) return;

            if (_dialoguePanel == null) _dialoguePanel = Level.FindScript<DialoguePanel>() ?? _hostActor.AddScript<DialoguePanel>();
            if (_questLogPanel == null) _questLogPanel = Level.FindScript<QuestLogPanel>() ?? _hostActor.AddScript<QuestLogPanel>();
            if (_questTracker == null) _questTracker = Level.FindScript<QuestTracker>() ?? _hostActor.AddScript<QuestTracker>();
            if (_notification == null) _notification = NarrativeNotification.Instance ?? Level.FindScript<NarrativeNotification>() ?? _hostActor.AddScript<NarrativeNotification>();

            if (ReferenceEquals(_boundTales, tales) && _uiBuilt)
            {
                _uiBuilder?.Update();
                return;
            }

            RebindTales(tales);
        }

        /// <summary>把面板与 HUD 的事件订阅切换到新的 TalesComponent。</summary>
        private static void RebindTales(TalesComponent tales)
        {
            UnbindTales();
            _boundTales = tales;

            if (_dialoguePanel != null) _dialoguePanel.Bind(tales);
            if (_questLogPanel != null) _questLogPanel.Bind(tales);
            if (_questTracker != null) _questTracker.Bind(tales);

            // 任务事件 → 通知面板 + 玩法 HUD 提示（UE5 由蓝图 ShowNotification 承担）
            //          → 音乐主题（UE5 由蓝图调 SetTheme；Flax 无蓝图，代码侧必须有调用方）
            _onQuestStarted = (component, quest) =>
            {
                string name = quest != null ? quest.QuestName : string.Empty;
                NarrativeNotification.ShowQuestStarted(name);
                _gameplayHUD?.ShowNotification($"任务开始：{name}", 4f);
                NarrativePro.Music.NarrativeMusicDriver.OnQuestStarted();
            };
            _onQuestSucceeded = (component, quest) =>
            {
                string name = quest != null ? quest.QuestName : string.Empty;
                NarrativeNotification.ShowQuestCompleted(name);
                _gameplayHUD?.ShowNotification($"任务完成：{name}", 4f);
                NarrativePro.Music.NarrativeMusicDriver.OnQuestSucceeded();
            };
            _onQuestFailed = (component, quest) =>
            {
                string name = quest != null ? quest.QuestName : string.Empty;
                NarrativeNotification.ShowQuestFailed(name);
                _gameplayHUD?.ShowNotification($"任务失败：{name}", 4f);
                NarrativePro.Music.NarrativeMusicDriver.OnQuestFailed();
            };
            tales.OnQuestStarted += _onQuestStarted;
            tales.OnQuestSucceeded += _onQuestSucceeded;
            tales.OnQuestFailed += _onQuestFailed;

            // 对话文本 → 字幕通道（对应 UE5 FSubtitleManager::OnSetSubtitleText 的挂接）
            _onNPCLineStarted = (component, dialogue, node, line, speaker) =>
            {
                if (_subsystem != null) _subsystem.OnSetSubtitle(line != null ? line.Text : string.Empty);
            };
            _onPlayerLineStarted = (component, dialogue, node, line) =>
            {
                if (_subsystem != null) _subsystem.OnSetSubtitle(line != null ? line.Text : string.Empty);
            };
            _onDialogueFinished = (component, dialogue, reason) =>
            {
                if (_subsystem != null) _subsystem.OnSetSubtitle(string.Empty);
            };
            tales.OnNPCDialogueLineStarted += _onNPCLineStarted;
            tales.OnPlayerDialogueLineStarted += _onPlayerLineStarted;
            tales.OnDialogueFinished += _onDialogueFinished;

            // 面板视图（真实 Flax GUI 控件挂进 HUD 根）
            if (_commonHUD != null && _commonHUD.RootControl != null)
            {
                _uiBuilder = new NarrativeUIBuilder();
                _uiBuilder.BuildAll(tales, _commonHUD.RootControl);
                _uiBuilt = true;
            }

            NarrativeLog.Log($"[NarrativePro] 对话/任务面板已接入 TalesComponent（{(tales.Actor != null ? tales.Actor.Name : "?")}）");
        }

        /// <summary>退订当前 TalesComponent 并销毁面板视图。</summary>
        private static void UnbindTales()
        {
            var tales = _boundTales;
            if (tales != null)
            {
                if (_onQuestStarted != null) tales.OnQuestStarted -= _onQuestStarted;
                if (_onQuestSucceeded != null) tales.OnQuestSucceeded -= _onQuestSucceeded;
                if (_onQuestFailed != null) tales.OnQuestFailed -= _onQuestFailed;
                if (_onNPCLineStarted != null) tales.OnNPCDialogueLineStarted -= _onNPCLineStarted;
                if (_onPlayerLineStarted != null) tales.OnPlayerDialogueLineStarted -= _onPlayerLineStarted;
                if (_onDialogueFinished != null) tales.OnDialogueFinished -= _onDialogueFinished;

                if (_dialoguePanel != null) _dialoguePanel.Unbind();
                if (_questLogPanel != null) _questLogPanel.Unbind();
                if (_questTracker != null) _questTracker.Unbind();
            }

            _onQuestStarted = null;
            _onQuestSucceeded = null;
            _onQuestFailed = null;
            _onNPCLineStarted = null;
            _onPlayerLineStarted = null;
            _onDialogueFinished = null;

            if (_uiBuilder != null)
            {
                _uiBuilder.DisposeAll();
                _uiBuilder = null;
            }

            _uiBuilt = false;
            _boundTales = null;
        }

        // =====================================================================
        // 菜单 / 通知对外入口
        // =====================================================================

        /// <summary>
        /// 在指定层打开一个菜单（对应 UE5 UNarrativeGameplayHUD::OpenMenu）。
        /// 层未注册时返回 null —— 层由本引导在 <see cref="RegisterLayers"/> 中注册。
        /// </summary>
        public static NarrativeMenu OpenMenu(Type menuType, GameplayTag layerTag = default)
        {
            if (_gameplayHUD == null) return null;
            return _gameplayHUD.OpenMenu(menuType, IsValid(layerTag) ? layerTag : NarrativeGameplayTags.UI_Layer_Menu);
        }

        /// <summary>按类型名打开菜单（对应 UE5 的 TSubclassOf 字符串写法）。</summary>
        public static NarrativeMenu OpenMenu(string menuClassName, GameplayTag layerTag = default)
        {
            if (_gameplayHUD == null) return null;
            return _gameplayHUD.OpenMenu(menuClassName, IsValid(layerTag) ? layerTag : NarrativeGameplayTags.UI_Layer_Menu);
        }

        /// <summary>
        /// 在全屏视频层播放一段视频（对应 UE5 把 CommonVideoPlayer 摆进过场/UMG 并 OpenSource+Play）。
        /// <paramref name="durationSeconds"/> 大于 0 时到时间自动关闭；自然放完也会关闭。
        /// </summary>
        /// <returns>播放器控件；HUD 根不可用时返回 null。</returns>
        public static NarrativeCommonVideoPlayer PlayVideo(string videoPath, bool bLoop = false, float durationSeconds = 0f)
        {
            if (_commonHUD == null || !_commonHUD.IsInitialized)
            {
                _commonHUD?.Initialize(null);
            }

            var root = _commonHUD != null ? _commonHUD.RootControl : null;
            if (root == null)
            {
                NarrativeLog.LogWarning("[NarrativePro] PlayVideo：GUI 根控件不可用，视频层未创建。");
                return null;
            }

            if (_videoRoot == null)
            {
                _videoRoot = new ContainerControl
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                };
                root.AddChild(_videoRoot);
                root.SizeChanged += control => LayoutVideoRoot();
            }

            if (_videoPlayer == null)
            {
                _videoPlayer = new NarrativeCommonVideoPlayer
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = Float2.Zero,
                };
                _videoPlayer.OnPlaybackFinished += _ => StopVideo();
                _videoRoot.AddChild(_videoPlayer);
            }

            _videoRoot.Visible = true;
            _videoRoot.BringToFront();
            LayoutVideoRoot();

            _videoPlayer.IsLooping = bLoop;
            _videoPlayer.BPSetVideo(videoPath);
            _videoPlayer.BPPlayFromStart();
            _videoAutoCloseRemaining = durationSeconds > 0f ? durationSeconds : 0f;
            return _videoPlayer;
        }

        /// <summary>停止并隐藏视频层（对应 UE5 过场结束 CloseVideo + RemoveWidget）。</summary>
        public static void StopVideo()
        {
            _videoAutoCloseRemaining = 0f;

            if (_videoPlayer != null)
            {
                _videoPlayer.BPClose();
            }

            if (_videoRoot != null)
            {
                _videoRoot.Visible = false;
            }
        }

        /// <summary>当前运行期视频播放器（未创建时为 null）。</summary>
        public static NarrativeCommonVideoPlayer VideoPlayer => _videoPlayer;

        /// <summary>
        /// 切换默认菜单（<see cref="NarrativePauseMenu"/>）：栈顶已是同一菜单则关闭，否则压入 Menu 层。
        /// 这条入口让 <see cref="NarrativeGameplayHUD.OpenMenu"/> 与已注册的层在运行期真正可达
        /// （HUD 自身也按 Esc 轮询调用同一逻辑，两边共用一份实现）。
        /// </summary>
        public static NarrativeMenu ToggleDefaultMenu() => _gameplayHUD != null ? _gameplayHUD.ToggleDefaultMenu() : null;

        /// <summary>关闭默认菜单。</summary>
        public static void CloseDefaultMenu() => _gameplayHUD?.CloseDefaultMenu();

        /// <summary>
        /// 按武器切换准星（对应 <c>WeaponItem.CrosshairWidgetPath</c>：UE5 装备时换准星蓝图子类）。
        /// 传 null/空串表示回到默认准星。
        /// </summary>
        public static CrosshairWidget SetWeaponCrosshair(string crosshairClassName)
        {
            if (_gameplayHUD == null) return null;
            return string.IsNullOrEmpty(crosshairClassName)
                ? _gameplayHUD.EnsureDefaultCrosshair()
                : _gameplayHUD.SetCrosshair(crosshairClassName);
        }

        /// <summary>显示/隐藏准星（对应 UE5 过场时隐藏 HUD 上的准星）。</summary>
        public static void SetCrosshairVisible(bool bVisible) => _gameplayHUD?.SetCrosshairVisible(bVisible);

        /// <summary>推准星扩散（开火/移动时调用）。</summary>
        public static void SetCrosshairSpread(float spread) => _gameplayHUD?.SetCrosshairSpread(spread);

        /// <summary>关闭栈顶菜单（对应 UE5 的返回键行为）。</summary>
        public static bool CloseTopMenu() => _subsystem != null && _subsystem.CloseTopMenu();

        /// <summary>显示一条小提示（对应 UE5 ShowNotification）。</summary>
        public static void ShowNotification(string text, float duration = 5f) => _gameplayHUD?.ShowNotification(text, duration);

        /// <summary>显示一条重大通知（对应 UE5 ShowMajorNotification）。</summary>
        public static void ShowMajorNotification(string text, string subtext, float duration = 5f) => _gameplayHUD?.ShowMajorNotification(text, subtext, duration);

        /// <summary>设置字幕文本（对应 UE5 FSubtitleManager::SetSubtitleText）。</summary>
        public static void SetSubtitle(string text) => _subsystem?.OnSetSubtitle(text);

        /// <summary>显示/隐藏任务日志面板。</summary>
        public static void ToggleQuestLog(bool bShow)
        {
            if (_uiBuilder == null) return;
            if (bShow) _uiBuilder.ShowQuestLog();
            else _uiBuilder.HideQuestLog();
        }

        // =====================================================================
        // 角色创建器可达入口
        // =====================================================================

        /// <summary>
        /// 打开角色创建器（本引导对外提供的公开入口；<see cref="NarrativeProPlugin.OpenCreator"/> 转发到这里）。
        /// <para>
        /// UE5 里 <c>ACharacterCreatorPlayerController</c> 由创建器关卡的 GameMode/世界设置自动生成并 Possess，
        /// Flax 端口没人创建它，整条 LoadConfiguration → CreateDefaultAttributeSet → CreatorOptionWidget →
        /// InitializeFromCharacterAndAttributes 链因此不可达。这里按需把会话控制器挂到运行期宿主 Actor，
        /// 用工厂解析表单打开会话，并把每个 <see cref="CreatorOptionWidget"/> 的真实子控件
        /// （标量滑条 <c>NarrativeAnalogSlider</c> / 选项按钮）铺进本引导创建的容器，
        /// 使面板可见、可点、可把属性集应用到预览角色。
        /// </para>
        /// </summary>
        /// <param name="configJsonPath">创建器配置 JSON 路径；为空时在 <see cref="DefaultCreatorDirectory"/> 下搜索含该表单的配置。</param>
        /// <param name="formTag">表单标签；无效时取 <c>CharacterCreator_Form_Male</c>。</param>
        /// <returns>是否成功打开。</returns>
        public static bool OpenCreator(string configJsonPath = null, GameplayTag formTag = default)
        {
            if (!_installed) Install();

            // 创建器可能在玩法场景尚未装配时被调用（主菜单/创建关卡），此处补一次装配。
            // 取最后加载的场景：0 号是常驻 RootScene（非玩法场景，会被场景门禁拒绝）。
            if (_hostActor == null)
            {
                InitializeForScene(Level.ScenesCount > 0 ? Level.GetScene(Level.ScenesCount - 1) : null);
            }
            if (_hostActor == null) return false;

            var tag = IsValid(formTag) ? formTag : NarrativeGameplayTags.CharacterCreator_Form_Male;
            var form = ResolveCreatorForm(configJsonPath, tag);
            if (form == null)
            {
                NarrativeLog.LogWarning($"[NarrativePro] 创建器表单解析失败（{tag}，配置：{(string.IsNullOrEmpty(configJsonPath) ? DefaultCreatorDirectory : configJsonPath)}）");
                return false;
            }

            _creatorController ??= _hostActor.AddScript<CharacterCreatorPlayerController>();
            if (_creatorController == null) return false;

            if (!_creatorController.OpenCreator(form)) return false;

            // 应用目标：本地玩家角色的视觉组件（Apply() → InitializeFromCharacterAndAttributes）
            var player = _localController != null ? _localController.GetControlledCharacter() : Level.FindScript<NarrativePlayerCharacter>();
            if (player != null)
            {
                _creatorController.SetApplyTarget(player.GetCharacterVisual(), player.Actor);
            }

            BuildCreatorUI();
            _bCreatorOpen = true;
            return true;
        }

        /// <summary>关闭角色创建器并回收其 UI 容器。</summary>
        public static void CloseCreator()
        {
            _bCreatorOpen = false;
            _creatorController?.CloseCreator();
            DestroyCreatorUI();
        }

        /// <summary>创建器会话是否打开。</summary>
        public static bool IsCreatorOpen => _bCreatorOpen;

        private static CharacterCreatorForm ResolveCreatorForm(string configJsonPath, GameplayTag formTag)
        {
            if (!string.IsNullOrEmpty(configJsonPath))
            {
                return FindFormInFile(configJsonPath, formTag);
            }

            try
            {
                // 两个候选目录：项目相对路径（编辑器/PIE 的工作目录）与数据根解析后的绝对路径。
                // 其它模块（Music/Crowd/Items…）统一走 NarrativeDataPaths.Resolve，
                // 创建器目录此前只看相对路径，工作目录不是工程根时会直接判定“目录不存在”。
                foreach (var directory in GetCreatorSearchDirectories())
                {
                    try
                    {
                        if (!System.IO.Directory.Exists(directory)) continue;
                        foreach (var file in System.IO.Directory.GetFiles(directory, "*.json"))
                        {
                            var form = FindFormInFile(file, formTag);
                            if (form != null) return form;
                        }
                    }
                    catch (Exception ex)
                    {
                        NarrativeLog.LogWarning($"[NarrativePro] 创建器配置目录扫描失败 {directory}：{ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                NarrativeLog.LogWarning("[NarrativePro] 创建器配置目录扫描失败：" + ex.Message);
            }

            return null;
        }

        /// <summary>创建器配置的搜索目录（相对 + 数据根解析），去重后按顺序尝试。</summary>
        private static System.Collections.Generic.List<string> GetCreatorSearchDirectories()
        {
            var result = new System.Collections.Generic.List<string>();

            void Add(string path)
            {
                if (string.IsNullOrEmpty(path)) return;
                for (int i = 0; i < result.Count; i++)
                {
                    if (string.Equals(result[i], path, System.StringComparison.Ordinal)) return;
                }
                result.Add(path);
            }

            Add(DefaultCreatorDirectory);

            try
            {
                Add(NarrativePro.Data.NarrativeDataPaths.Resolve(DefaultCreatorDirectory));
            }
            catch
            {
                // 解析失败只保留相对路径
            }

            return result;
        }

        private static CharacterCreatorForm FindFormInFile(string path, GameplayTag formTag)
        {
            try
            {
                var config = CharacterCreatorFactory.LoadConfiguration(path);
                return CharacterCreatorFactory.FindForm(config, formTag);
            }
            catch (Exception ex)
            {
                NarrativeLog.LogWarning($"[NarrativePro] 创建器配置加载失败 {path}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 把创建器会话的选项控件铺进 HUD 根上的真实容器（TopLeft 锚 + 显式重排）。
        /// 标量用 <see cref="CreatorOptionWidget.EnableScalarSlider"/> 产出真正的 Flax Slider；
        /// 向量/网格/毛发用按钮行驱动 <see cref="CreatorOptionWidget.SelectVectorOption"/> /
        /// <see cref="CreatorOptionWidget.SelectItemOption"/>（后两者的绑定对象是纯 C# 类，非 GUI 控件）。
        /// </summary>
        private static void BuildCreatorUI()
        {
            if (_creatorController == null || _commonHUD == null || _commonHUD.RootControl == null) return;

            if (_creatorRoot == null)
            {
                // Flax 的 Control 没有 Name，层标识由本引导自身持有
                _creatorRoot = new ContainerControl
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                };
                _commonHUD.RootControl.AddChild(_creatorRoot);
                _commonHUD.RootControl.SizeChanged += _ => LayoutCreatorUI();
            }

            foreach (var row in _creatorRows)
            {
                if (row != null) row.Dispose();
            }
            _creatorRows.Clear();

            float y = 8f;
            foreach (var widget in _creatorController.OptionWidgets)
            {
                if (widget == null) continue;

                var row = new ContainerControl
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(16f, y),
                    Size = new Float2(420f, 46f),
                };                _creatorRoot.AddChild(row);
                _creatorRows.Add(row);

                string optionName = widget.CreatorOption != null ? widget.CreatorOption.OptionDisplayName : string.Empty;
                var titleLabel = new Label
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    Location = new Float2(0f, 0f),
                    Size = new Float2(420f, 18f),
                    Text = optionName,
                    TextColor = Color.White,
                };
                row.AddChild(titleLabel);

                switch (widget.CreatorOption)
                {
                    case CharacterCreatorOption_Scalar scalar:
                    {
                        // 真实滑条：CreatorOptionWidget 的子控件，父级就是本行容器
                        var slider = widget.EnableScalarSlider(row);
                        if (slider != null)
                        {
                            slider.AnchorPreset = AnchorPresets.TopLeft;
                            slider.Location = new Float2(0f, 20f);
                            slider.Size = new Float2(300f, 22f);
                        }

                        var valueLabel = new Label
                        {
                            AnchorPreset = AnchorPresets.TopLeft,
                            Location = new Float2(310f, 20f),
                            Size = new Float2(100f, 20f),
                            Text = FormatScalar(widget),
                            TextColor = Color.LightGray,
                        };
                        row.AddChild(valueLabel);
                        widget.ScalarValueChanged += value => valueLabel.Text = value.ToString("0.###");

                        // CreatorOptionWidget 只构造 NarrativeSpinBox、不给父级（它以前是纯数据类），
                        // 现在它是真实 Flax 控件，必须挂进行容器才会进绘制树。
                        var spinBox = widget.ScalarSpinBox;
                        if (spinBox != null)
                        {
                            row.Size = new Float2(420f, 72f);
                            spinBox.Parent = row;
                            spinBox.AnchorPreset = AnchorPresets.TopLeft;
                            spinBox.Location = new Float2(0f, 46f);
                            spinBox.Size = new Float2(300f, 24f);
                            spinBox.RebuildWidget();
                            spinBox.OnValueChange += value => valueLabel.Text = value.ToString("0.###");
                            spinBox.OnValueCommitted += value => widget.PushScalarValue(value);
                        }
                        break;
                    }

                    default:
                    {
                        // 向量/网格/毛发：选项列表驱动为按钮行，点击直接写属性集
                        var options = widget.OptionComboBox != null ? widget.OptionComboBox.DefaultOptions : null;
                        float x = 0f;
                        for (int i = 0; i < (options?.Count ?? 0); i++)
                        {
                            string optionText = options[i];
                            int index = i;

                            var button = new Button
                            {
                                AnchorPreset = AnchorPresets.TopLeft,
                                Location = new Float2(x, 20f),
                                Size = new Float2(84f, 22f),
                                Text = Shorten(optionText),
                            };
                            button.Clicked += () =>
                            {
                                if (widget.CreatorOption is CharacterCreatorOption_Vector)
                                    widget.SelectVectorOption(index);
                                else
                                    widget.SelectItemOption(optionText);
                            };
                            row.AddChild(button);
                            x += 88f;
                        }
                        break;
                    }
                }

                y += row.Size.Y + 6f;
            }

            _creatorContentBottomY = y;

            // 会话按钮：随机 / 重置 / 应用 / 关闭
            float buttonX = 16f;
            AddCreatorSessionButton("随机", ref buttonX, () => _creatorController?.Randomize());
            AddCreatorSessionButton("重置", ref buttonX, () => _creatorController?.Reset());
            AddCreatorSessionButton("应用", ref buttonX, () => _creatorController?.Apply());
            AddCreatorSessionButton("关闭", ref buttonX, CloseCreator);

            LayoutCreatorUI();
            _creatorRoot.Visible = true;
        }

        private static void AddCreatorSessionButton(string text, ref float x, Action onClick)
        {
            if (_creatorRoot == null) return;

            // 行高按实际内容累加（Scalar 行含 spin box 时更高），空则回落到保守值
            float y = _creatorContentBottomY > 0f ? _creatorContentBottomY + 8f : _creatorRows.Count * 52f + 24f;
            var button = new Button
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = new Float2(x, y),
                Size = new Float2(90f, 26f),
                Text = text,
            };
            button.Clicked += () => onClick?.Invoke();
            _creatorRoot.AddChild(button);
            _creatorRows.Add(button);
            x += 96f;
        }

        private static void LayoutVideoRoot()
        {
            if (_videoRoot == null || _commonHUD == null || _commonHUD.RootControl == null) return;

            Float2 rootSize = _commonHUD.RootControl.Size;
            _videoRoot.AnchorMin = Float2.Zero;
            _videoRoot.AnchorMax = Float2.Zero;
            _videoRoot.Location = Float2.Zero;
            _videoRoot.Size = rootSize;

            if (_videoPlayer == null || rootSize.X <= 0f || rootSize.Y <= 0f) return;

            // 16:9 适配后居中（TopLeft 锚 + 显式 Location，避免非 TopLeft 锚在父尺寸为 0 时算错位置）
            float width = rootSize.X;
            float height = width * 9f / 16f;
            if (height > rootSize.Y)
            {
                height = rootSize.Y;
                width = height * 16f / 9f;
            }

            _videoPlayer.Size = new Float2(width, height);
            _videoPlayer.Location = new Float2((rootSize.X - width) * 0.5f, (rootSize.Y - height) * 0.5f);
        }

        private static void LayoutCreatorUI()
        {
            if (_creatorRoot == null) return;

            Float2 rootSize = _commonHUD != null && _commonHUD.RootControl != null
                ? _commonHUD.RootControl.Size
                : new Float2(1280f, 720f);

            float width = Math.Min(460f, rootSize.X);
            float height = Math.Min(rootSize.Y - 48f, (_creatorRows.Count * 52f) + 70f);

            _creatorRoot.AnchorMin = Float2.Zero;
            _creatorRoot.AnchorMax = Float2.Zero;
            _creatorRoot.Location = new Float2(rootSize.X - width - 24f, 24f);
            _creatorRoot.Size = new Float2(width, Math.Max(120f, height));
        }

        private static void DestroyCreatorUI()
        {
            for (int i = _creatorRows.Count - 1; i >= 0; i--)
            {
                var control = _creatorRows[i];
                if (control == null) continue;
                if (control is ContainerControl container) container.DisposeChildren();
                control.Dispose();
            }
            _creatorRows.Clear();

            if (_creatorRoot != null)
            {
                var parent = _creatorRoot.Parent;
                if (parent != null) parent.RemoveChild(_creatorRoot);
                _creatorRoot.Dispose();
                _creatorRoot = null;
            }
        }

        private static string FormatScalar(CreatorOptionWidget widget)
        {
            if (widget?.ScalarSpinBox != null)
            {
                return widget.ScalarSpinBox.Value.ToString("0.###");
            }
            return string.Empty;
        }

        private static string Shorten(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            return text.Length <= 10 ? text : text.Substring(0, 10);
        }

        private static bool IsValid(GameplayTag tag)
        {
            // GameplayTag 是结构体：默认值即无效
            return tag.IsValid();
        }

        // =====================================================================
        // 回收
        // =====================================================================

        /// <summary>回收运行期对象（切场景/卸载插件时调用）。</summary>
        public static void Teardown()
        {
            CloseCreatorQuietly();
            UnbindTales();

            for (int i = _hookedStacks.Count - 1; i >= 0; i--)
            {
                var stack = _hookedStacks[i];
                if (stack != null) stack.OnStackChanged -= HandleStackChanged;
            }
            _hookedStacks.Clear();

            _bCreatorOpen = false;
            _creatorController = null;
            _localController = null;
            _dialoguePanel = null;
            _questLogPanel = null;
            _questTracker = null;
            _notification = null;

            // 宿主装配状态：脚本随宿主 Actor（场景 Actor）一起销毁，这里只清缓存与订阅
            if (_hostIdentity != null) _hostIdentity.OnHostRecordLoaded -= HandleHostRecordLoaded;
            _hostIdentity = null;
            _hostAsc = null;
            _hostInventory = null;
            _hostEquipment = null;
            _hostInteraction = null;
            _hostTales = null;
            _hostPlayerState = null;
            _playerHostActor = null;
            _attachedHostId = Guid.Empty;
            _bHostComponentsAttached = false;
            _bHostIsNarrativeCharacter = false;
            _hostResolveAttempts = 0;
            _hostResolveTimer = 0f;
            _gameState = null;
            _bNetworkAdapterWired = false;
            _networkRetryTimer = 0f;
            _networkRetryAttempts = 0;

            NarrativeInputRouter.Reset();

            if (_hostActor != null)
            {
                FlaxEngine.Object.Destroy(_hostActor);
            }

            GameplayScene = null;
            _hostActor = null;
            _pump = null;
            _characterSubsystem = null;
            _subsystem = null;
            _commonHUD = null;
            _gameplayHUD = null;
            _layersRegistered = false;
            _rosterDone = false;
            _initializedSceneId = Guid.Empty;
        }

        private static void CloseCreatorQuietly()
        {
            if (_creatorController != null && _creatorController.bCreatorOpen)
            {
                _creatorController.CloseCreator();
            }
            DestroyCreatorUI();
        }
    }

    /// <summary>
    /// 引导驱动脚本。挂在运行期宿主 Actor 上，把「GUI 必须主线程创建」的装配步骤
    /// 从插件 Initialize 推到首帧之后的 Update 里（对应 UE5 引擎在 World 初始化后自动装配）。
    /// </summary>
    public class NarrativeRuntimePump : Script
    {
        public override void OnUpdate()
        {
            base.OnUpdate();
            NarrativeRuntimeBootstrap.Tick();
        }
    }
}
