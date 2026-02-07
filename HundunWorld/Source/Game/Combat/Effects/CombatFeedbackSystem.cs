using System;
using FlaxEngine;
using Game.Combat.Effects; // 引用SkillEffectManager
using GameCombatEffects = Game.Combat.Effects; // 为Game.Combat.Effects创建别名

// 添加AnimationCurve类定义
public class AnimationCurve
{
    private float _time1, _value1, _time2, _value2;
    
    private AnimationCurve(float time1, float value1, float time2, float value2)
    {
        _time1 = time1;
        _value1 = value1;
        _time2 = time2;
        _value2 = value2;
    }
    
    public static AnimationCurve EaseInOut(float time1, float value1, float time2, float value2)
    {
        return new AnimationCurve(time1, value1, time2, value2);
    }
    
    public float Evaluate(float time)
    {
        // 简单线性插值实现
        float t = Mathf.Clamp((time - _time1) / (_time2 - _time1), 0f, 1f);
        // 使用缓入缓出函数
        t = t < 0.5f ? 2 * t * t : 1 - 2 * (1 - t) * (1 - t);
        return Mathf.Lerp(_value1, _value2, t);
    }
}

namespace HundunWorld.Game.Combat.Effects
{
    /// <summary>
    /// 战斗反馈系统
    /// 整合视觉、听觉、触觉反馈，提供完整的战斗打击感
    /// </summary>
    public class CombatFeedbackSystem : Script
    {
        /// <summary>
        /// 反馈强度等级
        /// </summary>
        public enum FeedbackIntensity
        {
            None = 0,
            VeryWeak = 1,
            Weak = 2,
            Normal = 3,
            Strong = 4,
            VeryStrong = 5,
            Extreme = 6
        }

        /// <summary>
        /// 反馈类型
        /// </summary>
        public enum FeedbackType
        {
            Hit,                // 普通命中
            CriticalHit,        // 暴击
            Block,              // 格挡
            Parry,              // 弹反
            Dodge,              // 闪避
            KnockBack,          // 击退
            KnockDown,          // 击倒
            Stun,               // 眩晕
            Death,              // 死亡
            SkillCast,          // 技能施放
            UltimateSkill       // 终结技
        }

        [Header("系统引用")]
        [Tooltip("相机震动系统")]
        public CameraShakeSystem CameraShake;

        [Tooltip("技能特效管理器")]
        public SkillEffectManager EffectManager;

        [Tooltip("伤害数字系统")]
        public DamageNumberSystem DamageNumbers;

        [Header("打击顿帧设置")]
        [Tooltip("是否启用打击顿帧")]
        public bool EnableHitStop = true;

        [Tooltip("顿帧时间缩放映射")]
        public AnimationCurve HitStopCurve = AnimationCurve.EaseInOut(0, 0.1f, 1, 0.8f);

        [Header("屏幕效果")]
        [Tooltip("是否启用屏幕闪光")]
        public bool EnableScreenFlash = true;

        [Tooltip("暴击时的屏幕闪光颜色")]
        public Color CriticalFlashColor = new Color(1.0f, 0.8f, 0.2f, 0.3f);

        [Tooltip("受伤时的屏幕晕影颜色")]
        public Color DamageVignetteColor = new Color(1.0f, 0.2f, 0.2f, 0.5f);

        [Header("运动模糊")]
        [Tooltip("是否启用运动模糊")]
        public bool EnableMotionBlur = true;

        [Tooltip("击退时的运动模糊强度")]
        public float KnockBackMotionBlur = 0.8f;

        [Header("时间减速")]
        [Tooltip("是否启用时间减速效果")]
        public bool EnableTimeScale = true;

        [Tooltip("子弹时间时间缩放")]
        public float BulletTimeScale = 0.3f;

        [Tooltip("子弹时间持续时间")]
        public float BulletTimeDuration = 1.0f;

        [Header("调试")]
        [Tooltip("显示调试信息")]
        public bool ShowDebug = false;

        // 当前顿帧状态
        private bool isInHitStop = false;
        private float hitStopTimer = 0;
        private float hitStopDuration = 0;
        private float originalTimeScale = 1.0f;

        // 时间减速状态
        private bool isInSlowMotion = false;
        private float slowMotionTimer = 0;
        private float slowMotionDuration = 0;

