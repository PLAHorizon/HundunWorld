// 模块关系说明：
// 本文件属于 NarrativePro.Spawners，是「生成器栓系 / 重生 / 反生成」这套决策的纯接缝：
// 给定「到栓系目标的距离、UntetherDistance、存档记录是否存在、是否曾被击杀、生成前置条件」，
// 输出该生成 / 该找回栓系 / 该按存档重生 / 该标记已击杀 / 该移除 / 什么都不做。
// 规则本体在这里，**不引用 FlaxEngine 类型**；实时侧（SpawnComponent / NPCSpawnComponent /
// SpawnerBase）与 harness 调的是同一份实现。
//
// UE5 依据（只读参考工程 …/NarrativeArsenal，行号逐条核对过）：
//   - Private/Spawners/SpawnComponent.cpp:11-15   ShouldSpawnActor（基类只看 IsActive()）
//   - Private/Spawners/SpawnComponent.cpp:17-20   ShouldDespawnActor（SpawnedActor.IsValid()）
//   - Private/Spawners/SpawnComponent.cpp:22-31   TrySpawnActor
//   - Private/Spawners/SpawnComponent.cpp:33-36   TryDespawnActor -> RemoveActor
//   - Private/Spawners/SpawnComponent.cpp:43-52   RemoveActor
//   - Private/Spawners/SpawnerBase.cpp:75-97      SpawnActors（:86 ShouldSpawnActor → :88 TrySpawnActor）
//   - Private/Spawners/SpawnerBase.cpp:99-115     RemoveActors（:108 ShouldDespawnActor → :110 TryDespawnActor）
//   - Private/Spawners/NPCSpawnComponent.cpp:16-25  构造函数（:18 UntetherDistance=10000、:19 bDontSpawnIfPreviouslyKilled=true）
//   - Private/Spawners/NPCSpawnComponent.cpp:27-47  ShouldSpawnActor（击杀记录 ⇒ 不生成）
//   - Private/Spawners/NPCSpawnComponent.cpp:50-58  ShouldDespawnActor（原样转发基类）
//   - Private/Spawners/NPCSpawnComponent.cpp:60-157 SpawnActor（:62/:64/:66 三层前置、:74 bTetheredToPlayer、
//                                                    :76 NPCGuid 取舍、:78 PC 判空、:82 清栓系标记、
//                                                    :84-88 GetTether→找回并 UntetherNPC、
//                                                    :95-104 DoesRecordExist 重生 / 否则 bWasKilled=true、
//                                                    :127 才绑死亡回调、:138-142 OptionalGoal）
//   - Private/Spawners/NPCSpawnComponent.cpp:159-181 TryDespawnActor（:169 严格 <、:173 置栓系标记、
//                                                    :181 退回 RemoveActor）
//   - Private/Spawners/NPCSpawnComponent.cpp:184-197 RemoveActor（有 NPC 控制器走 CleanUp(0)，否则 Destroy）
//   - Private/Spawners/NPCSpawnComponent.cpp:214-221 SpawnedNPCDied（bIsDead && KilledActor == SpawnedActor）
//   - Private/UnrealFramework/NarrativePlayerController.cpp:658-696 TetherNPC（GUID 无效 :665、
//                                                    已有同 GUID :670-677 ⇒ 返回 false）

using System;

namespace NarrativePro.Spawners
{
    /// <summary>一次生成请求的裁决结果。对应 UE5 <c>UNPCSpawnComponent::SpawnActor</c> 的分支出口。</summary>
    public enum ESpawnResumeAction
    {
        /// <summary>前置条件不足（无 SpawnerBase 宿主 / 无 NPC 定义 / 无角色子系统）：本次不生成。</summary>
        Refuse,

        /// <summary>未处于栓系状态：按常规流程新生成。</summary>
        SpawnNormally,

