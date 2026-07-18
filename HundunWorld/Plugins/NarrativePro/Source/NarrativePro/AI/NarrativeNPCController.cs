using System;
using FlaxEngine;
using NarrativePro.AI.Activities;
using NarrativePro.Core;
using NarrativePro.GAS;
using NarrativePro.Interaction;
using NarrativePro.Items;

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

        /// <summary>平滑聚焦旋转速度</summary>
        public float SmoothFocusInterpSpeed = 30.0f;

        /// <summary>缓存的平滑目标旋转</summary>
        [NonSerialized]
        private Quaternion _smoothTargetRotation = Quaternion.Identity;

        /// <summary>缓存的 NPC 定义路径</summary>
        public string NPCDefinitionPath = "";

        /// <summary>NPC 定义实例（运行时加载）</summary>
        [NonSerialized]
        public NPCDefinition NPCData;

        public override void OnEnable()
        {
            base.OnEnable();
            OwnedCharacter = Actor;

            // 查找活动组件
            NPCActivityComponent = Actor.GetScript<NPCActivityComponent>();
            if (NPCActivityComponent != null)
            {
                NPCActivityComponent.OwnerControllerId = ID.ToString();
            }

            // 查找交互组件
            InteractionComponent = Actor.GetScript<NarrativeInteractableComponent>();
        }

        public override void OnDisable()
        {
            // 返还攻击令牌
            ReturnToken();
            base.OnDisable();
        }

        public override void OnUpdate()
        {
            base.OnUpdate();
            UpdateControlRotation(Time.DeltaTime, true);
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

        /// <summary>
        /// 请求攻击令牌。从目标 ASC 占用一个攻击槽位。
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

            // 归还旧令牌
            if (GrantedToken != null)
            {
                ReturnToken();
            }

            // TODO [需接入 GAS 标签计数系统]: 实现攻击令牌系统（UE5 由 GAS AttackTokens 标签计数管理，Flax 的 GameplayTagContainer 不支持计数）
            // 简化实现：直接占用
            GrantedToken = targetToAttack;
            return true;
        }

        /// <summary>归还攻击令牌</summary>
        public bool ReturnToken()
        {
            if (GrantedToken == null) return false;
            // TODO [需接入 GAS 标签计数系统]: 释放 GAS AttackTokens 标签
            GrantedToken = null;
            return true;
        }

        /// <summary>令牌被抢夺时调用</summary>
        public void TokenStolen()
        {
            GrantedToken = null;
        }

        /// <summary>处理死亡事件</summary>
        public virtual void HandleDeath(Actor killedActor, NarrativeAbilitySystemComponent killedActorASC)
        {
            NarrativeLog.Log($"NPC {GetNPCName()} 死亡事件处理");
            // 停止当前活动
            NPCActivityComponent?.StopCurrentActivity();
            // 归还令牌
            ReturnToken();
        }

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
            // 平滑朝向聚焦目标：若有平滑目标旋转，则用 Slerp 插值 Actor 朝向
            if (_smoothTargetRotation != Quaternion.Identity)
            {
                Quaternion current = Actor.Orientation;
                Quaternion next = Quaternion.Slerp(current, _smoothTargetRotation, Mathf.Clamp(SmoothFocusInterpSpeed * deltaTime, 0f, 1f));
                Actor.Orientation = next;
            }
        }

        /// <summary>设置平滑聚焦目标旋转</summary>
        public void SetSmoothFocusRotation(Quaternion target)
        {
            _smoothTargetRotation = target;
        }
    }
}
