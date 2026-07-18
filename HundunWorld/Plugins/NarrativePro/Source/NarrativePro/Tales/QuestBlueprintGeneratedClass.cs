using System;
using NarrativePro.Core;
using QuestClass = NarrativePro.Tales.Quest.Quest;

namespace NarrativePro.Tales
{
    /// <summary>
    /// 任务蓝图的蓝图编译产物类。对应 UE5 UQuestBlueprintGeneratedClass（UBlueprintGeneratedClass 派生）。
    /// UE5 中由任务编译器编译任务并将其存储到 QuestTemplate 中供运行时使用。
    /// 详见 https://heapcleaner.wordpress.com/2016/06/12/inside-of-unreal-engine-blueprint/
    /// Flax 无蓝图编译系统，此处改为 [Serializable] 普通类占位 + 占位实现。
    /// </summary>
    [Serializable]
    public class QuestBlueprintGeneratedClass
    {
        /// <summary>
        /// 任务模板。运行时从此模板复制并初始化任务实例。
        /// UE5 中为 UPROPERTY() UQuest* QuestTemplate。
        /// </summary>
        public QuestClass QuestTemplate { get; private set; }

        /// <summary>
        /// 初始化任务。对应 UE5 UQuestBlueprintGeneratedClass::InitializeQuest。
        /// 从 QuestTemplate 复制并初始化给定任务实例。
        /// </summary>
        /// <param name="quest">待初始化的任务实例</param>
        public virtual void InitializeQuest(QuestClass quest)
        {
            // Flax-不兼容: UE5 的 BlueprintGeneratedClass 在 Flax 无对应物，保留占位。原文 TODO: Flax 无蓝图编译产物机制。若需要从模板复制初始化，
            // 应通过深拷贝 QuestTemplate 的状态/分支/任务到目标任务。
            // 目前 Flax 中由 QuestFactory 直接从 JSON 加载，无需此流程。
            if (quest != null && QuestTemplate != null)
            {
                NarrativeLog.Log("QuestBlueprintGeneratedClass.InitializeQuest: 占位实现，Flax 中由 QuestFactory 直接加载");
            }
        }

        /// <summary>获取任务模板。对应 UE5 GetQuestTemplate。</summary>
        public QuestClass GetQuestTemplate()
        {
            return QuestTemplate;
        }

        /// <summary>
        /// 设置任务模板。对应 UE5 SetQuestTemplate。
        /// UE5 中会清理模板上的 RF_Public | RF_ArchetypeObject | RF_DefaultSubObject 标志，
        /// Flax 无对象标志系统，此处仅做赋值。
        /// </summary>
        public void SetQuestTemplate(QuestClass questTemplate)
        {
            QuestTemplate = questTemplate;
        }
    }
}
