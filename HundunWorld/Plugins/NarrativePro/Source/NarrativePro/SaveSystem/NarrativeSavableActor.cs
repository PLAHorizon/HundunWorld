// 模块关系说明：
// 本文件属于新模块 NarrativePro.SaveSystem（UE5 NarrativeSaveSystem 移植）。
// 与现有 NarrativePro.Save 模块的关系详见 NarrativeStableActor.cs 顶部说明。

using System;
using FlaxEngine;

namespace NarrativePro.SaveSystem
{
    /// <summary>
    /// 可存档 Actor 接口。对应 UE5 INarrativeSavableActor。
    /// 可存档 Actor 是“稳定 Actor”的扩展：除了拥有稳定 GUID 外，
    /// 还会由存档系统创建一份存档记录，让其可以保存任意自定义数据。
    /// </summary>
    public interface INarrativeSavableActor : INarrativeStableActor
    {
        /// <summary>通知 Actor 即将被保存，需要填充自身的存档数据。</summary>
        void PrepareForSave();

        /// <summary>通知 Actor 已从存档加载完成。</summary>
        void Load();

        /// <summary>
        /// 是否希望存档系统自动重新生成动态 Actor。
        /// 默认返回 true；NPC 若由聚落生成则应返回 false，自行处理生成。
        /// </summary>
        bool ShouldRespawn();

        /// <summary>
        /// 用于动态 Actor：将稳定 GUID 设置为存档系统从记录中取回的 GUID，
        /// 这样后续保存时存档系统能正确识别此 Actor。
        /// </summary>
        void SetActorGUID(Guid savedGUID);
    }

    /// <summary>
    /// INarrativeSavableActor 的默认实现基类（可选继承）。
    /// 对应 UE5 INarrativeSavableActor::_Implementation 默认实现。
    /// 不强制继承；实现接口的类可直接复用这些静态默认方法。
    /// </summary>
    public static class NarrativeSavableActorDefaults
    {
        /// <summary>PrepareForSave 默认空实现。</summary>
        public static void PrepareForSaveDefault(INarrativeSavableActor self)
        {
            // 默认无操作
        }

        /// <summary>Load 默认空实现。</summary>
        public static void LoadDefault(INarrativeSavableActor self)
        {
            // 默认无操作
        }

        /// <summary>ShouldRespawn 默认返回 true。</summary>
        public static bool ShouldRespawnDefault(INarrativeSavableActor self)
        {
            return true;
        }

        /// <summary>SetActorGUID 默认空实现（子类应覆盖以真正接收 GUID）。</summary>
        public static void SetActorGUIDDefault(INarrativeSavableActor self, Guid savedGUID)
        {
            // 默认无操作；UE5 中此处会 checkf(false) 提示未实现
        }
    }
}
