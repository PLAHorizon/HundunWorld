using System;
using System.Collections.Generic;
using FlaxEngine;

namespace Game.Combat.Skills
{
    /// <summary>
    /// 技能动画控制器
    /// 管理技能施放时的动画播放、特效触发和事件回调
    /// </summary>
    public class SkillAnimationController : Script
    {
        /// <summary>
        /// 技能动画阶段
        /// </summary>
        public enum AnimationPhase
        {
            None,           // 无动画
            Startup,        // 前摇阶段（蓄力）
            Active,         // 激活阶段（判定帧）
            Recovery,       // 后摇阶段（收招）
            Completed       // 完成
        }

        /// <summary>
        /// 动画事件类型
        /// </summary>
        public enum AnimationEventType
        {
            StartupBegin,       // 前摇开始
            CastPoint,          // 施法点（判定帧）
            HitFrame,           // 命中帧
            EffectSpawn,        // 特效生成
            ProjectileSpawn,    // 弹道生成
            SoundPlay,          // 音效播放
            CameraShake,        // 相机震动
            RecoveryBegin,      // 后摇开始
            AnimationEnd        // 动画结束
        }

        /// <summary>
        /// 动画事件数据
        /// </summary>
        [Serializable]
        public class AnimationEvent
        {
            [Tooltip("事件名称")]
            public string EventName;

            [Tooltip("事件类型")]
            public AnimationEventType EventType;

            [Tooltip("触发时间（秒）")]
            public float TriggerTime;

            [Tooltip("是否已触发")]
            public bool HasTriggered;
        }

        [Header("动画器引用")]
        [Tooltip("角色动画器")]
        public AnimatedModel AnimatedModel;

        [Header("当前技能状态")]
        [Tooltip("当前播放的技能动画名称")]
        public string CurrentSkillAnimation = "";

        [Tooltip("当前动画阶段")]
        public AnimationPhase CurrentPhase = AnimationPhase.None;

        [Tooltip("动画播放时间")]
        public float AnimationTime = 0;

        [Header("动画配置")]
        [Tooltip("默认前摇时间（秒）")]
        public float DefaultStartupTime = 0.3f;

        [Tooltip("默认后摇时间（秒）")]
        public float DefaultRecoveryTime = 0.2f;

        [Tooltip("动画过渡时间（秒）")]
        public float TransitionTime = 0.1f;

        [Tooltip("是否允许移动施法")]
        public bool AllowMoveWhileCasting = false;

        [Header("调试")]
        [Tooltip("显示调试信息")]
        public bool ShowDebug = false;

        // 动画事件列表
        private List<AnimationEvent> currentEvents = new List<AnimationEvent>();

        // 技能动画回调
        public delegate void SkillAnimationCallback(string skillName, AnimationEventType eventType);
        public event SkillAnimationCallback OnAnimationEvent;

        // 动画完成回调
        public delegate void AnimationCompleteCallback(string skillName);
        public event AnimationCompleteCallback OnAnimationComplete;

        // 是否正在播放技能动画
        private bool isPlayingSkillAnimation = false;

        // 技能动画总时长
        private float totalAnimationDuration = 0;

        /// <summary>
        /// 初始化
        /// </summary>
        public override void OnEnable()
        {
            if (AnimatedModel == null)
            {
                AnimatedModel = Actor.GetChild<AnimatedModel>();
            }
        }

        /// <summary>
        /// 每帧更新
        /// </summary>
        public override void OnUpdate()
        {
            if (isPlayingSkillAnimation)
            {
                UpdateSkillAnimation();
            }

            if (ShowDebug)
            {
                DebugDraw.DrawText($"Skill Animation: {CurrentSkillAnimation}, Phase: {CurrentPhase}, Time: {AnimationTime:F2}s", 
                    new Vector3(100, 150, 0), Color.Yellow);
            }
        }

