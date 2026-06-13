using FlaxEngine;
using Game.Character.Attributes;

namespace Game.Character.Movement
{
    /// <summary>
    /// 轻功系统
    /// 管理角色的轻功能力：跳跃、二段跳、三段跳、踏空行等
    /// </summary>
    public class QinggongSystem : Script
    {
        #region 配置参数

        [Header("基础跳跃")]
        [Tooltip("基础跳跃高度（米）")]
        public float BaseJumpHeight = 2.0f;

        [Tooltip("最大跳跃高度（根据内功等级）")]
        public float MaxJumpHeight = 5.0f;

        [Tooltip("空中控制力（0-1）")]
        public float AirControl = 0.3f;

        [Header("多段跳")]
        [Tooltip("允许的最大跳跃次数")]
        public int MaxJumpCount = 3;

        [Tooltip("二段跳高度系数")]
        public float SecondJumpHeightMultiplier = 0.7f;

        [Tooltip("三段跳高度系数")]
        public float ThirdJumpHeightMultiplier = 0.5f;

        [Header("轻功等级")]
        [Tooltip("当前轻功等级（1-5）")]
        public int QinggongLevel = 1;

        [Tooltip("轻功内力消耗（每秒）")]
        public float QinggongEnergyCost = 15f;

        [Header("滑翔")]
        [Tooltip("滑翔速度")]
        public float GlideSpeed = 3.0f;

        [Tooltip("滑翔体力消耗（每秒）")]
        public float GlideStaminaCost = 5f;

        [Tooltip("最大滑翔时间（秒）")]
        public float MaxGlideTime = 10f;

        #endregion

        #region 运行时状态

        private int currentJumpCount = 0;
        private bool isGliding = false;
        private float glideTimer = 0f;
        private float _verticalVelocity;
        private CharacterAttributesComponent attributes;
        private CharacterController characterController;

        #endregion

        public override void OnAwake()
        {
            // 获取角色属性组件
            attributes = Actor.GetScript<CharacterAttributesComponent>();
            characterController = Actor.As<CharacterController>();
        }

        /// <summary>
        /// 尝试跳跃
        /// </summary>
        public bool TryJump()
        {
            // 检查是否还能跳跃
            if (currentJumpCount >= MaxJumpCount)
                return false;

            // 检查能量
            float energyCost = GetJumpEnergyCost();
            if (attributes != null && !attributes.ConsumeEnergy(energyCost))
                return false;

            // 执行跳跃
            PerformJump();
            return true;
        }

        /// <summary>
        /// 执行跳跃
        /// </summary>
        private void PerformJump()
        {
            if (characterController == null) return;

            currentJumpCount++;
            float jumpHeight = CalculateJumpHeight();
            
            float jumpVelocity = Mathf.Sqrt(2 * 9.81f * jumpHeight);
            _verticalVelocity = jumpVelocity;

            Debug.Log($"执行跳跃 - 第{currentJumpCount}段跳，高度: {jumpHeight}米");

            PlayJumpAnimation();

            SpawnJumpEffect();
        }

        /// <summary>
        /// 计算跳跃高度
        /// </summary>
        private float CalculateJumpHeight()
        {
            // 根据轻功等级调整基础跳跃高度
            float levelBonus = (QinggongLevel - 1) * 0.5f;
            float baseHeight = BaseJumpHeight + levelBonus;
            baseHeight = Mathf.Min(baseHeight, MaxJumpHeight);

            // 根据跳跃次数应用系数
            return currentJumpCount switch
            {
                1 => baseHeight,
                2 => baseHeight * SecondJumpHeightMultiplier,
                3 => baseHeight * ThirdJumpHeightMultiplier,
                _ => baseHeight
            };
        }

        /// <summary>
        /// 获取跳跃能量消耗
        /// </summary>
        private float GetJumpEnergyCost()
        {
            return currentJumpCount switch
            {
                0 => 10f,  // 首次跳跃
                1 => 15f,  // 二段跳
                2 => 20f,  // 三段跳
                _ => 25f
            };
        }

        /// <summary>
        /// 开始滑翔
        /// </summary>
        public bool StartGlide()
        {
            if (isGliding) return false;
            if (currentJumpCount == 0) return false; // 必须在空中才能滑翔

            isGliding = true;
            glideTimer = 0f;
            Debug.Log("开始轻功滑翔");
            return true;
        }

