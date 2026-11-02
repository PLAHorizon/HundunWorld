using System;
using System.Collections.Generic;

namespace MetaHuman
{
    /// <summary>模板与「一份资产的名字清单」核对后的结果。纯数据，不碰引擎类型。</summary>
    public class MetaHumanBindingReport
    {
        public readonly List<string> MatchedSlots = new List<string>();
        public readonly List<string> MissingSlots = new List<string>();
        public readonly List<string> MatchedMorphs = new List<string>();
        public readonly List<string> MissingMorphs = new List<string>();
        public readonly List<string> UnusedAssetNodes = new List<string>();
        public readonly List<string> Notes = new List<string>();

        /// <summary>模板效果里引用了、但资产骨架没有的骨骼名。</summary>
        public readonly List<string> MissingBoneTargets = new List<string>();

        /// <summary>
        /// 逐条头部参数的承载量。判据单位是「参数」而不是「名字」：
        /// 一条参数缺一个目标，画面上是半个动作，既不算全可承载也不算写不出来。
        /// </summary>
        public int HeadParams;
        public int ServableParams;
        public int PartiallyServableParams;
        public readonly List<string> UnservableParams = new List<string>();
        public readonly List<string> NoTargetParams = new List<string>();
        public readonly List<string> PartialSample = new List<string>();

        public int MissingCount
        {
            get { return MissingSlots.Count + MissingMorphs.Count + MissingBoneTargets.Count; }
        }

        public override string ToString()
        {
            return "命中槽位 " + MatchedSlots.Count + " / 缺槽位 " + MissingSlots.Count
                + "；命中 morph " + MatchedMorphs.Count + " / 缺 morph " + MissingMorphs.Count
                + "；缺骨骼目标 " + MissingBoneTargets.Count
                + "；资产未引用骨骼 " + UnusedAssetNodes.Count
                + "；头部参数 " + HeadParams + " 条：全可承载 " + ServableParams
                + " / 部分 " + PartiallyServableParams
                + " / 写不出来 " + UnservableParams.Count
                + " / 只挂材质 " + NoTargetParams.Count;
        }
    }

    /// <summary>
    /// 模板↔资产名字的核对与回填，纯核实现。
    ///
    /// 这层刻意只吃「名字集合」而不是 SkinnedModel：绑定的正确性（槽位候选名对不对、
    /// morph 名录是否与资产一致、区域/左右判定会不会归错）本来与引擎无关，
    /// 放在纯核里就能拿真实的 .dna 名字清单离线跑断言，而不只是「编译过」。
    /// 引擎侧的取名与导入在 Runtime 的 MetaHumanRigBinder 里，是这层的薄封装。
    /// </summary>
    public static class MetaHumanBinding
    {
        /// <summary>
        /// 命中判定：槽位候选名里任意一个在资产里存在即算命中；morph 按名精确匹配。
        /// assetMorphs 为空而模板要求 blendshape 时要在 Notes 里说清楚——那是「导入没开开关」的形态，
        /// 与「资产真没有 morph」在结果上必须可区分。
        /// </summary>
        public static MetaHumanBindingReport Bind(MetaHumanTemplate template,
                                                  ICollection<string> assetBones,
                                                  ICollection<string> assetMorphs,
                                                  int assetCount = 1)
        {
            MetaHumanBindingReport report = new MetaHumanBindingReport();
            if (template == null || assetBones == null || assetMorphs == null)
            {
                report.Notes.Add("模板或名字清单为空，无法核对");
                return report;
            }

            if (assetCount <= 0)
            {
                report.Notes.Add("没有可用资产，核对结果不代表任何绑定成功");
                return report;
            }

            if (assetMorphs.Count == 0 && template.RequiresBlendShapeImport)
            {
                report.Notes.Add("资产里没有任何 blendshape：导入时未开 ImportBlendShapes，形状参数会全部失效");
            }

            foreach (KeyValuePair<string, List<string>> slot in template.SkeletonSlotMap)
            {
                string hit = null;
                for (int i = 0; i < slot.Value.Count; i++)
                {
                    if (assetBones.Contains(slot.Value[i]))
                    {
                        hit = slot.Value[i];
                        break;
                    }
                }

                if (hit == null)
                {
                    report.MissingSlots.Add(slot.Key);
                }
                else
                {
                    report.MatchedSlots.Add(slot.Key + " -> " + hit);
                }
            }

            for (int i = 0; i < template.MorphCatalog.Count; i++)
            {
                string morph = template.MorphCatalog[i];
                if (assetMorphs.Contains(morph))
                {
                    report.MatchedMorphs.Add(morph);
                }
                else
                {
                    report.MissingMorphs.Add(morph);
                }
            }

            foreach (string target in template.BoneTargets())
            {
                if (!assetBones.Contains(target))
                {
                    report.MissingBoneTargets.Add(target);
                }
            }

            foreach (string bone in assetBones)
            {
                if (!TemplateReferencesBone(template, bone))
                {
                    report.UnusedAssetNodes.Add(bone);
                }
            }

            CountHeadServability(template, assetBones, assetMorphs, report);

            return report;
        }