        /// <summary>此前栓系过且找到栓系记录：找回那个 NPC 并从玩家控制器解系（不是新生成）。</summary>
        ReattachFromTether,

        /// <summary>栓系记录已丢失但存档记录还在：按存档原地重生。</summary>
        SpawnFromSavedRecord,

        /// <summary>
        /// 端口口径：既没有栓系记录、又没有存档子系统在场时，仍按常规流程生成
        /// （UE5 在 NPCSpawnComponent.cpp:95 那种情况下什么都不做，本次返回 nullptr）。
        /// </summary>
        SpawnWithoutSaveSubsystem,

        /// <summary>栓系记录与存档记录都没有：标记曾被击杀，本次不生成。</summary>
        MarkKilledAndHold,

        /// <summary>处于栓系状态却拿不到玩家控制器：UE5 保持 bTetheredToPlayer 不变（:76 之内才清），本次不生成。</summary>
        HoldTethered,

        /// <summary>
        /// 栓系标记已清、但既无栓系记录也无存档子系统在场：UE5 走的就是这条 —— NPC 仍为空，
        /// 本次返回 nullptr 且**不**标记击杀（NPCSpawnComponent.cpp:95 的 if 不成立）。
        /// </summary>
        HoldRecordLostWithoutSaveSubsystem,
    }

    /// <summary>生成请求裁决的输出。</summary>
    public struct SpawnResumeDecision
    {
        public ESpawnResumeAction Action;

        /// <summary>是否要把 <c>bTetheredToPlayer</c> 清掉（UE5 :78 只在真的拿到 PC 之后才清）。</summary>
        public bool bClearTetheredFlag;

        /// <summary>是否要把 <c>bWasKilled</c> 置真（UE5 :103）。</summary>
        public bool bShouldMarkKilled;

        /// <summary>是否需要调用 SpawnNPC（ReattachFromTether 走的是找回，不是新生成）。</summary>
        public bool bNeedsNewSpawn;
    }

    /// <summary>反生成尝试的裁决结果。</summary>
    public enum ETetherDespawnAction
    {
        /// <summary>不满足栓系条件（没有已生成 NPC / 范围内没有受控玩家）：直接移除。</summary>
        RemoveActor,

        /// <summary>玩家在 <c>UntetherDistance</c> 内且栓系成功：保留 NPC、标记已栓系。</summary>
        TetherToPlayer,

        /// <summary>玩家在范围内但栓系被拒（GUID 无效 / 已有同 GUID 栓系）：退回移除。</summary>
        TetherRefusedRemoveActor,
    }

    /// <summary>
    /// 栓系 / 重生 / 反生成纯决策集。
    /// </summary>
    public static class SpawnDecisionRules
    {
        /// <summary>UE5 <c>UNPCSpawnComponent</c> 构造函数的栓系半径（NPCSpawnComponent.cpp:18）。</summary>
        public const float DefaultUntetherDistance = 10000f;

        /// <summary>UE5 构造函数的「曾被击杀就不再生成」默认值（NPCSpawnComponent.cpp:19）。</summary>
        public const bool DefaultDontSpawnIfPreviouslyKilled = true;

        /// <summary>
        /// 该生成吗。对应 UE5 <c>UNPCSpawnComponent::ShouldSpawnActor</c>（NPCSpawnComponent.cpp:27-47）：
        /// 已有生成物 → 否；<c>bDontSpawnIfPreviouslyKilled &amp;&amp; bWasKilled</c> → 否；无定义 → 否。
        /// <para>
        /// 端口差异（保留，不是修）：UE5 基类那一关是 <c>IsActive()</c>（SpawnComponent.cpp:11-15），
        /// Flax 的 Script 激活态由引擎按脚本创建顺序回调 OnEnable，兄弟组件在 SpawnerBase.OnEnable
        /// 那一刻的 Enabled 不可控，因此端口用「没有已生成的 Actor」承担同一位置
        /// （对应 UE5 里 BeginPlay 时 SpawnedActor 必为空的情形）。
        /// </para>
        /// </summary>
        public static bool ShouldSpawnActor(bool hasSpawnedActor, bool bDontSpawnIfPreviouslyKilled, bool bWasKilled, bool hasDefinition)
        {
            if (hasSpawnedActor) return false;
            if (bDontSpawnIfPreviouslyKilled && bWasKilled) return false;
            if (!hasDefinition) return false;
            return true;
        }

