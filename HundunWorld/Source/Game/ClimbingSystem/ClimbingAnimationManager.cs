using FlaxEngine;
using System;
using System.Collections.Generic;

// 添加缺失的类型定义
public struct Keyframe
{
    public float Time;
    public float Value;
    public float InTangent;
    public float OutTangent;
    
    public Keyframe(float time, float value, float inTangent = 0, float outTangent = 0)
    {
        Time = time;
        Value = value;
        InTangent = inTangent;
        OutTangent = outTangent;
    }
}

public class AnimationClip
{
    public string Name { get; set; }
    public float Length { get; set; }
    public bool IsLooping { get; set; }
    
    public AnimationClip(string name, float length = 1.0f)
    {
        Name = name;
        Length = length;
        IsLooping = false;
    }
}

namespace HundunWorld.Game.ClimbingSystem
{
    /// <summary>
    /// 爬墙动画管理器
    /// 负责管理爬墙相关的动画播放和混合
    /// </summary>
    public class ClimbingAnimationManager : Script
    {
        #region 动画参数
        [Header("动画混合设置")]
        [Tooltip("动画混合时间")]
        public float BlendDuration = 0.3f;
        
        [Tooltip("动画过渡曲线")]
        public AnimationCurve BlendCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);
        
        [Header("动画层权重")]
        [Tooltip("基础爬墙动画层权重")]
        public float BaseLayerWeight = 1.0f;
        
        [Tooltip("手势动画层权重")]
        public float HandLayerWeight = 0.8f;
        
        [Tooltip("身体姿态动画层权重")]
        public float BodyLayerWeight = 0.6f;
        
        [Header("IK设置")]
        [Tooltip("是否启用IK")]
        public bool EnableIK = true;
        
        [Tooltip("左手IK权重")]
        public float LeftHandIKWeight = 1.0f;
        
        [Tooltip("右手IK权重")]
        public float RightHandIKWeight = 1.0f;
        #endregion

        #region 动画状态
        public enum ClimbAnimationState
        {
            None,
            Idle,
            Moving,
            Grabbing,
            Hanging,
            Mantling,
            Sliding,
            Exiting
        }

        private ClimbAnimationState _currentState = ClimbAnimationState.None;
        private ClimbAnimationState _previousState = ClimbAnimationState.None;
        private float _stateTransitionTime = 0f;
        private float _currentBlendFactor = 0f;
        #endregion

        #region 动画数据
        private Dictionary<string, AnimationClip> _climbAnimations;
        private Dictionary<string, Transform[]> _ikTargets;
        private SkinnedModel _skinnedModel;
        // private Skeleton _skeleton; // 暂时注释掉，因为Skeleton类型可能不可用
        #endregion

        #region IK目标点
        private Vector3 _leftHandTarget = Vector3.Zero;
        private Vector3 _rightHandTarget = Vector3.Zero;
        private Vector3 _leftFootTarget = Vector3.Zero;
        private Vector3 _rightFootTarget = Vector3.Zero;
        private Quaternion _leftHandRotation = Quaternion.Identity;
        private Quaternion _rightHandRotation = Quaternion.Identity;
        #endregion

        #region 引用
        private EnhancedClimbingController _climbingController;
        private PlayerController _playerController;
        #endregion

        public override void OnStart()
        {
            InitializeComponents();
            LoadClimbAnimations();
            SetupIKTargets();
            Debug.Log("[ClimbAnim] 爬墙动画管理器已初始化");
        }

        public override void OnUpdate()
        {
            UpdateAnimationState();
            UpdateAnimationBlending();
            UpdateIKTargets();
        }

        public override void OnLateUpdate()
        {
            if (EnableIK)
            {
                ApplyIK();
            }
        }

        #region 初始化
        private void InitializeComponents()
        {
            _climbingController = Actor.Parent?.GetScript<EnhancedClimbingController>();
            _playerController = Actor.Parent?.GetScript<PlayerController>();
            _skinnedModel = Actor.Parent?.GetScript<SkinnedModel>();
            
            if (_skinnedModel != null)
            {
                // Skeleton访问方式可能需要调整，暂时注释掉
                // _skeleton = _skinnedModel.Skeleton;
            }
        }

        private void LoadClimbAnimations()
        {
            _climbAnimations = new Dictionary<string, AnimationClip>
            {
                // 这些应该是实际的动画资源路径
                ["Climb_Idle"] = LoadAnimation("Animations/Climbing/Climb_Idle.anim"),
                ["Climb_Move"] = LoadAnimation("Animations/Climbing/Climb_Move.anim"),
                ["Climb_Grab"] = LoadAnimation("Animations/Climbing/Climb_Grab.anim"),
                ["Climb_Hang"] = LoadAnimation("Animations/Climbing/Climb_Hang.anim"),
                ["Climb_Mantle"] = LoadAnimation("Animations/Climbing/Climb_Mantle.anim"),
                ["Climb_Slide"] = LoadAnimation("Animations/Climbing/Climb_Slide.anim"),
                ["Climb_Exit"] = LoadAnimation("Animations/Climbing/Climb_Exit.anim")
            };
        }

