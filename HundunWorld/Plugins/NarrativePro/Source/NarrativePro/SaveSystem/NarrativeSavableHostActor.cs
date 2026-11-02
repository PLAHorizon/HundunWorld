// 模块关系说明：
// 本文件属于 NarrativePro.SaveSystem（UE5 NarrativeSaveSystem 移植）。
//
// UE5 里玩家三元组（PlayerController / PlayerState / Pawn）之外的每个「要进档的 Actor」
// 都实现 INarrativeStableActor / INarrativeSavableActor，存档子系统靠 GUID 定位它
// （见 NarrativeSaveSubsystem.cs:502-520 记录采集、:682-706 记录还原）。
//
// 本游戏（HundunWorld）不实例化任何插件角色：场景里只有名为 "Player" 的普通 Actor
// 挂游戏自己的 PlayerController（Source/Game/GameSceneInitializer.cs:46-73）。
// 运行期引导把 TalesComponent / ASC / 背包 / 交互组件挂到那个宿主 Actor 上，
// 而宿主本身没有任何 GUID 访问器 —— NarrativeSavableActorResolver.Resolve 返回 null
// （NarrativeSavableActorResolver.cs:143-178），CreateActorRecord 直接 bail
// （NarrativeSaveSubsystem.cs:1212-1223），任务进度于是永远进不了世界档。
//
// 本脚本补的就是这个「身份」：实现 INarrativeSavableActor，提供跨会话稳定的 GUID，
// 并显式声明 ShouldRespawn()=false（宿主由场景/引导每次重建，不需要存档系统动态重生，
// 因此也不会出现「Prefab 路径解析失败」的动态重生报错）。

using System;
using FlaxEngine;
using NarrativePro.Core;

namespace NarrativePro.SaveSystem
{
    /// <summary>
    /// 运行期宿主 Actor 的稳定身份。对应 UE5「实现 INarrativeSavableActor 的宿主 Actor」。
    /// <para>
    /// 由 <c>NarrativeRuntimeBootstrap</c> 在宿主装配模式下挂到本地玩家宿主 Actor 上，
    /// 使 <see cref="NarrativeSavableActorResolver"/> 能解析出绑定，进而让同一 Actor 上的
    /// <c>TalesComponent</c> / <c>NarrativeAbilitySystemComponent</c>（INarrativeSavableComponent）
    /// 被 <c>NarrativeSaveSubsystem.CreateActorRecord</c> 收进记录，并在读档时由
    /// <c>LoadActorFromRecord</c> 逐组件回调 <c>Load()</c> 还原。
    /// </para>
    /// </summary>
    public class NarrativeSavableHostActor : Script, INarrativeSavableActor
    {
        /// <summary>本地玩家宿主的默认身份键（同一键 → 同一 GUID，跨会话/跨进程稳定）。</summary>
        public const string LocalPlayerHostKey = "NarrativePro.LocalPlayerHost";

        /// <summary>
        /// 当前注册表里的宿主身份（场景切换后由 OnEnable/OnDisable 维护，供解析器与引导复用）。
        /// </summary>
        public static NarrativeSavableHostActor Instance;

        /// <summary>
        /// 稳定 GUID。初值为空表示「按 <see cref="StableKey"/> 惰性计算」。
        /// 字段名 <c>ActorGUID</c> 同时命中 <see cref="NarrativeSavableActorBinding.GuidMemberNames"/>，
        /// 即使接口路径被绕过，反射兜底也能读到同一个值。
        /// </summary>
        public Guid ActorGUID = Guid.Empty;

        /// <summary>身份键：决定惰性生成的 GUID。多个宿主需要不同档时用不同键区分。</summary>
        public string StableKey = LocalPlayerHostKey;

        /// <summary>
        /// 本 Actor 是否由存档系统负责动态重生。宿主由场景文件/运行期引导每次重建，因此恒为 false：
        /// 记录只做「状态还原」，不做「Actor 重生」，避免读档侧的 Prefab 路径校验失败。
        /// </summary>
        public bool bShouldRespawn = false;

        /// <summary>读档完成（本 Actor 的记录被回放到宿主上）时触发。引导借此补跑一次面板/任务接线。</summary>
        public event Action<NarrativeSavableHostActor> OnHostRecordLoaded;

        public override void OnEnable()
        {
            base.OnEnable();
            Instance = this;

            // 提前固化 GUID：OnEnable 可能早于任何存档调用，保证同一场景内的其它系统
            // （如按 GUID 反查宿主的生成器）拿到的是同一个身份。
            EnsureGuid();
        }

