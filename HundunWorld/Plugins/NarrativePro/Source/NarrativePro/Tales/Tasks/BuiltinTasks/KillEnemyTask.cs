using System;
using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.GAS;
using NarrativePro.Items;

namespace NarrativePro.Tales.Tasks
{
    /// <summary>
    /// 「杀掉指定敌人」的任务条目。对应 UE5 蓝图任务 <c>BPT_KillEnemy</c>
    /// （Content/Pro/Core/Tales/Tasks/BPT_KillEnemy.uasset）。
    /// <para>
    /// 二进制证据：图里创建了一个内联的 <c>BPT_WaitGameplayEvent</c> 子任务
    /// （<c>Default__BPT_WaitGameplayEvent_C</c> + <c>GameplayEvent.KilledEnemy</c> 标签、
    /// <c>CallFunc_WaitGameplayEventToActor_ReturnValue</c> 的同类语义），
    /// 再用 <c>CallFunc_GetNarrativeCharacter_ReturnValue</c> +
    /// <c>K2Node_DynamicCast_AsNarrative_Character_Owner</c> +
    /// <c>CallFunc_GetCharacterDefinition_ReturnValue</c> 与变量 <c>EnemyToKill</c> 比对
    /// <c>EqualEqual_ObjectObject</c>，即「事件的 Owner 就是我要点名杀掉的那个 NPC」；
    /// 变量 <c>NpcName</c> / <c>NumReplicatedProperties</c> 说明目标按 NPC 名解析。
    /// </para>
    /// <para>
    /// 端口差异（重要）：<c>Narrative.GameplayEvent.KilledEnemy</c> 在端口只登记了标签
    /// （Core/NarrativeGameplayTags.cs:68/:341），全工程没有任何
    /// <c>HandleGameplayEvent</c> 广播点，因此照抄 UE5 的事件等待会永远不触发。
    /// 端口改用真实的死亡事实源 <see cref="NarrativeAbilitySystemComponent.OnDeathStateChanged"/>
    /// （GAS/NarrativeAbilitySystemComponent.cs:261，由
    /// <c>HandleOutOfHealth</c> → <c>SetDeadState</c> 广播），并保留
    /// <see cref="KillEventTag"/> 让内容作者仍可以按 UE5 口径改挂别的事件。
    /// </para>
    /// </summary>
    [Serializable]
    public class KillEnemyTask : NarrativeTask
    {
        /// <summary>要杀死的敌人的 Actor 提供者。</summary>
        public NarrativePro.Core.NarrativeActorProvider EnemyProvider { get; set; }

        /// <summary>按 NPC 定义 ID 点名。对应图里的 <c>NpcName</c> / <c>EnemyToKill</c>。</summary>
        public string EnemyNpcId { get; set; } = "";

        /// <summary>按存档 GUID 点名。对应 <c>NPCSpawnComponent.NPCSaveGUID</c>。</summary>
        public Guid EnemySaveGUID { get; set; } = Guid.Empty;

        /// <summary>
        /// 可选：额外等待角色 ASC 上的 gameplay 事件标签（UE5 用
        /// <c>Narrative.GameplayEvent.KilledEnemy</c>）。留空即只认死亡状态。
        /// </summary>
        public string KillEventTag { get; set; } = "";

        private NarrativeAbilitySystemComponent _enemyAsc;
        private Actor _enemyActor;
        private NarrativeAbilitySystemComponent _eventAsc;
        private GameplayTag _eventTag;
        private bool _bChainedEventCallback;

        protected override void OnBeginTask()
        {
            RefreshEnemy();

            if (_enemyAsc != null)
            {
                if (_enemyAsc.IsDead)
                {
                    CompleteTask();
                    return;
                }
                _enemyAsc.OnDeathStateChanged += HandleDeathStateChanged;
            }
            else
            {
                NarrativeLog.LogWarning($"KillEnemyTask: 还找不到敌人（EnemyNpcId='{EnemyNpcId}'），改用轮询等待其生成");
            }

            BindKillEvent();

            // 敌人可能稍后才被流式加载/生成；也用来兜住「击杀者不是本机玩家」的情形。
            TickInterval = 0.5f;
        }

        private void RefreshEnemy()
        {
            var actor = BuiltinTaskSupport.ResolveTargetActor(EnemyProvider, EnemyNpcId, EnemySaveGUID);
            if (actor == null) return;

            _enemyActor = actor;
            _enemyAsc = ArsenalStatics.GetNarrativeAbilitySystemComponent(actor);
        }

        /// <summary>
        /// 可选事件等待通路：端口 ASC 的事件回调表是「一个标签一个回调」的字典
        /// （<c>RegisterGenericGameplayEventCallback</c>，
        /// NarrativeAbilitySystemComponent.cs:916 —— 注释明写「同一标签重复注册会替换旧回调」），
        /// 且没有读取既有回调的 API，所以这里只能在结束时注销，不能像 UE5 的多播委托那样加挂。
        /// </summary>
        private void BindKillEvent()
        {
            if (string.IsNullOrEmpty(KillEventTag)) return;

            _eventTag = new GameplayTag(KillEventTag);
            if (!_eventTag.IsValid()) return;

            _eventAsc = _enemyAsc ?? BuiltinTaskSupport.GetAsc(this);
            if (_eventAsc == null) return;

            _bChainedEventCallback = true;
            _eventAsc.RegisterGenericGameplayEventCallback(_eventTag, HandleKillEvent);
        }

        private void HandleKillEvent(GameplayEventData payload)
        {
            if (!bIsActive) return;
            if (!OwnerMatchesEnemy(payload)) return;
            CompleteTask();
        }

        /// <summary>UE5 的 DynamicCast_AsNarrative_Character_Owner + 定义比对：事件的发起者必须是要杀的敌人。</summary>
        private bool OwnerMatchesEnemy(GameplayEventData payload)
        {
            if (_enemyActor == null) return true;
            if (payload == null) return false;
            return payload.Instigator == _enemyActor || payload.Target == _enemyActor;
        }

        private void HandleDeathStateChanged(Actor actor, NarrativeAbilitySystemComponent asc, bool bIsDead)
        {
            if (!bIsActive || !bIsDead) return;
            if (_enemyAsc != null && asc != _enemyAsc) return;
            CompleteTask();
        }

        protected override void TickTask()
        {
            if (IsComplete()) return;

            if (_enemyAsc == null)
            {
                RefreshEnemy();
                if (_enemyAsc == null) return;
                _enemyAsc.OnDeathStateChanged += HandleDeathStateChanged;
                if (!_bChainedEventCallback && !string.IsNullOrEmpty(KillEventTag))
                    BindKillEvent();
            }

            if (_enemyAsc.IsDead)
                CompleteTask();
        }

        public override Actor GetNavigationMarkerAttachActor()
        {
            if (_enemyActor == null) RefreshEnemy();
            return _enemyActor;
        }

        protected override void OnEndTask()
        {
            if (_enemyAsc != null)
                _enemyAsc.OnDeathStateChanged -= HandleDeathStateChanged;

            if (_bChainedEventCallback && _eventAsc != null && _eventTag.IsValid())
                _eventAsc.UnregisterGenericGameplayEventCallback(_eventTag);

            _bChainedEventCallback = false;

            _eventAsc = null;
            _enemyAsc = null;
            _enemyActor = null;
        }

        public override string GetTaskDescription()
        {
            if (!string.IsNullOrEmpty(DescriptionOverride)) return DescriptionOverride;
            return string.IsNullOrEmpty(EnemyNpcId) ? "消灭敌人" : $"消灭 {EnemyNpcId}";
        }
    }
}
