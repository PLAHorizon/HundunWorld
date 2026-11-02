using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.AI.Activities;
using NarrativePro.Core;
using NarrativePro.GAS;
using NarrativePro.Interaction;
using NarrativePro.Items;
using NarrativePro.Navigation;

namespace NarrativePro.AI
{
    /// <summary>
    /// NPC 控制器。对应 UE5 ANarrativeNPCController。
    /// UE5 中继承 AAIController；Flax 中作为 Script 挂载到 NPC 角色 Actor 上。
    /// 管理 NPC 活动组件、交互组件、攻击令牌、死亡处理。
    /// 注：Flax 无 AIController/Pawn 概念，控制器作为 Script 附加到 NPC Actor。
    /// </summary>
    public class NarrativeNPCController : Script
    {
        /// <summary>拥有的 NPC 角色 Actor</summary>
        [NonSerialized]
        public Actor OwnedCharacter;

        /// <summary>NPC 活动组件（运行时查找）</summary>
        [NonSerialized]
        public NPCActivityComponent NPCActivityComponent;

        /// <summary>NPC 交互组件（运行时查找）</summary>
        [NonSerialized]
        public NarrativeInteractableComponent InteractionComponent;

        /// <summary>当前占用的攻击令牌目标 ASC</summary>
        [NonSerialized]
        public NarrativeAbilitySystemComponent GrantedToken;

        /// <summary>
        /// 是否启用自动战斗（攻击令牌领取/归还 + 按频率激活攻击能力）。
        /// UE5 由行为树/AbilityService + EQS 驱动（NarrativeNPCController.cpp:262-299），
        /// Flax 无 BT 运行时，等价驱动放在控制器 Tick。
        /// </summary>
        public bool bAutoCombat = true;

        /// <summary>战斗决策间隔（秒）。对应 UE5 BT Service 的 TickInterval。</summary>
        public float CombatThinkInterval = 0.25f;

        /// <summary>下次战斗决策时间（游戏秒）。</summary>
        private float _nextCombatThinkTime;

        /// <summary>
        /// 攻击出手的角度容差（度）。对应 UE5 攻击节点前的朝向门控：
        /// hitscan/弹道走 <c>Actor.Direction</c>（NarrativeCombatAbility.GetTargetDataUsingTrace），
        /// 未大致转向目标前不开火，否则必miss。
        /// </summary>
        public float AttackFacingDegrees = 15f;

        /// <summary>
        /// 目标搜索半径相对武器范围的倍率（&gt;1 时可在武器范围外发现目标，
        /// 此时不开火、改为长距离逼近；对应 UE5 BT 的 MoveTo 分支）。
        /// </summary>
        public float CombatSearchRangeMultiplier = 2f;

        /// <summary>战斗逼近中的长距离移动任务（由本控制器 OnUpdate 逐帧驱动；Flax 无内建 tick）。</summary>
        private AITask_LongMove _combatMoveTask;

        /// <summary>当前焦点是否由战斗 AI 持有（脱离时只清自己设置的焦点，不踩活动系统的）。</summary>
        private bool _bCombatFocusSet;

        /// <summary>默认物品装载是否已发放（对应 UE5 InitNewCharacter 的一次性发放）。</summary>
        private bool _bDefaultLoadoutApplied;

        /// <summary>平滑聚焦旋转速度</summary>
        public float SmoothFocusInterpSpeed = 30.0f;

        /// <summary>缓存的平滑目标旋转</summary>
        [NonSerialized]
        private Quaternion _smoothTargetRotation = Quaternion.Identity;

        /// <summary>是否已设过平滑聚焦旋转（显式标志；Identity 不能当"未设置"哨兵，+Z 正前方的目标旋转恰是 Identity）。</summary>
        private bool _bHasSmoothTargetRotation;

        /// <summary>缓存的 NPC 定义路径</summary>
        public string NPCDefinitionPath = "";

        /// <summary>NPC 定义实例（运行时加载）</summary>
        [NonSerialized]
        public NPCDefinition NPCData;

        /// <summary>
        /// 本控制器是否拥有模拟权威。对应 UE5 <c>AActor::HasAuthority()</c>。
        /// Flax 无内建 Actor 权威模型，默认本地即权威；联机时由服务端/宿主置为 false。
        /// </summary>
        public bool bHasAuthority = true;

        /// <summary>当前活跃的行为树资产路径。对应 UE5 BrainComponent 上运行的行为树。</summary>
        [NonSerialized]
        public string ActiveBehaviourTreePath = "";

        /// <summary>本控制器的黑板。对应 UE5 <c>UBlackboardComponent</c>。</summary>
        [NonSerialized]
        public NarrativeNPCBlackboard Blackboard;

        /// <summary>松散标签引用计数。对应 UE5 <c>FGameplayTagCountContainer</c> 的松散标签部分。</summary>
        private readonly Dictionary<string, int> _looseTagCounts = new Dictionary<string, int>();

        public override void OnEnable()
        {
            base.OnEnable();
            OwnedCharacter = Actor;

            if (Blackboard == null)
            {
                Blackboard = new NarrativeNPCBlackboard();
            }

            // 查找活动组件
            NPCActivityComponent = Actor.GetScript<NPCActivityComponent>();
            if (NPCActivityComponent != null)
            {
                NPCActivityComponent.OwnerControllerId = ID.ToString();
                NPCActivityComponent.OwnerController = this;
            }

            // 查找交互组件
            InteractionComponent = Actor.GetScript<NarrativeInteractableComponent>();

            // 绑定本角色 ASC 的死亡事件（对应 UE5 NarrativeNPCController.cpp:160-166：
            // NASC->OnDeathStateChanged.AddUniqueDynamic(this, &ThisClass::HandleDeath)）
            var asc = GetAbilitySystemComponent();
            if (asc != null)
            {
                asc.OnDeathStateChanged -= HandleDeath;
                asc.OnDeathStateChanged += HandleDeath;
            }
        }

