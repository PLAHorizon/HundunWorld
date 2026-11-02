using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.CommonUI
{
    /// <summary>
    /// Narrative CommonUI 模块入口。对应 UE5 FNarrativeCommonUIModule（实现 IModuleInterface）。
    ///
    /// 移植简化点：
    /// 1. UE5 中模块通过 IMPLEMENT_MODULE 宏注册到模块管理器，提供 StartupModule/ShutdownModule 钩子。
    /// 2. Flax 无模块管理器等价物。这里以静态类占位，提供等价的 Startup/Shutdown 静态方法，
    ///    由 NarrativeProPlugin 或游戏启动代码显式调用。
    /// 3. 当前模块启动/关闭无具体逻辑（与 UE5 源文件一致）。
    /// </summary>
    public static class NarrativeCommonUI
    {
        private static bool _isStarted;

        /// <summary>模块是否已启动。</summary>
        public static bool IsStarted => _isStarted;

        /// <summary>
        /// 模块启动。对应 UE5 FNarrativeCommonUIModule::StartupModule。
        /// 此代码在 UE5 中模块加载到内存后执行。
        /// </summary>
        public static void StartupModule()
        {
            if (_isStarted)
            {
                NarrativeLog.LogWarning("NarrativeCommonUI 模块已启动，重复调用 StartupModule。");
                return;
            }

            // Flax 无模块管理器：这里的启动工作就是 UE5 StartupModule 实际做的那件事 ——
            // 把 CommonUI 控件依赖的全局主题源准备好（各控件在 RebuildWidget/ApplyTheme 时向它取色）。
            var settings = NarrativeUIDeveloperSettings.GetMutableDefault();
            if (settings == null)
            {
                NarrativeLog.LogWarning("NarrativeCommonUI：UI 主题设置不可用，控件将使用各自的默认色。");
            }

            _isStarted = true;
            NarrativeLog.Log("NarrativeCommonUI 模块已启动。");
        }

        /// <summary>
        /// 模块关闭。对应 UE5 FNarrativeCommonUIModule::ShutdownModule。
        /// 此函数在卸载模块前调用（支持动态重载的模块）。
        /// </summary>
        public static void ShutdownModule()
        {
            if (!_isStarted) return;

            // 关闭时不做全局状态清理：主题设置是进程级单例（等价 UE5 的 config 对象），
            // 控件实例的回收由各自的宿主（NarrativeCommonHUD / 运行期引导）负责。
            _isStarted = false;
            NarrativeLog.Log("NarrativeCommonUI 模块已关闭。");
        }
    }
}
