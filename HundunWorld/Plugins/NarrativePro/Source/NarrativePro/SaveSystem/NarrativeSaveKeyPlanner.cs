// 模块关系说明：
// 本文件属于 NarrativePro.SaveSystem（UE5 NarrativeSaveSystem 移植）。
//
// 为什么需要它：「一个 Actor 作用域里哪些对象要归档、各自的稳定键是什么」由
// NarrativeSavableActorResolver 的两个循环决定，而这两个循环骑在 Actor.GetScripts() 上
// （FlaxEngine.Script，无引擎环境既拿不到也造不出），所以进程外一直打不到：
//   ① 序号必须在*每一个*被枚举到的对象上推进（含被排除/被跳过的），少推一次就会让
//      同类多实例的键相对读档侧挪位 —— 读档时把字节写进错误的同类实例（本项目踩过的缺陷类）；
//   ② 纯状态提供者不得重复入组件档，归档通道自身（NarrativeSaveStateProviderComponent）
//      不得作为独立状态源；
//   ③ 稳定 GUID 的取值次序必须是「真·持久化稳定 id（INarrativeStableActor）优先」，
//      其次才是按 UE5 方法名的反射探测与按字段名的猜测 —— 名字启发式一旦盖过真 id，
//      改名/加字段就会让同一个 Actor 换一把 GUID，整条存档记录从此孤立。
//
// 本文件把这些裁决抽成纯函数（不引用 FlaxEngine 类型）：实时侧只负责把每个候选对象折成
// SaveCandidateView（类型判定 + 稳定键），裁决与共用的键拼装（NarrativeSaveComponentKeys.NextKey）
// 都住在这里；harness 用纯 [Serializable] 类喂同一份实现做端到端契约断言。

using System;
using System.Collections.Generic;
using System.Reflection;

namespace NarrativePro.SaveSystem
{
    /// <summary>一个参与归档裁决的候选对象的纯数据视图。</summary>
    public sealed class SaveCandidateView
    {
        /// <summary>被归档的对象（实时侧是 Script/Actor，纯层只比引用）。</summary>
        public object ObjectKey;

        /// <summary>该对象的基键（<see cref="NarrativeSaveStateArchive.GetKeyForObject"/>）。</summary>
        public string BaseKey;

        /// <summary>实现了 INarrativeSaveStateProvider（自绘状态字节）。</summary>
        public bool bIsProvider;

        /// <summary>实现了 INarrativeSavableComponent。</summary>
        public bool bIsSavableComponent;

        /// <summary>带显式存档标记（[NarrativeSaveKey] 一类）。</summary>
        public bool bHasExplicitMarkers;

        /// <summary>有可反射归档的状态。</summary>
        public bool bHasArchivableState;

        /// <summary>就是该 Actor 绑定的宿主对象（其状态写在记录 ByteData 里）。</summary>
        public bool bIsBindingTarget;

        /// <summary>是归档通道组件自身（NarrativeSaveStateProviderComponent）。</summary>
        public bool bIsArchiveChannel;

        /// <summary>被调用方显式排除（例如正在被询问的 provider 自身）。</summary>
        public bool bExcluded;
    }

    /// <summary>稳定 GUID 的来源（次序即优先级）。</summary>
    public enum EStableGuidSource
    {
        /// <summary>什么都没找到 —— 调用方按「无稳定 GUID」走动态生成分支。</summary>
        None,

        /// <summary>真·持久化稳定 id：对象实现了 <see cref="INarrativeStableActor"/>。</summary>
        StableActorInterface,

        /// <summary>按 UE5 方法名反射到的 <c>GetActorGUID()</c>。</summary>
        ReflectedMethod,

        /// <summary>按字段/属性名猜出来的承载成员（最弱的一档，名字启发式）。</summary>
        CarriedMember,
    }

