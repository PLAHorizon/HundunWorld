// 模块关系说明：
// 本文件属于新模块 NarrativePro.SaveSystem（UE5 NarrativeSaveSystem 移植）。
// 与现有 NarrativePro.Save 模块（NarrativeSaveData / NarrativeSaveManager / ISaveStorageProvider）的关系：
//   - Save 模块负责“任务/对话/任务进度”等 Narrative 业务数据序列化与简单文件存储；
//   - SaveSystem 模块负责“场景内可存档 Actor / 组件 / 玩家数据”的快照式存档（基于 Actor GUID + 字节归档）。
// 两者职责互补：SaveSystem 负责世界级快照，Save 负责业务级数据，二者可共存；如需统一可在调用层组合。

using System;
using FlaxEngine;

namespace NarrativePro.SaveSystem
{
    /// <summary>
    /// 稳定 Actor 接口。对应 UE5 INarrativeStableActor。
    /// 实现此接口的 Actor 将被视为“稳定 Actor”——可通过 GUID 在跨会话中引用，
    /// 因为 GUID 可以安全序列化到磁盘，而 Actor 引用不能。
    /// </summary>
    public interface INarrativeStableActor
    {
        /// <summary>获取该 Actor 的稳定 GUID。返回 Invalid 表示不希望被存档系统记录。</summary>
        Guid GetActorGUID();
    }
}
