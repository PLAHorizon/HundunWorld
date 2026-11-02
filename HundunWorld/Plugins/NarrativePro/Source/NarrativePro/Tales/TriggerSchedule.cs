using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.Tales
{
    /// <summary>
    /// 触发器调度。对应 UE5 UTriggerSchedule。
    /// 包含一组 TriggerSet 资产引用，按需激活/停用整组触发器。
    /// </summary>
    [Serializable]
    public class TriggerSchedule
    {
        public TriggerSchedule()
        {
        }

        /// <summary>组成此调度的触发器集路径列表（替代 UE5 TSoftObjectPtr&lt;UTriggerSet&gt;）。</summary>
        public List<string> TriggerSetPaths = new List<string>();

        /// <summary>已加载的触发器集实例缓存（运行期填充）。</summary>
        [NonSerialized]
        public List<TriggerSet> LoadedTriggerSets = new List<TriggerSet>();

        /// <summary>加载所有引用的 TriggerSet。Flax 中通过 JSON 反序列化加载。</summary>
        public void LoadAll()
        {
            LoadedTriggerSets.Clear();
            if (TriggerSetPaths == null) return;
            foreach (var path in TriggerSetPaths)
            {
                var set = LoadTriggerSet(path);
                if (set != null)
                {
                    LoadedTriggerSets.Add(set);
                }
            }
        }

        /// <summary>初始化并激活所有已加载的触发器集。</summary>
        public void ActivateAll(Actor ownerCharacter)
        {
            if (LoadedTriggerSets == null) return;
            foreach (var set in LoadedTriggerSets)
            {
                if (set == null) continue;
                set.InitializeAll(ownerCharacter);
                set.ActivateAll();
            }
        }

        /// <summary>停用所有已加载的触发器集。</summary>
        public void DeactivateAll()
        {
            if (LoadedTriggerSets == null) return;
            foreach (var set in LoadedTriggerSets)
            {
                set?.DeactivateAll();
            }
        }

        /// <summary>
        /// 从指定路径加载 TriggerSet。Flax 中无原生 DataAsset，
        /// 走 <see cref="TriggerSetFactory"/> 的多态 JSON 工厂（"type" 判别字段 + 大小写不敏感绑定，
        /// 与 QuestFactory/DialogueFactory 一致；直接 Deserialize&lt;TriggerSet&gt; 会因抽象元素 NarrativeTrigger 失败），
        /// 子类可重写为其他加载方式。
        /// </summary>
        protected virtual TriggerSet LoadTriggerSet(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            try
            {
                string resolved = NarrativePro.Data.NarrativeDataPaths.ResolveForRead(path, "trigger set");
                if (string.IsNullOrEmpty(resolved) || !File.Exists(resolved))
                {
                    NarrativeLog.LogWarning($"TriggerSchedule.LoadTriggerSet: 文件不存在: {resolved}");
                    return null;
                }
                return TriggerSetFactory.LoadTriggerSetFile(resolved);
            }
            catch (Exception ex)
            {
                NarrativeLog.LogError($"Failed to load TriggerSet from {path}: {ex.Message}");
                return null;
            }
        }
    }
}
