using System;
using FlaxEngine;
using NarrativePro.Core;

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

        /// <summary>初始化游戏实例。对应 UE5 Init。</summary>
        public virtual void Init()
        {
            NarrativeLog.Log("[NarrativeGameInstance] Init");
            // TODO [需接入子系统初始化系统]: 初始化子系统、加载核心配置
        }

        /// <summary>关闭游戏实例。对应 UE5 Shutdown。</summary>
        public virtual void Shutdown()
        {
            NarrativeLog.Log("[NarrativeGameInstance] Shutdown");
            // TODO [需接入子系统释放系统]: 释放子系统资源
        }

        /// <summary>开始会话。对应 UE5 OnStart。</summary>
        public virtual void OnStart()
        {
            // TODO [需接入玩家进入系统]: 玩家进入游戏时的初始化
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
