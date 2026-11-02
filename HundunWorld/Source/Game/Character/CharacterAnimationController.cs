using System.Collections.Generic;
using FlaxEngine;

namespace HundunWorld.Game.Character
{
    public enum CharacterAnimationState
    {
        Idle,
        Walk,
        Run,
        Crouch,
        Jump,
        Fall,
        Death
    }

    public class CharacterAnimationController : Script
    {
        [Header("组件引用")]
        public AnimatedModel AnimatedModel;

        [Header("剪辑库")]
        // 播放单段剪辑时使用的资产目录（剪辑名 = 文件名去后缀）
        public string ClipsFolder = "Content/Character/Models/Pro Magic Pack";

        [Header("动画参数名称")]
        public string IsWalkingParamName = "IsWalking";
        public string IsRunningParamName = "IsRunning";
        public string IsJumpingParamName = "IsJumping";
        public string IsCrouchingParamName = "IsCrouching";
        public string IsDeadParamName = "IsDead";
        public string MoveSpeedParamName = "MoveSpeed";

        [Header("动画状态")]
        public CharacterAnimationState CurrentState { get; private set; } = CharacterAnimationState.Idle;

        private AnimGraphParameter _isWalkingParam;
        private AnimGraphParameter _isRunningParam;
        private AnimGraphParameter _isJumpingParam;
        private AnimGraphParameter _isCrouchingParam;
        private AnimGraphParameter _isDeadParam;
        private AnimGraphParameter _moveSpeedParam;

        private bool _animationParamsInitialized;

        public override void OnStart()
        {
            if (AnimatedModel == null)
            {
                AnimatedModel = Actor.GetChild<AnimatedModel>();
                if (AnimatedModel == null)
                {
                    AnimatedModel = Actor.FindActor<AnimatedModel>();
                }
            }

            TryInitializeAnimationParameters();
            SetAnimationState(CharacterAnimationState.Idle);
        }

        public override void OnUpdate()
        {
            if (!_animationParamsInitialized)
            {
                TryInitializeAnimationParameters();
            }

            HandleClipDebugKeys();
        }

        [Header("剪辑试播键（F7 下一段 / F8 回到状态机图）")]
        public bool EnableClipDebugKeys = true;
        public KeyboardKeys NextClipKey = KeyboardKeys.F7;
        public KeyboardKeys StopClipKey = KeyboardKeys.F8;

        private int _clipCursor = -1;

        /// <summary>
        /// 逐段试播 Pro Magic Pack 剪辑，用 F7/F8 手动翻页，方便肉眼检查重定向后的姿态是否撕裂。
        /// 默认开启；不想占用按键时把 <see cref="EnableClipDebugKeys"/> 关掉。
        /// </summary>
        private void HandleClipDebugKeys()
        {
            if (!EnableClipDebugKeys)
                return;

            if (Input.GetKeyDown(StopClipKey))
            {
                StopClip();
                Debug.Log("[CharacterAnimationController] 停止剪辑，回到状态机图");
                return;
            }

            if (!Input.GetKeyDown(NextClipKey))
                return;

            _clipCursor = (_clipCursor + 1) % CharacterClips.MagicPack.Length;
            var clip = CharacterClips.MagicPack[_clipCursor];
            var ok = PlayClip(clip, false);
            Debug.Log($"[CharacterAnimationController] 试播 {_clipCursor + 1}/{CharacterClips.MagicPack.Length} '{clip}' -> {(ok ? "OK" : "FAILED")}");
        }

        private bool TryInitializeAnimationParameters()
        {
            if (_animationParamsInitialized) return true;

            if (AnimatedModel == null) return false;

            if (AnimatedModel.SkinnedModel == null || !AnimatedModel.SkinnedModel.IsLoaded
                || AnimatedModel.AnimationGraph == null || !AnimatedModel.AnimationGraph.IsLoaded)
            {
                return false;
            }

            try
            {
                _isWalkingParam = AnimatedModel.GetParameter(IsWalkingParamName);
                _isRunningParam = AnimatedModel.GetParameter(IsRunningParamName);
                _isJumpingParam = AnimatedModel.GetParameter(IsJumpingParamName);
                _isCrouchingParam = AnimatedModel.GetParameter(IsCrouchingParamName);
                _isDeadParam = AnimatedModel.GetParameter(IsDeadParamName);
                _moveSpeedParam = AnimatedModel.GetParameter(MoveSpeedParamName);

                _animationParamsInitialized = true;
                Debug.Log("[CharacterAnimationController] 动画参数初始化完成");
                return true;
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[CharacterAnimationController] 初始化动画参数失败: {ex.Message}");
                return false;
            }
        }

