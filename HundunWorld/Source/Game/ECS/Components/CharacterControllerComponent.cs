using Arch.Core.Utils;
using FlaxEngine;

namespace HundunWorld.Game.ECS.Components
{
    /// <summary>
    /// 角色控制器组件，用于存储角色控制相关的信息
    /// </summary>
    public struct CharacterControllerComponent
    {
        /// <summary>
        /// 移动速度
        /// </summary>
        public float MoveSpeed;

        /// <summary>
        /// 跑步速度倍数
        /// </summary>
        public float RunSpeedMultiplier;

        /// <summary>
        /// 冲刺速度倍数
        /// </summary>
        public float SprintSpeedMultiplier;

        /// <summary>
        /// 蹲伏速度倍数
        /// </summary>
        public float CrouchSpeedMultiplier;

        /// <summary>
        /// 滑行速度倍数
        /// </summary>
        public float SlideSpeedMultiplier;

        /// <summary>
        /// 跳跃力度
        /// </summary>
        public float JumpForce;

        /// <summary>
        /// 重力
        /// </summary>
        public float Gravity;

        /// <summary>
        /// 是否在地面上
        /// </summary>
        public bool IsGrounded;

        /// <summary>
        /// 目标位置（用于点击移动）
        /// </summary>
        public Vector3 TargetPosition;

        /// <summary>
        /// 是否正在移动到目标位置
        /// </summary>
        public bool IsMovingToTarget;

        /// <summary>
        /// 角色朝向
        /// </summary>
        public Vector3 FacingDirection;

        /// <summary>
        /// 角色控制器半径
        /// </summary>
        public float Radius;

        /// <summary>
        /// 角色控制器高度
        /// </summary>
        public float Height;

        /// <summary>
        /// 是否正在跑步
        /// </summary>
        public bool IsRunning;

        /// <summary>
        /// 是否正在冲刺
        /// </summary>
        public bool IsSprinting;

        /// <summary>
        /// 是否正在蹲伏
        /// </summary>
        public bool IsCrouching;

        /// <summary>
        /// 是否正在滑行
        /// </summary>
        public bool IsSliding;

        /// <summary>
        /// 当前体力值
        /// </summary>
        public float CurrentStamina;

        /// <summary>
        /// 最大体力值
        /// </summary>
        public float MaxStamina;

        /// <summary>
        /// 体力恢复速度
        /// </summary>
        public float StaminaRegenRate;

        /// <summary>
        /// 冲刺体力消耗率
        /// </summary>
        public float SprintStaminaCost;

        /// <summary>
        /// 滑行持续时间
        /// </summary>
        public float SlideDuration;

        /// <summary>
        /// 最大滑行时间
        /// </summary>
        public float MaxSlideTime;

        /// <summary>
        /// 滑行减速度
        /// </summary>
        public float SlideDeceleration;

        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="moveSpeed">移动速度</param>
        /// <param name="jumpForce">跳跃力度</param>
        public CharacterControllerComponent(float moveSpeed, float jumpForce)
        {
            MoveSpeed = moveSpeed;
            RunSpeedMultiplier = 2.0f;
            SprintSpeedMultiplier = 2.5f;
            CrouchSpeedMultiplier = 0.5f;
            SlideSpeedMultiplier = 1.8f;
            JumpForce = jumpForce;
            Gravity = -9.81f;
            IsGrounded = true;
            TargetPosition = Vector3.Zero;
            IsMovingToTarget = false;
            FacingDirection = Vector3.Forward;
            Radius = 0.5f;
            Height = 1.8f;
            IsRunning = false;
            IsSprinting = false;
            IsCrouching = false;
            IsSliding = false;
            CurrentStamina = 100.0f;
            MaxStamina = 100.0f;
            StaminaRegenRate = 15.0f;
            SprintStaminaCost = 20.0f;
            SlideDuration = 0f;
            MaxSlideTime = 1.5f;
            SlideDeceleration = 8.0f;
        }

        public override string ToString()
        {
            return $"CharacterController(MoveSpeed:{MoveSpeed}, JumpForce:{JumpForce}, IsGrounded:{IsGrounded}, IsSprinting:{IsSprinting}, IsSliding:{IsSliding})";
        }
    }
}