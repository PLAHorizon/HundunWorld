using System;
using System.Collections.Generic;

namespace MetaHuman
{
    /// <summary>一次重绑的结论：哪些节点对上了、哪些没有、走的是哪一档匹配。</summary>
    public class MetaHumanRetargetResult
    {
        public readonly List<string> Mapped = new List<string>();
        public readonly List<string> Unmapped = new List<string>();
        public readonly List<string> Notes = new List<string>();
        public int SourceNodeCount;

        /// <summary>是否启用镜像兜底。判定依据是两份骨架名字的总体对照，见 BuildMapping 里的说明。</summary>
        public bool MirrorTierEnabled;
        public int ExactOverlapCount;
        public int MirrorOverlapCount;

        /// <summary>各档匹配各自命中多少，用来发现「看着全对，其实只走了第一档」。</summary>
        public int ExactMatches;
        public int CaseInsensitiveMatches;
        public int MirrorMatches;
        public int AliasMatches;
        public int SlotMatches;
    }

    /// <summary>
    /// 骨骼动画自动绑定的匹配层：给定目标骨架与来源骨架的节点名，产出「目标节点名 → 来源节点名」映射。
    ///
    /// 纯核实现（不碰 SkinnedModel）的理由与绑骨核对同理：匹配的对错本来就与引擎无关，
    /// 放这里才能拿真实 .dna 抽出来的骨架名字清单离线跑断言，
    /// 而不是等导入后发现「左手动画绑到右手」。
    /// 写入引擎的通道是 SkinnedModel.SkeletonRetargets，那部分在编辑器层。
    /// </summary>
    public static class MetaHumanRetarget
    {
        public const string TierExact = "exact";
        public const string TierCase = "case";
        public const string TierMirror = "mirror";
        public const string TierAlias = "alias";
        public const string TierSlot = "slot";

        /// <summary>逐节点匹配。sourceNames 顺序无关，重复名以第一个出现的为准。</summary>
        public static Dictionary<string, string> BuildMapping(IList<string> targetNames,
                                                              IList<string> sourceNames,
                                                              MetaHumanTemplate template,
                                                              MetaHumanRetargetResult result)
        {
            Dictionary<string, string> mapping = new Dictionary<string, string>(StringComparer.Ordinal);
            if (result == null)
            {
                return mapping;
            }

            if (targetNames == null || sourceNames == null)
            {
                result.Notes.Add("骨架名字清单为空，无法匹配");
                return mapping;
            }

            result.SourceNodeCount = sourceNames.Count;

            // 两张查找表：先按大小写敏感的精确名找，找不到再按忽略大小写找。
            // 只用一张大小写不敏感的表会让精确命中被抢先占位 ——
            // SKM_Face.dna 里真同时存在 "Archetype" 与 "archetype" 两个节点名，
            // 后者会被前者的表项遮蔽，于是"精确同名"降级成"猜它是同一个"。
            Dictionary<string, string> exactByName = new Dictionary<string, string>(StringComparer.Ordinal);
            Dictionary<string, string> foldedByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < sourceNames.Count; i++)
            {
                string name = sourceNames[i];
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                if (!exactByName.ContainsKey(name))
                {
                    exactByName[name] = name;
                }

                if (!foldedByName.ContainsKey(name))
                {
                    foldedByName[name] = name;
                }
            }

            // 镜像档能不能开，看的是两份骨架的**总体**对照，不是单个节点缺没缺：
            // 只有"目标的精确名字几乎撞不上、而换侧名字几乎全撞得上"时才说明两侧命名约定相反。
            // 否则同一个骨架里少了一根关节，也会被镜像到它的对岸去——左手动画静默绑到右手，
            // 节点数还完全正常，比报"未匹配"糟得多。
            int exactOverlap = 0;
            int mirrorOverlap = 0;
            for (int i = 0; i < targetNames.Count; i++)
            {
                string targetName = targetNames[i];
                if (targetName == null)
                {
                    continue;
                }

                if (exactByName.ContainsKey(targetName))
                {
                    exactOverlap++;
                    continue;
                }

                string mirrorName = Mirror(targetName);
                if (mirrorName != null && foldedByName.ContainsKey(mirrorName))
                {
                    mirrorOverlap++;
                }
            }