        /// <summary>
        /// 初始化
        /// </summary>
        public override void OnEnable()
        {
            // 自动查找系统引用
            if (CameraShake == null)
                CameraShake = Scene.FindScript<CameraShakeSystem>();

            if (EffectManager == null)
                EffectManager = Scene.FindScript<SkillEffectManager>();

            if (DamageNumbers == null)
                DamageNumbers = Scene.FindScript<DamageNumberSystem>();

            originalTimeScale = Time.TimeScale;

            if (ShowDebug)
            {
                Debug.Log("CombatFeedbackSystem initialized");
            }
        }

        /// <summary>
        /// 每帧更新
        /// </summary>
        public override void OnUpdate()
        {
            UpdateHitStop();
            UpdateSlowMotion();

            if (ShowDebug)
            {
                string status = isInHitStop ? "HitStop" : (isInSlowMotion ? "SlowMotion" : "Normal");
                DebugDraw.DrawText($"Combat Feedback: {status}, TimeScale: {Time.TimeScale:F2}", 
                    new Vector3(100, 300, 0), Color.Red);
            }
        }

        /// <summary>
        /// 触发战斗反馈
        /// </summary>
        /// <param name="feedbackType">反馈类型</param>
        /// <param name="intensity">强度</param>
        /// <param name="hitPosition">命中位置</param>
        /// <param name="attacker">攻击者</param>
        /// <param name="target">目标</param>
        /// <param name="damage">伤害值</param>
        public void TriggerFeedback(FeedbackType feedbackType, FeedbackIntensity intensity, 
            Vector3 hitPosition, Actor attacker = null, Actor target = null, float damage = 0)
        {
            switch (feedbackType)
            {
                case FeedbackType.Hit:
                    OnHit(intensity, hitPosition, damage, target);
                    break;

                case FeedbackType.CriticalHit:
                    OnCriticalHit(intensity, hitPosition, damage, target);
                    break;

                case FeedbackType.Block:
                    OnBlock(intensity, hitPosition, target);
                    break;

                case FeedbackType.Parry:
                    OnParry(intensity, hitPosition, attacker, target);
                    break;

                case FeedbackType.Dodge:
                    OnDodge(intensity, hitPosition, target);
                    break;

                case FeedbackType.KnockBack:
                    OnKnockBack(intensity, hitPosition, target);
                    break;

                case FeedbackType.KnockDown:
                    OnKnockDown(intensity, hitPosition, target);
                    break;

                case FeedbackType.Stun:
                    OnStun(intensity, hitPosition, target);
                    break;

                case FeedbackType.Death:
                    OnDeath(intensity, hitPosition, target);
                    break;

                case FeedbackType.SkillCast:
                    OnSkillCast(intensity, hitPosition, attacker);
                    break;

                case FeedbackType.UltimateSkill:
                    OnUltimateSkill(intensity, hitPosition, attacker);
                    break;
            }
        }

        /// <summary>
        /// 普通命中反馈
        /// </summary>
        private void OnHit(FeedbackIntensity intensity, Vector3 hitPosition, float damage, Actor target)
        {
            // 相机震动
            if (CameraShake != null)
            {
                var shakeLevel = MapFeedbackToIntensity(intensity);
                CameraShake.TriggerShake(shakeLevel, 0.3f + shakeLevel * 0.2f); // 根据强度调整持续时间
            }

            // 顿帧效果
            if (EnableHitStop)
            {
                float hitStopTime = GetHitStopDuration(intensity);
                StartHitStop(hitStopTime);
            }

            // 伤害数字
            if (DamageNumbers != null && damage > 0)
            {
                DamageNumbers.ShowDamageNumber(damage, hitPosition, false, false, false);
            }

            // 命中特效
            if (EffectManager != null)
            {
                EffectManager.PlayHitEffect("HitEffect_Normal", hitPosition);
            }

            if (ShowDebug)
            {
                Debug.Log($"Hit feedback: Intensity={intensity}, Damage={damage}");
            }
        }