        public void SetAnimationState(CharacterAnimationState newState)
        {
            if (!_animationParamsInitialized)
            {
                if (!TryInitializeAnimationParameters())
                {
                    CurrentState = newState;
                    return;
                }
            }

            if (CurrentState == newState && newState != CharacterAnimationState.Walk && newState != CharacterAnimationState.Run)
                return;

            CurrentState = newState;

            SetBoolParam(_isWalkingParam, newState == CharacterAnimationState.Walk || newState == CharacterAnimationState.Run);
            SetBoolParam(_isRunningParam, newState == CharacterAnimationState.Run);
            SetBoolParam(_isJumpingParam, newState == CharacterAnimationState.Jump);
            SetBoolParam(_isCrouchingParam, newState == CharacterAnimationState.Crouch);
            SetBoolParam(_isDeadParam, newState == CharacterAnimationState.Death);
        }

        public void SetMoveSpeed(float speed)
        {
            SetFloatParam(_moveSpeedParam, speed);
        }

        public void SetIsWalking(bool value)
        {
            SetBoolParam(_isWalkingParam, value);
        }

        public void SetIsRunning(bool value)
        {
            SetBoolParam(_isRunningParam, value);
        }

        public void SetIsJumping(bool value)
        {
            SetBoolParam(_isJumpingParam, value);
        }

        public void SetIsCrouching(bool value)
        {
            SetBoolParam(_isCrouchingParam, value);
        }

        public void SetIsDead(bool value)
        {
            SetBoolParam(_isDeadParam, value);
        }

        public void PlayIdle()
        {
            SetAnimationState(CharacterAnimationState.Idle);
        }

        public void PlayWalk()
        {
            SetAnimationState(CharacterAnimationState.Walk);
        }

        public void PlayRun()
        {
            SetAnimationState(CharacterAnimationState.Run);
        }

        public void PlayCrouch()
        {
            SetAnimationState(CharacterAnimationState.Crouch);
        }

        public void PlayJump()
        {
            SetAnimationState(CharacterAnimationState.Jump);
        }

        public void PlayFall()
        {
            SetAnimationState(CharacterAnimationState.Fall);
        }

        public void PlayDeath()
        {
            SetAnimationState(CharacterAnimationState.Death);
        }

        private void SetBoolParam(AnimGraphParameter param, bool value)
        {
            if (param != null)
            {
                param.Value = value;
            }
        }

        private void SetFloatParam(AnimGraphParameter param, float value)
        {
            if (param != null)
            {
                param.Value = value;
            }
        }

        public bool IsAnimationParamsInitialized()
        {
            return _animationParamsInitialized;
        }

        // ==================== 单段剪辑播放 ====================

        /// <summary>
        /// 本版的 <see cref="AnimatedModel"/> 没有 PlayAnimation：剪辑只能经 AnimationGraph 求值。
        /// 这里用引擎给编辑器动画预览窗用的同一套「虚拟图」机制（Content.CreateVirtualAsset + InitAsAnimation），
        /// 避免为每段剪辑手搓 .flax 图资产。
        /// </summary>
        private static readonly Dictionary<string, AnimationGraph> _clipGraphs = new Dictionary<string, AnimationGraph>();

        private AnimationGraph _graphBeforeClip;

        /// <summary>当前正在播放的剪辑名（null 表示回到状态机图）。</summary>
        public string ActiveClip { get; private set; }

        /// <summary>
        /// 播放目录下的单段剪辑（按文件名，空格与大小写照原样）。
        /// </summary>
        /// <param name="clipName">剪辑名，如 <c>Standing 2H Magic Attack 03</c></param>
        /// <param name="loop">是否循环</param>
        /// <param name="speed">播放速度（写 AnimatedModel.UpdateSpeed）</param>
        /// <returns> False if the clip or the graph could not be prepared.</returns>
        public bool PlayClip(string clipName, bool loop = false, float speed = 1.0f)
        {
            if (string.IsNullOrEmpty(clipName))
                return false;
            if (AnimatedModel == null)
            {
                Debug.LogWarning("[CharacterAnimationController] PlayClip 失败：AnimatedModel 为空");
                return false;
            }

            var model = AnimatedModel.SkinnedModel;
            if (model == null || !model.IsLoaded)
            {
                Debug.LogWarning("[CharacterAnimationController] PlayClip 失败：SkinnedModel 未就绪");
                return false;
            }

            var graph = GetClipGraph(model, ClipsFolder, clipName, loop);
            if (graph == null)
                return false;

            if (_graphBeforeClip == null)
                _graphBeforeClip = AnimatedModel.AnimationGraph;

            AnimatedModel.AnimationGraph = graph;
            AnimatedModel.UpdateSpeed = speed;
            ActiveClip = clipName;

            // 图换了，之前 GetParameter 拿到的句柄属于旧图；作废让 OnUpdate 按新图重绑，
            // 否则移动状态会静默写到一个已经不存在的图上。
            InvalidateParameterBindings();
            return true;
        }

        /// <summary>回到剪辑之前挂着的动画图。</summary>
        public void StopClip()
        {
            if (ActiveClip == null)
                return;

            if (AnimatedModel != null && _graphBeforeClip != null)
                AnimatedModel.AnimationGraph = _graphBeforeClip;

            _graphBeforeClip = null;
            ActiveClip = null;
            InvalidateParameterBindings();
        }

