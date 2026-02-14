using FlaxEngine;
using Game.Combat.Skills;

namespace Game.Combat
{
    /// <summary>
    /// 技能动画演示控制器
    /// 展示如何使用技能动画系统播放各种技能动画
    /// </summary>
    public class SkillAnimationDemo : Script
    {
        [Header("组件引用")]
        [Tooltip("技能动画控制器")]
        public SkillAnimationController AnimationController;

        [Tooltip("动画状态机")]
        public SkillAnimationStateMachine StateMachine;

        [Header("演示设置")]
        [Tooltip("是否启用键盘控制")]
        public bool EnableKeyboardControl = true;

        [Tooltip("是否显示调试信息")]
        public bool ShowDebugInfo = true;

        [Tooltip("自动播放演示")]
        public bool AutoPlayDemo = false;

        [Tooltip("自动播放间隔（秒）")]
        public float AutoPlayInterval = 3.0f;

        private float _autoPlayTimer = 0f;
        private int _currentDemoSkillIndex = 0;

        // 演示技能ID列表
        private readonly int[] _demoSkills = new int[]
        {
            1001, // 金刚掌
            3001, // 寒冰掌
            4001, // 烈焰掌
            2001, // 青木藤缠
            4002, // 火球术
            5001, // 岩甲术
            4003, // 烈焰风暴
            3002, // 水愈术
            2002, // 春回大地
        };

        /// <summary>
        /// 初始化
        /// </summary>
        public override void OnStart()
        {
            // 自动获取组件
            if (AnimationController == null)
            {
                AnimationController = Actor.GetScript<SkillAnimationController>();
                if (AnimationController == null)
                {
                    Debug.LogWarning("[SkillAnimationDemo] 未找到SkillAnimationController，将自动添加");
                    AnimationController = new SkillAnimationController();
                    Actor.AddScript(AnimationController.GetType());
                }
            }

            if (StateMachine == null)
            {
                StateMachine = Actor.GetScript<SkillAnimationStateMachine>();
                if (StateMachine == null)
                {
                    Debug.LogWarning("[SkillAnimationDemo] 未找到SkillAnimationStateMachine，将自动添加");
                    StateMachine = new SkillAnimationStateMachine();
                    Actor.AddScript(StateMachine.GetType());
                }
            }

            // 启用调试信息
            if (ShowDebugInfo && AnimationController != null)
            {
                AnimationController.ShowDebug = true;
            }
            if (ShowDebugInfo && StateMachine != null)
            {
                StateMachine.ShowDebug = true;
            }

            // 订阅动画事件
            if (AnimationController != null)
            {
                AnimationController.OnAnimationEvent += OnAnimationEvent;
                AnimationController.OnAnimationComplete += OnAnimationComplete;
            }

            Debug.Log("[SkillAnimationDemo] 初始化完成");
            Debug.Log("[SkillAnimationDemo] 按键说明：");
            Debug.Log("  数字键 1-9: 播放对应技能动画");
            Debug.Log("  Space: 停止当前动画");
            Debug.Log("  R: 重置到待机状态");
        }

        /// <summary>
        /// 每帧更新
        /// </summary>
        public override void OnUpdate()
        {
            // 键盘控制
            if (EnableKeyboardControl)
            {
                HandleKeyboardInput();
            }

            // 自动播放演示
            if (AutoPlayDemo)
            {
                _autoPlayTimer += Time.DeltaTime;
                if (_autoPlayTimer >= AutoPlayInterval)
                {
                    PlayNextDemoSkill();
                    _autoPlayTimer = 0f;
                }
            }
        }

        /// <summary>
        /// 处理键盘输入
        /// </summary>
        private void HandleKeyboardInput()
        {
            // 数字键 1-9 播放技能
            if (Input.GetKeyDown(KeyboardKeys.V))
                PlaySkillById(1001); // 金刚掌

            if (Input.GetKeyDown(KeyboardKeys.Numpad2))
                PlaySkillById(3001); // 寒冰掌

            if (Input.GetKeyDown(KeyboardKeys.Numpad3))
                PlaySkillById(4001); // 烈焰掌

            if (Input.GetKeyDown(KeyboardKeys.Numpad4))
                PlaySkillById(2001); // 青木藤缠

            if (Input.GetKeyDown(KeyboardKeys.Numpad5))
                PlaySkillById(4002); // 火球术

            if (Input.GetKeyDown(KeyboardKeys.Numpad6))
                PlaySkillById(5001); // 岩甲术

            if (Input.GetKeyDown(KeyboardKeys.Numpad7))
                PlaySkillById(4003); // 烈焰风暴

            if (Input.GetKeyDown(KeyboardKeys.Numpad8))
                PlaySkillById(3002); // 水愈术

            if (Input.GetKeyDown(KeyboardKeys.Numpad9))
                PlaySkillById(2002); // 春回大地

            // 空格键停止动画
            if (Input.GetKeyDown(KeyboardKeys.Spacebar))
            {
                StopCurrentAnimation();
            }

            // R键重置状态
            if (Input.GetKeyDown(KeyboardKeys.R))
            {
                ResetToIdle();
            }
        }

