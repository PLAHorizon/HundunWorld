using System;
using System.Collections.Generic;
using FlaxEngine;
using MetaHuman;

namespace MetaHuman.Runtime
{
    /// <summary>
    /// 模板与真实 SkinnedModel 之间的对照器：这一层只负责向引擎要名字
    /// （骨骼清单、blendshape 清单、回填用的资产清单），判定全在纯核的 <see cref="MetaHumanBinding"/>。
    ///
    /// 拆成两层是因为「改了数值没反应」这类缺陷都出在名字对不上，
    /// 而名字核对本身不需要引擎——放纯核里才能拿真实 .dna 的名字清单离线跑断言，
    /// 而不是只有一句「它编译过了」。
    /// </summary>
    public static class MetaHumanRigBinder
    {
        /// <summary>列出资产的真实骨骼名。</summary>
        public static List<string> AssetBoneNames(SkinnedModel asset)
        {
            List<string> names = new List<string>();
            if (asset == null)
            {
                return names;
            }

            SkeletonNode[] nodes = asset.Nodes;
            for (int i = 0; i < nodes.Length; i++)
            {
                names.Add(nodes[i].Name);
            }

            return names;
        }

        public static HashSet<string> AssetMorphNames(SkinnedModel asset)
        {
            HashSet<string> set = new HashSet<string>(StringComparer.Ordinal);
            if (asset == null)
            {
                return set;
            }

            string[] blendShapes = asset.BlendShapes;
            for (int i = 0; i < blendShapes.Length; i++)
            {
                set.Add(blendShapes[i]);
            }

            return set;
        }

        /// <summary>把若干资产的骨骼名与 morph 名并成两份清单（一个角色由多个网格共同组成）。</summary>
        public static void CollectNames(SkinnedModel[] assets, out HashSet<string> bones, out HashSet<string> morphs, out int usableAssets)
        {
            bones = new HashSet<string>(StringComparer.Ordinal);
            morphs = new HashSet<string>(StringComparer.Ordinal);
            usableAssets = 0;
            if (assets == null)
            {
                return;
            }

            for (int i = 0; i < assets.Length; i++)
            {
                if (assets[i] == null)
                {
                    continue;
                }

                usableAssets++;
                foreach (string bone in AssetBoneNames(assets[i]))
                {
                    bones.Add(bone);
                }

                foreach (string morph in AssetMorphNames(assets[i]))
                {
                    morphs.Add(morph);
                }
            }
        }

        /// <summary>用一个（或多个）模型资产核对模板。</summary>
        public static MetaHumanBindingReport Bind(MetaHumanTemplate template, SkinnedModel[] assets)
        {
            HashSet<string> bones;
            HashSet<string> morphs;
            int usable;
            CollectNames(assets, out bones, out morphs, out usable);

            MetaHumanBindingReport report = MetaHumanBinding.Bind(template, bones, morphs, usable);

            // 具体是哪份资产缺 blendshape 只有这一层知道，点名到资产名才有用
            if (assets != null && template != null && template.RequiresBlendShapeImport && morphs.Count == 0)
            {
                for (int i = 0; i < assets.Length; i++)
                {
                    if (assets[i] != null)
                    {
                        report.Notes.Add(MetaHumanAssetUtil.DisplayName(assets[i]) + " 无 blendshape（导入未开 ImportBlendShapes？）");
                    }
                }
            }

            return report;
        }

        /// <summary>
        /// 把资产里真实存在的 morph 名录回填进模板，并为每个新出现的 morph 生成一根形状滑杆。
        /// 编辑器「扫描资产」用的就是它：模板因此始终描述手上这份资产，而不是一份想象中的资产。
        /// </summary>
        public static int ImportMissingMorphs(MetaHumanTemplate template, SkinnedModel[] assets)
        {
            HashSet<string> bones;
            HashSet<string> morphs;
            int usable;
            CollectNames(assets, out bones, out morphs, out usable);
            return MetaHumanBinding.ImportMissingMorphs(template, morphs);
        }

        /// <summary>把报告写成可读文本，编辑器窗口与日志共用一份格式。</summary>
        public static string Format(string title, MetaHumanBindingReport report)
        {
            return MetaHumanBinding.Format(title, report);
        }
    }
}