        public override void OnDisable()
        {
            // 解绑死亡事件（与 OnEnable 的绑定成对）
            var asc = GetAbilitySystemComponent();
            if (asc != null)
            {
                asc.OnDeathStateChanged -= HandleDeath;
            }

            // 返还攻击令牌
            ReturnToken();
            base.OnDisable();
        }

        public override void OnUpdate()
        {
            base.OnUpdate();
            UpdateControlRotation(Time.DeltaTime, true);

            // 一次性发放默认装载（对应 UE5 InitNewCharacter 的物品发放时机）
            TryApplyDefaultItemLoadout();

            // 攻击令牌回路（对应 UE5 BT/EQS 驱动的 RequestAttackToken→攻击→ReturnToken）
            UpdateCombatAI();

            // 战斗逼近移动必须由调用方逐帧驱动（AITask_LongMove 无内建 tick 拥有者）
            if (_combatMoveTask != null)
            {
                if (_combatMoveTask.IsRunning)
                {
                    _combatMoveTask.TickTask(Time.DeltaTime);
                }
                else
                {
                    _combatMoveTask = null;
                }
            }
        }

        /// <summary>获取 NPC 数据资产</summary>
        public NPCDefinition GetNPCData()
        {
            return NPCData;
        }

        /// <summary>获取 NPC 名称</summary>
        public string GetNPCName()
        {
            if (NPCData != null && !string.IsNullOrEmpty(NPCData.NPCName))
            {
                return NPCData.NPCName;
            }
            return Actor?.Name ?? "NPC";
        }

        /// <summary>检查 NPC 是否存活</summary>
        public bool IsAlive()
        {
            var asc = GetAbilitySystemComponent();
            if (asc == null) return true;
            return !asc.IsDead;
        }

        /// <summary>获取 NPC 角色的能力系统组件</summary>
        public NarrativeAbilitySystemComponent GetAbilitySystemComponent()
        {
            return Actor?.GetScript<NarrativeAbilitySystemComponent>();
        }

        /// <summary>获取受控的 NPC 角色</summary>
        public Actor GetControlledNPC()
        {
            return OwnedCharacter;
        }

        /// <summary>获取拥有的 NPC（无论当前拥有什么 Pawn）</summary>
        public Actor GetOwnedNPC()
        {
            return OwnedCharacter;
        }

        /// <summary>获取活动组件</summary>
        public NPCActivityComponent GetActivityComponent()
        {
            return NPCActivityComponent;
        }

        /// <summary>获取交互组件</summary>
        public NarrativeInteractableComponent GetInteractionComponent()
        {
            return InteractionComponent;
        }

        // ===== 权威与标签（对应 UE5 AAIController / IGameplayTagAssetInterface） =====

        /// <summary>本控制器是否有模拟权威。对应 UE5 <c>Actor->HasAuthority()</c>。</summary>
        public bool HasAuthority()
        {
            return bHasAuthority;
        }

        /// <summary>设置模拟权威（联机宿主/客户端切换时调用）。</summary>
        public void SetAuthority(bool bInAuthority)
        {
            bHasAuthority = bInAuthority;
        }

        /// <summary>
        /// 取本 NPC 当前拥有的全部 gameplay 标签。
        /// 对应 UE5 在 <c>CanRunActivity</c> 里读取控制器自身的标签集做 BlockTags/RequireTags 判定。
        /// </summary>
        public GameplayTagContainer GetOwnedGameplayTags()
        {
            var container = new GameplayTagContainer();
            var asc = GetAbilitySystemComponent();
            asc?.GetOwnedGameplayTags(container);

            // 松散标签已经通过 ASC 的 GameplayTags 生效；若 ASC 尚未就绪，用本地计数兜底
            if (container.Count == 0 && asc == null)
            {
                foreach (var kvp in _looseTagCounts)
                {
                    if (kvp.Value > 0) container.AddTag(new GameplayTag(kvp.Key));
                }
            }
            return container;
        }

