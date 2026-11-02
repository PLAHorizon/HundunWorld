// 模块关系说明：
// 本文件属于 NarrativePro.Tales（UE5 NarrativeArsenal 对话状态机移植）。
//
// 为什么需要它：回复链与对话行的选择逻辑长在 UDialogueNode / UDialogueNode_NPC 上，
// 而它们的条件过滤必须经由 TalesComponent（FlaxEngine.Script，无引擎环境 new 不出来），
// 于是「条件挡住的回复要被跳过」「回复成环时必须截断而不是无限循环」这两条
// 只能靠读代码相信。UE5 的 GetReplyChain（DialogueSM.cpp:377-438）里根本没有环保护：
// 它只排除「回到起点 this」，A→B→A 这种互指环会一直循环下去 ——
// 端口补了 visited 集合（有意的加固），但没有断言就无法证明它真的截断。
//
// 本文件把选择规则抽成纯泛型函数（不依赖 FlaxEngine / Script 类型）：
//   - 节点集合与「条件是否通过」由调用方喂进来，链的走法、环的截断、空集回退都在纯层；
//   - 行选择（候选顺序、条件过滤、随机下标）同样收敛在纯层，随机源由调用方注入，
//     因此给定同一个种子就能复现同一条行序。
// 实时通路 DialogueNode_NPC.GetReplyChain / DialogueNode.GetFirstValidNPCReply /
// DialogueNode.GetPlayerReplies / DialogueNode.GetRandomLine 全部经由这里。

using System;
using System.Collections.Generic;

namespace NarrativePro.Tales
{
    /// <summary>一轮回复链选择的结果。</summary>
    public sealed class DialogueChainResult<T>
    {
        /// <summary>入链节点（第一个元素恒为起点，对应 UE5:382 先 Add(CurrentNode)）。</summary>
        public readonly List<T> Chain = new List<T>();

        /// <summary>
        /// 因成环而被丢弃的候选节点（null 表示没有检测到环）。
        /// 端口在检测到环时*不*把该节点入链并立即结束走链。
        /// </summary>
        public T CycleNode;

        /// <summary>是否检测到环。</summary>
        public bool bCycleDetected => CycleNode != null;
    }

    /// <summary>
    /// 对话回复链 / 对话行选择的纯函数层（不引用 FlaxEngine / Script 类型，可无引擎运行）。
    /// </summary>
    public static class NarrativeDialogueSelection
    {
        /// <summary>
        /// 走一条 NPC 连续回复链。对应 UE5 <c>UDialogueNode_NPC::GetReplyChain</c>（DialogueSM.cpp:377-438）。
        /// <para>
        /// 每一跳：按调用方给定的顺序（UE5 用图位置排序，竖直走线时按 X 否则按 Y）取
        /// 第一个「不是起点且条件通过」的回复；找不到就结束。
        /// 端口的环保护：已入链的节点再次命中时丢弃并立即结束走链（UE5 无此保护，会无限循环）。
        /// </para>
        /// </summary>
        /// <param name="start">起点节点（不得为 null，null 时返回空链而不是崩溃）。</param>
        /// <param name="candidatesFor">取某节点的全部 NPC 回复（必须已按图位置排序）。</param>
        /// <param name="conditionPasses">条件是否通过；null 表示不做条件过滤（编辑器预览路径）。</param>
        public static DialogueChainResult<T> BuildReplyChain<T>(
            T start,
            Func<T, List<T>> candidatesFor,
            Func<T, bool> conditionPasses) where T : class
        {
            var result = new DialogueChainResult<T>();
            if (start == null) return result;

            result.Chain.Add(start);

            // 引用语义的 visited 集：DialogueNode 族没有重写 Equals/GetHashCode，
            // 但下游类型可能重写，故显式按引用比较（与 UE5 的指针比较一致）。
            var visited = new HashSet<T>(System.Collections.Generic.ReferenceEqualityComparer.Instance) { start };

            var current = start;
            while (current != null)
            {
                var candidates = candidatesFor != null ? candidatesFor(current) : null;

                T next = null;
                bool bCycleDetected = false;

                if (candidates != null)
                {
                    foreach (var reply in candidates)
                    {
                        // UE5:424 `Reply != this` —— 起点自身永远不能作为下一跳（端口额外挡掉 null，
                        // UE5 对 null 元素会直接解引用崩溃）。
                        if (reply == null || ReferenceEquals(reply, start)) continue;

                        if (conditionPasses != null && !conditionPasses(reply)) continue;

                        if (!visited.Add(reply))
                        {
                            // 环：丢弃该候选并结束走链（端口加固，UE5 会在此无限循环）。
                            result.CycleNode = reply;
                            bCycleDetected = true;
                            break;
                        }

                        next = reply;
                        break; // UE5:427 「只用第一个条件通过的回复」
                    }
                }

                if (bCycleDetected || next == null) break;

                result.Chain.Add(next);
                current = next;
            }

            return result;
        }

        /// <summary>
        /// 取第一个条件通过的候选。对应 UE5 <c>UDialogueNode::GetFirstValidNPCReply</c>（DialogueSM.cpp:139-147），
        /// 没有可用候选时返回 null（调用方负责按文档回退）。
        /// </summary>
        public static T FirstPassing<T>(IEnumerable<T> candidates, Func<T, bool> conditionPasses)
        {
            if (candidates == null) return default;

            foreach (var candidate in candidates)
            {
                if (candidate == null) continue; // UE5 对 null 元素会崩，端口跳过
                if (conditionPasses == null || conditionPasses(candidate)) return candidate;
            }

            return default;
        }

        /// <summary>
        /// 保留条件通过的全部候选（保持输入顺序）。对应 UE5 <c>UDialogueNode::GetPlayerReplies</c>
        /// 的过滤段（DialogueSM.cpp:156-163），排序由调用方完成。
        /// </summary>
        public static List<T> FilterPassing<T>(IEnumerable<T> candidates, Func<T, bool> conditionPasses)
        {
            var result = new List<T>();
            if (candidates == null) return result;

            foreach (var candidate in candidates)
            {
                if (candidate == null) continue;
                if (conditionPasses == null || conditionPasses(candidate)) result.Add(candidate);
            }

            return result;
        }

        /// <summary>
        /// 候选行的收集顺序：备选行在前、主行在后。
        /// 对应 UE5 <c>GetRandomLine</c>（DialogueSM.cpp:33-40）构造 <c>AllLines</c> 的顺序 ——
        /// 顺序决定同种子下的随机结果，写反会让同一对话资产播出不同的行。
        /// </summary>
        public static List<T> CollectCandidateLines<T>(IEnumerable<T> alternatives, T main)
        {
            var all = new List<T>();
            if (alternatives != null)
            {
                foreach (var alt in alternatives)
                {
                    if (alt != null) all.Add(alt);
                }
            }
            if (main != null) all.Add(main);
            return all;
        }

        /// <summary>
        /// 随机下标。返回 -1 表示「没有可用候选」，调用方据此回退到默认构造的空行
        /// （UE5 的 <c>GetRandomLine</c> 在无通过行时返回默认 FDialogue，而不是 null ——
        /// 这条「空集不崩」的契约由 harness 钉住）。
        /// 随机源由调用方注入，因此同一个 <see cref="Random"/> 实例（同种子）必然复现同一序列。
        /// </summary>
        public static int PickRandomIndex(int count, Random rng)
        {
            if (count <= 0) return -1;
            if (rng == null) return -1;
            return rng.Next(count);
        }
    }
}
