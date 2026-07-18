using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.Items;

namespace NarrativePro.SkillTrees
{
    /// <summary>
    /// 已保存的 Perk 数据。对应 UE5 FSavedPerk（USTRUCT, SaveGame）。
    /// </summary>
    [Serializable]
    public class FSavedPerk
    {
        /// <summary>Perk 类路径，加载时据此检索。对应 UE5 TSubclassOf&lt;UTreePerk&gt;。</summary>
        public string PerkClass = "";

        /// <summary>恢复时设定的 Perk 等级。</summary>
        public int PerkLevel = 1;
    }

    /// <summary>
    /// 已保存的技能数据。对应 UE5 FSavedSkill（USTRUCT, SaveGame）。
    /// </summary>
    [Serializable]
    public class FSavedSkill
    {
        /// <summary>技能类路径，加载时据此检索。对应 UE5 TSubclassOf&lt;UTreeSkill&gt;。</summary>
        public string SkillClass = "";

        /// <summary>恢复时设定的技能等级。</summary>
        public int SkillLevel = 1;
    }

    /// <summary>
    /// 技能树存档数据。对应 UE5 FSkillTreeSaveData（USTRUCT, SaveGame）。
    /// </summary>
    [Serializable]
    public class FSkillTreeSaveData
    {
        /// <summary>已保存的技能及其等级。</summary>
        public List<FSavedSkill> SavedSkills = new List<FSavedSkill>();

        /// <summary>已购买的 Perk 及其等级。</summary>
        public List<FSavedPerk> SavedPerks = new List<FSavedPerk>();

        /// <summary>是否存在任何存档数据。</summary>
        public bool HasSaveData()
        {
            return SavedSkills.Count > 0 || SavedPerks.Count > 0;
        }

        /// <summary>清空所有存档数据。</summary>
        public void ClearData()
        {
            SavedSkills.Clear();
            SavedPerks.Clear();
        }
    }

    /// <summary>
    /// Perk 类路径数组容器。对应 UE5 FPerkArray（USTRUCT）。
    /// 用于 <see cref="SkillTreeComponent.PrerequisiteMap"/> 的值类型。
    /// </summary>
    [Serializable]
    public class FPerkArray
    {
        /// <summary>Perk 类路径列表。对应 UE5 TArray&lt;TSubclassOf&lt;UTreePerk&gt;&gt;。</summary>
        public List<string> Array = new List<string>();
    }

    /// <summary>
    /// 技能树组件。对应 UE5 USkillTreeComponent（UActorComponent + INarrativeSavableComponent, within=PlayerState）。
    /// 挂载到 PlayerState 上，持有玩家的技能与已购买 Perk。
    /// 技能由若干相互链接的节点（Perk）组成，技能与 Perk 均有等级。
    /// UE5 中 Current perks 仅在服务器设置、未支持复制；Flax 中改为本地逻辑 + 事件回调。
    /// 通过 <see cref="PrepareForSave"/> / <see cref="Load"/> 实现存档（映射 INarrativeSavableComponent）。
    /// </summary>
    public class SkillTreeComponent : Script
    {
        /// <summary>技能树中的技能及其 Perk。当前 Perk 仅本地设置。</summary>
        public List<TreeSkill> SkillTreeSkills = new List<TreeSkill>();

        /// <summary>所有已购买的 Perk。</summary>
        public List<TreePerk> PurchasedPerks = new List<TreePerk>();

        /// <summary>
        /// 前置 Perk 映射：给定 Perk 类路径，得到购买该 Perk 前必须已购买的所有 Perk 类路径。
        /// 缓存以提升查询效率与整洁度。
        /// </summary>
        public Dictionary<string, FPerkArray> PrerequisiteMap = new Dictionary<string, FPerkArray>();

        /// <summary>技能树存档数据。</summary>
        public FSkillTreeSaveData SkillTreeSaveData = new FSkillTreeSaveData();

        /// <summary>当前可用的技能点数。</summary>
        public int SkillTreePoints = 0;

        public override void OnEnable()
        {
            base.OnEnable();
            NarrativeLog.Log("[SkillTree] SkillTreeComponent 已启用。");
        }

