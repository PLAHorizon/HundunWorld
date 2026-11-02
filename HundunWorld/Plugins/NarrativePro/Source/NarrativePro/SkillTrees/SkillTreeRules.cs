using System;
using System.Collections.Generic;

namespace NarrativePro.SkillTrees
{
    /// <summary>
    /// 技能树的纯接缝：前置映射构建与前置判定。<see cref="SkillTreeComponent"/> 线上路径也走这里，
    /// harness 因此在无引擎进程里断得到同一条规则。
    /// 对应 UE5 <c>SkillTreeComponent.cpp:17-41</c>（BeginPlay 构建 PrerequisiteMap）
    /// 与 <c>:109-125</c>（HasRequiredPerks）。
    /// </summary>
    public static class SkillTreeRules
    {
        /// <summary>
        /// 反向登记前置关系：对每条 <c>PerkConfig.LinkedTo</c>，把“该 LinkedTo 节点的前置”加上本 Perk。
        /// 即购买了 <c>PerkConfig.Perk</c> 才可能购买 <c>LinkedTo</c> 里列的 Perk。
        /// 与 UE5 <c>FindOrAdd(...).AddUnique(...)</c> 一致：同一前置不会重复入表。
        /// </summary>
        public static Dictionary<string, FPerkArray> BuildPrerequisiteMap(IEnumerable<TreeSkill> skills)
        {
            var map = new Dictionary<string, FPerkArray>(StringComparer.Ordinal);
            if (skills == null) return map;

            foreach (TreeSkill skill in skills)
            {
                if (skill == null || skill.Perks == null) continue;

                foreach (FPerkConfig perkConfig in skill.Perks)
                {
                    if (perkConfig == null || string.IsNullOrEmpty(perkConfig.Perk)) continue;
                    if (perkConfig.LinkedTo == null) continue;

                    foreach (string linkedTo in perkConfig.LinkedTo)
                    {
                        if (string.IsNullOrEmpty(linkedTo)) continue;

                        FPerkArray perkArray;
                        if (!map.TryGetValue(linkedTo, out perkArray))
                        {
                            perkArray = new FPerkArray();
                            map[linkedTo] = perkArray;
                        }

                        if (!perkArray.Array.Contains(perkConfig.Perk))
                        {
                            perkArray.Array.Add(perkConfig.Perk);
                        }
                    }
                }
            }

            return map;
        }

        /// <summary>
        /// 前置是否满足。UE5 <c>HasRequiredPerks</c> 是 <b>ANY</b> 语义：拥有任一前置即可购买；
        /// 表里没有条目（无前置）时直接放行。
        /// </summary>
        /// <param name="prerequisiteMap"><see cref="BuildPrerequisiteMap"/> 的产物</param>
        /// <param name="perkClassPath">待购买的 Perk 类路径</param>
        /// <param name="ownsPerk">“是否已拥有该 Perk”的查询（通常绑 <c>SkillTreeComponent.HasPerk</c>）</param>
        public static bool HasRequiredPerks(
            Dictionary<string, FPerkArray> prerequisiteMap,
            string perkClassPath,
            Func<string, bool> ownsPerk)
        {
            if (string.IsNullOrEmpty(perkClassPath)) return false;
            if (prerequisiteMap == null) return true;

            FPerkArray perkArray;
            if (!prerequisiteMap.TryGetValue(perkClassPath, out perkArray)) return true;
            if (perkArray?.Array == null || perkArray.Array.Count == 0) return true;

            if (ownsPerk == null) return false;
            foreach (string reqPath in perkArray.Array)
            {
                if (ownsPerk(reqPath)) return true;
            }

            return false;
        }

        /// <summary>
        /// 找出一个技能里持有指定 Perk 的配置项（<c>BuyPerk</c> 需要 OwnerSkill 来升技能等级）。
        /// 对应 UE5 在 <c>SkillTreeSkills</c> 里按 <c>Perks[].Perk == Perk</c> 检索的写法。
        /// </summary>
        public static TreeSkill FindSkillOwningPerk(IEnumerable<TreeSkill> skills, string perkClassPath)
        {
            if (skills == null || string.IsNullOrEmpty(perkClassPath)) return null;

            foreach (TreeSkill skill in skills)
            {
                if (skill?.Perks == null) continue;
                foreach (FPerkConfig config in skill.Perks)
                {
                    if (config != null && string.Equals(config.Perk, perkClassPath, StringComparison.Ordinal))
                    {
                        return skill;
                    }
                }
            }

            return null;
        }
    }
}