        /// <summary>
        /// 暴击反馈
        /// </summary>
        private void OnCriticalHit(FeedbackIntensity intensity, Vector3 hitPosition, float damage, Actor target)
        {
            // 强化相机震动
            if (CameraShake != null)
            {
                var shakeLevel = MapFeedbackToIntensity((FeedbackIntensity)((int)intensity + 1));
                CameraShake.TriggerShake(shakeLevel, 0.3f + shakeLevel * 0.2f); // 根据强度调整持续时间
            }

            // 更长的顿帧
            if (EnableHitStop)
            {
                float hitStopTime = GetHitStopDuration(intensity) * 1.5f;
                StartHitStop(hitStopTime);
            }

            // 暴击伤害数字
            if (DamageNumbers != null && damage > 0)
            {
                DamageNumbers.ShowDamageNumber(damage, hitPosition, true, false, false);
            }

            // 暴击特效（更华丽）
            if (EffectManager != null)
            {
                EffectManager.PlayHitEffect("HitEffect_Critical", hitPosition);
            }

            // 屏幕闪光
            if (EnableScreenFlash)
            {
                PlayScreenFlash(CriticalFlashColor, 0.2f);
            }

            if (ShowDebug)
            {
                Debug.Log($"Critical hit feedback: Intensity={intensity}, Damage={damage}");
            }
        }

        /// <summary>
        /// 格挡反馈
        /// </summary>
        private void OnBlock(FeedbackIntensity intensity, Vector3 hitPosition, Actor target)
        {
            // 轻微震动
            if (CameraShake != null)
            {
                CameraShake.TriggerShake(0.2f, 0.3f); // 轻微震动
            }

            // 格挡文本
            if (DamageNumbers != null)
            {
                DamageNumbers.ShowText("BLOCKED", hitPosition, Color.Gray);
            }

            // 格挡特效（火花）
            if (EffectManager != null)
            {
                EffectManager.PlayHitEffect("BlockEffect_Spark", hitPosition);
            }

            if (ShowDebug)
            {
                Debug.Log($"Block feedback at {hitPosition}");
            }
        }

        /// <summary>
        /// 弹反反馈
        /// </summary>
        private void OnParry(FeedbackIntensity intensity, Vector3 hitPosition, Actor attacker, Actor target)
        {
            // 强烈震动
            if (CameraShake != null)
            {
                CameraShake.TriggerShake(0.8f, 0.5f); // 强烈震动
            }

            // 子弹时间
            if (EnableTimeScale)
            {
                StartSlowMotion(BulletTimeScale, 0.5f);
            }

            // 强力顿帧
            if (EnableHitStop)
            {
                StartHitStop(0.15f);
            }

            // 弹反特效
            if (EffectManager != null)
            {
                EffectManager.PlayHitEffect("ParryEffect_Counter", hitPosition);
            }

            if (ShowDebug)
            {
                Debug.Log($"Parry feedback at {hitPosition}");
            }
        }

        /// <summary>
        /// 闪避反馈
        /// </summary>
        private void OnDodge(FeedbackIntensity intensity, Vector3 hitPosition, Actor target)
        {
            // 短暂时间减速（极限闪避）
            if (EnableTimeScale)
            {
                StartSlowMotion(0.5f, 0.8f);
            }

            // MISS文本
            if (DamageNumbers != null)
            {
                DamageNumbers.ShowText("MISS", hitPosition, Color.Yellow);
            }

            // 残影特效
            if (EffectManager != null && target != null)
            {
                EffectManager.PlayEffect("DodgeEffect_AfterImage", target.Position, target.Orientation, target);
            }

            if (ShowDebug)
            {
                Debug.Log($"Dodge feedback at {hitPosition}");
            }
        }

        /// <summary>
        /// 击退反馈
        /// </summary>
        private void OnKnockBack(FeedbackIntensity intensity, Vector3 hitPosition, Actor target)
        {
            // 强烈震动
            if (CameraShake != null)
            {
                CameraShake.TriggerShake(0.8f, 0.5f); // 强烈震动
            }

            // 运动模糊
            if (EnableMotionBlur)
            {
                ApplyMotionBlur(KnockBackMotionBlur, 0.5f);
            }

            // 击退特效
            if (EffectManager != null)
            {
                EffectManager.PlayHitEffect("KnockBackEffect_Impact", hitPosition);
            }

            if (ShowDebug)
            {
                Debug.Log($"Knock back feedback at {hitPosition}");
            }
        }

