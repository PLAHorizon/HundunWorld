using Arch.Core.Utils;
using FlaxEngine;

namespace HundunWorld.Game.ECS.Components
{
    /// <summary>
    /// 相机组件，用于存储第三人称相机的相关信息
    /// </summary>
    public struct CameraComponent
    {
        /// <summary>
        /// 相机与目标的距离
        /// </summary>
        public float Distance;

        /// <summary>
        /// 相机的俯仰角（上下角度）
        /// </summary>
        public float Pitch;

        /// <summary>
        /// 相机的偏航角（左右角度）
        /// </summary>
        public float Yaw;

        /// <summary>
        /// 相机的最小距离
        /// </summary>
        public float MinDistance;

        /// <summary>
        /// 相机的最大距离
        /// </summary>
        public float MaxDistance;

        /// <summary>
        /// 相机的最小俯仰角
        /// </summary>
        public float MinPitch;

        /// <summary>
        /// 相机的最大俯仰角
        /// </summary>
        public float MaxPitch;

        /// <summary>
        /// 相机偏移量
        /// </summary>
        public Vector3 Offset;

        /// <summary>
        /// 相机是否正在被控制
        /// </summary>
        public bool IsControlling;

        /// <summary>
        /// 相机的视场角
        /// </summary>
        public float FOV;

        /// <summary>
        /// 相机震动效果
        /// </summary>
        public Vector3 ShakeOffset;

        /// <summary>
        /// 是否跟随角色旋转
        /// </summary>
        public bool FollowCharacterRotation;

        /// <summary>
        /// 角色旋转跟随的延迟时间（秒）
        /// </summary>
        public float RotationFollowDelay;

        /// <summary>
        /// 角色旋转跟随的速度
        /// </summary>
        public float RotationFollowSpeed;

        /// <summary>
        /// 上次手动旋转相机的时间
        /// </summary>
        public float LastManualRotateTime;

        /// <summary>
        /// 上次角色旋转时间
        /// </summary>
        public float LastCharacterRotationTime;

        /// <summary>
        /// 角色上次旋转角度
        /// </summary>
        public float LastCharacterYaw;

        /// <summary>
        /// 理想距离
        /// </summary>
        public float IdealDistance;

        /// <summary>
        /// 当前实际距离
        /// </summary>
        public float CurrentDistance;

        /// <summary>
        /// 相机旋转平滑速度
        /// </summary>
        public float RotationSmoothing;

        /// <summary>
        /// 相机位置平滑速度
        /// </summary>
        public float PositionSmoothing;

        /// <summary>
        /// 启用碰撞检测
        /// </summary>
        public bool EnableCollisionDetection;

        /// <summary>
        /// 碰撞检测层
        /// </summary>
        public LayersMask CollisionLayers;

        /// <summary>
        /// 碰撞偏移量
        /// </summary>
        public float CollisionOffset;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="distance">距离</param>
        /// <param name="pitch">俯仰角</param>
        /// <param name="yaw">偏航角</param>
        public CameraComponent(float distance, float pitch, float yaw)
        {
            Distance = distance;
            Pitch = pitch;
            Yaw = yaw;
            MinDistance = 2.0f;
            MaxDistance = 20.0f;
            MinPitch = -45.0f;
            MaxPitch = 45.0f;
            Offset = Vector3.Zero;
            IsControlling = false;
            FOV = 60.0f;
            ShakeOffset = Vector3.Zero;
            FollowCharacterRotation = true;
            RotationFollowDelay = 1.0f;
            RotationFollowSpeed = 90.0f;
            LastManualRotateTime = 0f;
            LastCharacterRotationTime = 0f;
            LastCharacterYaw = 0f;
            IdealDistance = distance;
            CurrentDistance = distance;
            RotationSmoothing = 5.0f;
            PositionSmoothing = 10.0f;
            EnableCollisionDetection = true;
            CollisionLayers = LayersMask.Default;
            CollisionOffset = 0.2f;
        }

        public override string ToString()
        {
            return $"Camera(Distance:{Distance}, Pitch:{Pitch}, Yaw:{Yaw}, FollowRotation:{FollowCharacterRotation})";
        }
    }
}