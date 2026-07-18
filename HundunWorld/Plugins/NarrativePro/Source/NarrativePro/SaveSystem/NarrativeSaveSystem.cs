// 模块关系说明：
// 本文件属于新模块 NarrativePro.SaveSystem（UE5 NarrativeSaveSystem 移植）。
// 此文件对应 UE5 NarrativeSaveSystem.h 的模块入口（FNarrativeSaveSystemModule）。
// Flax 无 Module 概念，改为静态模块类，提供模块级初始化/卸载入口，
// 实际初始化由各子系统/单例自行处理。

using NarrativePro.Core;

namespace NarrativePro.SaveSystem
{
    /// <summary>
    /// 存档系统模块入口。对应 UE5 FNarrativeSaveSystemModule。
    /// Flax 中无 IModuleInterface 等价物，使用静态类提供模块级生命周期入口。
    /// </summary>
    public static class NarrativeSaveSystemModule
    {
        private static bool _isStarted;

        /// <summary>模块是否已启动。</summary>
        public static bool IsStarted => _isStarted;

        /// <summary>启动模块。对应 UE5 StartupModule。</summary>
        public static void StartupModule()
        {
            if (_isStarted) return;
            _isStarted = true;
            NarrativeLog.Log("[SaveSystem] 模块启动");
        }

        /// <summary>关闭模块。对应 UE5 ShutdownModule。</summary>
        public static void ShutdownModule()
        {
            if (!_isStarted) return;
            _isStarted = false;
            NarrativeLog.Log("[SaveSystem] 模块关闭");
        }
    }
}
