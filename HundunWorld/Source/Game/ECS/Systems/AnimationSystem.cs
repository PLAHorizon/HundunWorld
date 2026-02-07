using Arch.Core;
using Arch.Core.Utils;
using FlaxEngine;
using HundunWorld.Game.ECS.Components;

namespace HundunWorld.Game.ECS.Systems
{
    /// <summary>
    /// 动画系统，处理实体的动画播放
    /// </summary>
    public class AnimationSystem : BaseSystem
    {
        private QueryDescription _animationQuery;
        private QueryDescription _qinggongQuery;

        public override void Initialize(World world)
        {
            base.Initialize(world);
            
            // 查询具有动画状态组件的实体
            _animationQuery = new QueryDescription().WithAll<AnimationStateComponent>();
            
            // 查询具有轻功组件的实体
            _qinggongQuery = new QueryDescription().WithAll<QinggongComponent, VelocityComponent>();
        }

        public override void Update(World world, float deltaTime)
        {
            // 更新动画状态
            UpdateAnimations(world, deltaTime);
            
            // 更新轻功动画
            UpdateQinggongAnimations(world, deltaTime);
        }

        /// <summary>
        /// 更新动画状态
        /// </summary>
        private void UpdateAnimations(World world, float deltaTime)
        {
            world.Query(in _animationQuery, (Entity entity, ref AnimationStateComponent animation) =>
            {
                if (animation.IsPlaying)
                {
                    // 更新播放时间
                    animation.PlayTime += deltaTime * animation.PlaySpeed;
                    
                    // 这里可以添加动画事件触发逻辑
                    // 例如：在特定帧触发特效、音效等
                }
            });
        }

        /// <summary>
        /// 更新轻功动画
        /// </summary>
        private void UpdateQinggongAnimations(World world, float deltaTime)
        {
            world.Query(in _qinggongQuery, (Entity entity, ref QinggongComponent qinggong, ref VelocityComponent velocity) =>
            {
                // 根据轻功状态切换动画
                if (qinggong.IsGliding)
                {
                    // 播放滑翔动画
                    if (world.Has<AnimationStateComponent>(entity))
                    {
                        var anim = world.Get<AnimationStateComponent>(entity);
                        if (anim.CurrentAnimation != "Glide")
                        {
                            anim.CurrentAnimation = "Glide";
                            anim.PlayTime = 0f;
                            anim.IsPlaying = true;
                            world.Set(entity, anim);
                        }
                    }
                }
                else if (qinggong.CurrentJumpCount > 0)
                {
                    // 播放跳跃动画
                    if (world.Has<AnimationStateComponent>(entity))
                    {
                        var anim = world.Get<AnimationStateComponent>(entity);
                        string jumpAnim = qinggong.CurrentJumpCount switch
                        {
                            1 => "Jump_First",
                            2 => "Jump_Second",
                            3 => "Jump_Third",
                            _ => "Jump_Default"
                        };
                        
                        if (anim.CurrentAnimation != jumpAnim)
                        {
                            anim.CurrentAnimation = jumpAnim;
                            anim.PlayTime = 0f;
                            anim.IsPlaying = true;
                            world.Set(entity, anim);
                        }
                    }
                }
            });
        }

        /// <summary>
        /// 播放动画
        /// </summary>
        public static void PlayAnimation(World world, Entity entity, string animationName, float playSpeed = 1.0f, bool loop = true)
        {
            if (!world.IsAlive(entity))
                return;

            var animComponent = new AnimationStateComponent(animationName, playSpeed, loop)
            {
                IsPlaying = true
            };

            world.Set(entity, animComponent);
        }

        /// <summary>
        /// 停止动画
        /// </summary>
        public static void StopAnimation(World world, Entity entity)
        {
            if (!world.IsAlive(entity) || !world.Has<AnimationStateComponent>(entity))
                return;

            var anim = world.Get<AnimationStateComponent>(entity);
            anim.IsPlaying = false;
            world.Set(entity, anim);
        }
    }

    /// <summary>
    /// 视觉特效系统，处理实体上的特效
    /// </summary>
    public class VisualEffectSystem : BaseSystem
    {
        private QueryDescription _effectQuery;

        public override void Initialize(World world)
        {
            base.Initialize(world);
            
            // 查询具有特效组件的实体
            _effectQuery = new QueryDescription().WithAll<EffectVisualComponent, PositionComponent>();
        }

        public override void Update(World world, float deltaTime)
        {
            // 更新特效
            UpdateEffects(world, deltaTime);
        }