    /// <summary>
    /// 归档键规划的纯函数层（不引用 FlaxEngine / Script 类型，可无引擎运行）。
    /// </summary>
    public static class NarrativeSaveKeyPlanner
    {
        /// <summary>
        /// Actor 状态段的候选裁决：按枚举顺序为*每一个*候选推进同类内序号，然后再过滤。
        /// 对应 NarrativeSavableActorResolver.AddStateTargets 的脚本循环。
        /// </summary>
        /// <param name="candidates">按 GetScripts(actor) 顺序折出来的候选视图。</param>
        /// <param name="outKeysPerCandidate">输出：与输入同长的键数组（供断言「序号确实逐步推进」）。</param>
        public static List<NarrativeStateTarget> PlanStateTargets(
            IReadOnlyList<SaveCandidateView> candidates,
            out string[] outKeysPerCandidate)
        {
            var targets = new List<NarrativeStateTarget>();
            outKeysPerCandidate = Array.Empty<string>();
            if (candidates == null || candidates.Count == 0) return targets;

            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            outKeysPerCandidate = new string[candidates.Count];

            for (int i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                if (candidate == null) continue;

                // 关键不变式：先推进序号再决定是否入档 —— 跳过计数会让同类多实例的键挪位。
                string positionalKey = NarrativeSaveComponentKeys.NextKey(candidate.BaseKey, counts);
                outKeysPerCandidate[i] = positionalKey;

                if (candidate.bExcluded) continue;
                if (candidate.bIsArchiveChannel) continue; // 归档通道自身不作为独立状态源

                bool hasState = candidate.bIsBindingTarget || candidate.bIsProvider ||
                                candidate.bIsSavableComponent || candidate.bHasArchivableState;
                if (!hasState) continue;

                targets.Add(new NarrativeStateTarget(positionalKey, candidate.ObjectKey));
            }

            return targets;
        }

        /// <summary>
        /// 组件段的候选裁决（对应 NarrativeSavableActorResolver.CollectSavableComponents 的脚本循环）。
        /// 纯状态提供者由 Actor 状态段统一捕获，因此在这一段被排除，避免同一对象两份字节。
        /// </summary>
        public static List<KeyValuePair<string, object>> PlanComponents(
            IReadOnlyList<SaveCandidateView> candidates,
            bool includeAllScripts)
        {
            var result = new List<KeyValuePair<string, object>>();
            if (candidates == null || candidates.Count == 0) return result;

            var counts = new Dictionary<string, int>(StringComparer.Ordinal);

            for (int i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                if (candidate == null) continue;

                string positionalKey = NarrativeSaveComponentKeys.NextKey(candidate.BaseKey, counts);

                if (candidate.bIsBindingTarget) continue;      // 宿主对象的状态写在记录 ByteData 中
                if (candidate.bIsProvider && !candidate.bIsSavableComponent) continue;

                bool archiveBySetting = includeAllScripts && candidate.bHasArchivableState;
                if (!candidate.bIsSavableComponent && !candidate.bHasExplicitMarkers && !archiveBySetting) continue;

                result.Add(new KeyValuePair<string, object>(positionalKey, candidate.ObjectKey));
            }

            return result;
        }

        /// <summary>
        /// 同一 Actor 作用域内的键唯一性检查（同类多实例靠序号区分；显式 [NarrativeSaveKey]
        /// 若被两个对象同时声明，位置化键无法再保证唯一，必须能在纯层判出来）。
        /// </summary>
        public static bool HasDuplicates(IReadOnlyList<string> keys)
        {
            if (keys == null || keys.Count < 2) return false;

            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < keys.Count; i++)
            {
                if (!seen.Add(keys[i] ?? string.Empty)) return true;
            }
            return false;
        }

        /// <summary>
        /// 稳定 GUID 的发现次序：真·持久化稳定 id &gt; 反射方法名 &gt; 字段/属性名启发式。
        /// 对应 <see cref="NarrativeSavableActorBinding.GetActorGUID"/>，实时侧与 harness 共用。
        /// </summary>
        /// <param name="target">宿主对象（Script 或 Actor）。</param>
        /// <param name="outSource">输出命中的是哪一档。</param>
        /// <returns>发现的 GUID；没有任何来源时返回 <see cref="Guid.Empty"/>。</returns>
        public static Guid DiscoverStableGuid(object target, out EStableGuidSource outSource)
        {
            outSource = EStableGuidSource.None;
            if (target == null) return Guid.Empty;

            // ① 真·持久化稳定 id：实现了插件接口就用它，绝不落到名字启发式。
            if (target is INarrativeStableActor stable)
            {
                var interfaceGuid = stable.GetActorGUID();
                outSource = EStableGuidSource.StableActorInterface;
                return interfaceGuid;
            }

            // ② UE5 同名方法（影子实现）。
            var methodValue = NarrativeReflect.InvokeGuid(target, "GetActorGUID");
            if (methodValue.HasValue)
            {
                outSource = EStableGuidSource.ReflectedMethod;
                return methodValue.Value;
            }

            // ③ 最弱的一档：按常见承载成员名去猜。读不到即 Guid.Empty（调用方走动态生成分支）。
            var memberValue = NarrativeReflect.ReadGuidMember(target, NarrativeSavableActorBinding.GuidMemberNames);
            if (memberValue.HasValue)
            {
                outSource = EStableGuidSource.CarriedMember;
                return memberValue.Value;
            }

            return Guid.Empty;
        }
    }
}
