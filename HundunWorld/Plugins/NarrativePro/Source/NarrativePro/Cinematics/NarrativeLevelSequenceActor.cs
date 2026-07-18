using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.Items;

namespace NarrativePro.Cinematics
{
    /// <summary>
    /// 序列绑定配置。对应 UE5 FNarrativeSequencerBindingConfig。
    /// 描述要绑定到序列中的角色及其起始变换和标签。
    /// </summary>
    [Serializable]
    public class NarrativeSequencerBindingConfig
    {
        /// <summary>要绑定的角色 Actor</summary>
        [NonSerialized]
        public Actor Character;

        /// <summary>序列开始前角色应到达的变换</summary>
        public Transform CinematicStartTransform = Transform.Identity;

        /// <summary>绑定期间应用到角色的标签</summary>
        public GameplayTagContainer TagsToApplyWhilstBound = new GameplayTagContainer();

        public NarrativeSequencerBindingConfig()
        {
            TagsToApplyWhilstBound.AddTag(new GameplayTag("Narrative.State.SequencerControlled"));
            TagsToApplyWhilstBound.AddTag(new GameplayTag("Narrative.State.Invulnerable"));
        }
    }

    /// <summary>
    /// 序列播放设置。对应 UE5 FNarrativeSequencePlaybackSettings。
    /// 在基础播放设置上增加 Narrative 特有功能。
    /// </summary>
    [Serializable]
    public class NarrativeSequencePlaybackSettings
    {
        /// <summary>是否将角色走到第 0 帧的变换位置（无缝过渡）</summary>
        public bool bWalkBindingsToStartTransform = false;

        /// <summary>是否将控制旋转 yaw 匹配到 Pawn（混合出序列时有用）</summary>
        public bool bUpdateControlRotationToPawn = false;

        /// <summary>是否显示电影黑边</summary>
        public bool bShowCinematicBars = false;

        /// <summary>是否允许玩家跳过此序列</summary>
        public bool bCanSkip = true;

        /// <summary>播放序列前是否停止对话</summary>
        public bool bStopDialogue = false;

        /// <summary>是否隐藏必要 HUD 元素（任务更新等）</summary>
        public bool bHideEvenEssentialHUDElements = false;

        /// <summary>是否自动播放</summary>
        public bool bAutoPlay = true;

        /// <summary>要绑定的角色配置列表</summary>
        public List<NarrativeSequencerBindingConfig> BindingConfigs = new List<NarrativeSequencerBindingConfig>();

        /// <summary>绑定期间应用到角色的标签</summary>
        public GameplayTagContainer TagsToApplyWhilstBound = new GameplayTagContainer();

        /// <summary>若任何绑定角色获得这些标签则停止播放</summary>
        public GameplayTagContainer StopTags = new GameplayTagContainer();

        public NarrativeSequencePlaybackSettings()
        {
            TagsToApplyWhilstBound.AddTag(new GameplayTag("Narrative.State.SequencerControlled"));
            TagsToApplyWhilstBound.AddTag(new GameplayTag("Narrative.State.Invulnerable"));
            StopTags.AddTag(new GameplayTag("Narrative.State.IsDead"));
        }
    }

    /// <summary>
    /// 叙事序列播放完成回调。
    /// </summary>
    public delegate void OnSequenceFinished(NarrativeLevelSequenceActor sequenceActor);

    /// <summary>
    /// 叙事序列 Actor。对应 UE5 ANarrativeLevelSequenceActor。
    /// UE5 中继承 ALevelSequenceActor；Flax 无 LevelSequence 等价物。
    /// 此类为 Script 占位，保留数据结构和接口，播放逻辑以 Flax-不兼容 标记。
    /// Flax-不兼容: UE5 的 LevelSequence 在 Flax 无对应物，保留占位。原文 TODO: 未来可接入 Flax 的动画/时间轴系统或自定义序列播放器。
    /// </summary>
    public class NarrativeLevelSequenceActor : Script
    {
        /// <summary>叙事播放设置</summary>
        public NarrativeSequencePlaybackSettings NarrativeSequenceParams = new NarrativeSequencePlaybackSettings();

        /// <summary>拥有此序列的控制器 Actor</summary>
        [NonSerialized]
        public Actor OwnerController;

        /// <summary>序列资源路径（Flax-不兼容：Flax 无 LevelSequence 资源）</summary>
        public string LevelSequencePath = "";

        /// <summary>是否正在等待角色到达绑定位置</summary>
        [NonSerialized]
        protected bool bPendingBeginSequence = false;

        /// <summary>序列播放完成事件</summary>
        public event OnSequenceFinished OnFinished;

        public override void OnEnable()
        {
            base.OnEnable();
        }

        public override void OnDisable()
        {
            base.OnDisable();
        }

        /// <summary>获取所有绑定到序列的对象</summary>
        public List<Actor> GetBoundObjects()
        {
            var result = new List<Actor>();
            if (NarrativeSequenceParams?.BindingConfigs != null)
            {
                foreach (var config in NarrativeSequenceParams.BindingConfigs)
                {
                    if (config?.Character != null) result.Add(config.Character);
                }
            }
            return result;
        }