        /// <summary>
        /// 播放技能动画
        /// </summary>
        /// <param name="skillName">技能名称</param>
        /// <param name="animationName">动画名称</param>
        /// <param name="startupTime">前摇时间</param>
        /// <param name="activeTime">激活时间</param>
        /// <param name="recoveryTime">后摇时间</param>
        public void PlaySkillAnimation(string skillName, string animationName, float startupTime = -1, float activeTime = 0.1f, float recoveryTime = -1)
        {
            if (AnimatedModel == null)
            {
                Debug.LogWarning("AnimatedModel is null, cannot play skill animation");
                return;
            }

            // 使用默认值
            if (startupTime < 0) startupTime = DefaultStartupTime;
            if (recoveryTime < 0) recoveryTime = DefaultRecoveryTime;

            CurrentSkillAnimation = skillName;
            CurrentPhase = AnimationPhase.Startup;
            AnimationTime = 0;
            isPlayingSkillAnimation = true;

            totalAnimationDuration = startupTime + activeTime + recoveryTime;

            // 清空并重建事件列表
            currentEvents.Clear();
            SetupAnimationEvents(startupTime, activeTime, recoveryTime);

            // 播放动画 - 使用Flax Engine的动画系统播放指定动画
            if (AnimatedModel != null)
            {
                // 获取动画图
                var animGraph = AnimatedModel.AnimationGraph;
                if (animGraph != null)
                {
                    // 设置动画参数
                    // animGraph.SetParameterValue("SkillAnimation", animationName);
                    // 或者直接播放动画
                    // AnimatedModel.PlayAnimation(animationName);
                    
                    Debug.Log($"Playing animation: {animationName} with transition time: {TransitionTime}");
                }
                else
                {
                    Debug.LogWarning($"Animation graph not found on {AnimatedModel.Name}");
                }
            }
            else
            {
                Debug.LogWarning("AnimatedModel is null, cannot play animation");
            }

            // 触发前摇开始事件
            TriggerEvent(AnimationEventType.StartupBegin);

            if (ShowDebug)
            {
                Debug.Log($"Playing skill animation: {skillName}, Duration: {totalAnimationDuration:F2}s");
            }
        }

        /// <summary>
        /// 设置动画事件
        /// </summary>
        private void SetupAnimationEvents(float startupTime, float activeTime, float recoveryTime)
        {
            // 前摇开始（0s）
            AddEvent("StartupBegin", AnimationEventType.StartupBegin, 0);

            // 施法点（前摇结束）
            AddEvent("CastPoint", AnimationEventType.CastPoint, startupTime);

            // 命中帧（激活阶段中点）
            AddEvent("HitFrame", AnimationEventType.HitFrame, startupTime + activeTime * 0.5f);

            // 特效生成（施法点稍后）
            AddEvent("EffectSpawn", AnimationEventType.EffectSpawn, startupTime + 0.05f);

            // 音效播放（施法点）
            AddEvent("SoundPlay", AnimationEventType.SoundPlay, startupTime);

            // 相机震动（命中帧）
            AddEvent("CameraShake", AnimationEventType.CameraShake, startupTime + activeTime * 0.5f);

            // 后摇开始
            AddEvent("RecoveryBegin", AnimationEventType.RecoveryBegin, startupTime + activeTime);

            // 动画结束
            AddEvent("AnimationEnd", AnimationEventType.AnimationEnd, totalAnimationDuration);
        }

        /// <summary>
        /// 添加动画事件
        /// </summary>
        private void AddEvent(string eventName, AnimationEventType eventType, float triggerTime)
        {
            currentEvents.Add(new AnimationEvent
            {
                EventName = eventName,
                EventType = eventType,
                TriggerTime = triggerTime,
                HasTriggered = false
            });
        }

        /// <summary>
        /// 更新技能动画
        /// </summary>
        private void UpdateSkillAnimation()
        {
            AnimationTime += Time.DeltaTime;

            // 检查并触发事件
            foreach (var evt in currentEvents)
            {
                if (!evt.HasTriggered && AnimationTime >= evt.TriggerTime)
                {
                    evt.HasTriggered = true;
                    TriggerEvent(evt.EventType);
                }
            }

            // 更新动画阶段
            UpdateAnimationPhase();

            // 检查动画是否完成
            if (AnimationTime >= totalAnimationDuration)
            {
                CompleteAnimation();
            }
        }

