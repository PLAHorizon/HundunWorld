using FlaxEngine;
using HundunWorld.Game;

namespace Game
{
    /// <summary>
    /// 相机碰撞调试工具
    /// 用于诊断和可视化相机碰撞检测系统
    /// </summary>
    public class CameraCollisionDebugger : Script
    {
        [Header("引用")]
        [Tooltip("要调试的第三人称相机")]
        public ThirdPersonCamera CameraToDebug;
        
        [Header("可视化设置")]
        [Tooltip("启用射线可视化")]
        public bool EnableRayVisualization = true;
        
        [Tooltip("射线颜色（无碰撞）")]
        public Color RayColorNormal = Color.Green;
        
        [Tooltip("射线颜色（有碰撞）")]
        public Color RayColorHit = Color.Red;
        
        [Tooltip("启用碰撞点标记")]
        public bool ShowHitPoints = true;
        
        [Tooltip("碰撞点标记大小")]
        public float HitPointSize = 0.2f;
        
        [Header("调试信息")]
        [Tooltip("显示详细日志")]
        public bool ShowDetailedLogs = true;
        
        [Tooltip("每帧都输出日志")]
        public bool LogEveryFrame = false;
        
        public override void OnStart()
        {
            if (CameraToDebug == null)
            {
                CameraToDebug = Actor.GetScript<ThirdPersonCamera>();
                if (CameraToDebug == null)
                {
                    Debug.LogError("[CameraDebugger] 未找到ThirdPersonCamera脚本！");
                    Enabled = false;
                    return;
                }
            }
            
            Debug.Log("[CameraDebugger] 初始化完成");
            LogCurrentSettings();
        }
        
        public override void OnUpdate()
        {
            if (CameraToDebug == null || CameraToDebug.Target == null) return;
            
            if (LogEveryFrame)
            {
                LogCurrentSettings();
            }
            
            // 可视化碰撞检测
            if (EnableRayVisualization)
            {
                VisualizeCollisionRays();
            }
        }
        
        /// <summary>
        /// 输出当前设置
        /// </summary>
        private void LogCurrentSettings()
        {
            if (!ShowDetailedLogs) return;
            
            Debug.Log("=== 相机碰撞系统设置 ===");
            Debug.Log($"启用碰撞检测: {CameraToDebug.EnableCameraCollision}");
            Debug.Log($"启用智能避障: {CameraToDebug.EnableSmartAvoidance}");
            Debug.Log($"射线数量: {CameraToDebug.CollisionRayCount}");
            Debug.Log($"碰撞半径: {CameraToDebug.CollisionRadius}");
            Debug.Log($"碰撞层级掩码: {CameraToDebug.CollisionLayerMask}");
            Debug.Log($"自动排除角色: {CameraToDebug.AutoExcludeTargetCollision}");
            Debug.Log($"最小距离: {CameraToDebug.MinDistance}");
            Debug.Log($"最大距离: {CameraToDebug.MaxDistance}");
            Debug.Log($"当前距离: {CameraToDebug.Distance}");
            Debug.Log($"智能避障角度: {CameraToDebug.SmartAvoidancePitch}°");
            Debug.Log("========================");
        }
        
        /// <summary>
        /// 可视化碰撞射线
        /// </summary>
        private void VisualizeCollisionRays()
        {
            if (CameraToDebug.Target == null) return;
            
            Vector3 focusPoint = CameraToDebug.Target.Position + CameraToDebug.FocusOffset;
            Vector3 cameraPos = CameraToDebug.Actor.Position;
            Vector3 direction = (cameraPos - focusPoint).Normalized;
            float distance = CameraToDebug.Distance;
            
            // 绘制主射线
            RayCastHit hit;
            bool hasHit = Physics.RayCast(focusPoint, direction, out hit, distance, CameraToDebug.CollisionLayerMask);
            
            Color rayColor = hasHit ? RayColorHit : RayColorNormal;
            DebugDraw.DrawLine(focusPoint, focusPoint + direction * distance, rayColor, 0, false);
            
            if (hasHit && ShowHitPoints)
            {
                DebugDraw.DrawSphere(new BoundingSphere(hit.Point, HitPointSize), Color.Yellow, 0, false);
                
                // 显示碰撞信息
                string hitName = "Unknown";
                if (hit.Collider != null)
                {
                    var actor = hit.Collider.AttachedRigidBody?.Parent ?? hit.Collider.Parent;
                    if (actor != null)
                    {
                        hitName = actor.Name;
                    }
                }
                
                DebugDraw.DrawText(
                   $"Hit: {hitName}\nDist: {hit.Distance:F2}m",
                   hit.Point + Vector3.Up * 0.5f,                    
                    Color.White,
                    0
                );
            }
        }
        
        /// <summary>
        /// 手动触发碰撞检测测试
        /// </summary>
        public void TestCollisionDetection()
        {
            if (CameraToDebug == null || CameraToDebug.Target == null)
            {
                Debug.LogWarning("[CameraDebugger] 无法测试：相机或目标为空");
                return;
            }
            
            Vector3 focusPoint = CameraToDebug.Target.Position + CameraToDebug.FocusOffset;
            Vector3 cameraPos = CameraToDebug.Actor.Position;
            Vector3 direction = (cameraPos - focusPoint).Normalized;
            
            Debug.Log("=== 碰撞检测测试 ===");
            Debug.Log($"聚焦点: {focusPoint}");
            Debug.Log($"相机位置: {cameraPos}");
            Debug.Log($"方向: {direction}");
            Debug.Log($"检测距离: {CameraToDebug.Distance}");
            
            RayCastHit hit;
            bool hasHit = Physics.RayCast(focusPoint, direction, out hit, CameraToDebug.Distance, CameraToDebug.CollisionLayerMask);
            
            Debug.Log($"碰撞结果: {hasHit}");
            
            if (hasHit)
            {
                Debug.Log($"碰撞点: {hit.Point}");
                Debug.Log($"碰撞距离: {hit.Distance:F2}m");
                Debug.Log($"碰撞法线: {hit.Normal}");
                
                if (hit.Collider != null)
                {
                    var actor = hit.Collider.AttachedRigidBody?.Parent ?? hit.Collider.Parent;
                    string actorName = actor != null ? actor.Name : "Unknown";
                    Debug.Log($"碰撞对象: {actorName}");
                    Debug.Log($"碰撞层级: {hit.Collider.Layer}");
                }
            }
            
            Debug.Log("==================");
        }
    }
}
