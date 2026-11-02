using System;
using System.Collections.Generic;
using FlaxEngine;
using MetaHuman;
using MetaHuman.Runtime;

namespace MetaHumanEditor
{
    /// <summary>
    /// 骨骼动画自动绑定：让一份在别的骨架上做的动画能在目标数字人骨架上播。
    /// 走的是引擎原生通道 SkinnedModel.SkeletonRetargets（节点名映射表），
    /// 不是自己逐帧搬姿势 —— 后者在动画评估之后还要重刷，成本高且与动画图打架。
    /// </summary>
    public static class MetaHumanAnimationBinder
    {
        public class Result
        {
            public readonly List<string> Mapped = new List<string>();
            public readonly List<string> Unmapped = new List<string>();
            public readonly List<string> Notes = new List<string>();
            public int SourceNodeCount;

            // 各档命中数要透到这一层：工装回执读的是 Result，而不是内层的匹配结果对象。
            // 不透出来的话，"这批映射是同名命中还是别名命中"就只能靠手里的部分名单去推算，
            // 名单覆盖不全就会造出假矛盾（离线夹具只带来源骨架 1109 个节点名里的 52 个）。
            public int ExactMatches;
            public int CaseInsensitiveMatches;
            public int MirrorMatches;
            public int AliasMatches;
            public int SlotMatches;
        }

        /// <summary>
        /// 生成映射条目：key 是目标骨架的节点名，value 是来源骨架的节点名
        /// （引擎注释写明「maps this skeleton node name to other skeleton node」）。
        /// 匹配规则本身在纯核的 MetaHumanRetarget 里，这一层只负责向引擎取名字与写回资产。
        /// </summary>
        public static SkinnedModel.SkeletonRetarget BuildEntry(SkinnedModel target, SkinnedModel source, MetaHumanTemplate template, Result result)
        {
            List<string> sourceNames = MetaHumanRigBinder.AssetBoneNames(source);
            List<string> targetNames = MetaHumanRigBinder.AssetBoneNames(target);

            MetaHumanRetargetResult inner = new MetaHumanRetargetResult();
            Dictionary<string, string> mapping = MetaHumanRetarget.BuildMapping(targetNames, sourceNames, template, inner);

            result.SourceNodeCount = inner.SourceNodeCount;
            result.ExactMatches = inner.ExactMatches;
            result.CaseInsensitiveMatches = inner.CaseInsensitiveMatches;
            result.MirrorMatches = inner.MirrorMatches;
            result.AliasMatches = inner.AliasMatches;
            result.SlotMatches = inner.SlotMatches;
            foreach (string note in inner.Notes)
            {
                result.Notes.Add(note);
            }

            foreach (string mapped in inner.Mapped)
            {
                result.Mapped.Add(mapped);
            }

            foreach (string unmapped in inner.Unmapped)
            {
                result.Unmapped.Add(unmapped);
            }

            SkinnedModel.SkeletonRetarget entry = new SkinnedModel.SkeletonRetarget();
            Guid sourceId = MetaHuman.Runtime.MetaHumanAssetUtil.IdOf(source);
            entry.SourceAsset = sourceId;
            entry.SkeletonAsset = sourceId;
            entry.NodesMapping = mapping;

            return entry;
        }

        /// <summary>把条目写回目标资产（保留既有条目，按来源资产覆盖同名项）。</summary>
        public static bool Apply(SkinnedModel target, SkinnedModel source, MetaHumanTemplate template, Result result)
        {
            if (target == null || source == null)
            {
                result.Notes.Add("目标或来源资产为空");
                return false;
            }

            SkinnedModel.SkeletonRetarget entry = BuildEntry(target, source, template, result);

            List<SkinnedModel.SkeletonRetarget> merged = new List<SkinnedModel.SkeletonRetarget>();
            foreach (SkinnedModel.SkeletonRetarget existing in target.SkeletonRetargets)
            {
                if (existing.SourceAsset == entry.SourceAsset)
                {
                    continue;
                }

                merged.Add(existing);
            }

            merged.Add(entry);
            target.SkeletonRetargets = merged.ToArray();

            // 只改内存不够：retarget 条目随编辑器关闭一起消失，而报告上一句"已写入"照样是绿的。
            // 注意 Flax 这里返回值是 true=存不了（与直觉相反），判错方向就等于把失败写成成功。
            if (target.Save(false, string.Empty))
            {
                result.Notes.Add("条目已算出但资产落盘失败（Save 返回 true=存不了），关掉编辑器就会丢");
                return false;
            }

            result.Notes.Add("已保存到资产：" + MetaHuman.Runtime.MetaHumanAssetUtil.DisplayName(target));
            return true;
        }

        /// <summary>
        /// 为一批动画资产补「动画 → 目标骨架」条目。
        /// 引擎在 AnimGroup 里是按**动画资产 ID** 查 SkeletonRetargets 的（SkinnedModel.cpp GetSkeletonMapping(anim)），
        /// 再用 SkeletonAsset 的节点名去对动画通道名；只写骨架模型 ID 的条目在播放时永远命中不了。
        /// </summary>
        public static int ApplyAnimations(SkinnedModel target, SkinnedModel sourceSkeleton, Animation[] animations, Result result)
        {
            if (target == null || sourceSkeleton == null)
            {
                result.Notes.Add("目标骨架或来源骨架为空，动画条目未写");
                return 0;
            }

            Guid skeletonId = MetaHuman.Runtime.MetaHumanAssetUtil.IdOf(sourceSkeleton);
            List<SkinnedModel.SkeletonRetarget> merged = new List<SkinnedModel.SkeletonRetarget>();
            foreach (SkinnedModel.SkeletonRetarget existing in target.SkeletonRetargets)
            {
                merged.Add(existing);
            }

            int added = 0;
            foreach (Animation anim in animations)
            {
                if (anim == null)
                {
                    continue;
                }

                Guid animId = MetaHuman.Runtime.MetaHumanAssetUtil.IdOf(anim);
                for (int i = merged.Count - 1; i >= 0; i--)
                {
                    if (merged[i].SourceAsset == animId)
                    {
                        merged.RemoveAt(i);
                    }
                }

                SkinnedModel.SkeletonRetarget entry = new SkinnedModel.SkeletonRetarget();
                entry.SourceAsset = animId;
                entry.SkeletonAsset = skeletonId;
                entry.NodesMapping = new Dictionary<string, string>();
                merged.Add(entry);
                added++;
            }

            target.SkeletonRetargets = merged.ToArray();

            if (target.Save(false, string.Empty))
            {
                result.Notes.Add("动画条目已算出但资产落盘失败（Save 返回 true=存不了）");
                return -1;
            }

            result.Notes.Add("已为 " + added + " 份动画写入动画源条目并保存");
            return added;
        }

        public static string Format(string targetName, string sourceName, Result result)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append("动画绑定 ").Append(targetName).Append(" ← ").Append(sourceName)
              .Append("：映射 ").Append(result.Mapped.Count)
              .Append(" 个节点，未映射 ").Append(result.Unmapped.Count)
              .Append(" 个（来源共 ").Append(result.SourceNodeCount).Append("）");
            foreach (string note in result.Notes)
            {
                sb.AppendLine().Append("  ").Append(note);
            }

            return sb.ToString();
        }
    }
}
