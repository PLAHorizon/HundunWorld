using FlaxEngine;
using System;
using System.Collections.Generic;
using HundunWorld.Game.UI.GameMain;

namespace HundunWorld.Game.Combat
{
    /// <summary>
    /// 技能释放助手
    /// 简化技能释放流程，处理目标选择、范围指示、技能校验等
    /// </summary>
    public class SkillCastHelper
    {
        private static SkillCastHelper _instance;
        public static SkillCastHelper Instance => _instance ??= new SkillCastHelper();

        // 系统引用
        private TargetSelectionSystem _targetSystem;
        private AOEIndicatorSystem _aoeIndicator;
        private CombatHUDManager _hudManager;
        private CombatSystemManager _combatManager;

        // 当前正在准备的技能
        private SkillInfo _preparingSkill;
        private bool _waitingForAOEConfirm = false;

        // 玩家实体ID
        private ulong _playerEntityId = 0;

        private SkillCastHelper()
        {
            Debug.Log("[SkillCastHelper] 初始化");
        }

        /// <summary>
        /// 初始化系统引用
        /// </summary>
        public void Initialize(ulong playerEntityId)
        {
            _playerEntityId = playerEntityId;

            _targetSystem = TargetSelectionSystem.Instance;
            _aoeIndicator = AOEIndicatorSystem.Instance;
            _hudManager = CombatHUDManager.Instance;
            _combatManager = CombatSystemManager.Instance;

            if (_targetSystem == null)
                Debug.LogWarning("[SkillCastHelper] 未找到目标选择系统");
            if (_aoeIndicator == null)
                Debug.LogWarning("[SkillCastHelper] 未找到AOE指示器系统");
            if (_hudManager == null)
                Debug.LogWarning("[SkillCastHelper] 未找到CombatHUDManager");
            if (_combatManager == null)
                Debug.LogWarning("[SkillCastHelper] 未找到CombatSystemManager");

            Debug.Log($"[SkillCastHelper] 初始化完成，玩家实体ID: {playerEntityId}");
        }

        /// <summary>
        /// 释放技能（主入口）
        /// </summary>
        public bool CastSkill(SkillInfo skill)
        {
            if (skill == null)
            {
                Debug.LogWarning("[SkillCastHelper] 技能为空");
                return false;
            }

            Debug.Log($"[SkillCastHelper] 尝试释放技能: {skill.Name}");

            // 根据技能类型选择释放方式
            if (IsAOESkill(skill))
            {
                return PrepareAOESkill(skill);
            }
            else
            {
                return CastSingleTargetSkill(skill);
            }
        }

        /// <summary>
        /// 释放单体目标技能
        /// </summary>
        private bool CastSingleTargetSkill(SkillInfo skill)
        {
            // 获取当前目标
            var target = _targetSystem?.CurrentTarget;
            if (target == null)
            {
                ShowError("请先选择目标");
                return false;
            }

            // TODO: 从Actor获取实体ID（需要实现Actor到EntityId的映射）
            ulong targetEntityId = 0; // 临时值

            // 创建攻击动作
            var attack = new AttackAction
            {
                AttackerId = _playerEntityId,
                DefenderId = targetEntityId,
                Skill = skill,
                AttackPosition = target.Position
            };

            // 执行攻击
            var result = _combatManager?.ProcessAttack(attack);
            if (result != null && result.IsSuccess)
            {
                // 添加战斗日志
                if (result.DamageResult.IsCritical)
                {
                    _hudManager?.AddCombatLog(CombatLogType.Critical,
                        $"暴击! 对 {target.Name} 造成 {result.ActualDamage:F0} 点伤害!");
                }
                else
                {
                    _hudManager?.AddCombatLog(CombatLogType.Damage,
                        $"对 {target.Name} 造成 {result.ActualDamage:F0} 点伤害");
                }

                Debug.Log($"[SkillCastHelper] 技能释放成功: {skill.Name}");
                return true;
            }
            else
            {
                ShowError(result?.ErrorMessage ?? "技能释放失败");
                return false;
            }
        }

