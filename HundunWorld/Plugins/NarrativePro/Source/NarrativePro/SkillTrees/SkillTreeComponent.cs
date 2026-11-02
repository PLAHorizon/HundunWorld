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
    public class SkillTreeComponent : Script, NarrativePro.SaveSystem.INarrativeSavableComponent
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

            // 内容头：UE5 的 SkillTreeSkills 是在玩家状态蓝图里直接作者化赋值
            // （Content/Pro/Core/Data/SkillTrees/<Tree>/Skill_*.uasset）；Flax 侧这些反射资产换成 JSON，
            // 组件启用时灌进来。场景里已经作者化过技能的组件保持原样（作者优先）。
            int applied = SkillTreeLoader.ApplyTo(this);

            BuildPrerequisiteMap();
            NarrativeLog.Log($"[SkillTree] SkillTreeComponent 已启用：技能 {SkillTreeSkills.Count} 个" +
                             $"（内容灌入 {applied} 个）。");
        }

        /// <summary>
        /// 构建前置 Perk 映射。对应 UE5 BeginPlay 中的 prereq map 构建（SkillTreeComponent.cpp:17-41）：
        /// 遍历所有技能的 PerkConfig，将每个 Perk 的 LinkedTo 反向登记为“购买 LinkedTo 前需先拥有该 Perk”。
        /// 规则本体在 <see cref="SkillTreeRules.BuildPrerequisiteMap"/>（无引擎依赖，harness 断的是同一条）。
        /// </summary>
        protected virtual void BuildPrerequisiteMap()
        {
            PrerequisiteMap.Clear();
            foreach (KeyValuePair<string, FPerkArray> entry in SkillTreeRules.BuildPrerequisiteMap(SkillTreeSkills))
            {
                PrerequisiteMap[entry.Key] = entry.Value;
            }
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
        /// 只给 Perk 类路径的购买入口：所属技能由技能树里的引用位置反查
        /// （<see cref="SkillTreeRules.FindSkillOwningPerk"/>）。
        /// UE5 侧这条信息由天赋菜单的按钮控件持有（WBP_PerkButton 同时带 Perk 类与 OwningSkill），
        /// 端口先补出程序入口，避免"只能由 UI 传实例"导致 BuyPerk 在插件里没有可调入口。
        /// </summary>
        public virtual bool BuyPerk(string perkClassPath)
        {
            return BuyPerk(perkClassPath, SkillTreeRules.FindSkillOwningPerk(SkillTreeSkills, perkClassPath));
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
            if (ownerSkill == null)
            {
                NarrativeLog.LogWarning($"[SkillTree] BuyPerk 需要非空 OwnerSkill：{perkClassPath}");
                return false;
            }

            if (!CanBuyPerk(perkClassPath, out string reason))
            {
                NarrativeLog.LogWarning($"[SkillTree] 无法购买 Perk {perkClassPath}：{reason}");
                return false;
            }

            TreePerk perk = GetPerk(perkClassPath);
            if (perk == null)
            {
                perk = InstantiatePerk(perkClassPath, ownerSkill);
                if (perk == null)
                {
                    NarrativeLog.LogWarning($"[SkillTree] 无法创建 Perk 实例：{perkClassPath}（需注册 Perk 工厂）");
                    return false;
                }
                perk.PerkClassPath = perkClassPath;
                perk.OwningComponent = this;
                PurchasedPerks.Add(perk);
            }

            // 与 UE5 一致：PerkLevel 为 0 基，新实例 -1（未购买），首次购买后为 0
            perk.SetPerkLevel(perk.PerkLevel + 1);

            // 购买消耗 1 点技能点，同时提升所属技能等级（对应 UE5 .cpp:66-67）
            SkillTreePoints--;
            ownerSkill.SkillLevel++;

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

            TreePerk perk = GetPerk(perkClassPath);
            // 与 UE5 一致：PerkLevel 为 0 基，MaxLevels 为设计师填写的 1 基数，故比较 MaxLevels - 1
            if (perk != null && perk.PerkLevel >= perk.MaxLevels - 1)
            {
                outCantBuyReason = "Perk 已达最高等级";
                return false;
            }

            if (!HasRequiredPerks(perkClassPath))
            {
                outCantBuyReason = "未满足前置 Perk 条件";
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
        /// 与 UE5 HasRequiredPerks（SkillTreeComponent.cpp:109-125）一致：前置列表为 ANY 语义，
        /// 即拥有任一前置即可购买；无前置条目时视为可直接购买。
        /// 注意：本方法不考虑技能点，如需综合判断请使用 <see cref="CanBuyPerk"/>。
        /// </summary>
        public virtual bool HasRequiredPerks(string perkClassPath)
        {
            return SkillTreeRules.HasRequiredPerks(PrerequisiteMap, perkClassPath, HasPerk);
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
                if (string.IsNullOrEmpty(savedPerk.PerkClass)) continue;

                // 避免重复加载同一 Perk（UE5 蓝图中亦强调“不要重复添加属性效果”）
                TreePerk existing = GetPerk(savedPerk.PerkClass);
                if (existing != null)
                {
                    existing.SetPerkLevel(savedPerk.PerkLevel);
                    continue;
                }

                TreePerk perk = InstantiatePerk(savedPerk.PerkClass, null);
                if (perk != null)
                {
                    perk.PerkClassPath = savedPerk.PerkClass;
                    perk.OwningComponent = this;
                    PurchasedPerks.Add(perk);
                    perk.SetPerkLevel(savedPerk.PerkLevel);
                }
                else
                {
                    NarrativeLog.LogWarning($"[SkillTree] 加载时无法创建 Perk 实例：{savedPerk.PerkClass}（需注册 Perk 工厂）");
                }
            }

            NarrativeLog.Log($"[SkillTree] 存档加载完成：{SkillTreeSaveData.SavedSkills.Count} 技能，{SkillTreeSaveData.SavedPerks.Count} Perk。");
        }

        /// <summary>
        /// 根据 Perk 类路径与归属技能创建 Perk 实例。对应 UE5 NewObject&lt;UTreePerk&gt;(this, PerkClass)。
        /// 委托 <see cref="TreePerkFactory"/> 的注册表解析具体 Perk 类型（无 UE 反射）；
        /// 未注册路径回退到 <see cref="TreePerk"/> 基类。子类可重写以提供自定义实例化。
        /// </summary>
        protected virtual TreePerk InstantiatePerk(string perkClassPath, TreeSkill ownerSkill)
        {
            return TreePerkFactory.Create(perkClassPath, this);
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
