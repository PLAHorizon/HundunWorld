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

        [Header("选择设置")]
        [Tooltip("最大选择距离")]
        public float MaxSelectDistance = 50f;

        [Tooltip("目标高亮颜色")]
        public Color HighlightColor = new Color(1.0f, 1.0f, 0.0f, 0.8f);

        [Tooltip("是否显示选择框")]
        public bool ShowSelectionBox = true;

        [Tooltip("选择框线条宽度")]
        public float SelectionLineWidth = 2.0f;

        [Tooltip("是否启用调试日志")]
        public bool EnableDebugLog = false;

        // 当前选中的目标
        private Actor _currentTarget;

        // 可选择的目标列表
        private List<Actor> _selectableTargets = new List<Actor>();

        // 当前目标索引
        private int _currentTargetIndex = -1;

        // 上次更新目标列表的时间
        private float _lastUpdateTime = 0f;

        // 目标列表更新间隔（秒）
        private const float UpdateInterval = 0.5f;

        public override void OnAwake()
        {
            base.OnAwake();
            _instance = this;

            if (EnableDebugLog)
                Debug.Log("[TargetSelectionSystem] 初始化完成");
        }

        public override void OnUpdate()
        {
            // 定期更新可选择目标列表
            if (Time.GameTime - _lastUpdateTime > UpdateInterval)
            {
                UpdateSelectableTargets();
                _lastUpdateTime = Time.GameTime;
            }

            // 检查当前目标是否仍然有效
            if (_currentTarget != null && !IsValidTarget(_currentTarget))
            {
                DeselectTarget();
            }

            // Tab键切换目标
            if (Input.GetKeyDown(KeyboardKeys.Tab))
            {
                SelectNextTarget();
            }

            // Shift+Tab 反向切换
            if (Input.GetKey(KeyboardKeys.Shift) && Input.GetKeyDown(KeyboardKeys.Tab))
            {
                SelectPreviousTarget();
            }

            // 鼠标左键点击选择（按住Ctrl时）
            if (Input.GetKey(KeyboardKeys.Control) && Input.GetMouseButtonDown(MouseButton.Left))
            {
                TrySelectTargetAtCursor();
            }

            // 取消选择（ESC键）
            if (Input.GetKeyDown(KeyboardKeys.Escape))
            {
                DeselectTarget();
            }

            // 渲染目标高亮
            if (_currentTarget != null && ShowSelectionBox)
            {
                DrawTargetHighlight(_currentTarget);
            }
        }

        /// <summary>
        /// 更新可选择目标列表（敌对单位，距离内）
        /// </summary>
        private void UpdateSelectableTargets()
        {
            _selectableTargets.Clear();

            // 获取玩家位置
            var playerPos = Actor.Position;

            // 查找场景中所有Actor
            var allActors = Level.GetActors<Actor>(true);
            
            foreach (var actor in allActors)
            {
                // 跳过自己
                if (actor == Actor)
                    continue;

                // 检查是否是敌对单位
                if (!IsHostile(actor))
                    continue;

                // 检查是否存活
                if (!IsAlive(actor))
                    continue;

                // 检查距离
                float distance = Vector3.Distance(playerPos, actor.Position);
                if (distance <= MaxSelectDistance)
                {
                    _selectableTargets.Add(actor);
                }
            }

            // 按距离排序（近到远）
            _selectableTargets.Sort((a, b) =>
            {
                float distA = Vector3.Distance(playerPos, a.Position);
                float distB = Vector3.Distance(playerPos, b.Position);
                return distA.CompareTo(distB);
            });

            if (EnableDebugLog && _selectableTargets.Count > 0)
                Debug.Log($"[TargetSelectionSystem] 找到 {_selectableTargets.Count} 个可选目标");
        }

        /// <summary>
        /// 选择下一个目标（Tab键）
        /// </summary>
        private void SelectNextTarget()
        {
            if (_selectableTargets.Count == 0)
            {
                if (EnableDebugLog)
                    Debug.Log("[TargetSelectionSystem] 没有可选择的目标");
                return;
            }

            _currentTargetIndex = (_currentTargetIndex + 1) % _selectableTargets.Count;
            _currentTarget = _selectableTargets[_currentTargetIndex];

            if (EnableDebugLog)
                Debug.Log($"[TargetSelectionSystem] 切换目标: {_currentTarget.Name} ({_currentTargetIndex + 1}/{_selectableTargets.Count})");
            
            // 触发目标切换事件
            OnTargetChanged?.Invoke(_currentTarget);
        }

        /// <summary>
        /// 选择上一个目标（Shift+Tab）
        /// </summary>
        private void SelectPreviousTarget()
        {
            if (_selectableTargets.Count == 0)
            {
                if (EnableDebugLog)
                    Debug.Log("[TargetSelectionSystem] 没有可选择的目标");
                return;
            }

            _currentTargetIndex--;
            if (_currentTargetIndex < 0)
                _currentTargetIndex = _selectableTargets.Count - 1;

            _currentTarget = _selectableTargets[_currentTargetIndex];

            if (EnableDebugLog)
                Debug.Log($"[TargetSelectionSystem] 切换目标: {_currentTarget.Name} ({_currentTargetIndex + 1}/{_selectableTargets.Count})");
            
            OnTargetChanged?.Invoke(_currentTarget);
        }

        /// <summary>
        /// 尝试选择鼠标指向的目标
        /// </summary>
        private void TrySelectTargetAtCursor()
        {
            var camera = Camera.MainCamera;
            if (camera == null) return;

            // 从鼠标位置发射射线
            var mousePos = Input.MousePosition;
            var ray = camera.ConvertMouseToRay(mousePos);
            
            // 射线检测
            if (Physics.RayCast(ray.Position, ray.Direction, out RayCastHit hit, MaxSelectDistance))
            {
                // 获取碰撞的Actor
                Actor hitActor = null;
                if (hit.Collider != null)
                {
                    // 尝试获取父级Actor
                    hitActor = hit.Collider.Parent as Actor;
                }
                
                if (hitActor != null && IsHostile(hitActor) && IsAlive(hitActor))
                {
                    _currentTarget = hitActor;
                    _currentTargetIndex = _selectableTargets.IndexOf(hitActor);
                    
                    if (EnableDebugLog)
                        Debug.Log($"[TargetSelectionSystem] 点击选择目标: {hitActor.Name}");
                    
                    OnTargetChanged?.Invoke(_currentTarget);
                }
            }
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
        /// 绘制目标高亮
        /// </summary>
        private void DrawTargetHighlight(Actor target)
        {
            try
            {
                // 获取目标包围盒
                var center = target.Position;
                
                // 估算目标大小（如果有碰撞体）
                float radius = 1.0f;
                var collider = target.GetChild<Collider>();
                if (collider != null)
                {
                    // 使用Actor的包围盒作为替代
                    var bounds = target.Box;
                    radius = Math.Max(bounds.Size.X, bounds.Size.Z) * 0.5f;
                }

                // 绘制地面选择圈
                DebugDraw.DrawCircle(center, Vector3.Up, radius, HighlightColor, 0.0f);

                // 绘制第二个圈（动态效果）
                float pulseRadius = radius + Mathf.Sin(Time.GameTime * 3.0f) * 0.2f;
                DebugDraw.DrawCircle(center, Vector3.Up, pulseRadius, 
                    new Color(HighlightColor.R, HighlightColor.G, HighlightColor.B, HighlightColor.A * 0.5f), 0.0f);

                // 绘制头顶箭头指示
                Vector3 arrowPos = center + new Vector3(0, radius * 2.0f + 1.0f, 0);
                Vector3 arrowEnd = arrowPos - new Vector3(0, 0.8f, 0);
                DebugDraw.DrawLine(arrowPos, arrowEnd, HighlightColor, 0.0f);
                
                // 箭头顶点
                Vector3 arrowLeft = arrowEnd + new Vector3(-0.3f, 0.3f, 0);
                Vector3 arrowRight = arrowEnd + new Vector3(0.3f, 0.3f, 0);
                DebugDraw.DrawLine(arrowEnd, arrowLeft, HighlightColor, 0.0f);
                DebugDraw.DrawLine(arrowEnd, arrowRight, HighlightColor, 0.0f);

                // 在目标上方显示名称
                var textPos = center + new Vector3(0, radius * 2.0f + 1.5f, 0);
                DebugDraw.DrawText(target.Name, textPos, HighlightColor, 12, 0.0f);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[TargetSelectionSystem] 绘制高亮失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 判断是否是敌对单位
        /// </summary>
        private bool IsHostile(Actor actor)
        {
            // 方法1: 通过Tag判断
            if (actor.Tag == "Enemy" || actor.Tag == "Monster" || actor.Tag == "Boss")
                return true;

            // 方法2: 通过脚本判断（如果有敌对标识组件）
            // var enemyComponent = actor.GetScript<EnemyComponent>();
            // if (enemyComponent != null)
            //     return true;

            // 方法3: 通过层级判断
            if (actor.Layer == 8) // 假设Layer 8 是敌对单位层
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
        /// 检查目标是否仍然有效
        /// </summary>
        private bool IsValidTarget(Actor target)
        {
            if (target == null || !target.IsActive)
                return false;

            if (!IsAlive(target))
                return false;

            // 检查距离
            float distance = Vector3.Distance(Actor.Position, target.Position);
            if (distance > MaxSelectDistance * 1.5f) // 给一点容差
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
