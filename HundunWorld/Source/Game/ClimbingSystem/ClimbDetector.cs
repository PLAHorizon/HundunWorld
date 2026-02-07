using FlaxEngine;
using System.Collections.Generic;

namespace HundunWorld.Game.ClimbingSystem
{
    /// <summary>
    /// 攀爬检测器，用于检测角色周围的可攀爬表面
    /// </summary>
    public class ClimbDetector : Script
    {
        #region 检测参数
        
        /// <summary>
        /// 前方检测距离
        /// </summary>
        [Tooltip("前方检测距离")]
        public float ForwardDetectionDistance { get; set; } = 1.5f;
        
        /// <summary>
        /// 上方检测距离
        /// </summary>
        [Tooltip("上方检测距离")]
        public float UpwardDetectionDistance { get; set; } = 2.0f;
        
        /// <summary>
        /// 边缘检测高度偏移
        /// </summary>
        [Tooltip("边缘检测高度偏移")]
        public float EdgeHeightOffset { get; set; } = 0.2f;
        
        /// <summary>
        /// 可攀爬表面的最小高度
        /// </summary>
        [Tooltip("可攀爬表面的最小高度")]
        public float MinClimbableHeight { get; set; } = 0.5f;
        
        /// <summary>
        /// 可攀爬表面的最大高度
        /// </summary>
        [Tooltip("可攀爬表面的最大高度")]
        public float MaxClimbableHeight { get; set; } = 3.0f;
        
        /// <summary>
        /// 检测层掩码
        /// </summary>
        [Tooltip("检测层掩码")]
        public uint DetectionLayerMask { get; set; } = uint.MaxValue;
        
        /// <summary>
        /// 检测频率（每秒检测次数）
        /// </summary>
        [Tooltip("检测频率（每秒检测次数）")]
        public float DetectionFrequency { get; set; } = 10.0f;
        
        #endregion
        
        #region 检测结果
        
        /// <summary>
        /// 是否检测到可攀爬边缘
        /// </summary>
        public bool IsClimbableEdgeDetected { get; private set; } = false;
        
        /// <summary>
        /// 可攀爬边缘位置
        /// </summary>
        public Vector3 ClimbEdgePosition { get; private set; } = Vector3.Zero;
        
        /// <summary>
        /// 攀爬类型
        /// </summary>
        public ClimbType DetectedClimbType { get; private set; } = ClimbType.LowEdge;
        
        /// <summary>
        /// 攀爬表面法线
        /// </summary>
        public Vector3 ClimbSurfaceNormal { get; private set; } = Vector3.Backward;
        
        /// <summary>
        /// 攀爬表面位置
        /// </summary>
        public Vector3 ClimbSurfacePosition { get; private set; } = Vector3.Zero;
        
        /// <summary>
        /// 攀爬到顶部的目标位置
        /// </summary>
        public Vector3 MantleTargetPosition { get; private set; } = Vector3.Zero;
        
        #endregion
        
        #region 私有变量
        
        private float _lastDetectionTime = 0f;
        private PlayerController _playerController;
        
        #endregion
        
        #region 生命周期方法
        
        public override void OnStart()
        {
            _playerController = Actor.Parent?.GetScript<PlayerController>();
        }
        
        public override void OnUpdate()
        {
            // 按频率进行检测
            if (Time.GameTime - _lastDetectionTime >= 1.0f / DetectionFrequency)
            {
                DetectClimbableSurfaces();
                _lastDetectionTime = Time.GameTime;
            }
        }
        
        #endregion
        
        #region 检测方法
        
