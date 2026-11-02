using FlaxEngine;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HundunWorld.Game.Combat
{
    /// <summary>
    /// 目标选择系统
    /// 支持Tab键切换、鼠标点击选择、距离筛选
    /// </summary>
    public class TargetSelectionSystem : Script
    {
        private static TargetSelectionSystem _instance;
        public static TargetSelectionSystem Instance => _instance;

        /// <summary>
        /// 技能目标的最大生效距离（厘米；Flax 单位是厘米）。
        /// 原先按"米"写成 50f，在厘米制下等于 0.5 米，目标一离开脚下就判失效。
        /// </summary>
        [Header("选择设置")]
        [Tooltip("最大选择距离（厘米）")]
        public float MaxSelectDistance = 6000f;

        [Tooltip("是否启用调试日志")]
        public bool EnableDebugLog = false;

        // 当前选中的目标
        private Actor _currentTarget;

        /// <summary>可选敌对目标列表，由 <see cref="SetSelectableTargets"/> 发布（本系统不再自行扫描场景）。</summary>
        private List<Actor> _selectableTargets = new List<Actor>();

        // 当前目标索引
        private int _currentTargetIndex = -1;

        public override void OnAwake()
        {
            base.OnAwake();
            _instance = this;

            if (EnableDebugLog)
                Debug.Log("[TargetSelectionSystem] 初始化完成");
        }

        public override void OnUpdate()
        {
            // 输入绑定（Tab / Shift+Tab / Ctrl+左键 / ESC）与脚下高亮环已全部迁到
            // HundunWorld.Game.Selection.WorldSelectionService：
            //   - Tab 单键归第三人称相机（ThirdPersonCamera.cs 的 LockOn 开关），此处再抢一次会出现
            //     "按 Tab 既换目标又开关镜头锁定"；目标循环改走 Shift+Tab。
            //   - 原每 0.5 秒 Level.GetActors 全场景扫描按 Tag=="Monster"/Layer 8 判敌对，而全仓没有
            //     任何 Actor 带这些 Tag，扫描结果恒空；敌对判定现在由 SelectableEntityTag 提供。

            // 检查当前目标是否仍然有效
            if (_currentTarget != null && !IsValidTarget(_currentTarget))
            {
                DeselectTarget();
            }
        }

        /// <summary>
        /// 由选中系统发布"当前可选的敌对目标"有序列表（按距离升序），
        /// 供 <see cref="GetNearestEnemy"/> / <see cref="GetAllSelectableTargets"/> 消费。
        /// </summary>
        public void SetSelectableTargets(List<Actor> targets)
        {
            _selectableTargets.Clear();
            if (targets != null)
                _selectableTargets.AddRange(targets);

            if (_currentTarget != null)
                _currentTargetIndex = _selectableTargets.IndexOf(_currentTarget);
        }

        /// <summary>
        /// 取消选择
        /// </summary>
        public void DeselectTarget()
        {
            if (_currentTarget != null)
            {
                if (EnableDebugLog)
                    Debug.Log("[TargetSelectionSystem] 取消目标选择");
                
                _currentTarget = null;
                _currentTargetIndex = -1;
                OnTargetChanged?.Invoke(null);
            }
        }

        /// <summary>
        /// 手动设置目标
        /// </summary>
        public void SetTarget(Actor target)
        {
            if (target == null)
            {
                DeselectTarget();
                return;
            }

            if (IsHostile(target) && IsAlive(target))
            {
                _currentTarget = target;
                _currentTargetIndex = _selectableTargets.IndexOf(target);
                
                if (EnableDebugLog)
                    Debug.Log($"[TargetSelectionSystem] 手动设置目标: {target.Name}");
                
                OnTargetChanged?.Invoke(_currentTarget);
            }
        }

        /// <summary>
        /// 判断是否是敌对单位。
        /// <para>
        /// 主判据是 <see cref="HundunWorld.Game.Selection.SelectableEntityTag"/> 的 Kind——该标记由
        /// FlaxActorSyncSystem 按服务端下发的 EntityType 挂载，是网络层的事实源。
        /// 原先只认 <c>Actor.Tag</c> 与 Layer 8，而全仓没有任何 Actor 带这些 Tag/Layer，
        /// 导致 <see cref="SetTarget"/> 恒拒绝、技能永远取不到目标。Tag 判据保留给手工摆放的场景怪。
        /// </para>
        /// </summary>
        private bool IsHostile(Actor actor)
        {
            if (actor == null)
                return false;

            var tag = actor.GetScript<HundunWorld.Game.Selection.SelectableEntityTag>();
            if (tag != null)
                return tag.Kind == HundunWorld.Game.Selection.SelectableKind.Monster;

            if (actor.Tag == "Enemy" || actor.Tag == "Monster" || actor.Tag == "Boss")
                return true;

            if (actor.Layer == 8) // 约定 Layer 8 = 敌对单位层
                return true;

            return false;
        }

        /// <summary>
        /// 判断是否存活
        /// </summary>
        private bool IsAlive(Actor actor)
        {
            // 基础检查：Actor是否激活
            if (actor == null || !actor.IsActive)
                return false;

            // 检查是否有生命值组件（需要集成实际系统）
            // var healthComponent = actor.GetScript<HealthComponent>();
            // if (healthComponent != null)
            //     return healthComponent.CurrentHealth > 0;

            // 默认认为激活的Actor是存活的
            return true;
        }

        /// <summary>
        /// 检查目标是否仍然有效：**只判实体是否还存在**。
        /// <para>
        /// 原先还有一条"离玩家超过 MaxSelectDistance×1.5 就自动丢目标"，但选中语义是粘住的——
        /// 玩家没改选、没点空地之前，走远不该让目标凭空消失（表现上就是"打着打着目标自己没了"）。
        /// 目标真正消失由 AOI 决定：服务端按 chunk 订阅裁剪视野，实体离开 AOI 时下发 Despawn，
        /// FlaxActorSyncSystem 销毁对应 Actor，这里在下一帧自然判为无效。
        /// </para>
        /// </summary>
        private bool IsValidTarget(Actor target)
        {
            if (target == null || !target.IsActive)
                return false;

            if (!IsAlive(target))
                return false;

            return true;
        }

        /// <summary>
        /// 获取最近的敌对目标
        /// </summary>
        public Actor GetNearestEnemy()
        {
            return _selectableTargets.FirstOrDefault();
        }

        /// <summary>
        /// 获取所有可选目标
        /// </summary>
        public List<Actor> GetAllSelectableTargets()
        {
            return new List<Actor>(_selectableTargets);
        }

        /// <summary>
        /// 目标切换事件
        /// </summary>
        public event Action<Actor> OnTargetChanged;

        /// <summary>
        /// 获取当前目标
        /// </summary>
        public Actor CurrentTarget => _currentTarget;

        /// <summary>
        /// 是否有选中目标
        /// </summary>
        public bool HasTarget => _currentTarget != null;

        public override void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
            
            base.OnDestroy();
        }
    }
}
