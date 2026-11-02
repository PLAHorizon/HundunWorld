// 模块关系说明：
// 本文件属于 NarrativePro.SaveSystem（UE5 NarrativeSaveSystem 移植）。
//
// UE5 侧的生命周期钩子：
//   - NarrativeGameMode::InitGame(:23-34) 在 Actor BeginPlay 之前调用 SaveSub->InitializeSaveSystem(*World)；
//   - NarrativePlayerCharacter::OnCharacterVisualInitialized(:295-330) 依据 IsNewGame() 决定 InitNewCharacter 或 LoadPlayerData；
//   - UNarrativeSaveSubsystem::PostInitialize(:645-669) 绑定 Actor 生成/销毁与关卡增删事件。
// Flax 没有 GameMode/WorldSubsystem，本文件把同样的时序搬到插件生命周期上：
//   NarrativeProPlugin.Initialize → NarrativeSaveBootstrap.Install →
//   订阅 Level.SceneLoaded/SceneUnloading → 为玩法场景生成宿主 Actor 与 NarrativeSaveSubsystem 脚本 →
//   InitializeSaveSystem()（读档或建立新档）→ 玩家就绪后 DeferredLoadPlayerData() → 场景卸载前自动落盘。

using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.SaveSystem.Subsystems;

namespace NarrativePro.SaveSystem
{
    /// <summary>
    /// 存档系统生命周期接线器。由 NarrativeProPlugin 的 Initialize/Deinitialize 调用。
    /// </summary>
    public static class NarrativeSaveBootstrap
    {
        private const string HostActorName = "NarrativeSaveSubsystemHost";

        private static bool _installed;
        private static Actor _hostActor;
        private static NarrativeSaveSubsystem _subsystem;
        private static Guid _initializedSceneId = Guid.Empty;
        private static string _pendingOptionString = string.Empty;

        /// <summary>是否已安装生命周期钩子。</summary>
        public static bool IsInstalled => _installed;

        /// <summary>当前由接线器托管的子系统（可能为空）。</summary>
        public static NarrativeSaveSubsystem Subsystem => _subsystem;

        /// <summary>安装钩子并为当前场景初始化（幂等）。</summary>
        public static void Install()
        {
            if (_installed) return;
            _installed = true;

            NarrativeSaveSystemModule.StartupModule();

            Level.SceneLoaded += OnSceneLoaded;
            Level.SceneUnloading += OnSceneUnloading;

            // 进程退出兜底：插件不一定先于退出被卸载（独立构建直接 Exit 时），
            // 这里把激活叙事组件的任务/对话进度先落盘（Engine.RequestingExit 为无参事件，
            // 用法与 HundunWorldGame.cs:202 一致）。
            Engine.RequestingExit += OnEngineRequestingExit;

            NarrativeLog.Log("[SaveSystem] 存档系统生命周期钩子已安装");

            // 插件 Initialize 时场景通常已经加载完毕，直接为当前场景补一次初始化
            Scripting.InvokeOnUpdate(() =>
            {
                InitializeForScene(Level.GetScene(0), _pendingOptionString);
            });
        }

        /// <summary>卸载钩子（会先把当前世界状态落盘）。</summary>
        public static void Uninstall()
        {
            if (!_installed) return;

            Level.SceneLoaded -= OnSceneLoaded;
            Level.SceneUnloading -= OnSceneUnloading;
            Engine.RequestingExit -= OnEngineRequestingExit;

            ShutdownCurrentScene();

            NarrativeSaveSystemModule.ShutdownModule();
            _installed = false;
            NarrativeLog.Log("[SaveSystem] 存档系统生命周期钩子已卸载");
        }

        /// <summary>
        /// 设置下一次初始化使用的选项串（对应 UE5 GameMode->OptionsString，
        /// 形如 "LevelName?SaveGameName=Slot&LevelTransition=1"）。
        /// </summary>
        public static void SetOptionString(string optionString)
        {
            _pendingOptionString = optionString ?? string.Empty;
            if (_subsystem != null)
            {
                _subsystem.SetOptionString(_pendingOptionString);
            }
        }

        private static void OnSceneLoaded(Scene scene, Guid sceneId)
        {
            if (!_installed || scene == null) return;

            if (_subsystem != null)
            {
                // 已有子系统：把新的玩法场景登记为当前关卡，并把这些随场景一起加载的 Actor 记为 net startup
                Scripting.InvokeOnUpdate(() =>
                {
                    if (_subsystem == null) return;
                    var settings = SaveSystemDeveloperSettings.Instance;
                    if (!IsGameplayScene(scene, settings)) return;

                    _subsystem.SetSaveContextScene(scene);
                    _subsystem.RefreshNetStartupSnapshot();
                });
                return;
            }

            Scripting.InvokeOnUpdate(() => InitializeForScene(scene, _pendingOptionString));
        }

        /// <summary>进程退出兜底：把激活叙事组件的任务/对话进度落盘（与 Uninstall→ShutdownCurrentScene 的落盘幂等重复无害）。</summary>
        private static void OnEngineRequestingExit()
        {
            try
            {
                var tales = NarrativeProPlugin.Instance?.ActiveTalesComponent;
                if (tales != null)
                {
                    tales.SaveNarrativeStateToJson();
                }
            }
            catch (Exception ex)
            {
                NarrativeLog.LogError($"[SaveSystem] 退出时叙事业务存档落盘失败: {ex.Message}");
            }
        }