        public override void OnDisable()
        {
            if (ReferenceEquals(Instance, this)) Instance = null;
            base.OnDisable();
        }

        // ===== INarrativeStableActor / INarrativeSavableActor =====

        /// <summary>获取稳定 GUID。对应 UE5 INarrativeStableActor::GetActorGUID。</summary>
        public Guid GetActorGUID() => EnsureGuid();

        /// <summary>写回存档中的 GUID。对应 UE5 INarrativeSavableActor::SetActorGUID。</summary>
        public void SetActorGUID(Guid savedGUID)
        {
            if (savedGUID == Guid.Empty) return;
            ActorGUID = savedGUID;
        }

        /// <summary>保存前通知。宿主自身没有额外状态需要产出（组件快照由存档子系统统一驱动）。</summary>
        public void PrepareForSave()
        {
            EnsureGuid();
        }

        /// <summary>读档后通知：广播给引导，让面板/Tales 接线在还原完成后重新对账。</summary>
        public void Load()
        {
            EnsureGuid();
            OnHostRecordLoaded?.Invoke(this);
            NarrativeLog.Log($"[SaveSystem] 本地玩家宿主记录已还原（GUID={ActorGUID}，Actor={Actor?.Name}）");
        }

        /// <summary>是否需要存档系统重生本 Actor。见 <see cref="bShouldRespawn"/>。</summary>
        public bool ShouldRespawn() => bShouldRespawn;

        // ===== 内部 =====

        private Guid EnsureGuid()
        {
            if (ActorGUID == Guid.Empty)
            {
                ActorGUID = MakeDeterministicGuid(string.IsNullOrEmpty(StableKey) ? LocalPlayerHostKey : StableKey);
            }
            return ActorGUID;
        }

        /// <summary>
        /// 由身份键确定性生成 128 位 GUID（FNV-1a 双路混合，不依赖任何平台相关哈希实现）。
        /// 同一字符串在任何会话/任何机器上都得到同一 GUID，这正是稳定 Actor 的语义要求。
        /// </summary>
        public static Guid MakeDeterministicGuid(string key)
        {
            if (string.IsNullOrEmpty(key)) key = LocalPlayerHostKey;

            const ulong fnvPrime = 1099511628211UL;
            ulong h1 = 14695981039346656037UL;
            ulong h2 = 0x9E3779B97F4A7C15UL;

            for (int i = 0; i < key.Length; i++)
            {
                ulong c = key[i];
                h1 = (h1 ^ c) * fnvPrime;
                h2 = (h2 ^ (c + (ulong)i * 31UL)) * fnvPrime;
            }

            ulong h3 = (h1 ^ (h2 >> 32)) * fnvPrime;
            ulong h4 = (h2 ^ (h1 >> 17)) * 6364136223846793005UL + 1442695040888963407UL;

            byte[] bytes = new byte[16];
            Buffer.BlockCopy(BitConverter.GetBytes(h1), 0, bytes, 0, 8);
            Buffer.BlockCopy(BitConverter.GetBytes(h3 ^ h4), 0, bytes, 8, 8);
            return new Guid(bytes);
        }
    }

    /// <summary>
    /// 存档容器过滤器：识别「只服务于运行期装配、不该进出档」的容器 Actor。
    /// <para>
    /// <c>NarrativeRuntimeBootstrap</c> 每次玩法场景加载都会重建它的宿主（HUD / CommonUI 子系统 /
    /// 对话任务面板 / 泵脚本都挂在上面）。这类容器没有跨会话身份，一旦被写进档，
    /// 读档侧会走「动态重生」分支并报 Prefab 路径错误（NarrativeSaveSubsystem.cs:1106-1116），
    /// 因此在采集/对账/还原三侧统一跳过。
    /// </para>
    /// </summary>
    public static class NarrativeSaveContainerFilter
    {
        /// <summary>运行期引导宿主名（HUD/CommonUI/面板容器），与 <c>NarrativeRuntimeBootstrap.HostActorName</c> 同源。</summary>
        public static readonly string RuntimeBootstrapHostName =
            NarrativePro.CommonUI.NarrativeRuntimeBootstrap.HostActorName;

        /// <summary>该 Actor 是否为运行期引导容器（按名字判定，场景内外一致）。</summary>
        public static bool IsRuntimeBootstrapHost(Actor actor)
        {
            if (actor == null) return false;
            return string.Equals(actor.Name, RuntimeBootstrapHostName, StringComparison.Ordinal);
        }
    }
}
