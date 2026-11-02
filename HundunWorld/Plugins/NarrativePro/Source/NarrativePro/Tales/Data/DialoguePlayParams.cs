using System;
using System.Collections.Generic;
using NarrativePro.UnrealFramework;

namespace NarrativePro.Tales.Data
{
    /// <summary>
    /// 对话播放参数。对应 UE5 <c>FDialoguePlayParams</c>（Dialogue.h:168-232）。
    /// <para>
    /// 每个 <c>bOverride_xxx</c> 开关决定对应字段是否覆盖对话资产里的默认值
    /// （UE5 用 bitfield + EditCondition 模拟可空布尔，Flax 用成对布尔字段）。
    /// </para>
    /// </summary>
    [Serializable]
    public class DialoguePlayParams
    {
        /// <summary>从哪个节点 ID 开始播放；为空则从根节点开始。</summary>
        public string StartFromID { get; set; } = "";

        /// <summary>播放优先级；-1 表示使用对话自身的默认优先级。</summary>
        public int Priority { get; set; } = -1;

        /// <summary>是否覆盖 <see cref="bFreeMovement"/>。</summary>
        public bool bOverrideFreeMovement { get; set; } = false;

        /// <summary>对话期间是否允许自由移动。</summary>
        public bool bFreeMovement { get; set; } = true;

        /// <summary>是否覆盖 <see cref="bStopMovement"/>。</summary>
        public bool bOverrideStopMovement { get; set; } = false;

        /// <summary>对话开始时是否让其中的角色停下。</summary>
        public bool bStopMovement { get; set; } = false;

        /// <summary>是否覆盖 <see cref="bUnskippable"/>。</summary>
        public bool bOverrideUnskippable { get; set; } = false;

        /// <summary>是否禁止玩家跳过本对话的行。</summary>
        public bool bUnskippable { get; set; } = false;

        /// <summary>是否覆盖 <see cref="bCanBeExited"/>。对应 UE5 <c>bOverride_bCanBeExited</c>。</summary>
        public bool bOverrideCanBeExited { get; set; } = false;

        /// <summary>是否允许玩家中途退出本对话。对应 UE5 <c>bCanBeExited</c>（默认 true）。</summary>
        public bool bCanBeExited { get; set; } = true;

        /// <summary>
        /// 直接指定说话者 Actor（按说话者索引），替代用 NPCDefinition 解析。
        /// 对应 UE5 <c>TArray&lt;ANarrativeNPCCharacter*&gt; Speakers</c>。
        /// </summary>
        public List<NarrativeNPCCharacter> Speakers { get; set; } = new List<NarrativeNPCCharacter>();

        /// <summary>浅拷贝（值字段全复制，Speakers 复制引用列表）。用于任务对话参数改写 StartFromID。</summary>
        public DialoguePlayParams Clone()
        {
            var copy = (DialoguePlayParams)MemberwiseClone();
            copy.Speakers = Speakers != null ? new List<NarrativeNPCCharacter>(Speakers) : new List<NarrativeNPCCharacter>();
            return copy;
        }
    }
}