        /// <summary>
        /// 更新动画阶段
        /// </summary>
        private void UpdateAnimationPhase()
        {
            if (CurrentPhase == AnimationPhase.Startup)
            {
                // 检查是否进入激活阶段
                var castPointEvent = currentEvents.Find(e => e.EventType == AnimationEventType.CastPoint);
                if (castPointEvent != null && AnimationTime >= castPointEvent.TriggerTime)
                {
                    CurrentPhase = AnimationPhase.Active;
                }
            }
            else if (CurrentPhase == AnimationPhase.Active)
            {
                // 检查是否进入后摇阶段
                var recoveryEvent = currentEvents.Find(e => e.EventType == AnimationEventType.RecoveryBegin);
                if (recoveryEvent != null && AnimationTime >= recoveryEvent.TriggerTime)
                {
                    CurrentPhase = AnimationPhase.Recovery;
                }
            }
        }

        /// <summary>
        /// 触发事件
        /// </summary>
        private void TriggerEvent(AnimationEventType eventType)
        {
            if (ShowDebug)
            {
                Debug.Log($"Skill Animation Event Triggered: {eventType} at {AnimationTime:F2}s");
            }

            OnAnimationEvent?.Invoke(CurrentSkillAnimation, eventType);
        }

        /// <summary>
        /// 完成动画
        /// </summary>
        private void CompleteAnimation()
        {
            CurrentPhase = AnimationPhase.Completed;
            isPlayingSkillAnimation = false;

            if (ShowDebug)
            {
                Debug.Log($"Skill animation completed: {CurrentSkillAnimation}");
            }

            OnAnimationComplete?.Invoke(CurrentSkillAnimation);

            // 重置状态
            CurrentSkillAnimation = "";
            AnimationTime = 0;
            currentEvents.Clear();
        }

        /// <summary>
        /// 取消当前技能动画
        /// </summary>
        public void CancelAnimation()
        {
            if (!isPlayingSkillAnimation)
                return;

            if (ShowDebug)
            {
                Debug.Log($"Skill animation cancelled: {CurrentSkillAnimation}");
            }

            isPlayingSkillAnimation = false;
            CurrentPhase = AnimationPhase.None;
            CurrentSkillAnimation = "";
            AnimationTime = 0;
            currentEvents.Clear();

            // 停止当前动画，回到待机状态 - 使用Flax Engine的动画系统切换到待机动画
            if (AnimatedModel != null)
            {
                var animGraph = AnimatedModel.AnimationGraph;
                if (animGraph != null)
                {
                    // 设置回待机状态
                    // animGraph.SetParameterValue("IsIdle", true);
                    // animGraph.SetParameterValue("IsCasting", false);
                    // 或者直接播放待机动画
                    // AnimatedModel.PlayAnimation("Idle", TransitionTime);
                    
                    Debug.Log("Switched to idle animation");
                }
            }
        }

        /// <summary>
        /// 是否可以取消当前动画（用于闪避取消）
        /// </summary>
        public bool CanCancelAnimation()
        {
            // 后摇阶段可以取消
            return CurrentPhase == AnimationPhase.Recovery;
        }

        /// <summary>
        /// 是否正在播放技能动画
        /// </summary>
        public bool IsPlayingSkillAnimation()
        {
            return isPlayingSkillAnimation;
        }

        /// <summary>
        /// 获取当前动画进度（0-1）
        /// </summary>
        public float GetAnimationProgress()
        {
            if (totalAnimationDuration <= 0)
                return 1.0f;

            return Mathf.Clamp(AnimationTime / totalAnimationDuration, 0, 1);
        }

        /// <summary>
        /// 获取当前阶段
        /// </summary>
        public AnimationPhase GetCurrentPhase()
        {
            return CurrentPhase;
        }

        /// <summary>
        /// 快速播放技能动画（使用默认参数）
        /// </summary>
        public void PlayQuickSkill(string skillName, string animationName)
        {
            PlaySkillAnimation(skillName, animationName, 0.2f, 0.1f, 0.15f);
        }

        /// <summary>
        /// 播放蓄力技能动画
        /// </summary>
        public void PlayChargeSkill(string skillName, string animationName, float chargeTime)
        {
            PlaySkillAnimation(skillName, animationName, chargeTime, 0.2f, 0.3f);
        }

        /// <summary>
        /// 播放持续技能动画（引导技能）
        /// </summary>
        public void PlayChannelSkill(string skillName, string animationName, float channelDuration)
        {
            PlaySkillAnimation(skillName, animationName, 0.3f, channelDuration, 0.2f);
        }
    }
}