        /// <summary>
        /// 更新特效
        /// </summary>
        private void UpdateEffects(World world, float deltaTime)
        {
            world.Query(in _effectQuery, (Entity entity, ref EffectVisualComponent effect, ref PositionComponent position) =>
            {
                effect.ElapsedTime += deltaTime;

                // 自动销毁过期特效
                if (effect.AutoDestroy && effect.ElapsedTime >= effect.Duration)
                {
                    world.Remove<EffectVisualComponent>(entity);
                    Debug.Log($"Effect {effect.EffectName} expired and removed");
                }
            });
        }

        /// <summary>
        /// 播放特效
        /// </summary>
        public static void PlayEffect(World world, Entity entity, string effectName, float duration, Vector3 offset = default, bool follow = true, bool autoDestroy = true)
        {
            if (!world.IsAlive(entity))
                return;

            var effectComponent = new EffectVisualComponent(effectName, duration, offset, follow, autoDestroy);
            world.Set(entity, effectComponent);

            Debug.Log($"Playing effect {effectName} on entity {entity.Id}");
        }

        /// <summary>
        /// 移除特效
        /// </summary>
        public static void RemoveEffect(World world, Entity entity)
        {
            if (!world.IsAlive(entity) || !world.Has<EffectVisualComponent>(entity))
                return;

            world.Remove<EffectVisualComponent>(entity);
        }
    }

    /// <summary>
    /// 轻功系统（ECS版本），处理轻功相关逻辑
    /// </summary>
    public class QinggongSystemECS : BaseSystem
    {
        private QueryDescription _qinggongQuery;

        public override void Initialize(World world)
        {
            base.Initialize(world);
            
            // 查询具有轻功组件的实体
            _qinggongQuery = new QueryDescription().WithAll<QinggongComponent, VelocityComponent, PositionComponent>();
        }

        public override void Update(World world, float deltaTime)
        {
            // 更新轻功状态
            UpdateQinggongState(world, deltaTime);
        }

        /// <summary>
        /// 更新轻功状态
        /// </summary>
        private void UpdateQinggongState(World world, float deltaTime)
        {
            world.Query(in _qinggongQuery, (Entity entity, ref QinggongComponent qinggong, ref VelocityComponent velocity, ref PositionComponent position) =>
            {
                // 更新滑翔时间
                if (qinggong.IsGliding)
                {
                    qinggong.GlideTime += deltaTime;

                    // 应用滑翔物理
                    velocity.Velocity.Y *= 0.3f; // 降低下落速度

                    // 检查是否超过最大滑翔时间
                    if (qinggong.GlideTime >= qinggong.MaxGlideTime)
                    {
                        qinggong.IsGliding = false;
                        qinggong.GlideTime = 0f;
                    }
                }

                // 检查着陆（简化判断）
                if (position.Position.Y <= 0 && qinggong.CurrentJumpCount > 0)
                {
                    qinggong.CurrentJumpCount = 0;
                    qinggong.IsGliding = false;
                }
            });
        }

        /// <summary>
        /// 尝试跳跃
        /// </summary>
        public static bool TryJump(World world, Entity entity, float jumpHeight)
        {
            if (!world.IsAlive(entity) || !world.Has<QinggongComponent>(entity))
                return false;

            var qinggong = world.Get<QinggongComponent>(entity);

            // 检查是否还能跳跃
            if (qinggong.CurrentJumpCount >= qinggong.MaxJumpCount)
                return false;

            qinggong.CurrentJumpCount++;
            world.Set(entity, qinggong);

            // 应用跳跃速度
            if (world.Has<VelocityComponent>(entity))
            {
                var velocity = world.Get<VelocityComponent>(entity);
                float jumpVelocity = Mathf.Sqrt(2 * 9.81f * jumpHeight * qinggong.JumpHeightMultiplier);
                velocity.Velocity.Y = jumpVelocity;
                world.Set(entity, velocity);
            }

            // 播放跳跃特效
            VisualEffectSystem.PlayEffect(world, entity, $"Effect_Jump_{qinggong.CurrentJumpCount}", 1.0f);

            return true;
        }

        /// <summary>
        /// 开始滑翔
        /// </summary>
        public static bool StartGlide(World world, Entity entity)
        {
            if (!world.IsAlive(entity) || !world.Has<QinggongComponent>(entity))
                return false;

            var qinggong = world.Get<QinggongComponent>(entity);

            // 必须在空中才能滑翔
            if (qinggong.CurrentJumpCount == 0 || qinggong.IsGliding)
                return false;

            qinggong.IsGliding = true;
            qinggong.GlideTime = 0f;
            world.Set(entity, qinggong);

            return true;
        }

        /// <summary>
        /// 停止滑翔
        /// </summary>
        public static void StopGlide(World world, Entity entity)
        {
            if (!world.IsAlive(entity) || !world.Has<QinggongComponent>(entity))
                return;

            var qinggong = world.Get<QinggongComponent>(entity);
            qinggong.IsGliding = false;
            world.Set(entity, qinggong);
        }
    }
}
