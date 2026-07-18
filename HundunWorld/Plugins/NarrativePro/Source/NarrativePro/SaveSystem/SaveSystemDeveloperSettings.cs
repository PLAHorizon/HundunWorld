// 模块关系说明：
// 本文件属于新模块 NarrativePro.SaveSystem（UE5 NarrativeSaveSystem 移植）。
// 与现有 NarrativePro.Save 模块的关系详见 NarrativeStableActor.cs 顶部说明。

using System;
using NarrativePro.Core;

namespace NarrativePro.SaveSystem
{
    /// <summary>
    /// 存档系统开发者设置。对应 UE5 USaveSystemDeveloperSettings（继承 UDeveloperSettings）。
    /// UE5 中通过 config=Engine 持久化；Flax 中以 [Serializable] plain class + 单例 Instance 实现。
    /// </summary>
    [Serializable]
    public class SaveSystemDeveloperSettings
    {
        /// <summary>共享存档目录（相对于项目 Saved 目录）。对应 UE5 SharedSavesDirectory。</summary>
        public string SharedSavesDirectory = "SharedSaves";

        /// <summary>
        /// 存档对象类型路径。对应 UE5 SaveGameClass（FSoftClassPath）。
        /// 默认指向 NarrativePro.SaveSystem.NarrativeSave。
        /// </summary>
        public string SaveGameClassPath = "NarrativePro.SaveSystem.NarrativeSave, NarrativePro";

        /// <summary>单例实例。对应 UE5 GetDefault&lt;USaveSystemDeveloperSettings&gt;()。</summary>
        public static SaveSystemDeveloperSettings Instance { get; set; } = LoadDefault();

        private static SaveSystemDeveloperSettings LoadDefault()
        {
            // TODO [需接入设置加载系统]: 从 Flax 编辑器用户配置或 JSON 文件加载持久化设置。暂时返回默认实例。
            var settings = new SaveSystemDeveloperSettings();
            NarrativeLog.Log("[SaveSystem] SaveSystemDeveloperSettings 已使用默认值初始化。");
            return settings;
        }
    }
}