        /// <summary>使用生成器设置绑定（Flax-不兼容：简化实现）</summary>
        public virtual void SetBindingsUsingSpawner(Actor spawner)
        {
            // Flax-不兼容: UE5 的 LevelSequence 绑定在 Flax 无对应物，保留占位。原文 TODO: 从 NPCSpawner 获取 NPC 并绑定到序列
            NarrativeLog.Log("SetBindingsUsingSpawner：Flax 无 LevelSequence，未实现");
        }

        /// <summary>处理绑定并启动序列</summary>
        /// <returns>返回 false 表示因标签要求无法启动</returns>
        public virtual bool HandleBindingsAndStartSequence()
        {
            if (NarrativeSequenceParams == null) return false;

            // 检查停止标签
            foreach (var config in NarrativeSequenceParams.BindingConfigs)
            {
                if (config?.Character == null) continue;
                // Flax-不兼容: UE5 的 LevelSequence 标签检查依赖序列系统，在 Flax 无对应物，保留占位。原文 TODO: 检查角色是否拥有 StopTags 中的任何标签
            }

            if (NarrativeSequenceParams.bWalkBindingsToStartTransform)
            {
                // Flax-不兼容: UE5 的 LevelSequence 角色走位依赖序列系统，在 Flax 无对应物，保留占位。原文 TODO: 让角色走到绑定变换位置
                bPendingBeginSequence = true;
                NarrativeLog.Log("HandleBindingsAndStartSequence：等待角色到达位置");
                return true;
            }

            PlaySequence();
            return true;
        }

        /// <summary>更新序列和绑定参数</summary>
        public virtual void UpdateSequence(string levelSequencePath, NarrativeSequencePlaybackSettings settings)
        {
            LevelSequencePath = levelSequencePath;
            NarrativeSequenceParams = settings ?? new NarrativeSequencePlaybackSettings();
        }

        /// <summary>混合输出并停止序列</summary>
        public virtual void BlendOutAndStop()
        {
            // Flax-不兼容: UE5 的 LevelSequence 混合输出依赖序列系统，在 Flax 无对应物，保留占位。原文 TODO: 移除序列控制标签，等待 0.5 秒动画混合后销毁 Actor
            NarrativeLog.Log("BlendOutAndStop：混合输出并停止序列");
            OnFinished?.Invoke(this);
        }

        /// <summary>检查所有角色是否已到达绑定变换位置</summary>
        protected bool AreCharactersAtBindTransforms()
        {
            if (NarrativeSequenceParams?.BindingConfigs == null) return true;
            foreach (var config in NarrativeSequenceParams.BindingConfigs)
            {
                if (config?.Character == null) continue;
                float dist = Vector3.Distance(config.Character.Position, config.CinematicStartTransform.Translation);
                if (dist > 10f) return false;
            }
            return true;
        }

        /// <summary>尝试自动生成绑定配置</summary>
        protected void TryAutogenerateBindingConfigs()
        {
            // Flax-不兼容: UE5 的 LevelSequence 资产绑定在 Flax 无对应物，保留占位。原文 TODO: 从序列资产中查找标记绑定，自动生成 BindingConfigs
            NarrativeLog.Log("TryAutogenerateBindingConfigs：Flax 无 LevelSequence，未实现");
        }

        /// <summary>播放序列</summary>
        protected virtual void PlaySequence()
        {
            // Flax-不兼容: UE5 的 LevelSequence 系统在 Flax 无对应物，保留占位。原文 TODO: Flax 无 LevelSequence 系统，此处为占位
            // 未来可接入自定义时间轴或动画系统
            NarrativeLog.Log($"PlaySequence：播放序列 {LevelSequencePath}（Flax-不兼容：Flax 无 LevelSequence）");
            OnPlay();
        }

        /// <summary>序列开始播放时调用</summary>
        protected virtual void OnPlay()
        {
            // 应用绑定标签
            if (NarrativeSequenceParams?.BindingConfigs != null)
            {
                foreach (var config in NarrativeSequenceParams.BindingConfigs)
                {
                    if (config?.Character == null) continue;
                    // Flax-不兼容: UE5 的 LevelSequence 绑定标签应用依赖序列系统，在 Flax 无对应物，保留占位。原文 TODO: 通过 GAS 应用 TagsToApplyWhilstBound
                }
            }
        }

        /// <summary>序列停止时调用</summary>
        protected virtual void OnStop()
        {
            // 移除绑定标签
            if (NarrativeSequenceParams?.BindingConfigs != null)
            {
                foreach (var config in NarrativeSequenceParams.BindingConfigs)
                {
                    if (config?.Character == null) continue;
                    // Flax-不兼容: UE5 的 LevelSequence 绑定标签移除依赖序列系统，在 Flax 无对应物，保留占位。原文 TODO: 通过 GAS 移除 TagsToApplyWhilstBound
                }
            }
            OnFinished?.Invoke(this);
        }

        public override void OnUpdate()
        {
            base.OnUpdate();

            // 等待角色到达位置后播放序列
            if (bPendingBeginSequence)
            {
                if (AreCharactersAtBindTransforms())
                {
                    bPendingBeginSequence = false;
                    PlaySequence();
                }
            }
        }
    }
}
