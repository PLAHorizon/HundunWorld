using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.Items;

namespace NarrativePro.Tales
{
    /// <summary>
    /// 标签对话条目。对应 UE5 FTaggedDialogue。
    /// 描述一个可通过 GameplayTag 触发的对话（如问候、嘲讽、调查等）。
    /// </summary>
    [Serializable]
    public class TaggedDialogue
    {
        public TaggedDialogue()
        {
            Cooldown = 30f;
            LastPlayTime = -10000f;
            MaxDistance = 5000f;
        }

        /// <summary>触发此对话的标签（Narrative.TaggedDialogue 分类）。</summary>
        public GameplayTag Tag = GameplayTag.None;

        /// <summary>对话资产路径（替代 UE5 TSoftClassPtr&lt;UDialogue&gt;）。</summary>
        public string DialoguePath = "";

        /// <summary>再次播放此对话前的冷却时间（秒）。</summary>
        public float Cooldown = 30f;

        /// <summary>仅在距 NPC 此距离内才会触发。</summary>
        public float MaxDistance = 5000f;

        /// <summary>NPC 启动此标签对话所需的标签。</summary>
        public GameplayTagContainer RequiredTags = new GameplayTagContainer();

        /// <summary>NPC 拥有这些标签时阻止此对话（例如战斗中不问候）。</summary>
        public GameplayTagContainer BlockedTags = new GameplayTagContainer();

        /// <summary>上次播放时间（游戏时间秒）。</summary>
        public float LastPlayTime = -10000f;

        /// <summary>判断冷却是否已过。</summary>
        public bool IsCooldownReady(float currentTime)
        {
            return currentTime - LastPlayTime >= Cooldown;
        }

        /// <summary>判断 NPC 当前标签组合是否满足触发条件。</summary>
        public bool CanPlayWithTags(GameplayTagContainer npcTags)
        {
            if (BlockedTags != null && npcTags != null && npcTags.HasAny(BlockedTags))
                return false;
            if (RequiredTags != null && npcTags != null && !npcTags.HasAll(RequiredTags))
                return false;
            return true;
        }
    }

    /// <summary>
    /// 标签对话集合。对应 UE5 UTaggedDialogueSet。
    /// 包含 NPC 问候、威胁、调查等触发的对话列表。
    /// </summary>
    [Serializable]
    public class TaggedDialogueSet
    {
        /// <summary>NPC 的标签对话列表。</summary>
        public List<TaggedDialogue> TaggedDialogues = new List<TaggedDialogue>();

        /// <summary>根据标签查找标签对话，找不到返回 null。</summary>
        public TaggedDialogue FindByTag(GameplayTag tag)
        {
            if (TaggedDialogues == null) return null;
            foreach (var d in TaggedDialogues)
            {
                if (d != null && d.Tag == tag) return d;
            }
            return null;
        }

        /// <summary>
        /// 查找所有满足标签条件且冷却已过的标签对话。
        /// </summary>
        public List<TaggedDialogue> FindPlayable(GameplayTag tag, GameplayTagContainer npcTags, float currentTime)
        {
            var result = new List<TaggedDialogue>();
            if (TaggedDialogues == null) return result;
            foreach (var d in TaggedDialogues)
            {
                if (d == null) continue;
                if (tag.IsValid() && d.Tag != tag) continue;
                if (!d.CanPlayWithTags(npcTags)) continue;
                if (!d.IsCooldownReady(currentTime)) continue;
                result.Add(d);
            }
            return result;
        }
    }
}