        /// <summary>
        /// 基类版反生成门：只问「有没有已生成的 Actor」。
        /// 对应 UE5 <c>USpawnComponent::ShouldDespawnActor</c>（SpawnComponent.cpp:17-20）。
        /// </summary>
        public static bool ShouldDespawnActor(bool hasSpawnedActor)
        {
            return hasSpawnedActor;
        }

        /// <summary>
        /// NPC 版反生成门。UE5 <c>UNPCSpawnComponent::ShouldDespawnActor</c>（:50-58）只是原样转发基类；
        /// 端口额外加了「已栓系到玩家就不再反生成」。这条加法是必要的：端口的
        /// <c>NarrativePlayerController.TetherNPC</c> 没有 UE5 :670-677 的同 GUID 去重门
        /// （那段在 UnrealFramework，本任务不改），若此处放行，组件每轮都会为同一个 NPC 追加重复栓系。
        /// </summary>
        public static bool ShouldDespawnNpcActor(bool hasSpawnedActor, bool bTetheredToPlayer)
        {
            if (bTetheredToPlayer) return false;
            return ShouldDespawnActor(hasSpawnedActor);
        }

        /// <summary>
        /// 生成请求走哪条出口。对应 UE5 <c>SpawnActor_Implementation</c>（NPCSpawnComponent.cpp:60-157）。
        /// 钉住的三条：
        ///   ① 只有真的取到玩家控制器才清 <c>bTetheredToPlayer</c>（:82 位于 :78 的 PC 判空之内）——
        ///      拿不到 PC 时状态原样留着，下次再试；
        ///   ② 找不到栓系记录（:84 GetTether 落到 :90 的 else）⇒ 只有存档记录还在才重生（:97 DoesRecordExist → :99），
        ///      没有记录就 <c>bWasKilled = true</c>（:103）——「曾被击杀 + 记录已消失」因此**不会重生**；
        ///   ③ 存档子系统缺席时 UE5 什么都不做（:95 的 if 不成立，NPC 仍为空 ⇒ :152 返回 nullptr，也不标记击杀）；
        ///      端口按常规流程生成 —— 端口既有口径（未启用存档的会话里 NPC 必须能生成），由
        ///      <paramref name="spawnWhenSaveSubsystemMissing"/> 显式带出，harness 同时钉住两种结果。
        /// </summary>
        public static SpawnResumeDecision DecideSpawnResume(bool hasSpawnerOwner, bool hasDefinition, bool hasCharacterSubsystem,
            bool bTetheredToPlayer, bool hasPlayerController, bool tetherFoundAndValid, bool saveSubsystemPresent,
            bool recordExists, bool spawnWhenSaveSubsystemMissing)
        {
            var decision = new SpawnResumeDecision { Action = ESpawnResumeAction.Refuse };

            // UE5 :62 / :64 / :66 三层前置：宿主 SpawnerBase、NPC 定义、角色子系统
            if (!hasSpawnerOwner || !hasDefinition || !hasCharacterSubsystem)
            {
                return decision;
            }

            if (!bTetheredToPlayer)
            {
                decision.Action = ESpawnResumeAction.SpawnNormally;
                decision.bNeedsNewSpawn = true;
                return decision;
            }

            if (!hasPlayerController)
            {
                // bTetheredToPlayer 保持不变（UE5 的清标记发生在拿到 PC 之后）
                decision.Action = ESpawnResumeAction.HoldTethered;
                return decision;
            }

            decision.bClearTetheredFlag = true;

            if (tetherFoundAndValid)
            {
                decision.Action = ESpawnResumeAction.ReattachFromTether;
                return decision;
            }

            if (saveSubsystemPresent)
            {
                if (recordExists)
                {
                    decision.Action = ESpawnResumeAction.SpawnFromSavedRecord;
                    decision.bNeedsNewSpawn = true;
                }
                else
                {
                    decision.Action = ESpawnResumeAction.MarkKilledAndHold;
                    decision.bShouldMarkKilled = true;
                }
                return decision;
            }

            if (spawnWhenSaveSubsystemMissing)
            {
                decision.Action = ESpawnResumeAction.SpawnWithoutSaveSubsystem;
                decision.bNeedsNewSpawn = true;
            }
            else
            {
                // UE5 :95 的 if 不成立 ⇒ NPC 仍为空 ⇒ 本次返回 nullptr（不标记击杀）
                decision.Action = ESpawnResumeAction.HoldRecordLostWithoutSaveSubsystem;
            }
            return decision;
        }

