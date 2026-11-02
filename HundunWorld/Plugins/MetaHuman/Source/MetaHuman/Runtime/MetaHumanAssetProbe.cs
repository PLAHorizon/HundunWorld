using System.Collections.Generic;
using FlaxEngine;

namespace MetaHuman
{
    /// <summary>
    /// 从一具 <see cref="SkinnedModel"/> 上取「模板声明的名字里哪些真的存在」。
    ///
    /// 为什么只能在引擎里做：字节扫描 .flax 看不见骨架节点名——判别实验是
    /// `skm_uefn_mannequin.flax` 的节点名早已由引擎 API 实测为 pelvis/spine_01/…，
    /// 而扫描器对这些名字 ASCII 与 UTF-16 全部 0 命中（文档 §34）。
    ///
    /// 为什么单独成一份：引擎自检的「头部承载量」组和 `MetaHumanCharacter` 的装载期量测
    /// 都需要同一件事（逐名 `FindNode` + 读 `BlendShapes`）。两处各写一遍就会各自漂，
    /// 而漂了的后果是"资产到底能不能捏脸"这个数在两个地方对不上。
    /// 计数规则本身仍然只在 `MetaHumanBinding.Bind` 里，这里只负责取名字。
    /// </summary>
    public static class MetaHumanAssetProbe
    {
        /// <summary>
        /// 逐名探测而不是枚举整副骨架：`FindNode` 是生产代码里已经在用、签名已核实的 API，
        /// 而模板只关心它自己声明的那 363 个骨骼目标与 37 个 morph 目标。
        /// 代价要说清楚：`Bind` 报告里的「资产未引用骨骼」因此是按模板目标口径算的，不是整副骨架的全集。
        ///
        /// 返回**经别名对齐**的条数：写回端本来就认别名（文档 §44.1），
        /// 所以承载量若不认，身体那 12 条明明能对齐的目标会被报成"写不出来" —— 那是把一个真能用的
        /// 功能读数弄成 0。同时把"直接命中"和"经别名"分开计，免得两种不同的事长得一样。
        /// </summary>
        public static int CollectNames(SkinnedModel asset,
                                       MetaHumanTemplate template,
                                       out HashSet<string> bones,
                                       out HashSet<string> morphs)
        {
            bones = new HashSet<string>();
            morphs = new HashSet<string>();
            int aliasHits = 0;

            if (asset == null || template == null)
            {
                return 0;
            }

            foreach (string target in template.BoneTargets())
            {
                if (asset.FindNode(target) >= 0)
                {
                    bones.Add(target);
                    continue;
                }

                string[] aliases = MetaHumanBoneAliases.AliasesOf(target);
                for (int i = 0; i < aliases.Length; i++)
                {
                    if (asset.FindNode(aliases[i]) >= 0)
                    {
                        bones.Add(target);
                        aliasHits++;
                        break;
                    }
                }
            }

            string[] shapes = asset.BlendShapes;
            if (shapes != null)
            {
                for (int i = 0; i < shapes.Length; i++)
                {
                    morphs.Add(shapes[i]);
                }
            }

            return aliasHits;
        }

        public static MetaHumanBindingReport Measure(SkinnedModel asset,
                                                     MetaHumanTemplate template,
                                                     out HashSet<string> bones,
                                                     out HashSet<string> morphs)
        {
            CollectNames(asset, template, out bones, out morphs);
            return MetaHumanBinding.Bind(template, bones, morphs);
        }

        public static MetaHumanBindingReport Measure(SkinnedModel asset, MetaHumanTemplate template)
        {
            HashSet<string> bones;
            HashSet<string> morphs;
            return Measure(asset, template, out bones, out morphs);
        }
    }
}