            result.ExactOverlapCount = exactOverlap;
            result.MirrorOverlapCount = mirrorOverlap;
            bool allowMirror = mirrorOverlap > exactOverlap;
            result.MirrorTierEnabled = allowMirror;
            if (!allowMirror && mirrorOverlap > 0)
            {
                result.Notes.Add("两份骨架命名约定一致（精确同名 " + exactOverlap + " 个 > 换侧同名 "
                                 + mirrorOverlap + " 个），不启用镜像兜底：缺的关节会如实报未匹配，而不是绑到对岸");
            }

            for (int i = 0; i < targetNames.Count; i++)
            {
                string targetName = targetNames[i];
                string matched;
                string tier;
                if (TryMatchOne(targetName, exactByName, foldedByName, template, allowMirror, out matched, out tier))
                {
                    mapping[targetName] = matched;
                    result.Mapped.Add(targetName + " ← " + matched + " [" + tier + "]");
                    switch (tier)
                    {
                        case TierExact:
                            result.ExactMatches++;
                            break;
                        case TierCase:
                            result.CaseInsensitiveMatches++;
                            break;
                        case TierMirror:
                            result.MirrorMatches++;
                            break;
                        case TierAlias:
                            result.AliasMatches++;
                            break;
                        default:
                            result.SlotMatches++;
                            break;
                    }
                }
                else
                {
                    result.Unmapped.Add(targetName);
                }
            }

            if (result.Mapped.Count == 0)
            {
                result.Notes.Add("一个节点都没对上：来源骨架与目标骨架命名体系不同，需要模板槽位或人工映射");
            }

            // 重名骨架的静默丢失要如实说：`NodesMapping` 是**以目标名为键**的字典，
            // 而 Character_1 那类骨架有大量同名节点（IK/FK/矫正骨共用名字），
            // 于是"逐节点命中 8 条"落到字典里只剩 2 条 —— 不说出来，下一个人会当成"写入丢数据"去查。
            int collapsed = result.Mapped.Count - mapping.Count;
            if (collapsed > 0)
            {
                // 只报个数等于没报：要决定"要不要规范骨骼名"，得知道**具体是哪几根**被合并了。
                // 清单封顶 12 个（回执会被日志/JSON 截断），并明说是前 12 个而不是全部。
                Dictionary<string, int> hitsPerTargetName = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (string line in result.Mapped)
                {
                    int arrow = line.IndexOf('\u2190');
                    string name = (arrow > 0 ? line.Substring(0, arrow) : line).Trim();
                    if (name.Length == 0)
                    {
                        continue;
                    }

                    int seen;
                    hitsPerTargetName[name] = hitsPerTargetName.TryGetValue(name, out seen) ? seen + 1 : 1;
                }

                List<string> collidedNames = new List<string>();
                foreach (KeyValuePair<string, int> kv in hitsPerTargetName)
                {
                    if (kv.Value > 1)
                    {
                        collidedNames.Add(kv.Key + "×" + kv.Value);
                    }
                }

                collidedNames.Sort(StringComparer.Ordinal);
                int shown = Math.Min(12, collidedNames.Count);
                result.Notes.Add("目标骨架里有 " + collapsed + " 个重名节点被合并：映射表按名字收键，"
                                 + result.Mapped.Count + " 条逐节点命中只能落成 " + mapping.Count
                                 + " 条。被合并的名字（前 " + shown + " 个，共 " + collidedNames.Count
                                 + " 个）：" + string.Join(", ", collidedNames.GetRange(0, shown).ToArray())
                                 + "。要逐节点重绑，得先把这些骨骼改名规范化（同名节点无法按名字区分）。");
            }

            return mapping;
        }