        public override void OnDisable()
        {
            base.OnDisable();
        }

        /// <summary>给予玩家指定数量的技能点。</summary>
        public virtual void GiveSkillPoints(int points)
        {
            if (points <= 0) return;
            SkillTreePoints += points;
            NarrativeLog.Log($"[SkillTree] 获得 {points} 技能点，当前总计 {SkillTreePoints}。");
        }

        /// <summary>
        /// 尝试购买一个 Perk。会先校验前置 Perk 是否已购买。
        /// 对应 UE5 BuyPerk(TSubclassOf&lt;UTreePerk&gt;, UTreeSkill*)。
        /// </summary>
        /// <param name="perkClassPath">Perk 类路径标识</param>
        /// <param name="ownerSkill">归属技能实例</param>
        /// <returns>是否购买成功</returns>
        public virtual bool BuyPerk(string perkClassPath, TreeSkill ownerSkill)
        {
            if (string.IsNullOrEmpty(perkClassPath)) return false;

            if (!CanBuyPerk(perkClassPath, out string reason))
            {
                NarrativeLog.LogWarning($"[SkillTree] 无法购买 Perk {perkClassPath}：{reason}");
                return false;
            }

            TreePerk perk = GetPerk(perkClassPath);
            bool bNew = perk == null;
            if (bNew)
            {
                perk = InstantiatePerk(perkClassPath, ownerSkill);
                if (perk == null)
                {
                    NarrativeLog.LogWarning($"[SkillTree] 无法创建 Perk 实例：{perkClassPath}（需注册 Perk 工厂）");
                    return false;
                }
                perk.PerkClassPath = perkClassPath;
                perk.OwningComponent = this;
                perk.PerkLevel = -1; // 尚未购买
                PurchasedPerks.Add(perk);
            }

            // 首次购买置为 1 级，否则递增
            perk.PerkLevel = bNew ? 1 : perk.PerkLevel + 1;
            perk.SetPerkLevel(perk.PerkLevel);
            SkillTreePoints--;

            return true;
        }

        /// <summary>
        /// 返回是否可以购买指定 Perk。对应 UE5 CanBuyPerk(TSubclassOf&lt;UTreePerk&gt;, FText&amp;)。
        /// </summary>
        /// <param name="perkClassPath">Perk 类路径标识</param>
        /// <param name="outCantBuyReason">无法购买的原因（对应 UE5 FText）</param>
        public virtual bool CanBuyPerk(string perkClassPath, out string outCantBuyReason)
        {
            outCantBuyReason = "";

            if (string.IsNullOrEmpty(perkClassPath))
            {
                outCantBuyReason = "Perk 路径为空";
                return false;
            }

            if (!HasRequiredPerks(perkClassPath))
            {
                outCantBuyReason = "未满足前置 Perk 条件";
                return false;
            }

            TreePerk perk = GetPerk(perkClassPath);
            if (perk != null && perk.MaxLevels > 0 && perk.PerkLevel >= perk.MaxLevels)
            {
                outCantBuyReason = "Perk 已达最高等级";
                return false;
            }

            if (SkillTreePoints <= 0)
            {
                outCantBuyReason = "技能点不足";
                return false;
            }

            return true;
        }

        /// <summary>
        /// 返回是否已解锁指定 Perk 的前置 Perk。
        /// 注意：本方法不考虑技能点，如需综合判断请使用 <see cref="CanBuyPerk"/>。
        /// </summary>
        public virtual bool HasRequiredPerks(string perkClassPath)
        {
            if (string.IsNullOrEmpty(perkClassPath)) return false;

            if (!PrerequisiteMap.TryGetValue(perkClassPath, out FPerkArray prereqArray))
            {
                return true; // 无前置要求
            }

            if (prereqArray?.Array == null) return true;

            foreach (string reqPath in prereqArray.Array)
            {
                if (!HasPerk(reqPath)) return false;
            }

            return true;
        }

        /// <summary>查询指定 Perk 的当前等级。未购买返回 -1。</summary>
        public int GetPerkLevel(string perkClassPath)
        {
            TreePerk perk = GetPerk(perkClassPath);
            return perk != null ? perk.PerkLevel : -1;
        }

