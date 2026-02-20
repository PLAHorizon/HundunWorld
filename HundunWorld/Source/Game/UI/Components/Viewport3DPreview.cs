using FlaxEngine;
using FlaxEngine.GUI;
using System;

namespace HundunWorld.Game.UI.Components
{
    /// <summary>
    /// 3D视口预览组件，用于在UI中显示3D模型和动画
    /// </summary>
    public class Viewport3DPreview : ContainerControl
    {
        private SceneRenderTask _renderTask;
        private Camera _camera;
        private Actor _modelActor;
        private StaticModel _staticModel;
        private AnimatedModel _animatedModel;
        private CharacterAnimator _animator;
        private GPUTexture _renderTexture;
        private GPUTextureBrush _textureBrush;
        private bool _isInitialized = false;
        
        // 相机参数
        private float _cameraDistance = 300f;
        private float _cameraPitch = -15f;
        private float _cameraYaw = 0f;
        
        // 模型旋转控制
        private float _modelRotation = 0f;
        private bool _autoRotate = true;
        private bool _enableManualRotation = true;
        
        // 鼠标交互
        private bool _isMouseDragging = false;
        private Float2 _lastMousePosition;
        
        public Viewport3DPreview(Float2 size)
        {
            Size = size;
            BackgroundColor = Color.Black;
            ClipChildren = true;
        }
        