        /// <summary>
        /// 逐条参数问一句：**这具资产到底承不承载得了这条头部参数**。
        ///
        /// 为什么单独数一遍：`MissingBoneTargets` 只说"哪些名字不在骨架里"，而玩家能感知的单位是参数
        /// ——一条参数的三个骨骼目标里缺了一个，画面上是"半个动作"，既不是"全都有"也不是"全都没有"。
        /// 实测口径（文档 §34）：头部形状的主载体是 `FACIAL_` 那套脸骨（1488 条骨骼效果 vs 37 条 morph），
        /// 所以"资产有没有脸骨"比"导入有没有开 ImportBlendShapes"更早决定玩家能不能捏脸。
        /// 只统计非 Body 区域（与 `PartitionForUI` 的头部口径一致）；没有任何骨骼/morph 目标的参数
        /// 单独记 `NoTargetParams`（那是材质档），不混进"可承载"也不混进"写不出来"。
        /// </summary>
        private static void CountHeadServability(MetaHumanTemplate template,
                                                 ICollection<string> assetBones,
                                                 ICollection<string> assetMorphs,
                                                 MetaHumanBindingReport report)
        {
            foreach (MetaHumanParameter p in template.All())
            {
                if (p == null || p.Region == MetaHumanRegion.Body)
                {
                    continue;
                }

                report.HeadParams++;

                HashSet<string> bones = new HashSet<string>();
                HashSet<string> morphs = new HashSet<string>();
                if (p.Effects != null)
                {
                    for (int i = 0; i < p.Effects.Count; i++)
                    {
                        MetaHumanEffect effect = p.Effects[i];
                        if (effect == null || string.IsNullOrEmpty(effect.Target))
                        {
                            continue;
                        }

                        if (effect.Kind == MetaHumanEffectKind.BoneTranslate
                            || effect.Kind == MetaHumanEffectKind.BoneScale)
                        {
                            bones.Add(effect.Target);
                        }
                        else if (effect.Kind == MetaHumanEffectKind.Morph)
                        {
                            morphs.Add(effect.Target);
                        }
                    }
                }

                int need = bones.Count + morphs.Count;
                if (need == 0)
                {
                    report.NoTargetParams.Add(p.Id);
                    continue;
                }

                int hit = 0;
                foreach (string bone in bones)
                {
                    if (assetBones.Contains(bone))
                    {
                        hit++;
                    }
                }

                foreach (string morph in morphs)
                {
                    if (assetMorphs.Contains(morph))
                    {
                        hit++;
                    }
                }

                if (hit == need)
                {
                    report.ServableParams++;
                }
                else if (hit == 0)
                {
                    report.UnservableParams.Add(p.Id);
                }
                else
                {
                    report.PartiallyServableParams++;
                    if (report.PartialSample.Count < 8)
                    {
                        report.PartialSample.Add(p.Id);
                    }
                }
            }
        }