        /// <summary>丢弃当前动画图上的参数句柄，下一次更新会重新解析。</summary>
        private void InvalidateParameterBindings()
        {
            _animationParamsInitialized = false;
            _isWalkingParam = null;
            _isRunningParam = null;
            _isJumpingParam = null;
            _isCrouchingParam = null;
            _isDeadParam = null;
            _moveSpeedParam = null;
        }

        private static AnimationGraph GetClipGraph(SkinnedModel model, string folder, string clipName, bool loop)
        {
            var key = model.ID + "|" + clipName + "|" + loop;
            if (_clipGraphs.TryGetValue(key, out var cached) && cached)
                return cached;

            var canonical = CharacterClips.Resolve(clipName);
            if (canonical == null)
            {
                Debug.LogWarning("[CharacterAnimationController] 不在剪辑清单里的名字: " + clipName);
                return null;
            }

            var clip = Content.Load<Animation>(folder + "/" + canonical + ".flax", 10000.0);
            if (clip == null)
            {
                Debug.LogWarning("[CharacterAnimationController] 找不到剪辑资产: " + folder + "/" + canonical + ".flax");
                return null;
            }

            var graph = Content.CreateVirtualAsset<AnimationGraph>();
            if (graph == null)
            {
                Debug.LogWarning("[CharacterAnimationController] 创建虚拟 AnimationGraph 失败");
                return null;
            }

            // rootMotion 先不开：本项目的位移由 ECS/移动系统权威驱动
            if (graph.InitAsAnimation(model, clip, loop, false))
            {
                Object.Destroy(graph);
                Debug.LogWarning("[CharacterAnimationController] InitAsAnimation 失败: " + canonical);
                return null;
            }

            _clipGraphs[key] = graph;
            return graph;
        }
    }

    /// <summary>
    /// Pro Magic Pack 剪辑清单：调用方用名字播剪辑。
    /// Ty.flax 是 1 帧的空剪辑，不收录。
    /// </summary>
    public static class CharacterClips
    {
        /// <summary>Pro Magic Pack 目录下的可用剪辑名（= 文件名去后缀）。</summary>
        public static readonly string[] MagicPack =
        {
            "Crouch Idle",
            "Crouch To Standing Idle",
            "Crouch Turn Left 90",
            "Crouch Turn Right 90",
            "Crouch Walk Back",
            "Crouch Walk Forward",
            "Crouch Walk Left",
            "Crouch Walk Right",
            "Standing 1H Magic Attack 01",
            "Standing 1H Magic Attack 02",
            "Standing 1H Magic Attack 03",
            "standing 1H cast spell 01",
            "Standing 2H Cast Spell 01",
            "Standing 2H Magic Area Attack 01",
            "Standing 2H Magic Area Attack 02",
            "Standing 2H Magic Attack 01",
            "Standing 2H Magic Attack 02",
            "Standing 2H Magic Attack 03",
            "Standing 2H Magic Attack 04",
            "Standing 2H Magic Attack 05",
            "Standing Block End",
            "Standing Block Idle",
            "Standing Block React Large",
            "Standing Block Start",
            "standing idle",
            "standing idle 02",
            "Standing Idle 03",
            "Standing Idle 04",
            "Standing Idle To Crouch",
            "Standing Jump",
            "Standing Jump Running",
            "Standing Jump Running Landing",
            "Standing Land To Standing Idle",
            "Standing React Death Backward",
            "Standing React Death Forward",
            "Standing React Death Left",
            "Standing React Death Right",
            "Standing React Large From Back",
            "Standing React Large From Front",
            "Standing React Large From Left",
            "Standing React Large From Right",
            "Standing React Small From Back",
            "Standing React Small From Front",
            "Standing React Small From Left",
            "Standing React Small From Right",
            "Standing Run Back",
            "Standing Run Forward",
            "Standing Run Left",
            "Standing Run Right",
            "Standing Sprint Forward",
            "Standing Turn Left 90",
            "Standing Turn Right 90",
            "Standing Walk Back",
            "Standing Walk Forward",
            "Standing Walk Left",
            "Standing Walk Right",
        };

        private static Dictionary<string, string> _index;

        /// <summary>把调用方给的名字归一化到清单里的准确文件名（忽略大小写、多余空格与下划线）。</summary>
        public static string Resolve(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;

            if (_index == null)
            {
                _index = new Dictionary<string, string>();
                foreach (var clip in MagicPack)
                    _index[Normalize(clip)] = clip;
            }

            _index.TryGetValue(Normalize(name), out var canonical);
            return canonical;
        }

        private static string Normalize(string value)
        {
            var buffer = new System.Text.StringBuilder(value.Length);
            foreach (var c in value)
            {
                if (c == '_' || c == '-')
                    buffer.Append(' ');
                else if (c == ' ')
                    continue;
                else
                    buffer.Append(char.ToLowerInvariant(c));
            }
            return buffer.ToString().Replace(" ", "");
        }
    }
}