        /// <summary>
        /// 准备AOE技能（显示范围指示器）
        /// </summary>
        private bool PrepareAOESkill(SkillInfo skill)
        {
            _preparingSkill = skill;
            _waitingForAOEConfirm = true;

            // 显示AOE指示器
            var indicatorShape = GetIndicatorShape(skill);
            _hudManager?.ShowAOEIndicator(
                indicatorShape,
                skill.BaseDamage, // 临时使用BaseDamage作为半径
                90f, // 默认角度
                0f,  // 默认长度
                skill.Range
            );

            Debug.Log($"[SkillCastHelper] 等待AOE技能确认: {skill.Name}");
            return true;
        }

        /// <summary>
        /// 确认AOE技能释放
        /// </summary>
        public void ConfirmAOESkill()
        {
            if (!_waitingForAOEConfirm || _preparingSkill == null)
                return;

            // 检查是否在有效范围内
            if (!_hudManager.IsAOEInRange())
            {
                ShowError("目标位置超出技能范围");
                return;
            }

            // 获取AOE位置
            Vector3 targetPosition = _hudManager.GetAOEIndicatorPosition();

            // 查找范围内的所有敌人
            var enemiesInRange = FindEnemiesInRadius(targetPosition, _preparingSkill.BaseDamage);

            int hitCount = 0;
            foreach (var enemyId in enemiesInRange)
            {
                var attack = new AttackAction
                {
                    AttackerId = _playerEntityId,
                    DefenderId = enemyId,
                    Skill = _preparingSkill,
                    AttackPosition = targetPosition
                };

                var result = _combatManager?.ProcessAttack(attack);
                if (result != null && result.IsSuccess)
                {
                    hitCount++;
                }
            }

            // 添加战斗日志
            _hudManager?.AddCombatLog(CombatLogType.Skill,
                $"使用 {_preparingSkill.Name} 命中 {hitCount} 个目标");

            // 隐藏指示器
            _hudManager?.HideAOEIndicator();
            _waitingForAOEConfirm = false;
            _preparingSkill = null;

            Debug.Log($"[SkillCastHelper] AOE技能释放完成，命中 {hitCount} 个目标");
        }

        /// <summary>
        /// 取消AOE技能
        /// </summary>
        public void CancelAOESkill()
        {
            if (!_waitingForAOEConfirm)
                return;

            _hudManager?.HideAOEIndicator();
            _waitingForAOEConfirm = false;
            _preparingSkill = null;

            Debug.Log("[SkillCastHelper] 取消AOE技能");
        }

        /// <summary>
        /// 更新（在主循环中调用）
        /// </summary>
        public void Update()
        {
            if (!_waitingForAOEConfirm)
                return;

            // 检查确认输入（鼠标左键）
            if (Input.GetMouseButtonDown(MouseButton.Left))
            {
                ConfirmAOESkill();
            }

            // 检查取消输入（ESC或鼠标右键）
            if (Input.GetKeyDown(KeyboardKeys.Escape) || Input.GetMouseButtonDown(MouseButton.Right))
            {
                CancelAOESkill();
            }
        }

        /// <summary>
        /// 判断是否是AOE技能
        /// </summary>
        private bool IsAOESkill(SkillInfo skill)
        {
            // TODO: 根据技能类型判断
            // 临时实现：BaseDamage > 0 表示AOE
            return skill.BaseDamage > 2f;
        }

        /// <summary>
        /// 获取指示器形状
        /// </summary>
        private AOEIndicatorSystem.IndicatorShape GetIndicatorShape(SkillInfo skill)
        {
            // TODO: 根据技能类型返回不同形状
            // 临时实现：都返回圆形
            return AOEIndicatorSystem.IndicatorShape.Circle;
        }

        /// <summary>
        /// 查找半径内的所有敌人
        /// </summary>
        private List<ulong> FindEnemiesInRadius(Vector3 center, float radius)
        {
            // TODO: 实现真实的范围查找
            // 临时返回空列表
            return new List<ulong>();
        }

        /// <summary>
        /// 显示错误消息
        /// </summary>
        private void ShowError(string message)
        {
            Debug.LogWarning($"[SkillCastHelper] {message}");
            _hudManager?.AddCombatLog(CombatLogType.Info, message);
        }

        /// <summary>
        /// 是否正在等待AOE确认
        /// </summary>
        public bool IsWaitingForAOEConfirm => _waitingForAOEConfirm;

        /// <summary>
        /// 设置玩家实体ID
        /// </summary>
        public void SetPlayerEntityId(ulong entityId)
        {
            _playerEntityId = entityId;
            Debug.Log($"[SkillCastHelper] 更新玩家实体ID: {entityId}");
        }
    }
}