        /// <summary>
        /// 层级化标签判定：拥有者是否命中给定集合中的任意标签。
        /// 对应 UE5 <c>HasAnyMatchingGameplayTags</c>（父标签匹配子标签）。
        /// </summary>
        public bool HasAnyMatchingGameplayTags(GameplayTagContainer tagsToCheck)
        {
            if (tagsToCheck == null || tagsToCheck.Count == 0) return false;
            var owned = GetOwnedGameplayTags();
            foreach (var ownedName in owned.GetTags())
            {
                var ownedTag = new GameplayTag(ownedName);
                foreach (var checkName in tagsToCheck.GetTags())
                {
                    // check 是父标签时也应命中子标签（UE5 的层级匹配方向）
                    if (new GameplayTag(checkName).Matches(ownedTag)) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 层级化标签判定：拥有者是否满足给定集合的全部标签。
        /// 对应 UE5 <c>HasAllMatchingGameplayTags</c>。
        /// </summary>
        public bool HasAllMatchingGameplayTags(GameplayTagContainer tagsToCheck)
        {
            if (tagsToCheck == null || tagsToCheck.Count == 0) return true;
            var owned = GetOwnedGameplayTags();
            foreach (var checkName in tagsToCheck.GetTags())
            {
                var checkTag = new GameplayTag(checkName);
                bool found = false;
                foreach (var ownedName in owned.GetTags())
                {
                    if (checkTag.Matches(new GameplayTag(ownedName))) { found = true; break; }
                }
                if (!found) return false;
            }
            return true;
        }

        /// <summary>
        /// 加松散 gameplay 标签并计数。对应 UE5 <c>ASC->AddLooseGameplayTags(Tags, Count)</c>。
        /// 计数语义保留：同一标签被多个活动持有时，只有全部释放才真正移除。
        /// </summary>
        public void AddLooseGameplayTags(GameplayTagContainer tags, int count = 1)
        {
            if (tags == null || count <= 0) return;

            var asc = GetAbilitySystemComponent();
            foreach (var tagName in tags.GetTags())
            {
                if (string.IsNullOrEmpty(tagName)) continue;

                _looseTagCounts.TryGetValue(tagName, out int existing);
                _looseTagCounts[tagName] = existing + count;

                if (asc != null)
                {
                    var single = new GameplayTagContainer();
                    single.AddTag(new GameplayTag(tagName));
                    // 走 GAS 的动态标签通道，使效果系统与标签查询保持一致
                    asc.AddDynamicTagsGameplayEffect(single);
                }
            }
        }

        /// <summary>
        /// 减松散 gameplay 标签计数；归零才真正移除。
        /// 对应 UE5 <c>ASC->RemoveLooseGameplayTags(Tags, Count)</c>。
        /// </summary>
        public void RemoveLooseGameplayTags(GameplayTagContainer tags, int count = 1)
        {
            if (tags == null || count <= 0) return;

            var asc = GetAbilitySystemComponent();
            foreach (var tagName in tags.GetTags())
            {
                if (string.IsNullOrEmpty(tagName)) continue;
                if (!_looseTagCounts.TryGetValue(tagName, out int existing)) continue;

                int remaining = existing - count;
                if (remaining > 0)
                {
                    _looseTagCounts[tagName] = remaining;
                    continue;
                }

                _looseTagCounts.Remove(tagName);
                if (asc != null)
                {
                    asc.GameplayTags.RemoveTag(new GameplayTag(tagName));
                }
            }
        }

        // ===== 行为树 / 黑板（对应 UE5 AAIController 的 BrainComponent + Blackboard） =====

        /// <summary>取黑板组件。对应 UE5 <c>GetBlackboardComponent()</c>。</summary>
        public NarrativeNPCBlackboard GetBlackboardComponent()
        {
            Blackboard ??= new NarrativeNPCBlackboard();
            return Blackboard;
        }

        /// <summary>
        /// 运行行为树。对应 UE5 <c>AAIController::RunBehaviorTree</c>：
        /// 换树时先停掉旧的，成功返回 true 并把树路径记为当前活跃树。
        /// </summary>
        public virtual bool RunBehaviorTree(string behaviourTreePath)
        {
            if (string.IsNullOrEmpty(behaviourTreePath)) return false;

            if (!string.IsNullOrEmpty(ActiveBehaviourTreePath) && ActiveBehaviourTreePath != behaviourTreePath)
            {
                StopBehaviorTree();
            }

            ActiveBehaviourTreePath = behaviourTreePath;
            Blackboard?.NotifyTreeStarted(behaviourTreePath);
            return true;
        }

        /// <summary>停止行为树。对应 UE5 <c>AAIController::StopBehaviorTree</c>。</summary>
        public virtual void StopBehaviorTree()
        {
            if (string.IsNullOrEmpty(ActiveBehaviourTreePath)) return;
            Blackboard?.NotifyTreeStopped();
            ActiveBehaviourTreePath = "";
        }

        /// <summary>当前是否有行为树在跑。对应 UE5 <c>GetBrainComponent() != null</c>。</summary>
        public bool IsBehaviorTreeRunning() => !string.IsNullOrEmpty(ActiveBehaviourTreePath);

        /// <summary>
        /// 重启当前行为树。对应 UE5 <c>UBehaviorTreeComponent::RestartTree(CompleteRestart)</c>：
        /// 同一棵树被再次选中时不清 Brain，只是把执行状态复位。
        /// </summary>
        public virtual void RestartBehaviorTree()
        {
            if (string.IsNullOrEmpty(ActiveBehaviourTreePath)) return;

            Blackboard?.CompleteRestart();
            Blackboard?.NotifyTreeStarted(ActiveBehaviourTreePath);
        }

        /// <summary>取路径跟随组件。对应 UE5 <c>GetPathFollowingComponent()</c>。</summary>
        public NarrativePathFollowingComp GetPathFollowingComponent()
        {
            if (_pathFollowing == null && Actor != null)
            {
                _pathFollowing = Actor.GetScript<NarrativePathFollowingComp>();
            }
            return _pathFollowing;
        }

        private NarrativePathFollowingComp _pathFollowing;

        /// <summary>
        /// 移动完成回调。对应 UE5 <c>ANarrativeNPCController::OnMoveComplete</c>（:308-364）。
        /// Flax 的路径结束事件由 <see cref="NarrativePathFollowingComp"/> 转发到这里。
        /// </summary>
        public virtual void OnMoveComplete(EPathFollowingResult result)
        {
            var pathComponent = GetPathFollowingComponent();
            if (pathComponent == null) return;

            pathComponent.OnPathFinished(result);
        }

        /// <summary>把焦点设到指定位置。对应 UE5 <c>SetFocalPoint</c>。</summary>
        public void SetFocalPoint(Vector3 location)
        {
            FocalPoint = location;
            _bHasFocalPoint = true;
        }

        /// <summary>清除焦点。对应 UE5 <c>ClearFocus</c>。</summary>
        public void ClearFocalPoint()
        {
            _bHasFocalPoint = false;
            FocalPoint = Vector3.Zero;
        }

        private bool _bHasFocalPoint;
        private Vector3 FocalPoint;

        /// <summary>
        /// 请求攻击令牌。从目标 ASC 占用一个攻击槽位。
        /// 对应 UE5 <c>ANarrativeNPCController::RequestAttackToken</c>（NarrativeNPCController.cpp:262-273）：
        /// 已持有同一目标的令牌直接成功，否则由目标 ASC 裁决（满槽时可能抢夺他人令牌）。
        /// 返回 true 表示成功占用并可以攻击。
        /// </summary>
        public bool RequestAttackToken(NarrativeAbilitySystemComponent targetToAttack)
        {
            if (targetToAttack == null) return false;

            // 若已有令牌且目标相同，直接返回
            if (GrantedToken == targetToAttack)
            {
                return true;
            }

            // 归还旧令牌（对应 UE5 注释：RequestAttackToken 会自动把上一个令牌还给 ASC）
            if (GrantedToken != null)
            {
                ReturnToken();
            }

            // 由目标的 ASC 做真正的槽位/抢夺判定
            return targetToAttack.TryClaimToken(this);
        }

        /// <summary>
        /// 释放攻击令牌（UE5 侧的等价入口，供行为树/活动节点调用）。
        /// 对应 UE5 <c>ReleaseAttackToken</c>：把令牌还给当前授予方 ASC。
        /// </summary>
        public bool ReleaseAttackToken()
        {
            return ReturnToken();
        }

        /// <summary>
        /// 归还攻击令牌。
        /// 对应 UE5 <c>ANarrativeNPCController::ReturnToken</c>（:275-283）：
        /// 由授予方 ASC 从它的令牌表里把我们摘掉，控制器不自己删。
        /// </summary>
        public bool ReturnToken()
        {
            if (GrantedToken == null) return false;

            var granter = GrantedToken;
            // 先清引用，避免 ASC 侧 ReturnTokenAtIndex 再次回调时重复处理
            GrantedToken = null;
            granter.ReturnToken(this);
            return true;
        }

        /// <summary>
        /// 令牌被其它 NPC 抢走时由 ASC 调用。
        /// 对应 UE5 <c>ANarrativeNPCController::TokenStolen</c>（:285-288，蓝图可实现）。
        /// <para>
        /// UE5 里这个方法留给蓝图覆写；这里把它做成可观测的运行时信号：
        /// 清掉本地令牌引用、置被抢标记、广播事件，
        /// 让行为树/活动层在下一帧重新选目标而不是继续攻击一个已经没槽的位置。
        /// </para>
        /// </summary>
        public void TokenStolen()
        {
            var lostTarget = GrantedToken;
            GrantedToken = null;
            bTokenWasStolen = true;
            TokenStolenTime = Time.GameTime;

            // 令牌被抢说明本次攻击意图作废：把攻击冷却往后推一点，避免立刻回来抢同一个槽
            NextAttackTime = Mathf.Max(NextAttackTime, Time.GameTime + TokenStolenAttackPenalty);

            Blackboard?.SetValue(TokenStolenBlackboardKey, true);
            OnTokenStolen?.Invoke(lostTarget);
        }

        /// <summary>令牌被抢事件（参数为失去的目标 ASC）。</summary>
        public event Action<NarrativeAbilitySystemComponent> OnTokenStolen;

        /// <summary>本控制器的令牌是否被抢过。行为树/活动层可轮询该标记决定是否重新选目标。</summary>
        public bool bTokenWasStolen { get; private set; }

        /// <summary>最近一次令牌被抢的游戏时间（秒）。从未被抢为 -1。</summary>
        public float TokenStolenTime { get; private set; } = -1f;

        /// <summary>令牌被抢黑板键名，供行为树读取。</summary>
        public const string TokenStolenBlackboardKey = "Narrative.TokenStolen";

        /// <summary>令牌被抢后追加的攻击延迟（秒），避免立即回头抢同一个槽。</summary>
        public float TokenStolenAttackPenalty = 0.5f;

        /// <summary>下一次允许攻击的游戏时间（秒）。</summary>
        public float NextAttackTime = 0f;

        /// <summary>令牌被抢标记由上层消费后清零。</summary>
        public void ClearTokenStolenFlag()
        {
            bTokenWasStolen = false;
            Blackboard?.SetValue(TokenStolenBlackboardKey, false);
        }

        /// <summary>当前能否攻击（未被抢令牌挡住且冷却已过）。</summary>
        public bool CanAttackNow()
        {
            return GrantedToken != null && Time.GameTime >= NextAttackTime;
        }

        // ===== 战斗 AI：攻击令牌回路 + 默认装载 + 击杀掉落（工作项 6/7） =====

        /// <summary>
        /// NPC 生成后一次性发放默认物品装载。
        /// 对应 UE5 <c>ANarrativeCharacter::InitNewCharacter_Implementation</c>
        /// （NarrativeCharacter.cpp:735-745：遍历 GetDefaultItemLoadout 并 TryAddFromLootTable）。
        /// UE5 的调用点在 UnrealFramework 角色类中（不在本任务可编辑范围），由控制器在初始化
        /// 阶段做同等发放，并把同一批表注入 ASC 的 <c>DeathLootTables</c> 实现“击杀掉落”。
        /// </summary>
        protected virtual void TryApplyDefaultItemLoadout()
        {
            if (_bDefaultLoadoutApplied || !HasAuthority()) return;

            var npcCharacter = OwnedCharacter != null
                ? OwnedCharacter.GetScript<NarrativePro.UnrealFramework.NarrativeNPCCharacter>()
                : null;
            if (npcCharacter == null || GetNPCData() == null) return;

            // UE5 的发放点在角色 InitNewCharacter（现已落地 UnrealFramework/NarrativeCharacter.cs:796）。
            // 角色侧已发放时控制器不再重复发放；仅当定义只配置到控制器（角色定义未走 SetNPCDefinition）
            // 时，本方法作为兜底路径完成同等发放。
            if (npcCharacter.bDefaultItemLoadoutApplied)
            {
                _bDefaultLoadoutApplied = true;
                return;
            }

            // 定义一到手就只做一次（无论装载表是否为空）
            _bDefaultLoadoutApplied = true;
            npcCharacter.bDefaultItemLoadoutApplied = true;

            var loadout = npcCharacter.GetDefaultItemLoadout();
            if (loadout == null || loadout.Count == 0) return;

            var inventory = OwnedCharacter.GetScript<NarrativePro.Items.NarrativeInventoryComponent>();
            var asc = GetAbilitySystemComponent();
            var results = new List<NarrativePro.Items.ItemAddResult>();

            foreach (var roll in loadout)
            {
                if (roll == null) continue;

                // 发放给 NPC 自身（尸体内含物，对应 UE5 默认装载）
                if (inventory != null)
                {
                    inventory.TryAddFromLootTable(roll, results);
                }

                // 死亡时掷给击杀者的那一份（击杀掉落）
                if (asc != null && !asc.DeathLootTables.Contains(roll))
                {
                    asc.DeathLootTables.Add(roll);
                }
            }
        }

        /// <summary>
        /// 攻击令牌回路（工作项 6）：找最优敌对目标 → <see cref="RequestAttackToken"/>
        /// （内部自动归还旧令牌/走抢夺）→ <see cref="CanAttackNow"/>（含被抢惩罚）→
        /// 按能力频率激活攻击能力 → 成功后 <see cref="ClearTokenStolenFlag"/>。
        /// 对应 UE5 BT/EQS：Service 申请令牌、攻击节点激活能力、退出服务归还令牌。
        /// </summary>
        protected virtual void UpdateCombatAI()
        {
            if (!bAutoCombat || !HasAuthority()) return;

            var asc = GetAbilitySystemComponent();
            if (asc == null || asc.IsDead) return;

            // Narrative.State.NPC.DisableAggro（此前从未被消费）：持有该标签的 NPC 永不主动仇恨/攻击，
            // 并归还在手令牌（对应 UE5 BT 的攻击前置条件门控，友好村民如铁匠不惹事也不出手）。
            if (HasDisableAggroTag(asc))
            {
                if (GrantedToken != null)
                {
                    DisengageCombat();
                }
                return;
            }

            if (Time.GameTime < _nextCombatThinkTime) return;
            _nextCombatThinkTime = Time.GameTime + CombatThinkInterval;

            var attackHandle = asc.FindFirstCombatAbilityHandle();
            if (!attackHandle.IsValid) return;

            var combatAbility = asc.FindCombatAbility(attackHandle);
            float attackRange = ResolveBotAttackRange(combatAbility);

            // 搜索半径大于武器射程：范围外的敌对目标只逼近、不开火（UE5 BT 的 MoveTo 分支）
            float searchRange = attackRange * (CombatSearchRangeMultiplier > 1f ? CombatSearchRangeMultiplier : 1f);
            var target = FindBestAttackTarget(searchRange);
            if (target == null)
            {
                // 无目标：归还令牌并脱离（对应 UE5 BT service 退出分支的 ReturnToken）
                DisengageCombat();
                return;
            }

            var targetActor = target.Actor;
            if (targetActor == null)
            {
                DisengageCombat();
                return;
            }

            // 出手前把朝向转到目标：复用焦点平滑旋转通道，
            // UpdateControlRotation 每帧向 _smoothTargetRotation 做 Slerp（代码生成的 NPC 出生朝向是 Identity）
            SetFocalPoint(targetActor.Position);
            _bCombatFocusSet = true;

            float distance = Vector3.Distance(Actor.Position, targetActor.Position);
            if (distance > attackRange)
            {
                // 超出武器范围：用 AITask_LongMove 逼近到武器范围内再正常出手
                RequestCombatApproach(targetActor, attackRange);
                return;
            }

            // 已进入射程：停掉逼近驱动，避免与攻击节奏抢位置
            _combatMoveTask = null;

            // 申请目标身上的攻击令牌；换目标时 RequestAttackToken 会自动归还旧令牌
            if (!RequestAttackToken(target))
            {
                // 令牌被别人占满：UE5 由 BT 的"等一拍/换掩体"分支处理，端口没有 BT，
                // 所以这里显式让 NPC 就近贴到掩体后等待（前方有掩体才贴，没掩体维持原行为：站着等）。
                TryTakeCoverFromThreat(targetActor);
                return;
            }

            // 令牌在手且未到下次攻击时间（被抢时 TokenStolen 已把 NextAttackTime 推后）
            if (!CanAttackNow())
            {
                TryTakeCoverFromThreat(targetActor);
                return;
            }

            // 能出手了就离开掩体：掩体贴墙会把朝向锁在墙轴上，站着打才符合"探出即射击"的语义
            ReleaseCoverForAttack();

            // UE5 行为门控：未大致转向目标前不开火 —— hitscan 用 Actor.Direction 追踪，
            // 朝向偏差等于永远miss，宁可这一拍不出手。
            if (!IsRoughlyFacing(targetActor)) return;

            if (asc.TryActivateAbility(attackHandle))
            {
                float frequency = combatAbility != null ? combatAbility.GetBotAttackFrequency() : 1f;
                if (frequency <= 0f) frequency = 1f;
                NextAttackTime = Time.GameTime + 1f / frequency;

                // 对应 UE5：真正出手后清除“被抢”标记与黑板值
                if (bTokenWasStolen)
                {
                    ClearTokenStolenFlag();
                }
            }
        }

        /// <summary>
        /// 是否已大致转向目标（朝向与目标夹角的点积阈值）。
        /// 用与 <c>GetTargetDataUsingTrace</c> 完全相同的 <c>Actor.Direction</c> 判定，
        /// 保证门控与实际追踪一致性。
        /// </summary>
        protected virtual bool IsRoughlyFacing(Actor targetActor)
        {
            if (Actor == null || targetActor == null) return false;

            Vector3 toTarget = targetActor.Position - Actor.Position;
            if (toTarget.LengthSquared < 0.0001f) return true; // 贴身直接允许出手

            Vector3 aim = Actor.Direction;
            if (aim.LengthSquared < 0.0001f) aim = Vector3.Forward;

            float dot = Vector3.Dot(Vector3.Normalize(aim), Vector3.Normalize(toTarget));
            float minDot = (float)System.Math.Cos(AttackFacingDegrees * Mathf.DegreesToRadians);
            return dot >= minDot;
        }

        /// <summary>
        /// 超出武器范围时的逼近移动。对应 UE5 BT MoveTo（AcceptanceRadius 语义）：
        /// 走到目标最后已知位置的 80% 武器范围内即停；0.25s 决策节拍会重新评估并按需再发起。
        /// </summary>
        protected virtual void RequestCombatApproach(Actor targetActor, float attackRange)
        {
            if (_combatMoveTask != null && _combatMoveTask.IsRunning) return; // 让本次移动走完再重估

            float acceptance = attackRange * 0.8f;
            if (acceptance < 100f) acceptance = 100f; // 别贴进目标身体
            _combatMoveTask = AITask_LongMove.RunLongMove(this, targetActor.Position, acceptance);
        }

        /// <summary>脱离战斗：归还令牌、取消逼近、清理由战斗持有的焦点。</summary>
        protected virtual void DisengageCombat()
        {
            ReleaseAttackToken();
            _combatMoveTask = null;
            if (_bCombatFocusSet)
            {
                ClearFocalPoint();
                _bCombatFocusSet = false;
            }
        }

        /// <summary>
        /// 检查 ASC 持有者是否带 Narrative.State.NPC.DisableAggro 标签（层级匹配：子标签也算）。
        /// 由 <c>NPCDefinition.DefaultOwnedTags</c> 经角色装配进入 ASC。
        /// </summary>
        /// <summary>
        /// NPC 是否会在"打不了"的那一拍就近贴掩体。UE5 侧掩体是 BT/EQS 驱动的（C++ 里
        /// <c>TryEnterCover</c> 没有调用方），端口没有行为树，所以这条判断显式落在战斗环里。
        /// </summary>
        public bool bUsesCoverInCombat = true;

        /// <summary>
        /// 打不动时（令牌被占满 / 冷却中）尝试就近进掩体。已在掩体里则保持不动。
        /// 调用前 <see cref="SetFocalPoint"/> 已把朝向锁向威胁方向，而
        /// <c>TryEnterCover</c> 是朝**正前方**找掩体，所以贴到的一定是"我与威胁之间"的那面墙。
        /// </summary>
        protected virtual bool TryTakeCoverFromThreat(Actor threat)
        {
            if (!bUsesCoverInCombat || threat == null) return false;

            var movement = GetCharacterMovement();
            if (movement == null) return false;
            if (movement.HasCover()) return true;

            return movement.TryEnterCover();
        }

        /// <summary>能出手时脱开掩体：贴墙会把朝向钉在墙轴上，站着打才符合"探出即射击"。</summary>
        protected virtual void ReleaseCoverForAttack()
        {
            if (!bUsesCoverInCombat) return;
            var movement = GetCharacterMovement();
            if (movement != null && movement.HasCover())
            {
                movement.InvalidateCover();
            }
        }

        /// <summary>取本 NPC 角色的移动组件（掩体贴墙与攀爬/布娃娃共用同一份状态）。</summary>
        protected NarrativePro.Character.NarrativeCharacterMovement GetCharacterMovement()
        {
            return Actor != null ? Actor.GetScript<NarrativePro.Character.NarrativeCharacterMovement>() : null;
        }

        protected virtual bool HasDisableAggroTag(NarrativeAbilitySystemComponent targetASC)
        {
            if (targetASC == null) return false;

            var container = new GameplayTagContainer();
            targetASC.GetOwnedGameplayTags(container);
            if (container.Count == 0) return false;

            var aggroTag = NarrativePro.Core.NarrativeGameplayTags.State_NPC_DisableAggro;
            foreach (var ownedName in container.GetTags())
            {
                if (aggroTag.Matches(new GameplayTag(ownedName))) return true;
            }
            return false;
        }

        /// <summary>Bot 攻击半径：优先武器 GetAttackRange，其次能力默认值（对应 UE5 EQS 距离检查）。</summary>
        protected virtual float ResolveBotAttackRange(NarrativePro.GAS.NarrativeCombatAbility ability)
        {
            float fallback = ability != null ? ability.GetBotAttackRange() : 1000f;
            var weapon = ability != null ? ability.GetAbilityWeapon() : null;
            if (weapon != null)
            {
                float range = weapon.GetAttackRange();
                if (range > 0f) return range;
            }
            return fallback;
        }

        /// <summary>
        /// 战斗选目标查询（UE5 三个 EQS 测试 Team/AttackTokens/AttackPriority 的开关与阈值快照）。
        /// 默认只放行敌对、并要求候选身上还剩攻击令牌槽（满槽的目标先不去碰）；
        /// 距离门由 <see cref="FindBestAttackTarget"/> 每次按当拍搜索半径填入。
        /// </summary>
        public TargetSelectionQuery CombatTargetQuery = NarrativeTargetSelectionRules.CreateCombatQuery();

        /// <summary>
        /// 在攻击半径内找最优可攻击目标 ASC。裁决全部下沉到纯层
        /// <see cref="NarrativeTargetSelectionRules"/>（态度开关、令牌阈值、优先级阈值、距离门、
        /// 同分保留先枚举者），本方法只负责把引擎侧的 Actor/ASC 取成 <see cref="TargetCandidateView"/>。
        /// </summary>
        protected virtual NarrativeAbilitySystemComponent FindBestAttackTarget(float attackRange)
        {
            var myActor = OwnedCharacter;
            if (myActor == null || attackRange <= 0f) return null;

            Physics.OverlapSphere(myActor.Position, attackRange, out Collider[] overlaps, uint.MaxValue, false);
            if (overlaps == null || overlaps.Length == 0) return null;

            var query = CombatTargetQuery;
            query.MaxDistance = attackRange;

            NarrativeAbilitySystemComponent best = null;
            float bestScore = float.MaxValue;
            var seen = new List<NarrativeAbilitySystemComponent>();

            foreach (var collider in overlaps)
            {
                var hitActor = collider;
                if (hitActor == null) continue;

                var targetActor = FindActorWithASC(hitActor);
                if (targetActor == null || ReferenceEquals(targetActor, myActor)) continue;

                var targetASC = targetActor.GetScript<NarrativeAbilitySystemComponent>();
                if (targetASC == null || seen.Contains(targetASC)) continue;
                seen.Add(targetASC);

                var candidate = BuildTargetCandidate(myActor, targetActor, targetASC);
                if (!NarrativeTargetSelectionRules.IsAcceptableTarget(candidate, query)) continue;

                float score = NarrativeTargetSelectionRules.ScoreOf(candidate, query);
                if (GAS.NarrativeAttackTargetScoring.IsBetterCandidate(score, bestScore))
                {
                    bestScore = score;
                    best = targetASC;
                }
            }

            return best;
        }

        /// <summary>
        /// 引擎侧数据 → 规则层纯视图。UE5 由 EQS 的 ItemActor 直接提供这些取值，
        /// 端口把读取集中在这一处，裁决本身不在这里发生（方便 harness 用同一份规则驱动）。
        /// </summary>
        protected virtual TargetCandidateView BuildTargetCandidate(
            Actor myActor, Actor targetActor, NarrativeAbilitySystemComponent targetASC)
        {
            var view = new TargetCandidateView
            {
                Key = targetASC,
                Attitude = Core.ArsenalStatics.GetAttitude(myActor, targetActor),
                bIsSelf = ReferenceEquals(targetActor, myActor),
                bHasAbilitySystem = targetASC != null,
                bIsDead = targetASC != null && targetASC.IsDead,
                bHoldsDisableAggro = HasDisableAggroTag(targetASC),
                bHasCharacterDefinition = targetActor.GetScript<NarrativePro.UnrealFramework.NarrativeCharacter>()
                    ?.GetCharacterDefinition() != null,
                Distance = Vector3.Distance(myActor.Position, targetActor.Position),
                AttackPriority = targetASC != null ? targetASC.GetAttackPriority() : 0f,
                AvailableAttackTokens = targetASC != null ? targetASC.GetAvailableAttackTokens() : 0,
                GrantedAttackTokens = targetASC != null ? targetASC.GetNumGrantedAttackTokens() : 0,
                bAttackerHoldsTokenOnTarget = GrantedToken != null && ReferenceEquals(GrantedToken, targetASC),
            };
            return view;
        }

        /// <summary>沿父链找到挂载 ASC 的角色 Actor（命中体常为角色子碰撞体）。</summary>
        protected virtual Actor FindActorWithASC(Actor from)
        {
            var current = from;
            int guard = 0;
            while (current != null && guard++ < 32)
            {
                if (current.GetScript<NarrativeAbilitySystemComponent>() != null) return current;
                current = current.Parent;
            }
            return null;
        }

        /// <summary>处理死亡事件。对应 UE5 <c>HandleDeath_Implementation</c>（:290-305）。</summary>
        /// <param name="killedActor">被击杀的 Actor</param>
        /// <param name="killedActorASC">其能力系统组件</param>
        /// <param name="bIsDead">是否确认死亡；false 表示只是状态回调，不应触发清理</param>
        public virtual void HandleDeath(Actor killedActor, NarrativeAbilitySystemComponent killedActorASC, bool bIsDead = true)
        {
            // 只有真正死亡才请求清理（对应 UE5 `if (bIsDead)`）
            if (!bIsDead) return;

            // 本控制器只处理自己角色的死亡（事件绑定在自有 ASC 上，这里是防御性校验）
            if (!ReferenceEquals(killedActor, OwnedCharacter) &&
                !ReferenceEquals(killedActorASC, GetAbilitySystemComponent())) return;

            NarrativeLog.LogWarning($"{(HasAuthority() ? "Server" : "Client")} HandleDeath：NPC {GetNPCName()} 请求清理");

            // 对应 UE5 CleanUp(90.f)：留 90 秒给复活/表现流程
            CleanUp(CleanupDelaySeconds);

            // 对应 UE5 InteractionComponent->StopInteractBehavior(false)：
            // 交互侧目前未公开「停止交互行为」入口，这里先取消本控制器持有的攻击令牌，
            // 交互槽位由 PlayerInteractionComponent 在拥有者死亡后自行释放。
            // 需要的钩子：NarrativePro.Interaction.NarrativeInteractableComponent.StopInteractBehavior(bool bIsCancel)

            // 死亡时归还攻击令牌
            ReturnToken();
        }

        /// <summary>死亡后清理延迟（秒）。对应 UE5 写死的 90s。</summary>
        public float CleanupDelaySeconds = 90f;

        /// <summary>清理 NPC 及其角色</summary>
        /// <param name="removePawnDelay">销毁角色的延迟时间（秒）</param>
        public void CleanUp(float removePawnDelay = 0f)
        {
            NarrativeLog.Log($"NPC {GetNPCName()} 清理");
            ReturnToken();
            NPCActivityComponent?.StopCurrentActivity();
            if (removePawnDelay > 0f)
            {
                // 延迟销毁角色
                Destroy(Actor, removePawnDelay);
            }
            else
            {
                Destroy(Actor);
            }
        }

        /// <summary>更新控制旋转</summary>
        /// <param name="deltaTime">帧时间</param>
        /// <param name="bUpdatePawn">是否更新 Pawn 朝向</param>
        protected virtual void UpdateControlRotation(float deltaTime, bool bUpdatePawn)
        {
            if (Actor == null) return;

            // 对应 UE5：有焦点时朝焦点看（序列控制中除外，避免与演出抢朝向）
            if (_bHasFocalPoint && bUpdatePawn)
            {
                Vector3 viewOrigin = Actor.Position + Vector3.Up * ViewHeight;
                Vector3 toFocus = FocalPoint - viewOrigin;
                toFocus.Y = 0f;
                if (toFocus.LengthSquared > 1f)
                {
                    _smoothTargetRotation = Quaternion.LookAt(Vector3.Zero, Vector3.Normalize(toFocus), Vector3.Up);
                    _bHasSmoothTargetRotation = true;
                }
            }

            // 平滑朝向聚焦目标：以显式"已设目标"标志驱动。
            // （旧写法用 _smoothTargetRotation != Quaternion.Identity 判断"是否设过"，
            // 会把正前方 +Z 的焦点目标漏掉——LookAt(+Z) 的旋转本身就是 Identity。）
            if (_bHasSmoothTargetRotation)
            {
                Quaternion current = Actor.Orientation;
                Quaternion next = Quaternion.Slerp(current, _smoothTargetRotation, Mathf.Clamp(SmoothFocusInterpSpeed * deltaTime, 0f, 1f));
                Actor.Orientation = next;
            }
        }

        /// <summary>视角高度（对应 UE5 GetPawnViewLocation 的 eye 高度）。</summary>
        public float ViewHeight = 90f;

        /// <summary>设置平滑聚焦目标旋转</summary>
        public void SetSmoothFocusRotation(Quaternion target)
        {
            _smoothTargetRotation = target;
            _bHasSmoothTargetRotation = true;
        }
    }

    /// <summary>
    /// NPC 黑板。对应 UE5 <c>UBlackboardComponent</c>。
    /// <para>
    /// Flax 无内建行为树/黑板，这里给出活动系统真正需要的可观测能力：
    /// 类型化键值存储 + 变更事件 + 树切换时的复位（<c>CompleteRestart</c>）。
    /// 活动通过 <see cref="NPCActivity.SetupBlackboard"/> 写入目标位置/目标 Actor 等键，
    /// 移动/交互层再读取这些键驱动实际行为。
    /// </summary>
    public class NarrativeNPCBlackboard
    {
        private readonly Dictionary<string, object> _values = new Dictionary<string, object>(StringComparer.Ordinal);

        /// <summary>键变更事件（键名，新值）。</summary>
        public event Action<string, object> OnValueChanged;

        /// <summary>当前活跃的树路径。对应 UE5 黑板所属的 BrainComponent。</summary>
        public string ActiveTreePath { get; private set; } = "";

        public void SetValue(string key, object value)
        {
            if (string.IsNullOrEmpty(key)) return;
            _values.TryGetValue(key, out var previous);
            _values[key] = value;
            if (!ReferenceEquals(previous, value) && (previous == null || !previous.Equals(value)))
            {
                OnValueChanged?.Invoke(key, value);
            }
        }

        public T GetValue<T>(string key, T fallback = default)
        {
            if (string.IsNullOrEmpty(key)) return fallback;
            if (_values.TryGetValue(key, out var value) && value is T typed) return typed;
            return fallback;
        }

        public bool TryGetValue<T>(string key, out T value)
        {
            value = default;
            if (string.IsNullOrEmpty(key)) return false;
            if (_values.TryGetValue(key, out var raw) && raw is T typed)
            {
                value = typed;
                return true;
            }
            return false;
        }

        public bool HasValue(string key) => !string.IsNullOrEmpty(key) && _values.ContainsKey(key);

        public void ClearKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            _values.Remove(key);
        }

        /// <summary>
        /// 树开始时调用：对应 UE5 行为树启动时重建黑板视图。
        /// 保留跨树仍有意义的键（由 <see cref="PersistentKeys"/> 指定）。
        /// </summary>
        public void NotifyTreeStarted(string treePath)
        {
            ActiveTreePath = treePath ?? "";
        }

        /// <summary>树停止时调用。对应 UE5 BrainComponent 卸载。</summary>
        public void NotifyTreeStopped()
        {
            ActiveTreePath = "";
            CompleteRestart();
        }

        /// <summary>
        /// 完全复位。对应 UE5 <c>EBTRestartMode::CompleteRestart</c>：
        /// 清掉所有非持久键，让重启后的树重新写入。
        /// </summary>
        public void CompleteRestart()
        {
            var toRemove = new List<string>();
            foreach (var kvp in _values)
            {
                if (!PersistentKeys.Contains(kvp.Key)) toRemove.Add(kvp.Key);
            }
            foreach (var key in toRemove)
            {
                _values.Remove(key);
            }
        }

        /// <summary>跨树重启仍保留的键。对应 UE5 黑板中设了「持久化」的键。</summary>
        public readonly HashSet<string> PersistentKeys = new HashSet<string>(StringComparer.Ordinal);
    }
}
