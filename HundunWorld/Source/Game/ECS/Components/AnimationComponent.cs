using FlaxEngine;

namespace HundunWorld.Game.ECS.Components
{
    /// <summary>
    /// 动画状态组件
    /// </summary>
    public struct AnimationStateComponent
    {
        /// <summary>
        /// 当前动画名称
        /// </summary>
        public string CurrentAnimation;

        /// <summary>
        /// 动画播放时间
        /// </summary>
        public float PlayTime;

        /// <summary>
        /// 动画播放速度
        /// </summary>
        public float PlaySpeed;

        /// <summary>
        /// 是否循环播放
        /// </summary>
        public bool IsLooping;

        /// <summary>
        /// 是否正在播放
        /// </summary>
        public bool IsPlaying;

        /// <summary>
        /// 动画混合权重
        /// </summary>
        public float BlendWeight;

        public AnimationStateComponent(string animName, float playSpeed = 1.0f, bool loop = true)
        {
            CurrentAnimation = animName;
            PlayTime = 0f;
            PlaySpeed = playSpeed;
            IsLooping = loop;
            IsPlaying = false;
            BlendWeight = 1.0f;
        }
    }

    /// <summary>
    /// 轻功状态组件
    /// </summary>
    public struct QinggongComponent
    {
        /// <summary>
        /// 轻功等级
        /// </summary>
        public int Level;

        /// <summary>
        /// 当前跳跃次数
        /// </summary>
        public int CurrentJumpCount;

        /// <summary>
        /// 最大跳跃次数
        /// </summary>
        public int MaxJumpCount;

        /// <summary>
        /// 是否正在滑翔
        /// </summary>
        public bool IsGliding;

        /// <summary>
        /// 滑翔时间
        /// </summary>
        public float GlideTime;

        /// <summary>
        /// 最大滑翔时间
        /// </summary>
        public float MaxGlideTime;

        /// <summary>
        /// 跳跃高度系数
        /// </summary>
        public float JumpHeightMultiplier;

        public QinggongComponent(int level = 1, int maxJumps = 3, float maxGlideTime = 10f)
        {
            Level = level;
            CurrentJumpCount = 0;
            MaxJumpCount = maxJumps;
            IsGliding = false;
            GlideTime = 0f;
            MaxGlideTime = maxGlideTime;
            JumpHeightMultiplier = 1.0f;
        }
    }

    /// <summary>
    /// 特效组件，用于管理实体上的视觉特效
    /// </summary>
    public struct EffectVisualComponent
    {
        /// <summary>
        /// 特效名称
        /// </summary>
        public string EffectName;

        /// <summary>
        /// 特效持续时间
        /// </summary>
        public float Duration;

        /// <summary>
        /// 特效已播放时间
        /// </summary>
        public float ElapsedTime;

        /// <summary>
        /// 特效位置偏移
        /// </summary>
        public Vector3 PositionOffset;

        /// <summary>
        /// 是否跟随实体
        /// </summary>
        public bool FollowEntity;

        /// <summary>
        /// 是否自动销毁
        /// </summary>
        public bool AutoDestroy;

        public EffectVisualComponent(string effectName, float duration, Vector3 offset, bool follow = true, bool autoDestroy = true)
        {
            EffectName = effectName;
            Duration = duration;
            ElapsedTime = 0f;
            PositionOffset = offset;
            FollowEntity = follow;
            AutoDestroy = autoDestroy;
        }
    }
}