        /// <summary>
        /// 击倒反馈
        /// </summary>
        private void OnKnockDown(FeedbackIntensity intensity, Vector3 hitPosition, Actor target)
        {
            // 极强震动
            if (CameraShake != null)
            {
                CameraShake.TriggerShake(1.0f, 0.7f); // 极强震动
            }

            // 长时间顿帧
            if (EnableHitStop)
            {
                StartHitStop(0.2f);
            }

            // 击倒特效
            if (EffectManager != null)
            {
                EffectManager.PlayHitEffect("KnockDownEffect_Slam", hitPosition);
            }

            if (ShowDebug)
            {
                Debug.Log($"Knock down feedback at {hitPosition}");
            }
        }

        /// <summary>
        /// 眩晕反馈
        /// </summary>
        private void OnStun(FeedbackIntensity intensity, Vector3 hitPosition, Actor target)
        {
            // 眩晕特效（头顶星星）
            if (EffectManager != null && target != null)
            {
                Vector3 headPosition = target.Position + new Vector3(0, 2.0f, 0);
                EffectManager.PlayEffect("StunEffect_Stars", headPosition, Quaternion.Identity, target);
            }

            if (ShowDebug)
            {
                Debug.Log($"Stun feedback at {hitPosition}");
            }
        }

        /// <summary>
        /// 死亡反馈
        /// </summary>
        private void OnDeath(FeedbackIntensity intensity, Vector3 hitPosition, Actor target)
        {
            // 强烈震动
            if (CameraShake != null)
            {
                CameraShake.TriggerShake(0.8f, 0.5f); // 强烈震动
            }

            // 时间减速
            if (EnableTimeScale)
            {
                StartSlowMotion(0.3f, 1.5f);
            }

            // 死亡特效
            if (EffectManager != null)
            {
                EffectManager.PlayEffect("DeathEffect_Dissolve", hitPosition);
            }

            if (ShowDebug)
            {
                Debug.Log($"Death feedback at {hitPosition}");
            }
        }

        /// <summary>
        /// 技能施放反馈
        /// </summary>
        private void OnSkillCast(FeedbackIntensity intensity, Vector3 castPosition, Actor caster)
        {
            // 轻微震动
            if (CameraShake != null)
            {
                CameraShake.TriggerShake(MapFeedbackToIntensity((FeedbackIntensity)intensity), 0.5f);
            }

            if (ShowDebug)
            {
                Debug.Log($"Skill cast feedback at {castPosition}");
            }
        }

        /// <summary>
        /// 终结技反馈
        /// </summary>
        private void OnUltimateSkill(FeedbackIntensity intensity, Vector3 castPosition, Actor caster)
        {
            // 极强震动
            if (CameraShake != null)
            {
                CameraShake.TriggerShake(1.0f, 2.0f); // 极端震动
            }

            // 长时间子弹时间
            if (EnableTimeScale)
            {
                StartSlowMotion(0.2f, 2.0f);
            }

            // 屏幕闪光
            if (EnableScreenFlash)
            {
                PlayScreenFlash(Color.White, 0.5f);
            }

            if (ShowDebug)
            {
                Debug.Log($"Ultimate skill feedback at {castPosition}");
            }
        }

        /// <summary>
        /// 开始打击顿帧
        /// </summary>
        private void StartHitStop(float duration)
        {
            if (!EnableHitStop)
                return;

            isInHitStop = true;
            hitStopTimer = 0;
            hitStopDuration = duration;
            Time.TimeScale = 0.01f; // 几乎停止
        }

        /// <summary>
        /// 更新打击顿帧
        /// </summary>
        private void UpdateHitStop()
        {
            if (!isInHitStop)
                return;

            hitStopTimer += Time.UnscaledDeltaTime;

            if (hitStopTimer >= hitStopDuration)
            {
                isInHitStop = false;
                
                // 如果不在慢动作中，恢复正常时间
                if (!isInSlowMotion)
                {
                    Time.TimeScale = originalTimeScale;
                }
            }
        }

        /// <summary>
        /// 开始时间减速
        /// </summary>
        private void StartSlowMotion(float timeScale, float duration)
        {
            if (!EnableTimeScale)
                return;

            isInSlowMotion = true;
            slowMotionTimer = 0;
            slowMotionDuration = duration;
            
            if (!isInHitStop)
            {
                Time.TimeScale = timeScale;
            }
        }

        /// <summary>
        /// 更新时间减速
        /// </summary>
        private void UpdateSlowMotion()
        {
            if (!isInSlowMotion)
                return;

            slowMotionTimer += Time.UnscaledDeltaTime;

            if (slowMotionTimer >= slowMotionDuration)
            {
                isInSlowMotion = false;
                
                // 如果不在顿帧中，恢复正常时间
                if (!isInHitStop)
                {
                    Time.TimeScale = originalTimeScale;
                }
            }
        }