        private AnimationClip LoadAnimation(string path)
        {
            // 实际项目中这里会加载真实的动画资源
            // 目前返回null作为占位符
            Debug.LogWarning($"[ClimbAnim] 动画资源未找到: {path}");
            return null;
        }

        private void SetupIKTargets()
        {
            _ikTargets = new Dictionary<string, Transform[]>
            {
                ["LeftHand"] = new Transform[2],  // 目标位置和旋转
                ["RightHand"] = new Transform[2],
                ["LeftFoot"] = new Transform[2],
                ["RightFoot"] = new Transform[2]
            };
        }
        #endregion

        #region 状态管理
        private void UpdateAnimationState()
        {
            if (_climbingController == null)
                return;

            var climbingState = _climbingController.GetClimbingState();
            var newAnimState = ConvertClimbingStateToAnimationState(climbingState);

            if (newAnimState != _currentState)
            {
                _previousState = _currentState;
                _currentState = newAnimState;
                _stateTransitionTime = Time.GameTime;
                OnAnimationStateChanged(_previousState, _currentState);
            }
        }

        private ClimbAnimationState ConvertClimbingStateToAnimationState(EnhancedClimbingController.EnhancedClimbingState climbState)
        {
            return climbState switch
            {
                EnhancedClimbingController.EnhancedClimbingState.None => ClimbAnimationState.None,
                EnhancedClimbingController.EnhancedClimbingState.Approaching => ClimbAnimationState.Moving,
                EnhancedClimbingController.EnhancedClimbingState.Grabbing => ClimbAnimationState.Grabbing,
                EnhancedClimbingController.EnhancedClimbingState.Climbing => ClimbAnimationState.Moving,
                EnhancedClimbingController.EnhancedClimbingState.Hanging => ClimbAnimationState.Hanging,
                EnhancedClimbingController.EnhancedClimbingState.Mantling => ClimbAnimationState.Mantling,
                EnhancedClimbingController.EnhancedClimbingState.SlidingDown => ClimbAnimationState.Sliding,
                EnhancedClimbingController.EnhancedClimbingState.Exiting => ClimbAnimationState.Exiting,
                _ => ClimbAnimationState.None
            };
        }

        private void OnAnimationStateChanged(ClimbAnimationState from, ClimbAnimationState to)
        {
            Debug.Log($"[ClimbAnim] 动画状态变更: {from} -> {to}");
            PlayStateAnimation(to);
        }

        private void PlayStateAnimation(ClimbAnimationState state)
        {
            string animName = state switch
            {
                ClimbAnimationState.Idle => "Climb_Idle",
                ClimbAnimationState.Moving => "Climb_Move",
                ClimbAnimationState.Grabbing => "Climb_Grab",
                ClimbAnimationState.Hanging => "Climb_Hang",
                ClimbAnimationState.Mantling => "Climb_Mantling",
                ClimbAnimationState.Sliding => "Climb_Slide",
                ClimbAnimationState.Exiting => "Climb_Exit",
                _ => ""
            };

            if (!string.IsNullOrEmpty(animName) && _climbAnimations.ContainsKey(animName))
            {
                // 实际播放动画的逻辑
                Debug.Log($"[ClimbAnim] 播放动画: {animName}");
            }
        }
        #endregion

        #region 动画混合
        private void UpdateAnimationBlending()
        {
            if (_previousState == ClimbAnimationState.None)
            {
                _currentBlendFactor = 1.0f;
                return;
            }

            float elapsedTime = Time.GameTime - _stateTransitionTime;
            float blendProgress = Mathf.Clamp(elapsedTime / BlendDuration, 0f, 1f);
            _currentBlendFactor = BlendCurve.Evaluate(blendProgress);

            // 当混合完成时，清除前一个状态
            if (blendProgress >= 1.0f)
            {
                _previousState = ClimbAnimationState.None;
            }
        }
        #endregion

