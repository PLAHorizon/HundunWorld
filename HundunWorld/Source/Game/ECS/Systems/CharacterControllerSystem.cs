using Arch.Core;
using Arch.Core.Utils;
using FlaxEngine;
using HundunWorld.Game.ECS.Components;

namespace HundunWorld.Game.ECS.Systems
{
    /// <summary>
    /// 角色控制器系统，负责处理角色移动和控制逻辑
    /// </summary>
    public class CharacterControllerSystem : BaseSystem
    {
        private QueryDescription _queryDescription;
        private Actor _characterActor; // 用于射线检测的参考Actor

        public override void Initialize(World world)
        {
            base.Initialize(world);
            
            // 定义查询描述，查找具有角色控制器、位置和速度组件的实体
            _queryDescription = new QueryDescription().WithAll<CharacterControllerComponent, PositionComponent, VelocityComponent>();
        }

        /// <summary>
        /// 设置角色Actor（用于射线检测）
        /// </summary>
        /// <param name="characterActor">角色Actor</param>
        public void SetCharacterActor(Actor characterActor)
        {
            _characterActor = characterActor;
        }

        public override void Update(World world, float deltaTime)
        {
            // 更新输入
            UpdateInput(world, deltaTime);

            // 查询所有具有角色控制器、位置和速度组件的实体
            world.Query(in _queryDescription, (Entity entity, ref CharacterControllerComponent characterController, ref PositionComponent position, ref VelocityComponent velocity) =>
            {
                // 处理角色移动
                HandleCharacterMovement(ref characterController, ref position, ref velocity, deltaTime);

                // 处理角色朝向
                HandleCharacterRotation(ref characterController, ref position);

                // 更新组件
                world.Set(entity, characterController);
                world.Set(entity, position);
                world.Set(entity, velocity);
            });
        }

        /// <summary>
        /// 更新输入
        /// </summary>
        /// <param name="world">世界</param>
        /// <param name="deltaTime">时间增量</param>
        private void UpdateInput(World world, float deltaTime)
        {
            // 获取所有具有输入组件的实体
            var inputQuery = new QueryDescription().WithAll<InputComponent>();
            world.Query(in inputQuery, (Entity entity, ref InputComponent input) =>
            {
                // 获取轴输入
                input.Horizontal = Input.GetAxis("Horizontal");
                input.Vertical = Input.GetAxis("Vertical");
                
                // 获取鼠标输入
                input.MouseX = Input.GetAxis("Mouse X");
                input.MouseY = Input.GetAxis("Mouse Y");
                input.MouseWheel = Input.GetAxis("Mouse ScrollWheel");
                
                // 获取按键输入
                input.Fire1 = Input.GetMouseButton(MouseButton.Left);
                input.Fire2 = Input.GetMouseButton(MouseButton.Right);
                input.Jump = Input.GetKey(KeyboardKeys.Spacebar);
                
                // 获取鼠标屏幕位置
                input.MouseScreenPosition = Input.MouseScreenPosition;
                
                // 检查地面点击
                if (input.Fire1 && _characterActor != null)
                {
                    // 执行射线检测来确定点击位置
                    input.GroundClicked = PerformGroundRaycast(input.MouseScreenPosition, out Vector3 hitPoint);
                    if (input.GroundClicked)
                    {
                        input.MouseWorldPosition = hitPoint;
                    }
                }
                
                // 更新组件
                world.Set(entity, input);
            });
        }

        /// <summary>
        /// 执行地面射线检测
        /// </summary>
        /// <param name="screenPosition">屏幕位置</param>
        /// <param name="hitPoint">击中点</param>
        /// <returns>是否击中地面</returns>
        private bool PerformGroundRaycast(Float2 screenPosition, out Vector3 hitPoint)
        {
            hitPoint = Vector3.Zero;
            
            if (_characterActor == null)
                return false;

            // 从屏幕位置创建射线
            Camera mainCamera = Camera.MainCamera;
            if (mainCamera == null)
                return false;

            Ray ray = mainCamera.ConvertMouseToRay(screenPosition);

            // 执行射线检测，使用FlaxEngine的正确API
            // 对于地面检测，使用合理的检测距离（1000.0f是合理的最大检测距离）
            if (Physics.RayCast(ray.Position, ray.Direction, out var hitInfo, 1000.0f))
            {
                // 需要重新计算碰撞点
                hitPoint = hitInfo.Point;
                return true;
            }

            return false;
        }

