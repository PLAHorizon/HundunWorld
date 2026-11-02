using System;
using System.Collections.Generic;
using FlaxEngine;

namespace NarrativePro.Tales
{
    /// <summary>
    /// 触发器集。对应 UE5 UTriggerSet。
    /// 包含一组 NarrativeTrigger 实例，作为模板供调度使用。
    /// </summary>
    [Serializable]
    public class TriggerSet
    {
        /// <summary>资产名（JSON 侧可读标识，对应 UE5 DataAsset 对象名）。</summary>
        public string Name = "";

        /// <summary>
        /// 数据侧 NPC 绑定表。UE5 用 CharacterDefinition.TriggerSets 的资产引用表达
        /// 「这套触发器属于哪些 NPC」（CharacterDefinition.h:48-49）；Flax 的 NPC 定义是 JSON，
        /// 为避免反向改写 NPC 文件，触发器 JSON 自带 appliesToNpcIds，由
        /// <c>NarrativeDataLoader.WarmTriggers</c> 建立 NPC→触发器集 绑定，
        /// NPC 在定义无显式 TriggerSetPaths 时按此回退绑定（见 NarrativeNPCCharacter.ApplyTriggerSetsFromDefinition）。
        /// </summary>
        public List<string> AppliesToNpcIds = new List<string>();

        /// <summary>此集合包含的触发器及其事件。</summary>
        public List<NarrativeTrigger> Triggers = new List<NarrativeTrigger>();

        /// <summary>初始化所有触发器。</summary>
        public void InitializeAll(Actor ownerCharacter)
        {
            if (Triggers == null) return;
            foreach (var t in Triggers)
            {
                if (t == null) continue;
                t.OwnerCharacter = ownerCharacter;
                t.Initialize();
            }
        }

        /// <summary>激活所有触发器。</summary>
        public void ActivateAll()
        {
            if (Triggers == null) return;
            foreach (var t in Triggers)
            {
                t?.Activate();
            }
        }

        /// <summary>停用所有触发器。</summary>
        public void DeactivateAll()
        {
            if (Triggers == null) return;
            foreach (var t in Triggers)
            {
                t?.Deactivate();
            }
        }
    }
}