        private static void OnSceneUnloading(Scene scene, Guid sceneId)
        {
            if (!_installed || scene == null) return;
            if (_subsystem == null) return;

            var settings = SaveSystemDeveloperSettings.Instance;
            if (settings != null && !settings.bAutoSaveOnSceneUnloading) return;

            // 对应 UE5：切关卡前静默保存，开发者无需手动处理
            _subsystem.SaveOnSceneExit();
        }

        private static void InitializeForScene(Scene scene, string optionString)
        {
            if (!_installed) return;
            if (!Engine.IsPlayMode) return;                 // 编辑器编辑态不写存档
            if (scene == null) return;
            if (_initializedSceneId != Guid.Empty && _initializedSceneId == scene.ID) return;

            var settings = SaveSystemDeveloperSettings.Instance;
            if (settings != null && !settings.bAutoInitializeOnSceneLoaded) return;
            if (!IsGameplayScene(scene, settings)) return;

            var subsystem = CreateSubsystem();
            if (subsystem == null) return;

            _initializedSceneId = scene.ID;
            subsystem.SetOptionString(optionString ?? string.Empty);
            subsystem.SetSaveContextScene(scene);

            // 对应 UE5 ANarrativeGameMode::InitGame → InitializeSaveSystem
            subsystem.InitializeSaveSystem();
            NarrativeLog.Log($"[SaveSystem] 场景 {scene.Name} 存档系统已初始化");
        }

        private static void ShutdownCurrentScene()
        {
            if (_subsystem != null)
            {
                var settings = SaveSystemDeveloperSettings.Instance;
                if (settings == null || settings.bAutoSaveOnSceneUnloading)
                {
                    _subsystem.SaveOnSceneExit();
                }
            }

            DestroySubsystem();
            _initializedSceneId = Guid.Empty;
        }

        /// <summary>场景是否应纳入存档系统。默认排除登录/创建角色等 UI 场景，可用设置覆盖。</summary>
        private static bool IsGameplayScene(Scene scene, SaveSystemDeveloperSettings settings)
        {
            string name = scene.Name ?? string.Empty;
            if (string.IsNullOrEmpty(name)) return true;

            var excluded = settings != null ? settings.ExcludedSceneNames : null;
            if (excluded != null)
            {
                foreach (var pattern in excluded)
                {
                    if (string.IsNullOrEmpty(pattern)) continue;
                    if (name.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0) return false;
                }
            }
            return true;
        }

        private static NarrativeSaveSubsystem CreateSubsystem()
        {
            if (_subsystem != null) return _subsystem;

            try
            {
                // Flax 的 Actor 是密封/抽象体系，不能直接 new Actor()；
                // EmptyActor 是引擎提供的“用于承载脚本层级”的空 Actor（见 FlaxEngine.EmptyActor 文档）。
                var host = new EmptyActor
                {
                    Name = HostActorName,
                };

                // 有意常驻 RootScene（0 号场景）：存档子系统宿主是跨场景全局共享组件。
                // 显式传场景，不依赖"无父级 SpawnActor 默认落 Scenes[0]"的隐式行为。
                // Flax 约定：Level.SpawnActor 返回 true 表示失败
                if (Level.SpawnActor(host, Level.GetScene(0)))
                {
                    NarrativeLog.LogError("[SaveSystem] 存档子系统宿主 Actor 生成失败");
                    return null;
                }

                var subsystem = host.AddScript<NarrativeSaveSubsystem>();
                if (subsystem == null)
                {
                    NarrativeLog.LogError("[SaveSystem] NarrativeSaveSubsystem 脚本创建失败");
                    Actor.Destroy(host);
                    return null;
                }

                _hostActor = host;
                _subsystem = subsystem;
                subsystem.SetHostActor(host);
                return subsystem;
            }
            catch (Exception ex)
            {
                NarrativeLog.LogError($"[SaveSystem] 存档子系统创建异常: {ex.Message}");
                return null;
            }
        }

        private static void DestroySubsystem()
        {
            if (_subsystem != null)
            {
                _subsystem.OnWorldTeardown();
            }

            if (_hostActor != null)
            {
                Actor.Destroy(_hostActor);
            }

            _hostActor = null;
            _subsystem = null;
        }

        /// <summary>
        /// 手动触发一次完整存档（供 UI/主菜单“保存游戏”按钮调用）。
        /// </summary>
        public static bool SaveCurrentGame(string saveName = null, int slot = 0)
        {
            var subsystem = _subsystem ?? NarrativeSaveSubsystem.Instance;
            if (subsystem == null) return false;

            var settings = SaveSystemDeveloperSettings.Instance;
            string name = saveName ?? settings?.DefaultSaveName ?? "NarrativeSave";
            int useSlot = saveName == null && slot == 0 ? (settings?.DefaultSaveSlot ?? 0) : slot;
            return subsystem.Save(name, useSlot);
        }

        /// <summary>手动触发一次读档（供“读取游戏”按钮调用）。</summary>
        public static bool LoadCurrentGame(string saveName = null, int slot = 0)
        {
            var subsystem = _subsystem ?? NarrativeSaveSubsystem.Instance;
            if (subsystem == null) return false;

            var settings = SaveSystemDeveloperSettings.Instance;
            string name = saveName ?? settings?.DefaultSaveName ?? "NarrativeSave";
            int useSlot = saveName == null && slot == 0 ? (settings?.DefaultSaveSlot ?? 0) : slot;
            return subsystem.Load(name, useSlot);
        }
    }
}
