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
