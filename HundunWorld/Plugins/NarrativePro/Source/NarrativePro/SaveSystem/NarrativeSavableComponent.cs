// 模块关系说明：
// 本文件属于新模块 NarrativePro.SaveSystem（UE5 NarrativeSaveSystem 移植）。
// 与现有 NarrativePro.Save 模块的关系详见 NarrativeStableActor.cs 顶部说明。

using System;

namespace NarrativePro.SaveSystem
{
    /// <summary>
    /// 可存档组件接口。对应 UE5 INarrativeSavableComponent。
    /// 实现此接口的组件会被存档子系统捕获（前提是其所属 Actor 实现了 INarrativeSavableActor）。
    /// </summary>
    public interface INarrativeSavableComponent
    {
        /// <summary>通知组件即将被保存，需要填充自身的存档数据。</summary>
        void PrepareForSave();

        /// <summary>通知组件已从存档加载完成。</summary>
        void Load();
    }
}