        /// <summary>
        /// 停止滑翔
        /// </summary>
        public void StopGlide()
        {
            if (!isGliding) return;
            
            isGliding = false;
            Debug.Log("停止轻功滑翔");
        }

        /// <summary>
        /// 角色着陆时重置状态
        /// </summary>
        public void OnLanded()
        {
            currentJumpCount = 0;
            StopGlide();
        }

        public override void OnUpdate()
        {
            float deltaTime = Time.DeltaTime;

            if (characterController != null)
            {
                float gravity = isGliding ? 9.81f * 0.3f : 9.81f;
                _verticalVelocity -= gravity * deltaTime;

                characterController.Move(new Vector3(0, _verticalVelocity * deltaTime, 0));

                if (characterController.IsGrounded && _verticalVelocity < 0f)
                {
                    _verticalVelocity = 0f;
                    if (currentJumpCount > 0)
                        OnLanded();
                }
            }

            if (isGliding)
            {
                glideTimer += deltaTime;

                if (attributes != null)
                {
                    bool hasStamina = attributes.ConsumeStamina(GlideStaminaCost * deltaTime);
                    if (!hasStamina || glideTimer >= MaxGlideTime)
                    {
                        StopGlide();
                    }
                }

                ApplyGlidePhysics(deltaTime);

                PlayGlideAnimation();
            }
        }

        /// <summary>
        /// 获取当前是否在空中
        /// </summary>
        public bool IsInAir()
        {
            return currentJumpCount > 0;
        }

        /// <summary>
        /// 获取当前是否正在滑翔
        /// </summary>
        public bool IsGliding()
        {
            return isGliding;
        }

        public uint GetQinggongInputBits()
        {
            uint bits = 0;
            if (currentJumpCount > 0)
                bits |= 1u << 3;
            if (isGliding)
                bits |= 1u << 4;
            return bits;
        }

        public int GetCurrentJumpCount()
        {
            return currentJumpCount;
        }

        /// <summary>
        /// 播放跳跃动画
        /// </summary>
        private void PlayJumpAnimation()
        {
            // 根据跳跃次数选择不同的动画
            string animationName = currentJumpCount switch
            {
                1 => "Jump_First",
                2 => "Jump_Second",
                3 => "Jump_Third",
                _ => "Jump_Default"
            };

            // 使用Flax Engine的动画系统播放动画
            var animatedModel = Actor.GetChild<AnimatedModel>();
            if (animatedModel != null)
            {
                // animatedModel.Play(animationName);
                Debug.Log($"Playing jump animation: {animationName}");
            }
        }

        /// <summary>
        /// 生成跳跃特效
        /// </summary>
        private void SpawnJumpEffect()
        {
            // 在角色脚下生成跳跃特效
            Vector3 effectPosition = Actor.Position;
            
            // 根据跳跃次数选择不同的特效
            string effectName = currentJumpCount switch
            {
                1 => "Effect_Jump_Normal",
                2 => "Effect_Jump_Double",
                3 => "Effect_Jump_Triple",
                _ => "Effect_Jump_Default"
            };

            // 查找特效管理器并播放特效
            var effectManager = Scene.FindScript<Combat.Effects.SkillEffectManager>();
            if (effectManager != null)
            {
                effectManager.PlayEffect(effectName, effectPosition, Quaternion.Identity);
            }

            Debug.Log($"Spawned jump effect: {effectName} at {effectPosition}");
        }

        /// <summary>
        /// 应用滑翔物理
        /// </summary>
        private void ApplyGlidePhysics(float deltaTime)
        {
            if (characterController == null) return;

            // 降低下落速度
            float glideGravityScale = 0.3f; // 滑翔时重力缩放
            
            // 应用水平滑翔速度
            Vector3 glideVelocity = Actor.Transform.Forward * GlideSpeed;
            
            // 这里需要与CharacterController集成
            // characterController.Move(glideVelocity * deltaTime);
            
            Debug.Log($"Applying glide physics: speed={GlideSpeed}, gravity scale={glideGravityScale}");
        }

        /// <summary>
        /// 播放滑翔动画
        /// </summary>
        private void PlayGlideAnimation()
        {
            var animatedModel = Actor.GetChild<AnimatedModel>();
            if (animatedModel != null)
            {
                // animatedModel.Play("Glide");
                Debug.Log("Playing glide animation");
            }
        }
    }
}