        /// <summary>
        /// 处理角色移动
        /// </summary>
        /// <param name="characterController">角色控制器组件</param>
        /// <param name="position">位置组件</param>
        /// <param name="velocity">速度组件</param>
        /// <param name="deltaTime">时间增量</param>
        private void HandleCharacterMovement(ref CharacterControllerComponent characterController, ref PositionComponent position, ref VelocityComponent velocity, float deltaTime)
        {
            // 更新体力系统
            UpdateStaminaSystem(ref characterController, deltaTime);
            
            // 更新滑行系统
            UpdateSlideSystem(ref characterController, deltaTime);
            
            // 处理八方键移动
            Vector3 moveDirection = Vector3.Zero;
            
            // 基于输入计算移动方向
            moveDirection += Vector3.Forward * Input.GetAxis("Vertical");
            moveDirection += Vector3.Right * Input.GetAxis("Horizontal");
            
            // 标准化移动方向
            if (moveDirection.LengthSquared > 0.001f)
            {
                moveDirection.Normalize();
                characterController.FacingDirection = moveDirection;
            }
            
            // 处理辅助输入（跑步、冲刺、蹲伏、滑行）
            HandleAuxiliaryInputs(ref characterController);
            
            // 计算目标速度
            float targetSpeed = CalculateTargetSpeed(ref characterController, moveDirection);
            
            // 应用移动速度
            Vector3 moveVelocity = moveDirection * targetSpeed;
            
            // 处理重力
            if (!characterController.IsGrounded)
            {
                moveVelocity.Y += characterController.Gravity * deltaTime;
            }
            
            // 处理跳跃
            if (characterController.IsGrounded && Input.GetKey(KeyboardKeys.Spacebar))
            {
                moveVelocity.Y = characterController.JumpForce;
                characterController.IsGrounded = false;
            }
            
            // 处理点击移动
            if (characterController.IsMovingToTarget)
            {
                Vector3 directionToTarget = characterController.TargetPosition - position.Position;
                directionToTarget.Y = 0; // 忽略Y轴差异
                
                if (directionToTarget.LengthSquared > 0.1f)
                {
                    directionToTarget.Normalize();
                    moveVelocity = directionToTarget * targetSpeed;
                    characterController.FacingDirection = directionToTarget;
                }
                else
                {
                    characterController.IsMovingToTarget = false;
                    moveVelocity.X = 0;
                    moveVelocity.Z = 0;
                }
            }
            
            // 更新速度和位置
            velocity.Velocity = moveVelocity;
            position.Position += moveVelocity * deltaTime;
            
            // 简单的地面检测（在实际项目中应该使用碰撞检测）
            if (position.Position.Y < 0)
            {
                position.Position.Y = 0;
                characterController.IsGrounded = true;
                velocity.Velocity.Y = 0;
            }
        }

        /// <summary>
        /// 处理角色朝向
        /// </summary>
        /// <param name="characterController">角色控制器组件</param>
        /// <param name="position">位置组件</param>
        private void HandleCharacterRotation(ref CharacterControllerComponent characterController, ref PositionComponent position)
        {
            // 如果有移动方向，则旋转角色朝向
            if (characterController.FacingDirection.LengthSquared > 0.001f)
            {
                // 在实际项目中，这里需要旋转角色模型
                // 例如：_characterActor?.Orientation = Quaternion.LookRotation(characterController.FacingDirection);
            }
        }

        /// <summary>
        /// 设置角色移动到目标位置
        /// </summary>
        /// <param name="entity">实体</param>
        /// <param name="world">世界</param>
        /// <param name="targetPosition">目标位置</param>
        public void SetMoveToTarget(Entity entity, World world, Vector3 targetPosition)
        {
            if (world.Has<CharacterControllerComponent>(entity))
            {
                var characterController = world.Get<CharacterControllerComponent>(entity);
                characterController.TargetPosition = targetPosition;
                characterController.IsMovingToTarget = true;
                world.Set(entity, characterController);
            }
        }

        #region 冲刺和滑行系统

        /// <summary>
        /// 处理辅助输入（跑步、冲刺、蹲伏、滑行）
        /// </summary>
        /// <param name="characterController">角色控制器组件</param>
        private void HandleAuxiliaryInputs(ref CharacterControllerComponent characterController)
        {
            // 处理跑步输入
            characterController.IsRunning = Input.GetKey(KeyboardKeys.Shift);
            
            // 处理冲刺输入
            bool sprintPressed = Input.GetKey(KeyboardKeys.Shift);
            HandleSprintInput(ref characterController, sprintPressed);
            
            // 处理蹲伏输入
            if (Input.GetKeyDown(KeyboardKeys.C))
            {
                HandleCrouchInput(ref characterController);
            }
            
            // 处理滑行输入（冲刺状态下按蹲伏键）
            bool slidePressed = Input.GetKeyDown(KeyboardKeys.C);
            if (slidePressed && characterController.IsSprinting && characterController.IsGrounded)
            {
                StartSlide(ref characterController);
            }
        }