        #region IK系统
        private void UpdateIKTargets()
        {
            if (_climbingController == null || !_climbingController.IsClimbing())
                return;

            Vector3 wallNormal = _climbingController.GetWallNormal();
            Vector3 characterPosition = Actor.Parent.Position;

            // 计算手部IK目标点（基于墙面法线和角色位置）
            float armLength = 0.7f; // 假设手臂长度
            
            _leftHandTarget = characterPosition + wallNormal * (armLength - 0.1f) + 
                             Vector3.Up * 1.5f + Vector3.Right * 0.3f;
            _rightHandTarget = characterPosition + wallNormal * (armLength - 0.1f) + 
                              Vector3.Up * 1.5f - Vector3.Right * 0.3f;

            // 手部朝向应该与墙面法线对齐
            _leftHandRotation = _rightHandRotation = Quaternion.LookRotation(-wallNormal, Vector3.Up);

            // 脚部IK目标点（简单的地面接触）
            _leftFootTarget = characterPosition + Vector3.Down * 0.9f + Vector3.Right * 0.2f;
            _rightFootTarget = characterPosition + Vector3.Down * 0.9f - Vector3.Right * 0.2f;
        }

        private void ApplyIK()
        {
            // 暂时禁用IK功能，因为Skeleton类型不可用
            /*
            if (_skeleton == null)
                return;

            // 左手IK
            if (LeftHandIKWeight > 0.01f)
            {
                ApplyHandIK("LeftHand", _leftHandTarget, _leftHandRotation, LeftHandIKWeight);
            }

            // 右手IK
            if (RightHandIKWeight > 0.01f)
            {
                ApplyHandIK("RightHand", _rightHandTarget, _rightHandRotation, RightHandIKWeight);
            }

            // 脚部IK可以在需要时添加
            */
        }

        private void ApplyHandIK(string handName, Vector3 targetPosition, Quaternion targetRotation, float weight)
        {
            // 暂时禁用IK功能，因为Skeleton类型不可用
            /*
            try
            {
                // 获取手部骨骼索引
                int boneIndex = _skeleton.FindBone(handName + "_Ik");
                if (boneIndex == -1)
                    boneIndex = _skeleton.FindBone(handName);

                if (boneIndex != -1)
                {
                    // 获取当前骨骼变换
                    var currentTransform = _skeleton.Bones[boneIndex].Transform;
                    
                    // 计算目标变换
                    var targetTransform = Transform.Identity;
                    targetTransform.Translation = Vector3.Lerp(
                        currentTransform.Translation, 
                        targetPosition, 
                        weight
                    );
                    targetTransform.Orientation = Quaternion.Slerp(
                        currentTransform.Orientation, 
                        targetRotation, 
                        weight
                    );

                    // 应用变换到骨骼
                    _skeleton.Bones[boneIndex].Transform = targetTransform;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ClimbAnim] IK应用失败 {handName}: {ex.Message}");
            }
            */
        }
        #endregion

        #region 公共接口
        public void SetAnimationWeight(string layerName, float weight)
        {
            switch (layerName)
            {
                case "Base":
                    BaseLayerWeight = weight;
                    break;
                case "Hands":
                    HandLayerWeight = weight;
                    break;
                case "Body":
                    BodyLayerWeight = weight;
                    break;
            }
        }

        public float GetAnimationWeight(string layerName)
        {
            return layerName switch
            {
                "Base" => BaseLayerWeight,
                "Hands" => HandLayerWeight,
                "Body" => BodyLayerWeight,
                _ => 0f
            };
        }

        public void SetIKWeight(string limb, float weight)
        {
            switch (limb.ToLower())
            {
                case "lefthand":
                    LeftHandIKWeight = weight;
                    break;
                case "righthand":
                    RightHandIKWeight = weight;
                    break;
            }
        }

        public ClimbAnimationState GetCurrentAnimationState()
        {
            return _currentState;
        }

        public float GetBlendFactor()
        {
            return _currentBlendFactor;
        }
        #endregion

        #region 调试功能
        public override void OnDebugDraw()
        {
            if (!EnableIK)
                return;

            // 绘制IK目标点
            DebugDraw.DrawSphere(new BoundingSphere(_leftHandTarget, 0.05f), Color.Red, 0.0f);
            DebugDraw.DrawSphere(new BoundingSphere(_rightHandTarget, 0.05f), Color.Blue, 0.0f);
            DebugDraw.DrawSphere(new BoundingSphere(_leftFootTarget, 0.05f), Color.Yellow, 0.0f);
            DebugDraw.DrawSphere(new BoundingSphere(_rightFootTarget, 0.05f), Color.Green, 0.0f);

            // 绘制手部朝向
            DebugDraw.DrawLine(_leftHandTarget, _leftHandTarget + Vector3.Transform(Vector3.Forward, _leftHandRotation) * 0.2f, Color.Red, 0.0f);
            DebugDraw.DrawLine(_rightHandTarget, _rightHandTarget + Vector3.Transform(Vector3.Forward, _rightHandRotation) * 0.2f, Color.Blue, 0.0f);
        }
        #endregion
    }
}