        /// <summary>
        /// 获取顿帧时长
        /// </summary>
        private float GetHitStopDuration(FeedbackIntensity intensity)
        {
            return HitStopCurve.Evaluate((float)intensity / 6.0f);
        }

        /// <summary>
        /// 播放屏幕闪光
        /// </summary>
        private void PlayScreenFlash(Color color, float duration)
        {
            // 使用后处理效果或UI覆盆层实现屏幕闪光
            // 查找PostFx或UI Canvas
            var camera = Camera.MainCamera;
            if (camera != null)
            {
                // 可以使用PostFx效果
                // camera.PostFxVolume.ColorGrading.Saturation = ...
                        
                // 或者使用UI覆盆层
                // 创建一个全屏 UI Panel 并设置颜色和透明度
                Debug.Log($"Playing screen flash: color={color}, duration={duration}");
                        
                // 启动协程来淡入淡出效果
                StartScreenFlashCoroutine(color, duration);
            }
        }
        
        /// <summary>
        /// 屏幕闪光协程
        /// </summary>
        private async void StartScreenFlashCoroutine(Color color, float duration)
        {
            // 这里可以使用Flax的Coroutine或async/await
            // 渐变透明度从0到0.5再到0
            float halfDuration = duration * 0.5f;
                    
            // 淡入
            for (float t = 0; t < halfDuration; t += Time.DeltaTime)
            {
                float alpha = Mathf.Lerp(0, color.A, t / halfDuration);
                // 应用alpha到UI或后处理
                await System.Threading.Tasks.Task.Delay(16); // ~60fps
            }
                    
            // 淡出
            for (float t = 0; t < halfDuration; t += Time.DeltaTime)
            {
                float alpha = Mathf.Lerp(color.A, 0, t / halfDuration);
                // 应用alpha到UI或后处理
                await System.Threading.Tasks.Task.Delay(16);
            }
        }

        /// <summary>
        /// 应用运动模糊
        /// </summary>
        private void ApplyMotionBlur(float intensity, float duration)
        {
            // 使用后处理效果实现运动模糊
            var camera = Camera.MainCamera;
            if (camera != null)
            {
                // 启用运动模糊后处理
                // camera.PostFxVolume.MotionBlur.Enabled = true;
                // camera.PostFxVolume.MotionBlur.Intensity = intensity;
                
                Debug.Log($"Applying motion blur: intensity={intensity}, duration={duration}");
                
                // 启动协程来自动关闭效果
                StartMotionBlurCoroutine(intensity, duration);
            }
        }

        /// <summary>
        /// 运动模糊协程
        /// </summary>
        private async void StartMotionBlurCoroutine(float intensity, float duration)
        {
            // 等待指定时间
            await System.Threading.Tasks.Task.Delay((int)(duration * 1000));
            
            // 渐变减弱运动模糊
            float fadeTime = 0.3f;
            for (float t = 0; t < fadeTime; t += Time.DeltaTime)
            {
                float currentIntensity = Mathf.Lerp(intensity, 0, t / fadeTime);
                // camera.PostFxVolume.MotionBlur.Intensity = currentIntensity;
                await System.Threading.Tasks.Task.Delay(16);
            }
            
            // 关闭运动模糊
            // camera.PostFxVolume.MotionBlur.Enabled = false;
            Debug.Log("Motion blur effect ended");
        }

        /// <summary>
        /// 将反馈强度映射到相机震动强度
        /// </summary>
        private float MapFeedbackToIntensity(FeedbackIntensity feedback)
        {
            return feedback switch
            {
                FeedbackIntensity.None => 0.0f,
                FeedbackIntensity.VeryWeak => 0.1f,
                FeedbackIntensity.Weak => 0.2f,
                FeedbackIntensity.Normal => 0.4f,
                FeedbackIntensity.Strong => 0.6f,
                FeedbackIntensity.VeryStrong => 0.8f,
                FeedbackIntensity.Extreme => 1.0f,
                _ => 0.0f
            };
        }

        /// <summary>
        /// 清理
        /// </summary>
        public override void OnDisable()
        {
            // 恢复时间缩放
            Time.TimeScale = originalTimeScale;
        }
    }
}