        /// <summary>
        /// 反生成尝试：栓系还是移除。对应 UE5 <c>TryDespawnActor</c>（NPCSpawnComponent.cpp:159-181）。
        /// 判据：<c>GetDistanceTo &lt; UntetherDistance</c> 是**严格小于**（:169），恰好在边界上不栓系而是移除；
        /// 栓系条件不满足（无 NPC / 无 PC / 无受控 pawn / 超出距离）或栓系被拒时都退回 RemoveActor（:181）。
        /// </summary>
        public static ETetherDespawnAction DecideDespawnAttempt(bool hasSpawnedNpcCharacter, bool hasPlayerController,
            bool hasControlledPawnActor, float distanceToSpawnedNpc, float untetherDistance, bool tetherAccepted)
        {
            bool bWithinTetherRange = hasSpawnedNpcCharacter && hasPlayerController && hasControlledPawnActor
                                      && distanceToSpawnedNpc < untetherDistance;
            if (!bWithinTetherRange) return ETetherDespawnAction.RemoveActor;
            return tetherAccepted ? ETetherDespawnAction.TetherToPlayer : ETetherDespawnAction.TetherRefusedRemoveActor;
        }

        /// <summary>
        /// 要不要把这次死亡记成「本生成组件的 NPC 被击杀」。对应 UE5 <c>SpawnedNPCDied</c>
        /// （NPCSpawnComponent.cpp:214-221）：<c>bIsDead &amp;&amp; KilledActor == SpawnedActor.Get()</c>。
        /// 两条都要：复活回调（bIsDead=false）不记录，别的 Actor 的死活也不许记到本生成点头上。
        /// </summary>
        public static bool ShouldMarkSpawnKilled(bool bIsDead, bool killedActorIsSpawnedActor)
        {
            return bIsDead && killedActorIsSpawnedActor;
        }

        /// <summary>
        /// 要不要绑定死亡回调。对应 UE5 <c>SpawnActor</c> 里的 <c>if (bDontSpawnIfPreviouslyKilled)</c>
        /// （NPCSpawnComponent.cpp:127）—— 只有配了「击杀后不再生成」才监听 <c>OnDeathStateChanged</c>（:133）。
        /// </summary>
        public static bool ShouldBindDeathCallback(bool bDontSpawnIfPreviouslyKilled)
        {
            return bDontSpawnIfPreviouslyKilled;
        }

        /// <summary>
        /// 移除已生成 NPC 时用哪条通路。对应 UE5 <c>RemoveActor</c>（NPCSpawnComponent.cpp:184-197）：
        /// 有 NPC 控制器走 <c>CleanUp(0)</c>（要收尾交互/行为），否则直接销毁。
        /// </summary>
        public static bool ShouldCleanUpViaController(bool hasNpcController)
        {
            return hasNpcController;
        }
    }
}
