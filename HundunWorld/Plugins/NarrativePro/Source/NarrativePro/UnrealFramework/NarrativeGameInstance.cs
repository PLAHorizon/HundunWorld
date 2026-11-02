using System;
using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.Settings;

namespace NarrativePro.UnrealFramework
{
    /// <summary>
    /// Narrative 游戏实例。对应 UE5 UNarrativeGameInstance。
    /// UE5 中继承 UGameInstance；Flax 无 GameInstance 基类，改为单例 [Serializable] class。
    /// 负责全局游戏生命周期、子系统持有与跨地图状态保持。
    /// 简化点：
    /// - 移除 UE5 复制/RPC，改为本地逻辑 + 事件回调
    /// - 移除 UE5 GetEngine()/GetWorld() 等引擎接口依赖（Flax 引擎入口不同）
    /// </summary>
    [Serializable]
    public class NarrativeGameInstance
    {
        // ===== 单例 =====

        private static NarrativeGameInstance _instance;

        /// <summary>全局单例实例。</summary>
        public static NarrativeGameInstance Get()
        {
            if (_instance == null)
            {
                _instance = new NarrativeGameInstance();
            }
            return _instance;
        }

        /// <summary>设置单例实例（用于存档恢复或测试注入）。</summary>
        public static void SetInstance(NarrativeGameInstance instance)
        {
            _instance = instance;
        }

        // ===== 生命周期 =====

        /// <summary>是否已完成 Init（防止 GameMode 反复启停导致重复初始化）。</summary>
        private bool bInitialized = false;

        /// <summary>初始化游戏实例。对应 UE5 Init。</summary>
        public virtual void Init()
        {
            if (bInitialized) return;
            bInitialized = true;
            NarrativeLog.Log("[NarrativeGameInstance] Init");
            // Flax-已实现：核心配置在此预热——首次访问触发各 DeveloperSettings 单例的
            // LoadDefault（从 Content/NarrativePro/Settings/*.json 读取），等价 UE5 启动时加载 ini 配置。
            _ = ArsenalSettings.Instance;
            _ = NarrativeCombatDeveloperSettings.Instance;
            _ = NarrativeTimeOfDaySettings.Instance;
            _ = NarrativeInputSettings.Instance;
        }

        /// <summary>关闭游戏实例。对应 UE5 Shutdown。</summary>
        public virtual void Shutdown()
        {
            if (!bInitialized) return;
            bInitialized = false;
            NarrativeLog.Log("[NarrativeGameInstance] Shutdown");
            // 子系统（CharacterSubsystem/SaveSubsystem 等）在 Flax 中随场景生命周期自动释放，
            // 这里仅复位实例生命周期标记。
        }

        /// <summary>开始会话。对应 UE5 OnStart。</summary>
        public virtual void OnStart()
        {
            // Flax-已实现：由 NarrativeGameMode.InitGame 驱动（本地单人等价 UE5 的 GameInstance::Start），
            // 到达此处时配置已预热完毕；跨地图持久状态直接挂在本单例上。
            NarrativeLog.Log("[NarrativeGameInstance] OnStart");
        }

        /// <summary>预加载地图。对应 UE5 PreLoadMap。</summary>
        public virtual void PreLoadMap(string mapName)
        {
            NarrativeLog.Log($"[NarrativeGameInstance] PreLoadMap: {mapName}");
        }

        /// <summary>加载地图完成。对应 UE5 PostLoadMap。</summary>
        public virtual void PostLoadMap(string mapName)
        {
            NarrativeLog.Log($"[NarrativeGameInstance] PostLoadMap: {mapName}");
        }
    }
}
