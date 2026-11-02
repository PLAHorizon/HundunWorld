// 模块关系说明：
// 本文件属于 NarrativePro.SaveSystem（UE5 NarrativeSaveSystem 移植）。
//
// 为什么需要它：组件归档键 "类型全名#同类内序号" 在四个地方被拼出来、在一个地方被解析回去：
//   写侧：NarrativeSavableActorResolver.AddStateTargets / CollectSavableComponents、
//         NarrativeSaveSubsystem.CaptureActorState；
//   读侧：NarrativeSaveStateArchive.ResolveKeyInActor（拆键 + 按同类内序号选中）。
// 两侧一旦拼法/计数规则漂移，读档就会把字节写进*错误的*同类实例（本项目实际踩过的缺陷类）。
// 因此把“构造位置化键 / 拆解位置化键 / 按序号在有序候选里选中”收敛为无引擎依赖的纯函数，
// 写读两侧都只经由这里，进程外 harness 也直接驱动同一份实现。

using System;
using System.Collections.Generic;

namespace NarrativePro.SaveSystem
{
    /// <summary>
    /// 组件归档键的纯函数层（不引用 FlaxEngine 类型，可无引擎运行）。
    /// </summary>
    public static class NarrativeSaveComponentKeys
    {
        /// <summary>同类多实例的序号分隔符（"类型全名#1"）。</summary>
        public const char OrdinalSeparator = '#';

        /// <summary>
        /// 由基键与同类内出现次数得到位置化键：首次出现不带后缀，之后追加 <c>#序号</c>。
        /// 与 <see cref="SplitKey"/> 严格互逆。
        /// </summary>
        public static string MakePositionalKey(string baseKey, int occurrence)
        {
            if (occurrence <= 0) return baseKey ?? string.Empty;
            return baseKey + OrdinalSeparator + occurrence;
        }

        /// <summary>
        /// 按枚举顺序推进同名计数并返回该对象的位置化键。
        /// 调用方必须对*每一个*被枚举到的对象都调用一次（不能只给入档对象计数），
        /// 否则序号会相对读档侧的计数发生挪位。
        /// </summary>
        public static string NextKey(string baseKey, IDictionary<string, int> occurrenceCounts)
        {
            if (occurrenceCounts == null) return baseKey ?? string.Empty;
            occurrenceCounts.TryGetValue(baseKey, out int occurrence);
            occurrenceCounts[baseKey] = occurrence + 1;
            return MakePositionalKey(baseKey, occurrence);
        }

        /// <summary>去掉键尾部的 <c>#索引</c> 位置化后缀（无后缀或 '#' 在首位时原样返回）。</summary>
        public static string StripOrdinalSuffix(string key)
        {
            if (string.IsNullOrEmpty(key)) return key;
            int hashPos = key.IndexOf(OrdinalSeparator);
            return hashPos > 0 ? key.Substring(0, hashPos) : key;
        }

        /// <summary>
        /// 拆解位置化键为「基键 + 同类内序号」。无后缀时序号为 0；
        /// 后缀不是十进制数字时序号同样为 0（与旧解析行为一致）。
        /// </summary>
        public static void SplitKey(string key, out string baseKey, out int ordinal)
        {
            baseKey = key;
            ordinal = 0;
            if (string.IsNullOrEmpty(key)) return;

            int hashPos = key.IndexOf(OrdinalSeparator);
            if (hashPos <= 0) return;

            baseKey = key.Substring(0, hashPos);
            int.TryParse(key.Substring(hashPos + 1), out ordinal);
        }

        /// <summary>
        /// 在有序基键表里找第 <paramref name="ordinal"/> 个等于 <paramref name="baseKey"/> 的项，
        /// 命中返回其下标，否则返回 -1。null 项不参与匹配也不计数（对应脚本枚举里的空槽）。
        /// </summary>
        public static int IndexOfOrdinal(IReadOnlyList<string> orderedBaseKeys, string baseKey, int ordinal)
        {
            if (orderedBaseKeys == null || string.IsNullOrEmpty(baseKey)) return -1;

            int matchIndex = 0;
            for (int i = 0; i < orderedBaseKeys.Count; i++)
            {
                if (!string.Equals(orderedBaseKeys[i], baseKey, StringComparison.Ordinal)) continue;
                if (matchIndex == ordinal) return i;
                matchIndex++;
            }
            return -1;
        }

        /// <summary>
        /// 在有序显式键表里找第一个等于 <paramref name="baseKey"/> 的项（空键不参与匹配）。
        /// 显式键（[NarrativeSaveKey] / SaveComponentKey）与脚本枚举顺序无关，读档优先级最高。
        /// </summary>
        public static int IndexOfExplicitKey(IReadOnlyList<string> orderedExplicitKeys, string baseKey)
        {
            if (orderedExplicitKeys == null || string.IsNullOrEmpty(baseKey)) return -1;

            for (int i = 0; i < orderedExplicitKeys.Count; i++)
            {
                if (string.IsNullOrEmpty(orderedExplicitKeys[i])) continue;
                if (string.Equals(orderedExplicitKeys[i], baseKey, StringComparison.Ordinal)) return i;
            }
            return -1;
        }

        /// <summary>在有序键表里找第一个完全等于 <paramref name="key"/> 的项，命中返回下标，否则 -1。</summary>
        public static int IndexOfKey(IReadOnlyList<string> orderedKeys, string key)
        {
            if (orderedKeys == null || string.IsNullOrEmpty(key)) return -1;

            for (int i = 0; i < orderedKeys.Count; i++)
            {
                if (string.Equals(orderedKeys[i], key, StringComparison.Ordinal)) return i;
            }
            return -1;
        }
    }
}