        /// <summary>
        /// 检测可攀爬表面
        /// </summary>
        private void DetectClimbableSurfaces()
        {
            if (_playerController == null)
                return;
            
            // 重置检测结果
            IsClimbableEdgeDetected = false;
            
            // 获取角色前方方向
            Vector3 forward = Actor.Parent.Transform.Forward;
            forward.Y = 0;
            forward.Normalize();
            
            // 获取角色位置
            Vector3 characterPosition = Actor.Parent.Position;
            
            // 1. 检测前方是否有可攀爬表面
            Vector3 forwardStart = characterPosition + Vector3.Up * 1.0f; // 从角色胸部高度开始检测
            Vector3 forwardDirection = forward;
            
            if (Physics.RayCast(forwardStart, forwardDirection, out var forwardHit, ForwardDetectionDistance, DetectionLayerMask))
            {
                // 记录碰撞点和表面法线
                Vector3 hitPoint = forwardHit.Point;
                Vector3 surfaceNormal = forwardHit.Normal;
                
                // 2. 检测上方是否有空间可以攀爬
                Vector3 upwardStart = hitPoint + surfaceNormal * 0.1f + Vector3.Up * EdgeHeightOffset;
                Vector3 upwardDirection = Vector3.Up;
                
                // 检查从碰撞点向上是否有足够的空间
                if (!Physics.RayCast(upwardStart, upwardDirection, out var upwardHit, UpwardDetectionDistance, DetectionLayerMask))
                {
                    // 上方没有阻挡，可以攀爬
                    IsClimbableEdgeDetected = true;
                    ClimbSurfacePosition = hitPoint;
                    ClimbSurfaceNormal = surfaceNormal;
                    
                    // 计算边缘位置（在表面上方一点）
                    ClimbEdgePosition = hitPoint + Vector3.Up * EdgeHeightOffset;
                    
                    // 计算攀爬到顶部的目标位置
                    MantleTargetPosition = hitPoint + Vector3.Up * (UpwardDetectionDistance - 0.2f);
                    
                    // 确定攀爬类型
                    float heightDifference = MantleTargetPosition.Y - characterPosition.Y;
                    if (heightDifference <= 1.0f)
                    {
                        DetectedClimbType = ClimbType.LowEdge;
                    }
                    else if (heightDifference <= 2.0f)
                    {
                        DetectedClimbType = ClimbType.HighEdge;
                    }
                    else
                    {
                        DetectedClimbType = ClimbType.VerticalWall;
                    }
                }
                else
                {
                    // 上方有阻挡，检查是否是横杆
                    float overheadHeight = upwardHit.Point.Y - hitPoint.Y;
                    if (overheadHeight <= 0.5f)
                    {
                        // 可能是横杆
                        IsClimbableEdgeDetected = true;
                        ClimbSurfacePosition = hitPoint;
                        ClimbSurfaceNormal = surfaceNormal;
                        ClimbEdgePosition = upwardHit.Point;
                        MantleTargetPosition = ClimbEdgePosition + Vector3.Up * 0.5f;
                        DetectedClimbType = ClimbType.HorizontalBar;
                    }
                }
            }
        }
        
        /// <summary>
        /// 获取攀爬到顶部所需的时间
        /// </summary>
        /// <returns>攀爬时间（秒）</returns>
        public float GetMantleDuration()
        {
            switch (DetectedClimbType)
            {
                case ClimbType.LowEdge:
                    return 0.8f;
                case ClimbType.HighEdge:
                    return 1.2f;
                case ClimbType.VerticalWall:
                    return 2.0f;
                case ClimbType.HorizontalBar:
                    return 1.0f;
                default:
                    return 1.0f;
            }
        }
        
        /// <summary>
        /// 获取抓取边缘所需的时间
        /// </summary>
        /// <returns>抓取时间（秒）</returns>
        public float GetGrabDuration()
        {
            return 0.3f;
        }
        
        /// <summary>
        /// 获取悬挂时间
        /// </summary>
        /// <returns>悬挂时间（秒）</returns>
        public float GetHangDuration()
        {
            return 0.5f;
        }
        
        #endregion
        
        #region 调试可视化
        
        public override void OnDebugDraw()
        {
            if (!IsClimbableEdgeDetected)
                return;
                
            // 绘制检测到的攀爬表面
            DebugDraw.DrawSphere(new BoundingSphere(ClimbSurfacePosition, 0.1f), Color.Red);
            
            // 绘制边缘位置
            DebugDraw.DrawSphere(new BoundingSphere(ClimbEdgePosition, 0.1f), Color.Yellow);
            
            // 绘制目标位置
            DebugDraw.DrawSphere(new BoundingSphere(MantleTargetPosition, 0.1f), Color.Green);
            
            // 绘制表面法线
            DebugDraw.DrawLine(ClimbSurfacePosition, ClimbSurfacePosition + ClimbSurfaceNormal * 0.5f, Color.Blue);
            
            // 绘制检测射线
            Vector3 characterPosition = Actor.Parent.Position;
            Vector3 forwardStart = characterPosition + Vector3.Up * 1.0f;
            Vector3 forwardDirection = Actor.Parent.Transform.Forward;
            forwardDirection.Y = 0;
            forwardDirection.Normalize();
            
            DebugDraw.DrawLine(forwardStart, forwardStart + forwardDirection * ForwardDetectionDistance, Color.Cyan);
        }
        
        #endregion
    }
}