using System;
using NarrativePro.Core;
using DialogueClass = NarrativePro.Tales.Dialogue.Dialogue;

namespace NarrativePro.Tales
{
    /// <summary>
    /// 对话蓝图的蓝图编译产物类。对应 UE5 UDialogueBlueprintGeneratedClass（UBlueprintGeneratedClass 派生）。
    /// UE5 中由对话编译器编译对话并将其存储到 DialogueTemplate 中供运行时使用。
    /// 详见 https://heapcleaner.wordpress.com/2016/06/12/inside-of-unreal-engine-blueprint/
    /// Flax 无蓝图编译系统，此处改为 [Serializable] 普通类占位 + 占位实现。
    /// </summary>
    [Serializable]
    public class DialogueBlueprintGeneratedClass
    {
        /// <summary>
        /// 对话模板。运行时从此模板复制并初始化对话实例。
        /// UE5 中为 UPROPERTY() UDialogue* DialogueTemplate。
        /// </summary>
        public DialogueClass DialogueTemplate { get; private set; }

        /// <summary>
        /// 初始化对话。对应 UE5 UDialogueBlueprintGeneratedClass::InitializeDialogue。
        /// 从 DialogueTemplate 复制并初始化给定对话实例。
        /// </summary>
        /// <param name="dialogue">待初始化的对话实例</param>
        public virtual void InitializeDialogue(DialogueClass dialogue)
        {
            // Flax-不兼容: UE5 的 BlueprintGeneratedClass 在 Flax 无对应物，保留占位。原文 TODO: Flax 无蓝图编译产物机制。若需要从模板复制初始化，
            // 应通过深拷贝 DialogueTemplate 的节点/说话者/玩家信息到目标对话。
            // 目前 Flax 中由 DialogueFactory 直接从 JSON 加载，无需此流程。
            if (dialogue != null && DialogueTemplate != null)
            {
                NarrativeLog.Log("DialogueBlueprintGeneratedClass.InitializeDialogue: 占位实现，Flax 中由 DialogueFactory 直接加载");
            }
        }

        /// <summary>获取对话模板。对应 UE5 GetDialogueTemplate。</summary>
        public DialogueClass GetDialogueTemplate()
        {
            return DialogueTemplate;
        }

        /// <summary>
        /// 设置对话模板。对应 UE5 SetDialogueTemplate。
        /// UE5 中会清理模板上的 RF_ArchetypeObject | RF_DefaultSubObject 标志并设置 RF_Public，
        /// Flax 无对象标志系统，此处仅做赋值。
        /// </summary>
        public void SetDialogueTemplate(DialogueClass dialogueTemplate)
        {
            DialogueTemplate = dialogueTemplate;
        }
    }
}