        /// <summary>若已拥有指定 Perk，返回其实例；否则返回 null。</summary>
        public TreePerk GetPerk(string perkClassPath)
        {
            if (string.IsNullOrEmpty(perkClassPath)) return null;

            foreach (TreePerk p in PurchasedPerks)
            {
                if (p != null && p.PerkClassPath == perkClassPath) return p;
            }

            return null;
        }

        /// <summary>是否已拥有指定 Perk。</summary>
        public bool HasPerk(string perkClassPath)
        {
            return GetPerk(perkClassPath) != null;
        }

        /// <summary>存档：准备保存数据。对应 UE5 PrepareForSave_Implementation。</summary>
        public virtual void PrepareForSave()
        {
            SkillTreeSaveData.ClearData();

            foreach (TreeSkill skill in SkillTreeSkills)
            {
                if (skill == null) continue;
                SkillTreeSaveData.SavedSkills.Add(SkillToSaveData(skill));
            }

            foreach (TreePerk perk in PurchasedPerks)
            {
                if (perk == null) continue;
                SkillTreeSaveData.SavedPerks.Add(PerkToSaveData(perk));
            }
        }

        /// <summary>读档：恢复状态。对应 UE5 Load_Implementation。</summary>
        public virtual void Load()
        {
            if (!SkillTreeSaveData.HasSaveData())
            {
                NarrativeLog.Log("[SkillTree] 无存档数据可加载。");
                return;
            }

            foreach (FSavedSkill savedSkill in SkillTreeSaveData.SavedSkills)
            {
                TreeSkill skill = FindSkillByPath(savedSkill.SkillClass);
                if (skill != null)
                {
                    skill.SkillLevel = savedSkill.SkillLevel;
                }
                else
                {
                    NarrativeLog.LogWarning($"[SkillTree] 加载时未找到技能：{savedSkill.SkillClass}");
                }
            }

            foreach (FSavedPerk savedPerk in SkillTreeSaveData.SavedPerks)
            {
                TreePerk perk = InstantiatePerk(savedPerk.PerkClass, null);
                if (perk != null)
                {
                    perk.PerkClassPath = savedPerk.PerkClass;
                    perk.OwningComponent = this;
                    perk.PerkLevel = savedPerk.PerkLevel;
                    perk.SetPerkLevel(perk.PerkLevel);
                    PurchasedPerks.Add(perk);
                }
                else
                {
                    NarrativeLog.LogWarning($"[SkillTree] 加载时无法创建 Perk 实例：{savedPerk.PerkClass}（需注册 Perk 工厂）");
                }
            }

            NarrativeLog.Log($"[SkillTree] 存档加载完成：{SkillTreeSaveData.SavedSkills.Count} 技能，{SkillTreeSaveData.SavedPerks.Count} Perk。");
        }

        /// <summary>
        /// 根据 Perk 类路径与归属技能创建 Perk 实例。
        /// 由于 TSubclassOf 已映射为字符串路径占位，需要游戏侧通过注册表/反射在此处解析为具体 Perk 类型。
        /// 默认返回 null（占位），子类应重写以提供实际的 Perk 实例化逻辑。
        /// </summary>
        protected virtual TreePerk InstantiatePerk(string perkClassPath, TreeSkill ownerSkill)
        {
            return null;
        }

        /// <summary>根据技能类路径在 <see cref="SkillTreeSkills"/> 中检索技能实例。</summary>
        protected TreeSkill FindSkillByPath(string skillClassPath)
        {
            if (string.IsNullOrEmpty(skillClassPath)) return null;

            foreach (TreeSkill s in SkillTreeSkills)
            {
                if (s != null && s.SkillClassPath == skillClassPath) return s;
            }

            return null;
        }

        private FSavedSkill SkillToSaveData(TreeSkill skill)
        {
            return new FSavedSkill
            {
                SkillClass = skill.SkillClassPath,
                SkillLevel = skill.SkillLevel
            };
        }

        private FSavedPerk PerkToSaveData(TreePerk perk)
        {
            return new FSavedPerk
            {
                PerkClass = perk.PerkClassPath,
                PerkLevel = perk.PerkLevel
            };
        }
    }
}
