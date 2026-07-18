using System;
using DialogueClass = NarrativePro.Tales.Dialogue.Dialogue;

namespace NarrativePro.Tales
{
    /// <summary>
    /// 旧版对话资产。对应 UE5 UDialogueAsset（UDataAsset 派生）。
    /// 保留以便旧版本用户可右键将其转换为 DialogueBlueprint。
    /// Flax 中无 UDataAsset 概念，改为 [Serializable] 普通类。
    /// </summary>
    [Serializable]
    public class DialogueAsset
    {
        /// <summary>
        /// 对话实例。UE5 中为 Instanced UPROPERTY（UDialogue*），
        /// Flax 中直接持有 Dialogue 对象引用。
        /// </summary>
        public DialogueClass Dialogue { get; set; } = new DialogueClass();

        public DialogueAsset()
        {
            // UE5 中构造函数调用 CreateDefaultSubobject<UDialogue>("Dialogue")，
            // Flax 中通过字段初始化器直接 new。
        }
    }
}
