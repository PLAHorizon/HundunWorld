// 模块关系说明：
// 本文件是 NarrativePro.GAS 的**引擎无关账本层**（不引用 FlaxEngine 类型）：
// 记录"哪个激活效果句柄往持有者身上挂了哪些 GrantedTags"，并负责按句柄回收。
//
// 为什么需要它：GrantedTags 的挂/撤原本散在 NarrativeAbilitySystemComponent 的三处内联循环里
// （ApplyGameplayEffectSpecToSelf 的授予、RemoveActiveEffect 的撤销、OnUpdate 过期时的撤销），
// 而 ASC 是 FlaxEngine.Script 子类——无原生 FlaxEngine 库时 `new` 抛 DllNotFoundException，
// 进程外打不到这条规则。这条规则曾经出过真缺陷：
//   重击标签由 CommitExecute 经 AddDynamicTagsGameplayEffect 挂成 Infinite 效果，
//   但能力没有保存返回的句柄 ⇒ RemoveActiveEffect 永远无从撤销 ⇒ **一次重击之后所有攻击都按重击结算**。
// 因此账本必须能断言三件事：
//   ① 授予后持有者确实含该标签（重击判定成立）；
//   ② 按**同一个句柄**撤销后持有者不再含该标签（重击不会粘住）；
//   ③ 句柄无效（Instant 效果返回 Invalid=0）时撤销是空操作，既不误删别人的标签也不抛异常。
//
// 实时通路（NarrativeAbilitySystemComponent.ApplyGameplayEffectSpecToSelf 的授予、
// RemoveActiveEffect 与 OnUpdate 过期的撤销）全部经由这里，harness 驱动的是同一份实现。
//
// 口径说明（保持与抽取前完全一致）：
//   - 授予对持有者容器是幂等的（GameplayTagContainer 内部 HashSet 去重）；
//   - 撤销只删"该句柄当初挂上去的那批标签"，不做引用计数——
//     这与抽取前 RemoveActiveEffect 直接按 active.Effect.GrantedTags 删除的行为一致，
//     两个效果授予同名标签时先撤的那个会把标签一起带走（端口既有语义，本层不改规则）。

using System;
using System.Collections.Generic;
using NarrativePro.Items;

namespace NarrativePro.GAS
{
    /// <summary>
    /// GrantedTags 的挂/撤账本。
    /// </summary>
    public sealed class NarrativeGrantedTagLedger
    {
        /// <summary>无效句柄（对应 <see cref="ActiveGameplayEffectHandle.Invalid"/> 的 HandleId=0）。</summary>
        public const int InvalidHandleId = 0;

        private readonly Dictionary<int, List<string>> _grantedByHandle = new Dictionary<int, List<string>>();

        /// <summary>当前被账本跟踪的授予数量（一个 Infinite/Duration 效果一条）。</summary>
        public int TrackedCount => _grantedByHandle.Count;

        /// <summary>该句柄是否有授予记录。</summary>
        public bool IsTracked(int handleId)
        {
            return handleId > InvalidHandleId && _grantedByHandle.ContainsKey(handleId);
        }

        /// <summary>该句柄当初挂上的标签名（无记录返回空表，永不 null）。</summary>
        public IReadOnlyList<string> GetGrantedTags(int handleId)
        {
            if (handleId <= InvalidHandleId) return EmptyTags;
            return _grantedByHandle.TryGetValue(handleId, out var tags) ? tags : EmptyTags;
        }

        /// <summary>
        /// 把效果的 GrantedTags 写进持有者容器（对应抽取前 ApplyGameplayEffectSpecToSelf 里的内联 foreach）。
        /// </summary>
        public static void AddGrantedTags(GameplayTagContainer holder, GameplayTagContainer granted)
        {
            if (holder == null || granted == null) return;

            foreach (var tag in granted.GetTags())
            {
                holder.AddTag(new GameplayTag(tag));
            }
        }

        /// <summary>
        /// 从持有者容器里删除给定的一批标签名（对应抽取前 RemoveActiveEffect / OnUpdate 里的内联 foreach）。
        /// </summary>
        public static void RemoveGrantedTags(GameplayTagContainer holder, IEnumerable<string> tagNames)
        {
            if (holder == null || tagNames == null) return;

            foreach (var tag in tagNames)
            {
                if (string.IsNullOrEmpty(tag)) continue;
                holder.RemoveTag(new GameplayTag(tag));
            }
        }

        /// <summary>
        /// 记下一笔授予：句柄 → 标签名列表。句柄无效（Instant 效果不进 ActiveEffects）时不记账，
        /// 因此也就没有任何路径能"撤销一笔从未存在的授予"。
        /// </summary>
        public void TrackGrant(int handleId, GameplayTagContainer granted)
        {
            if (handleId <= InvalidHandleId || granted == null) return;

            var names = new List<string>();
            foreach (var tag in granted.GetTags())
            {
                if (!string.IsNullOrEmpty(tag)) names.Add(tag);
            }
            if (names.Count == 0) return;

            _grantedByHandle[handleId] = names;
        }

        /// <summary>
        /// 按句柄撤销授予：删除当初挂上的那批标签并取消记账。
        /// </summary>
        /// <returns>是否真的撤到了一笔记录（无效句柄/未记账 → false 且不改动态持有者容器）。</returns>
        public bool RevokeGrant(int handleId, GameplayTagContainer holder)
        {
            if (handleId <= InvalidHandleId) return false;
            if (!_grantedByHandle.TryGetValue(handleId, out var names)) return false;

            _grantedByHandle.Remove(handleId);
            RemoveGrantedTags(holder, names);
            return true;
        }

        private static readonly List<string> EmptyTags = new List<string>();
    }
}
