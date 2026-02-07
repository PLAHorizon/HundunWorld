using Arch.Core;
using Arch.Core.Utils;
using FlaxEngine;
using HundunWorld.Game.ECS.Components;

namespace HundunWorld.Game.ECS.Systems
{
    /// <summary>
    /// 相机系统，负责处理第三人称相机的逻辑
    /// </summary>
    public class CameraSystem : BaseSystem
    {
        private QueryDescription _queryDescription;
        private Actor _cameraActor;

        public override void Initialize(World world)
        {
            base.Initialize(world);

            // 定义查询描述，查找具有相机和位置组件的实体
            _queryDescription = new QueryDescription().WithAll<CameraComponent, PositionComponent>();
        }

        /// <summary>
        /// 设置相机Actor
        /// </summary>
        /// <param name="cameraActor">相机Actor</param>
        public void SetCameraActor(Actor cameraActor)
        {
            _cameraActor = cameraActor;
        }

        public override void Update(World world, float deltaTime)
        {
            if (_cameraActor == null)
                return;

            // 查询所有具有相机和位置组件的实体
            world.Query(in _queryDescription, (Entity entity, ref CameraComponent camera, ref PositionComponent position) =>
            {
                // 更新相机输入
                UpdateCameraInput(ref camera);

                // 计算相机位置
                CalculateCameraPosition(ref camera, ref position, _cameraActor);

                // 应用相机抖动效果
                ApplyCameraShake(ref camera, _cameraActor, deltaTime);
            });
        }

        /// <summary>
        /// 更新相机输入
        /// </summary>
        /// <param name="camera">相机组件</param>
        private void UpdateCameraInput(ref CameraComponent camera)
        {
            // 检查鼠标右键是否按下
            if (Input.GetMouseButtonDown(MouseButton.Right))
            {
                camera.IsControlling = true;
                // 隐藏并锁定光标
                Screen.CursorVisible = false;
                Screen.CursorLock = CursorLockMode.Clipped;
            }
            else if (Input.GetMouseButtonUp(MouseButton.Right))
            {
                camera.IsControlling = false;
                // 显示光标
                Screen.CursorVisible = true;
                Screen.CursorLock = CursorLockMode.None;
            }

            // 处理左Alt键切换跟随模式
            if (Input.GetKeyDown(KeyboardKeys.Alt))
            {
                camera.FollowCharacterRotation = !camera.FollowCharacterRotation;
                Debug.Log($"角色旋转跟随模式: {(camera.FollowCharacterRotation ? "开启" : "关闭")}");
            }

            if (camera.IsControlling)
            {
                // 获取鼠标移动
                float mouseX = Input.GetAxis("Mouse X");
                float mouseY = Input.GetAxis("Mouse Y");
                
                if (Mathf.Abs(mouseX) > 0.01f || Mathf.Abs(mouseY) > 0.01f)
                {
                    camera.Yaw += mouseX * 2.0f;
                    camera.Pitch -= mouseY * 2.0f;
                    
                    // 限制俯仰角
                    camera.Pitch = Mathf.Clamp(camera.Pitch, camera.MinPitch, camera.MaxPitch);
                    
                    // 记录手动旋转时间
                    camera.LastManualRotateTime = Time.GameTime;
                }
            }

            // 处理鼠标滚轮缩放
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (scroll != 0)
            {
                camera.IdealDistance -= scroll * 2.0f;
                camera.IdealDistance = Mathf.Clamp(camera.IdealDistance, camera.MinDistance, camera.MaxDistance);
                camera.LastManualRotateTime = Time.GameTime;
            }
        }

        /// <summary>
        /// 计算相机位置
        /// </summary>
        /// <param name="camera">相机组件</param>
        /// <param name="position">位置组件</param>
        /// <param name="cameraActor">相机Actor</param>
        private void CalculateCameraPosition(ref CameraComponent camera, ref PositionComponent position, Actor cameraActor)
        {
            // 平滑过渡到理想距离
            camera.CurrentDistance = Mathf.Lerp(camera.CurrentDistance, camera.IdealDistance, Time.DeltaTime * camera.PositionSmoothing);
            
            // 计算相机相对于角色的位置
            Vector3 targetPosition = position.Position + camera.Offset;

            // 计算相机角度
            float pitchRad = Mathf.DegreesToRadians * camera.Pitch;
            float yawRad = Mathf.DegreesToRadians * camera.Yaw;

            // 计算相机方向向量
            Vector3 direction = new Vector3(
                Mathf.Cos(pitchRad) * Mathf.Sin(yawRad),
                Mathf.Sin(pitchRad),
                Mathf.Cos(pitchRad) * Mathf.Cos(yawRad)
            );

            // 计算相机最终位置
            Vector3 cameraPosition = targetPosition - direction * camera.CurrentDistance;

            // 添加碰撞检测以防止相机穿透物体
            if (camera.EnableCollisionDetection)
            {
                cameraPosition = AdjustCameraPosition(ref camera, targetPosition, cameraPosition);
            }

            // 设置相机位置和朝向
            cameraActor.Position = cameraPosition;
            cameraActor.LookAt(targetPosition);
        }

        /// <summary>
        /// 调整相机位置以防止穿透物体
        /// </summary>
        /// <param name="camera">相机组件</param>
        /// <param name="targetPosition">目标位置</param>
        /// <param name="cameraPosition">相机位置</param>
        /// <returns>调整后的相机位置</returns>
        private Vector3 AdjustCameraPosition(ref CameraComponent camera, Vector3 targetPosition, Vector3 cameraPosition)
        {
            // 计算从目标到相机的方向和距离
            Vector3 directionToCamera = cameraPosition - targetPosition;
            float distanceToCamera = directionToCamera.Length;

            if (distanceToCamera > 0.001f)
            {
                directionToCamera.Normalize();

                // 执行射线检测，从目标位置向相机方向检测
                if (Physics.RayCast(targetPosition, directionToCamera, out RayCastHit hit, distanceToCamera, camera.CollisionLayers))
                {
                    // 考虑碰撞偏移
                    float hitDistance = hit.Distance - camera.CollisionOffset;
                    hitDistance = Mathf.Max(hitDistance, camera.MinDistance);
                    
                    cameraPosition = targetPosition + directionToCamera * hitDistance;
                }
            }

            return cameraPosition;
        }

        /// <summary>
        /// 应用相机抖动效果
        /// </summary>
        /// <param name="camera">相机组件</param>
        /// <param name="cameraActor">相机Actor</param>
        /// <param name="deltaTime">时间增量</param>
        private void ApplyCameraShake(ref CameraComponent camera, Actor cameraActor, float deltaTime)
        {
            // 简单的抖动效果实现
            // 在实际项目中，可能需要更复杂的抖动系统
            if (camera.ShakeOffset.LengthSquared > 0.001f)
            {
                cameraActor.Position += camera.ShakeOffset;
                // 逐渐减少抖动
                camera.ShakeOffset *= Mathf.Clamp(1.0f - deltaTime * 5.0f, 0.0f, 1.0f);
            }
        }

        /// <summary>
        /// 触发相机抖动
        /// </summary>
        /// <param name="entity">实体</param>
        /// <param name="world">世界</param>
        /// <param name="shakeAmount">抖动幅度</param>
        public void TriggerCameraShake(Entity entity, World world, Vector3 shakeAmount)
        {
            if (world.Has<CameraComponent>(entity))
            {
                var camera = world.Get<CameraComponent>(entity);
                camera.ShakeOffset += shakeAmount;
                world.Set(entity, camera);
            }
        }
    }
}