        /// <summary>
        /// 初始化视口
        /// </summary>
        public void InitializeViewport()
        {
            if (_isInitialized) return;
            
            try
            {
                // 创建渲染纹理
                var desc = GPUTextureDescription.New2D(
                    (int)Size.X,
                    (int)Size.Y,
                    PixelFormat.R8G8B8A8_UNorm);
                _renderTexture = new GPUTexture();
                _renderTexture.Init(ref desc);
                
                // 创建渲染任务
                _renderTask = new SceneRenderTask();
                _renderTask.Order = -100; // 确保在UI之前渲染
                _renderTask.Output = _renderTexture;
                _renderTask.Enabled = true; // 启用渲染任务
                
                // 创建相机Actor
                var cameraActor = new EmptyActor();
                cameraActor.Name = "PreviewCamera";
                Level.SpawnActor(cameraActor); // 将相机Actor添加到场景
                
                _camera = cameraActor.AddChild<Camera>();
                _camera.FieldOfView = 60f;
                _camera.NearPlane = 10f;
                _camera.FarPlane = 10000f;
                _camera.UsePerspective = true;
                
                // 设置相机位置
                UpdateCameraPosition();
                
                // 将相机添加到渲染任务
                _renderTask.ActorsSource = ActorsSources.CustomActors;
                _renderTask.CustomActors = new Actor[] { cameraActor };
                
                // 创建纹理画笔
                _textureBrush = new GPUTextureBrush(_renderTexture);
                
                _isInitialized = true;
                FlaxEngine.Debug.Log($"3D预览视口初始化成功 - 尺寸: {Size}");
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"3D预览视口初始化失败: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 更新相机位置
        /// </summary>
        private void UpdateCameraPosition()
        {
            if (_camera == null || _camera.Parent == null) return;
            
            // 计算相机位置
            var cameraPos = new Vector3(0, 80, -_cameraDistance);
            var rotation = Quaternion.Euler(_cameraPitch, _cameraYaw, 0);
            cameraPos = Vector3.Transform(cameraPos, rotation);
            
            // 设置相机位置和朝向
            _camera.Parent.Position = cameraPos;
            _camera.Parent.LookAt(new Vector3(0, 80, 0)); // 看向角色中心位置
        }
        
        /// <summary>
        /// 清理现有模型
        /// </summary>
        private void ClearModel()
        {
            if (_modelActor != null)
            {
                // 从渲染任务中移除模型
                if (_renderTask != null && _renderTask.CustomActors != null)
                {
                    var actors = _renderTask.CustomActors;
                    // 创建新数组，排除_modelActor
                    var newActors = new Actor[actors.Length > 1 ? actors.Length - 1 : 0];
                    int index = 0;
                    foreach (var actor in actors)
                    {
                        if (actor != _modelActor && actor != _camera?.Parent)
                        {
                            if (index < newActors.Length)
                                newActors[index++] = actor;
                        }
                    }
                    _renderTask.CustomActors = newActors;
                }
                
                // 销毁模型Actor
                FlaxEngine.Object.Destroy(_modelActor);
                _modelActor = null;
                _staticModel = null;
                _animatedModel = null;
                _animator = null;
            }
        }
        
        /// <summary>
        /// 从预制体加载角色模型
        /// </summary>
        /// <param name="prefab">预制体</param>
        public void LoadFromPrefab(Prefab prefab)
        {
            if (!_isInitialized)
            {
                FlaxEngine.Debug.LogWarning("视口未初始化，无法加载预制体");
                return;
            }
            
            if (prefab == null)
            {
                FlaxEngine.Debug.LogWarning("预制体为空");
                return;
            }
            
            try
            {
                // 清理现有模型
                ClearModel();
                
                // 实例化预制体
                _modelActor = PrefabManager.SpawnPrefab(prefab);
                if (_modelActor == null)
                {
                    FlaxEngine.Debug.LogWarning($"无法实例化预制体: {prefab.Path}");
                    return;
                }
                
                // 查找模型组件
                _animatedModel = _modelActor.GetChild<AnimatedModel>();
                _staticModel = _modelActor.GetChild<StaticModel>();
                
                FlaxEngine.Debug.Log($"预制体实例化成功 - AnimatedModel: {_animatedModel != null}, StaticModel: {_staticModel != null}");
                
                // 设置模型位置
                _modelActor.Position = Vector3.Zero;
                _modelActor.LocalScale = new Vector3(1, 1, 1);
                
                // 将模型添加到渲染任务
                var currentActors = _renderTask.CustomActors ?? new Actor[0];
                var newActors = new Actor[currentActors.Length + 1];
                Array.Copy(currentActors, newActors, currentActors.Length);
                newActors[newActors.Length - 1] = _modelActor;
                _renderTask.CustomActors = newActors;
                
                FlaxEngine.Debug.Log($"预制体加载成功: {prefab.Path}, 渲染任务Actor数量: {newActors.Length}");
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"加载预制体失败: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 加载静态3D模型
        /// </summary>
        /// <param name="modelName">静态模型名称</param>
        public void LoadStaticModel(string modelName)
        {
            if (!_isInitialized)
            {
                FlaxEngine.Debug.LogWarning("视口未初始化，无法加载静态模型");
                return;
            }
            
            try
            {
                // 清理现有模型
                ClearModel();
                
                // 加载静态模型
                var model = FlaxEngine.Content.LoadAsync<Model>(modelName);
                if (model == null)
                {
                    FlaxEngine.Debug.LogWarning($"无法加载静态模型: {modelName}");
                    return;
                }
                
                // 创建模型Actor并添加到场景
                _modelActor = new EmptyActor();
                _modelActor.Name = "StaticModelPreview";
                Level.SpawnActor(_modelActor);
                
                _staticModel = _modelActor.AddChild<StaticModel>();
                _staticModel.Model = model;
                
                // 设置模型位置和缩放
                _modelActor.Position = Vector3.Zero;
                _modelActor.LocalScale = new Vector3(1, 1, 1);
                
                // 将模型添加到渲染任务
                var currentActors = _renderTask.CustomActors ?? new Actor[0];
                var newActors = new Actor[currentActors.Length + 1];
                Array.Copy(currentActors, newActors, currentActors.Length);
                newActors[newActors.Length - 1] = _modelActor;
                _renderTask.CustomActors = newActors;
                
                FlaxEngine.Debug.Log($"静态模型加载成功: {modelName}");
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"加载静态模型失败: {ex.Message}");
            }
        }
        
        public void LoadAnimatedModel(string modelName)
        {
            if (!_isInitialized)
            {
                FlaxEngine.Debug.LogWarning("视口未初始化，无法加载动画模型");
                return;
            }
            
            try
            {
                // 清理现有模型
                ClearModel();
                
                // 加载蒙皮模型
                var skinnedModel = FlaxEngine.Content.LoadAsync<SkinnedModel>(modelName);
                if (skinnedModel == null)
                {
                    FlaxEngine.Debug.LogWarning($"无法加载蒙皮模型: {modelName}");
                    // 回退到静态模型
                    LoadStaticModel(modelName.Replace(".skinned", "").Replace(".Skinned", ""));
                    return;
                }
                
                // 创建模型Actor并添加到场景
                _modelActor = new EmptyActor();
                _modelActor.Name = "AnimatedModelPreview";
                Level.SpawnActor(_modelActor);
                
                _animatedModel = _modelActor.AddChild<AnimatedModel>();
                _animatedModel.SkinnedModel = skinnedModel;
                
                // 添加动画控制器
                _animator = _modelActor.AddScript<CharacterAnimator>();
                
                // 设置模型位置和缩放
                _modelActor.Position = Vector3.Zero;
                _modelActor.LocalScale = new Vector3(1, 1, 1);
                
                // 将模型添加到渲染任务
                var currentActors = _renderTask.CustomActors ?? new Actor[0];
                var newActors = new Actor[currentActors.Length + 1];
                Array.Copy(currentActors, newActors, currentActors.Length);
                newActors[newActors.Length - 1] = _modelActor;
                _renderTask.CustomActors = newActors;
                
                FlaxEngine.Debug.Log($"带动画模型加载成功: {modelName}");
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"加载带动画模型失败: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 播放动画
        /// </summary>
        /// <param name="animationName">动画名称</param>
        /// <param name="loop">是否循环播放</param>
        public void PlayAnimation(string animationName, bool loop = true)
        {
            if (_animator != null)
            {
                _animator.PlayAnimation(animationName);
            }
            else if (_animatedModel != null)
            {
                // 直接播放动画
                // 使用动画槽控制动画播放
                // _animatedModel.PlaySlotAnimation("Default", animationName, true);
            }
            else
            {
                FlaxEngine.Debug.LogWarning("没有可用的动画控制器");
            }
        }
        
        /// <summary>
        /// 停止动画
        /// </summary>
        public void StopAnimation()
        {
            if (_animator != null)
            {
                _animator.StopAnimation();
            }
            else if (_animatedModel != null)
            {
                // _animatedModel.StopSlotAnimation("Default");
            }
        }
        
        /// <summary>
        /// 设置自动旋转
        /// </summary>
        /// <param name="enabled">是否启用自动旋转</param>
        public void SetAutoRotation(bool enabled)
        {
            _autoRotate = enabled;
        }
        
        /// <summary>
        /// 设置手动旋转
        /// </summary>
        /// <param name="enabled">是否启用手动旋转</param>
        public void SetManualRotation(bool enabled)
        {
            _enableManualRotation = enabled;
        }
        
        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);
            
            // 自动旋转模型
            if (_autoRotate && _modelActor != null)
            {
                _modelRotation += 20f * deltaTime; // 每秒旋转20度
                if (_modelRotation >= 360f)
                    _modelRotation -= 360f;
                _modelActor.Orientation = Quaternion.Euler(0, _modelRotation, 0);
            }
            
            // 更新渲染任务
            if (_renderTask != null && _renderTask.Enabled)
            {
                // 更新相机视角
                UpdateCameraPosition();
            }
        }
        
        public override void DrawSelf()
        {
            base.DrawSelf();
            
            // 绘制渲染纹理
            if (_textureBrush != null && _renderTexture != null)
            {
                _textureBrush.Draw(new Rectangle(Float2.Zero, Size), Color.White);
            }
        }
        
        public override bool OnMouseDown(Float2 location, MouseButton button)
        {
            if (base.OnMouseDown(location, button))
                return true;
                
            if (_enableManualRotation && button == MouseButton.Left)
            {
                _isMouseDragging = true;
                _lastMousePosition = location;
                return true;
            }
            
            return false;
        }
        
        public override bool OnMouseUp(Float2 location, MouseButton button)
        {
            if (base.OnMouseUp(location, button))
                return true;
                
            if (_isMouseDragging && button == MouseButton.Left)
            {
                _isMouseDragging = false;
                return true;
            }
            
            return false;
        }
        
        public override void OnMouseMove(Float2 location)
        {
            base.OnMouseMove(location);
            
            if (_isMouseDragging && _enableManualRotation)
            {
                // 计算鼠标移动差值
                var delta = location - _lastMousePosition;
                _lastMousePosition = location;
                
                // 根据鼠标移动旋转模型
                _modelRotation += delta.X * 0.5f;
                if (_modelActor != null)
                {
                    _modelActor.Orientation = Quaternion.Euler(0, _modelRotation, 0);
                }
            }
        }
        
        public override void OnEndMouseCapture()
        {
            _isMouseDragging = false;
            base.OnEndMouseCapture();
        }
        
        public override void OnDestroy()
        {
            // 清理资源
            ClearModel();
            
            if (_renderTask != null)
            {
                _renderTask.Enabled = false;
                _renderTask = null;
            }
            
            if (_renderTexture != null)
            {
                _renderTexture.ReleaseGPU();
                _renderTexture = null;
            }
            
            _textureBrush = null;
            
            base.OnDestroy();
        }
        
        /// <summary>
        /// 获取动画控制器
        /// </summary>
        /// <returns>动画控制器实例</returns>
        public CharacterAnimator GetAnimator()
        {
            return _animator;
        }
        
        /// <summary>
        /// 获取模型旋转角度
        /// </summary>
        /// <returns>模型旋转角度</returns>
        public float GetModelRotation()
        {
            return _modelRotation;
        }
        
        /// <summary>
        /// 设置模型旋转角度
        /// </summary>
        /// <param name="rotation">旋转角度</param>
        public void SetModelRotation(float rotation)
        {
            _modelRotation = rotation;
            if (_modelActor != null)
            {
                _modelActor.Orientation = Quaternion.Euler(0, _modelRotation, 0);
            }
        }
    }
}