        private static bool TryMatchOne(string targetName,
                                        Dictionary<string, string> exactByName,
                                        Dictionary<string, string> foldedByName,
                                        MetaHumanTemplate template,
                                        bool allowMirror,
                                        out string matched,
                                        out string tier)
        {
            matched = null;
            tier = null;
            if (string.IsNullOrEmpty(targetName))
            {
                return false;
            }

            string hit;
            // 1) 精确同名（区分大小写）
            if (exactByName.TryGetValue(targetName, out hit))
            {
                matched = hit;
                tier = TierExact;
                return true;
            }

            // 2) 忽略大小写
            if (foldedByName.TryGetValue(targetName, out hit))
            {
                matched = hit;
                tier = TierCase;
                return true;
            }

            // 3) 镜像换侧（仅在总体对照表明命名约定相反时才启用）
            string mirrored = allowMirror ? Mirror(targetName) : null;
            if (mirrored != null && foldedByName.TryGetValue(mirrored, out hit))
            {
                matched = hit;
                tier = TierMirror;
                return true;
            }

            // 4) 跨命名族别名（UE 名 → 资产实测的 DEF-*/Humanoid 名）。
            //    排在模板槽位之前是刻意的：别名表是**一对一**（`Validate()` 守着一个别名名只认一个主人），
            //    而槽位是**多对一**（一个槽里同时登记 spine_01..04，取的是"第一个在来源里存在的候选"）——
            //    让多对一先跑，spine_02 与 spine_03 会被同一个 spine_01 一起接走，节点数照样正常，
            //    但动画会整条脊柱塌成一节，比报"未匹配"糟得多。
            string[] aliases = MetaHumanBoneAliases.AliasesOf(targetName);
            for (int i = 0; i < aliases.Length; i++)
            {
                string alias = aliases[i];
                if (string.IsNullOrEmpty(alias))
                {
                    continue;
                }

                if (exactByName.TryGetValue(alias, out hit))
                {
                    matched = hit;
                    tier = TierAlias;
                    return true;
                }

                if (foldedByName.TryGetValue(alias, out hit))
                {
                    matched = hit;
                    tier = TierAlias;
                    return true;
                }
            }

            // 4b) 反方向：目标自己就是资产侧名字（`DEF-spine`）而来源是 UE/MetaHuman 名字族。
            //     别名表原本只有 UE→资产 这一个方向，于是"把动画绑到项目角色上"这个方向完全不命中
            //     （实测反向只有 2 条精确同名，写进资产的重绑表基本没用）。
            string viaAsset = MetaHumanBoneAliases.TemplateNameForAsset(targetName);
            if (!string.IsNullOrEmpty(viaAsset))
            {
                if (exactByName.TryGetValue(viaAsset, out hit))
                {
                    matched = hit;
                    tier = TierAlias;
                    return true;
                }

                if (foldedByName.TryGetValue(viaAsset, out hit))
                {
                    matched = hit;
                    tier = TierAlias;
                    return true;
                }
            }

            // 5) 模板槽位候选名：槽位里登记的别名与对岸名
            if (template != null && template.SkeletonSlotMap != null)
            {
                foreach (KeyValuePair<string, List<string>> slot in template.SkeletonSlotMap)
                {
                    if (slot.Value == null || !ContainsName(slot.Value, targetName))
                    {
                        continue;
                    }

                    for (int i = 0; i < slot.Value.Count; i++)
                    {
                        string candidate = slot.Value[i];
                        if (foldedByName.TryGetValue(candidate, out hit))
                        {
                            matched = hit;
                            tier = TierSlot;
                            return true;
                        }

                        string candidateMirror = allowMirror ? Mirror(candidate) : null;
                        if (candidateMirror != null && foldedByName.TryGetValue(candidateMirror, out hit))
                        {
                            matched = hit;
                            tier = TierSlot;
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private static bool ContainsName(List<string> names, string name)
        {
            for (int i = 0; i < names.Count; i++)
            {
                if (string.Equals(names[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 换侧。真实 MetaHuman 骨架里成侧的名字只有两种写法：
        /// 小写后缀（clavicle_pec_l，身体 332 个）与 FACIAL_L_/FACIAL_R_ 前缀（脸 380 对），
        /// 这两条按量到的形态写；大写 _L/_R 的写法留给外部导入的动画骨架。
        /// </summary>
        public static string Mirror(string name)
        {
            if (string.IsNullOrEmpty(name) || name.Length < 2)
            {
                return null;
            }

            if (name.Length >= 8 && (name.StartsWith("FACIAL_L", StringComparison.Ordinal) || name.StartsWith("FACIAL_R", StringComparison.Ordinal)))
            {
                return (name[7] == 'L' ? "FACIAL_R" : "FACIAL_L") + name.Substring(8);
            }

            if (name[name.Length - 2] == '_')
            {
                char suffix = char.ToLowerInvariant(name[name.Length - 1]);
                if (suffix == 'l')
                {
                    return name.Substring(0, name.Length - 1) + (char.IsUpper(name[name.Length - 1]) ? "R" : "r");
                }

                if (suffix == 'r')
                {
                    return name.Substring(0, name.Length - 1) + (char.IsUpper(name[name.Length - 1]) ? "L" : "l");
                }
            }

            return null;
        }
    }
}
