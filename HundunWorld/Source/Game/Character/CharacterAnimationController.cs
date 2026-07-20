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
    }
}