        /// <summary>
        /// 播放技能动画
        /// </summary>
        public void PlaySkillById(int skillId)
        {
            if (AnimationController == null)
            {
                Debug.LogWarning("[SkillAnimationDemo] AnimationController未设置");
                return;
            }

            Debug.Log($"[SkillAnimationDemo] 播放技能 ID: {skillId}");
            AnimationController.PlaySkillAnimationById(skillId);
        }

        /// <summary>
        /// 播放下一个演示技能
        /// </summary>
        private void PlayNextDemoSkill()
        {
            if (_demoSkills.Length == 0) return;

            int skillId = _demoSkills[_currentDemoSkillIndex];
            PlaySkillById(skillId);

            _currentDemoSkillIndex = (_currentDemoSkillIndex + 1) % _demoSkills.Length;
        }

        /// <summary>
        /// 停止当前动画
        /// </summary>
        public void StopCurrentAnimation()
        {
            if (AnimationController != null)
            {
                AnimationController.CancelAnimation();
                Debug.Log("[SkillAnimationDemo] 动画已停止");
            }
        }

        /// <summary>
        /// 重置到待机状态
        /// </summary>
        public void ResetToIdle()
        {
            if (StateMachine != null)
            {
                StateMachine.Reset();
                Debug.Log("[SkillAnimationDemo] 已重置到待机状态");
            }
        }

        /// <summary>
        /// 动画事件回调
        /// </summary>
        private void OnAnimationEvent(string skillName, SkillAnimationController.AnimationEventType eventType)
        {
            Debug.Log($"[SkillAnimationDemo] 动画事件 - 技能: {skillName}, 事件: {eventType}");

            // 根据事件类型执行不同操作
            switch (eventType)
            {
                case SkillAnimationController.AnimationEventType.StartupBegin:
                    Debug.Log($"  → 技能开始前摇");
                    break;

                case SkillAnimationController.AnimationEventType.CastPoint:
                    Debug.Log($"  → 到达施法点！可以释放技能效果");
                    break;

                case SkillAnimationController.AnimationEventType.HitFrame:
                    Debug.Log($"  → 命中帧！检测碰撞");
                    break;

                case SkillAnimationController.AnimationEventType.EffectSpawn:
                    Debug.Log($"  → 生成特效");
                    break;

                case SkillAnimationController.AnimationEventType.RecoveryBegin:
                    Debug.Log($"  → 进入后摇阶段");
                    break;

                case SkillAnimationController.AnimationEventType.AnimationEnd:
                    Debug.Log($"  → 动画即将结束");
                    break;
            }
        }

        /// <summary>
        /// 动画完成回调
        /// </summary>
        private void OnAnimationComplete(string skillName)
        {
            Debug.Log($"[SkillAnimationDemo] ✅ 技能 {skillName} 动画播放完成");
        }

        /// <summary>
        /// 调试绘制
        /// </summary>
        public override void OnDebugDraw()
        {
            if (!ShowDebugInfo) return;

            var pos = Actor.Position + new Vector3(0, 3.0f, 0);

            // 显示当前状态
            string statusText = "=== 技能动画演示 ===\n";
            
            if (AnimationController != null)
            {
                statusText += $"当前技能: {AnimationController.CurrentSkillAnimation}\n";
                statusText += $"动画阶段: {AnimationController.CurrentPhase}\n";
                statusText += $"播放进度: {AnimationController.GetAnimationProgress():P0}\n";
            }

            if (StateMachine != null)
            {
                statusText += $"状态机: {StateMachine.CurrentState}\n";
            }

            statusText += $"\n自动播放: {(AutoPlayDemo ? "开启" : "关闭")}";
            
            if (AutoPlayDemo)
            {
                statusText += $"\n下次播放: {(AutoPlayInterval - _autoPlayTimer):F1}s";
            }

            DebugDraw.DrawText(statusText, pos, Color.White, 12);
        }

        public override void OnDestroy()
        {
            // 取消订阅
            if (AnimationController != null)
            {
                AnimationController.OnAnimationEvent -= OnAnimationEvent;
                AnimationController.OnAnimationComplete -= OnAnimationComplete;
            }

            base.OnDestroy();
        }
    }
}