        /// <summary>
        /// 处理冲刺输入
        /// </summary>
        /// <param name="characterController">角色控制器组件</param>
        /// <param name="sprintPressed">冲刺键是否按下</param>
        private void HandleSprintInput(ref CharacterControllerComponent characterController, bool sprintPressed)
        {
            if (sprintPressed && CanSprint(ref characterController) && !characterController.IsCrouching && !characterController.IsSliding)
            {
                characterController.IsSprinting = true;
            }
            else
            {
                characterController.IsSprinting = false;
            }
        }

        /// <summary>
        /// 处理蹲伏输入
        /// </summary>
        /// <param name="characterController">角色控制器组件</param>
        private void HandleCrouchInput(ref CharacterControllerComponent characterController)
        {
            if (!characterController.IsSliding)
            {
                characterController.IsCrouching = !characterController.IsCrouching;
                if (characterController.IsCrouching)
                {
                    characterController.IsSprinting = false;
                }
            }
        }

        /// <summary>
        /// 检查是否可以冲刺
        /// </summary>
        /// <param name="characterController">角色控制器组件</param>
        /// <returns>是否可以冲刺</returns>
        private bool CanSprint(ref CharacterControllerComponent characterController)
        {
            return characterController.IsGrounded && characterController.CurrentStamina > 10.0f;
        }

        /// <summary>
        /// 计算目标移动速度
        /// </summary>
        /// <param name="characterController">角色控制器组件</param>
        /// <param name="inputDirection">输入方向</param>
        /// <returns>目标速度</returns>
        private float CalculateTargetSpeed(ref CharacterControllerComponent characterController, Vector3 inputDirection)
        {
            if (inputDirection.LengthSquared <= 0.001f && !characterController.IsSliding)
                return 0f;
            
            float baseSpeed = characterController.MoveSpeed;
            
            // 根据状态调整速度
            if (characterController.IsSliding)
            {
                return baseSpeed * characterController.SlideSpeedMultiplier;
            }
            else if (characterController.IsCrouching)
            {
                return baseSpeed * characterController.CrouchSpeedMultiplier;
            }
            else if (characterController.IsSprinting && CanSprint(ref characterController))
            {
                return baseSpeed * characterController.SprintSpeedMultiplier;
            }
            else if (characterController.IsRunning)
            {
                return baseSpeed * characterController.RunSpeedMultiplier;
            }
            
            return baseSpeed;
        }

        /// <summary>
        /// 更新体力系统
        /// </summary>
        /// <param name="characterController">角色控制器组件</param>
        /// <param name="deltaTime">时间增量</param>
        private void UpdateStaminaSystem(ref CharacterControllerComponent characterController, float deltaTime)
        {
            if (characterController.IsSprinting)
            {
                // 消耗体力
                characterController.CurrentStamina -= characterController.SprintStaminaCost * deltaTime;
                characterController.CurrentStamina = Mathf.Max(characterController.CurrentStamina, 0.0f);
            }
            else
            {
                // 恢复体力
                characterController.CurrentStamina += characterController.StaminaRegenRate * deltaTime;
                characterController.CurrentStamina = Mathf.Min(characterController.CurrentStamina, characterController.MaxStamina);
            }
        }

        /// <summary>
        /// 开始滑行
        /// </summary>
        /// <param name="characterController">角色控制器组件</param>
        private void StartSlide(ref CharacterControllerComponent characterController)
        {
            if (characterController.IsSliding) return;
            
            characterController.IsSliding = true;
            characterController.SlideDuration = characterController.MaxSlideTime;
            characterController.IsCrouching = true;
            characterController.IsSprinting = false;
        }

        /// <summary>
        /// 更新滑行系统
        /// </summary>
        /// <param name="characterController">角色控制器组件</param>
        /// <param name="deltaTime">时间增量</param>
        private void UpdateSlideSystem(ref CharacterControllerComponent characterController, float deltaTime)
        {
            if (!characterController.IsSliding) return;
            
            characterController.SlideDuration -= deltaTime;
            
            // 滑行结束条件
            if (characterController.SlideDuration <= 0.0f)
            {
                EndSlide(ref characterController);
            }
        }

        /// <summary>
        /// 结束滑行
        /// </summary>
        /// <param name="characterController">角色控制器组件</param>
        private void EndSlide(ref CharacterControllerComponent characterController)
        {
            characterController.IsSliding = false;
            characterController.SlideDuration = 0f;
            characterController.IsCrouching = false;
        }

        #endregion
    }
}