        public static bool TemplateReferencesBone(MetaHumanTemplate template, string bone)
        {
            foreach (MetaHumanParameter p in template.All())
            {
                List<MetaHumanEffect> effects = p.Effects;
                for (int i = 0; i < effects.Count; i++)
                {
                    if (effects[i].Target == bone)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// 把资产里真实存在的 morph 名录回填进模板，并为每个新出现的 morph 生成一根形状滑杆。
        /// 返回新增条数。模板因此始终描述手上这份资产，而不是一份想象中的资产。
        /// </summary>
        public static int ImportMissingMorphs(MetaHumanTemplate template, ICollection<string> assetMorphs)
        {
            if (template == null || assetMorphs == null)
            {
                return 0;
            }

            int added = 0;
            HashSet<string> known = new HashSet<string>(template.MorphCatalog, StringComparer.Ordinal);
            List<string> fresh = new List<string>();
            foreach (string morph in assetMorphs)
            {
                if (known.Contains(morph))
                {
                    continue;
                }

                known.Add(morph);
                template.MorphCatalog.Add(morph);
                template.Parameters.Add(MakeMorphParameter(morph));
                fresh.Add(morph);
                added++;
            }

            // 名录变了，索引必须重建：否则新参数的 Index 与快照槽位会错位
            if (added > 0)
            {
                template.EnsureIndexed();
                LinkMirrorPairs(template, fresh);
                UnlinkOrphanSides(template, fresh);
            }

            return added;
        }

        /// <summary>
        /// 成对的 morph（同名只差 _L/_R）互指 PairId。
        /// 不做这一步，新回填的滑杆带着 Side 却没有对岸：校验会报错，
        /// 而且「联动」对刚导入的这一批永远无效。
        /// </summary>
        private static void LinkMirrorPairs(MetaHumanTemplate template, ICollection<string> morphNames)
        {
            foreach (string morph in morphNames)
            {
                if (morph.EndsWith("_L", StringComparison.Ordinal))
                {
                    string sibling = morph.Substring(0, morph.Length - 2) + "_R";
                    // 对岸可能在本批之前就在名录里（基础模板那 37 项就是这么来的），
                    // 所以按参数查，而不是只在这批新名里找——只找新名会漏配。
                    MetaHumanParameter left = template.Find("morph." + morph);
                    MetaHumanParameter right = template.Find("morph." + sibling);
                    if (left != null && right != null)
                    {
                        left.PairId = right.Id;
                        right.PairId = left.Id;
                    }
                }
            }
        }

        /// <summary>
        /// 侧向标记落单的那些降回 None。
        ///
        /// 为什么必须降：Side 是对「存在镜像联动」的声明，而资产名录由 DCC 决定——
        /// `eyeLeft_lod0_mesh__eye_pupilNarrow_L` 的右眼那份叫 `eyeRight_..._R`（前缀也换了），
        /// `McornerDepress_NSwrinkle_R` 这种复合名压根没有对称兄弟。
        /// 留着 Side 又没有对岸，校验直接拦下整份模板（「回填→另存」在真资产上变成死路），
        /// 而滑杆本身是无辜的：它照样能驱动那一个形状。
        /// </summary>
        private static void UnlinkOrphanSides(MetaHumanTemplate template, ICollection<string> morphNames)
        {
            foreach (string morph in morphNames)
            {
                MetaHumanParameter parameter = template.Find("morph." + morph);
                if (parameter == null || parameter.Side == MetaHumanSide.None)
                {
                    continue;
                }

                if (string.IsNullOrEmpty(parameter.PairId) || template.Find(parameter.PairId) == null)
                {
                    parameter.Side = MetaHumanSide.None;
                }
            }
        }

        public static MetaHumanParameter MakeMorphParameter(string morphName)
        {
            MetaHumanParameter parameter = new MetaHumanParameter();
            parameter.Id = "morph." + morphName;
            parameter.DisplayName = morphName;
            parameter.Region = RegionForMorph(morphName);
            parameter.Kind = MetaHumanParamKind.HeadShape;
            parameter.Side = SideForMorph(morphName);
            parameter.Min = -1f;
            parameter.Max = 1f;
            parameter.Default = 0f;
            parameter.Step = 0.01f;
            parameter.Effects.Add(MetaHumanEffect.Morph(morphName, 1f));
            return parameter;
        }

        public static MetaHumanSide SideForMorph(string morphName)
        {
            if (morphName.EndsWith("_L", StringComparison.Ordinal))
            {
                return MetaHumanSide.Left;
            }

            if (morphName.EndsWith("_R", StringComparison.Ordinal))
            {
                return MetaHumanSide.Right;
            }

            return MetaHumanSide.None;
        }

        public static MetaHumanRegion RegionForMorph(string morphName)
        {
            string low = morphName.ToLowerInvariant();
            if (low.Contains("blink") || low.Contains("eyelid") || low.Contains("squint") || low.Contains("eye"))
            {
                return MetaHumanRegion.Eye;
            }

            if (low.Contains("brow"))
            {
                return MetaHumanRegion.Brow;
            }

            if (low.Contains("nose") || low.Contains("wrinkle"))
            {
                return MetaHumanRegion.Nose;
            }

            if (low.Contains("lip") || low.Contains("mouth") || low.Contains("smile") || low.Contains("purse"))
            {
                return MetaHumanRegion.Lip;
            }

            if (low.Contains("cheek"))
            {
                return MetaHumanRegion.Cheek;
            }

            if (low.Contains("chin"))
            {
                return MetaHumanRegion.Chin;
            }

            if (low.Contains("jaw"))
            {
                return MetaHumanRegion.Jaw;
            }

            if (low.Contains("ear"))
            {
                return MetaHumanRegion.Ear;
            }

            if (low.Contains("neck"))
            {
                return MetaHumanRegion.Neck;
            }

            return MetaHumanRegion.Head;
        }

        /// <summary>把报告写成可读文本，编辑器窗口与日志共用一份格式。</summary>
        public static string Format(string title, MetaHumanBindingReport report)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append(title).Append("：").Append(report.ToString()).AppendLine();
            AppendSection(sb, "缺失槽位", report.MissingSlots);
            AppendSection(sb, "缺失 morph", report.MissingMorphs);
            AppendSection(sb, "缺失骨骼目标", report.MissingBoneTargets);
            AppendSection(sb, "备注", report.Notes);
            sb.Append("命中槽位 ").Append(report.MatchedSlots.Count)
              .Append("，命中 morph ").Append(report.MatchedMorphs.Count)
              .Append("，资产里未被模板引用的骨骼 ").Append(report.UnusedAssetNodes.Count);
            return sb.ToString();
        }

        private static void AppendSection(System.Text.StringBuilder sb, string label, List<string> items)
        {
            if (items.Count == 0)
            {
                return;
            }

            sb.Append("  ").Append(label).Append("(").Append(items.Count).Append("): ");
            for (int i = 0; i < items.Count && i < 12; i++)
            {
                sb.Append(items[i]).Append(i + 1 < items.Count && i < 11 ? " / " : "");
            }

            if (items.Count > 12)
            {
                sb.Append(" …");
            }

            sb.AppendLine();
        }
    }
}
