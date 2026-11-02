using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.AI;
using NarrativePro.Items;

namespace NarrativePro.Tales.Data
{
    /// <summary>
    /// 对话说话者信息。对应 UE5 <c>FSpeakerInfo</c>（Dialogue.h:30-86）。
    /// </summary>
    [Serializable]
    public class SpeakerInfo
    {
        /// <summary>说话者 ID。UE5 中若设置了 <see cref="NPCDataAsset"/> 则以其 NPCID 为准。</summary>
        public string SpeakerID { get; set; } = "";

        /// <summary>
        /// 显示名。UE5 在 NPro 里已删除该字段（改从 NPCDataAsset 派生），
        /// 但 C# 端 UI（DialoguePanel / NarrativeUIBuilder）仍依赖它，因此保留并在为空时回退到 NPC 定义名。
        /// </summary>
        public string DisplayName { get; set; } = "";

        /// <summary>字符串标签（旧字段，保留给 UI 与 JSON 兼容）。新代码请用 <see cref="OwnedTags"/>。</summary>
        public List<string> Tags { get; set; } = new List<string>();

        /// <summary>是否为玩家说话者。</summary>
        public bool IsPlayer { get; set; } = false;

        /// <summary>
        /// 说话者的 NPC 定义。设置后就用角色子系统按它去找真正的 NPC Actor。
        /// 对应 UE5 <c>NPCDataAsset</c>。
        /// </summary>
        public NPCDefinition NPCDataAsset;

        /// <summary>对话期间施加到该说话者身上的标签。对应 UE5 <c>OwnedTags</c>。</summary>
        public GameplayTagContainer OwnedTags { get; set; } = CreateDefaultOwnedTags();

        /// <summary>
        /// 该说话者默认镜头序列。对应 UE5 <c>DefaultSpeakerShot</c>（Instanced UNarrativeDialogueSequence）。
        /// 会被对话行上设置的 Shot 覆盖。
        /// </summary>
        public NarrativeDialogueSequence DefaultSpeakerShot;

        /// <summary>
        /// 该说话者默认镜头的 JSON 名字（经 Cinematics/NarrativeDialogueShotRegistry 按名解析）。
        /// UE5 没有该字段（默认镜头是 Instanced 对象），端口用它补齐"对话资产只写名字"的写法。
        /// </summary>
        public string DefaultSpeakerShotName { get; set; } = "";

        /// <summary>
        /// 取实际生效的说话者默认镜头：优先实例，其次按名解析（未命中返回 null，由调用方回退）。
        /// 缓存同 <see cref="DialogueLine.ResolveShot"/> 的理由 —— 引用相等用于「同镜头不重启」。
        /// </summary>
        public NarrativeDialogueSequence ResolveDefaultSpeakerShot()
        {
            if (DefaultSpeakerShot != null) return DefaultSpeakerShot;
            if (string.IsNullOrEmpty(DefaultSpeakerShotName)) return null;

            if (_resolvedDefaultShot != null && _resolvedDefaultShotName == DefaultSpeakerShotName) return _resolvedDefaultShot;
            _resolvedDefaultShot = NarrativePro.Cinematics.NarrativeDialogueShotRegistry.Resolve(DefaultSpeakerShotName);
            _resolvedDefaultShotName = DefaultSpeakerShotName;
            return _resolvedDefaultShot;
        }

        [NonSerialized] private NarrativeDialogueSequence _resolvedDefaultShot;
        [NonSerialized] private string _resolvedDefaultShotName;

        /// <summary>图中该说话者节点的自定义颜色。对应 UE5 <c>NodeColor</c>。</summary>
        public Color NodeColor { get; set; } = new Color(0.036161f, 0.115986f, 0.265625f, 1f);

        /// <summary>说话者替身 Actor 的变换。对应 UE5 <c>SpeakerAvatarTransform</c>。</summary>
        public Transform SpeakerAvatarTransform { get; set; } = Transform.Identity;

        /// <summary>缓存的说话者替身。对应 UE5 <c>SpeakerAvatar</c>（TWeakObjectPtr）。</summary>
        [NonSerialized]
        public Actor SpeakerAvatar;

        public SpeakerInfo() { }

        /// <summary>
        /// 对应 UE5 <c>GetSpeakerID</c>：有 NPC 定义时用它派生真实 ID。
        /// </summary>
        public virtual string GetSpeakerID()
        {
            if (NPCDataAsset != null && !string.IsNullOrEmpty(NPCDataAsset.NPCID))
            {
                return NPCDataAsset.NPCID;
            }

            return SpeakerID ?? "";
        }

        /// <summary>显示名：为空时回退到 NPC 定义名，再回退到说话者 ID。</summary>
        public virtual string GetDisplayName()
        {
            if (!string.IsNullOrEmpty(DisplayName)) return DisplayName;
            if (NPCDataAsset != null && !string.IsNullOrEmpty(NPCDataAsset.NPCName)) return NPCDataAsset.NPCName;
            return GetSpeakerID();
        }

        /// <summary>
        /// 对应 UE5 构造函数里注释掉的两个默认标签：
        /// Narrative.State.DialogueControlled 与 Narrative.State.Player.WantsHideHUD。
        /// </summary>
        private static GameplayTagContainer CreateDefaultOwnedTags()
        {
            var tags = new GameplayTagContainer();
            tags.AddTag(new GameplayTag("Narrative.State.DialogueControlled"));
            tags.AddTag(new GameplayTag("Narrative.State.Player.WantsHideHUD"));
            return tags;
        }
    }

    /// <summary>
    /// 玩家说话者信息。对应 UE5 <c>FPlayerSpeakerInfo</c>（Dialogue.h:88-106）。
    /// </summary>
    [Serializable]
    public class PlayerSpeakerInfo : SpeakerInfo
    {
        /// <summary>玩家选择回复时播放的镜头序列名。对应 UE5 <c>SelectingReplyShot</c> 的名字占位。</summary>
        public string SelectingReplyShotName { get; set; } = "";

        /// <summary>玩家选择回复时播放的镜头序列。对应 UE5 <c>SelectingReplyShot</c>。</summary>
        public NarrativeDialogueSequence SelectingReplyShot;

        /// <summary>
        /// 取实际生效的「选择回复」镜头：优先实例，其次按 <see cref="SelectingReplyShotName"/>
        /// 经镜头注册表解析（UE5 的 Instanced 对象引用在端口降为按名解析）。
        /// </summary>
        public NarrativeDialogueSequence ResolveSelectingReplyShot()
        {
            if (SelectingReplyShot != null) return SelectingReplyShot;
            if (string.IsNullOrEmpty(SelectingReplyShotName)) return null;

            if (_resolvedReplyShot != null && _resolvedReplyShotName == SelectingReplyShotName) return _resolvedReplyShot;
            _resolvedReplyShot = NarrativePro.Cinematics.NarrativeDialogueShotRegistry.Resolve(SelectingReplyShotName);
            _resolvedReplyShotName = SelectingReplyShotName;
            return _resolvedReplyShot;
        }

        [NonSerialized] private NarrativeDialogueSequence _resolvedReplyShot;
        [NonSerialized] private string _resolvedReplyShotName;

        public PlayerSpeakerInfo()
        {
            SpeakerID = "Player";
            IsPlayer = true;
        }

        /// <summary>玩家始终是 Player ID，不受 NPC 定义影响。</summary>
        public override string GetSpeakerID() => "Player";
    }
}
