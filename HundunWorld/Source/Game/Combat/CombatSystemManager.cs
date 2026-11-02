using FlaxEngine;
using System;
using System.Collections.Generic;
using HundunWorld.Game.Combat.Effects;

namespace HundunWorld.Game.Combat
{
    /// <summary>
    /// 战斗状态与技能效果的宿主：每帧推进效果计时、维护"是否在战斗中"的状态表。
    /// <para>
    /// 这里刻意只剩这两件事。原先的攻击结算链（<c>ProcessAttack</c> / <c>AttackAction</c> /
    /// <c>CombatActionResult</c> / <c>DamageCalculator</c> / <c>SkillInfo</c> / <c>PendingAction</c>）
    /// 已整体删除：它的唯一调用方 <c>CombatSystemCoordinator</c> 与 <c>SkillCastHelper</c> 本身就是
    /// 零引用死链，而伤害与五行的权威结算在 CombatGrain / CharacterGrain 一侧（ElementalDamageResolver）。
    /// 客户端再留一套本地结算，只会让人误以为屏幕上的数字也算数——
    /// 其中 <c>ConvertToCombatStats</c> 甚至把十个五行字段一律写死成 0。
    /// </para>
    /// </summary>
    public class CombatSystemManager
    {
        private static CombatSystemManager _instance;
        public static CombatSystemManager Instance => _instance ??= new CombatSystemManager();

        private readonly SkillEffectSystem _effectSystem;
        private readonly Dictionary<ulong, CombatState> _combatStates;

        /// <summary>
        /// 已经处理过死亡的实体。同一次死亡会先到 <c>DamageMessage</c>（剩余血量归零）再到
        /// <c>DeathMessage</c>，两条都接 here 的话击杀日志会写两遍、选中态会被清两次。
        /// 实体重新进入战斗（<see cref="GetCombatState"/> 建新状态）时撤掉标记。
        /// </summary>
        private readonly HashSet<ulong> _deathHandled = new HashSet<ulong>();

        private CombatSystemManager()
        {
            _effectSystem = SkillEffectSystem.Instance;
            _combatStates = new Dictionary<ulong, CombatState>();
        }

        /// <summary>
        /// 更新战斗系统：推进效果计时、递减技能冷却、清理过期战斗状态。
        /// </summary>
        public void Update(float deltaTime)
        {
            _effectSystem.UpdateEffects(deltaTime);
            UpdateSkillCooldowns(deltaTime);
            CleanupExpiredCombatStates();
        }

        /// <summary>
        /// 更新技能冷却时间
        /// </summary>
        private void UpdateSkillCooldowns(float deltaTime)
        {
            foreach (var kvp in _combatStates)
            {
                var state = kvp.Value;
                var cooldownsToRemove = new List<int>();

                foreach (var cooldown in state.SkillCooldowns)
                {
                    var remaining = cooldown.Value - deltaTime;
                    if (remaining <= 0)
                        cooldownsToRemove.Add(cooldown.Key);
                    else
                        state.SkillCooldowns[cooldown.Key] = remaining;
                }

                foreach (var skillId in cooldownsToRemove)
                    state.SkillCooldowns.Remove(skillId);
            }
        }

        /// <summary>
        /// 清理过期的战斗状态：10 秒无战斗行为即脱战，30 秒后整条状态丢弃。
        /// </summary>
        private void CleanupExpiredCombatStates()
        {
            var currentTime = Time.GameTime;
            var statesToRemove = new List<ulong>();

            foreach (var kvp in _combatStates)
            {
                var state = kvp.Value;
                if (currentTime - state.LastCombatTime > 10.0f)
                {
                    state.IsInCombat = false;
                    state.ComboCount = 0;

                    if (currentTime - state.LastCombatTime > 30.0f)
                        statesToRemove.Add(kvp.Key);
                }
            }

            foreach (var entityId in statesToRemove)
            {
                _combatStates.Remove(entityId);
                Debug.Log($"[CombatSystemManager] 清理实体 {entityId} 的战斗状态");
            }
        }

        private CombatState GetCombatState(ulong entityId)
        {
            if (!_combatStates.ContainsKey(entityId))
            {
                _combatStates[entityId] = new CombatState();
                // 复活/重新参战：允许再次处理它的死亡。
                _deathHandled.Remove(entityId);
            }
            return _combatStates[entityId];
        }

        /// <summary>
        /// 获取实体的战斗状态信息
        /// </summary>
        public CombatStateInfo GetCombatStateInfo(ulong entityId)
        {
            var state = GetCombatState(entityId);
            return new CombatStateInfo
            {
                IsInCombat = state.IsInCombat,
                ComboCount = state.ComboCount,
                ActiveCooldowns = new Dictionary<int, float>(state.SkillCooldowns)
            };
        }

        /// <summary>
        /// 实体死亡事件。CombatHUDManager 用它清除已死目标的选中态、
        /// CombatLogUI 用它写击杀日志。
        /// <para>
        /// 触发方是网络侧的死亡处理：<c>DamageResponseHandler</c> 在收到"剩余血量归零"的伤害包
        /// 与独立死亡包时调用 <see cref="HandleEntityDeath"/>。这条链以前是断的——事件只有订阅、
        /// 没有任何 raise，所以"目标死后仍被选中"的现象一直没被修好。
        /// </para>
        /// </summary>
        public event Action<ulong, ulong> EntityDied;

        /// <summary>
        /// 处理实体死亡：清效果、丢状态、通知订阅者。同一次死亡只处理一遍——
        /// "血量归零的伤害包"和随后的"独立死亡包"会先后到达，不去重就会写两遍击杀日志。
        /// </summary>
        public void HandleEntityDeath(ulong entityId, ulong killerId)
        {
            if (!_deathHandled.Add(entityId)) return;

            Debug.Log($"[CombatSystemManager] 实体 {entityId} 被 {killerId} 击杀");

            _effectSystem.ClearAllEffects(entityId);

            if (_combatStates.ContainsKey(entityId))
                _combatStates.Remove(entityId);

            EntityDied?.Invoke(entityId, killerId);
        }
    }

    /// <summary>
    /// 战斗状态
    /// </summary>
    public class CombatState
    {
        public bool IsInCombat { get; set; }
        public float LastCombatTime { get; set; }
        public float LastAttackTime { get; set; }
        public int ComboCount { get; set; }
        public ulong LastAttackerId { get; set; }
        public int PreviousSkillId { get; set; }
        public Dictionary<int, float> SkillCooldowns { get; set; } = new Dictionary<int, float>();
    }

    /// <summary>
    /// 战斗状态信息（对外暴露）
    /// </summary>
    public class CombatStateInfo
    {
        public bool IsInCombat { get; set; }
        public int ComboCount { get; set; }
        public Dictionary<int, float> ActiveCooldowns { get; set; }
    }
}
