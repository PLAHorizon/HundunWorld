using FlaxEngine;
using HundunWorld.Game.UI.Components;
using HundunWorld.Game.Rendering.Materials;

namespace HundunWorld.Game.Rendering
{
    /// <summary>
    /// 角色外观预览控制器 - 管理3D角色预览的渲染设置
    /// 集成光照配置、相机控制、后处理效果
    /// </summary>
    public class CharacterAppearancePreviewController : Script
    {
        #region 预览组件

        /// <summary>
        /// 3D预览视口
        /// </summary>
        [Header("预览组件")]
        [Tooltip("3D预览视口组件")]
        public Viewport3DPreview PreviewViewport;

        /// <summary>
        /// 角色渲染系统
        /// </summary>
        [Tooltip("角色渲染系统引用")]
        public CharacterRenderingSystem RenderingSystem;

        /// <summary>
        /// 外观编辑器
        /// </summary>
        [Tooltip("外观编辑器引用")]
        public CharacterAppearanceEditor AppearanceEditor;

        #endregion

        #region 光照配置

        /// <summary>
        /// 主光源
        /// </summary>
        [Header("光照配置")]
        [Tooltip("主方向光")]
        public DirectionalLight KeyLight;

        /// <summary>
        /// 补光
        /// </summary>
        [Tooltip("补光点光源")]
        public PointLight FillLight;

        /// <summary>
        /// 轮廓光
        /// </summary>
        [Tooltip("轮廓聚光灯")]
        public SpotLight RimLight;

        /// <summary>
        /// 环境探针
        /// </summary>
        [Tooltip("环境反射探针")]
        public EnvironmentProbe EnvironmentProbe;

        #endregion

        #region 相机设置

        /// <summary>
        /// 预览相机距离
        /// </summary>
        [Header("相机设置")]
        [Range(0.5f, 10f)]
        [Tooltip("相机到角色的距离")]
        public float CameraDistance = 2.0f;

        /// <summary>
        /// 相机高度偏移
        /// </summary>
        [Range(-2f, 2f)]
        [Tooltip("相机高度偏移")]
        public float CameraHeightOffset = 0.5f;

        /// <summary>
        /// 相机俯仰角
        /// </summary>
        [Range(-45f, 45f)]
        [Tooltip("相机俯仰角度")]
        public float CameraPitch = -10f;

        /// <summary>
        /// 启用自动旋转
        /// </summary>
        [Tooltip("是否启用角色自动旋转")]
        public bool EnableAutoRotation = true;

        /// <summary>
        /// 自动旋转速度
        /// </summary>
        [Range(0f, 60f)]
        [Tooltip("角色自动旋转速度（度/秒）")]
        public float AutoRotationSpeed = 15f;

        #endregion

        #region 预览模式

        /// <summary>
        /// 预览模式
        /// </summary>
        public enum PreviewMode
        {
            /// <summary>
            /// 面部特写
            /// </summary>
            FaceCloseUp,

            /// <summary>
            /// 上半身
            /// </summary>
            UpperBody,

            /// <summary>
            /// 全身
            /// </summary>
            FullBody
        }

        /// <summary>
        /// 当前预览模式
        /// </summary>
        [Header("预览模式")]
        [Tooltip("当前预览模式")]
        public PreviewMode CurrentMode = PreviewMode.UpperBody;

        #endregion

        #region 后处理设置

        /// <summary>
        /// 启用景深
        /// </summary>
        [Header("后处理设置")]
        [Tooltip("是否启用景深效果")]
        public bool EnableDOF = true;

        /// <summary>
        /// 启用泛光
        /// </summary>
        [Tooltip("是否启用泛光效果")]
        public bool EnableBloom = true;

        /// <summary>
        /// 启用SSAO
        /// </summary>
        [Tooltip("是否启用SSAO")]
        public bool EnableSSAO = true;

        #endregion

        private float _currentRotation = 0f;
        private Actor _previewCharacter;

        public override void OnStart()
        {
            SetupLighting();
            ApplyPreviewMode(CurrentMode);
            BindEditorEvents();
        }

        public override void OnUpdate()
        {
            if (EnableAutoRotation && _previewCharacter != null)
            {
                _currentRotation += AutoRotationSpeed * Time.DeltaTime;
                if (_currentRotation >= 360f)
                    _currentRotation -= 360f;

                _previewCharacter.Orientation = Quaternion.Euler(0, _currentRotation, 0);
            }

            // 如果使用Viewport3DPreview组件，同步旋转
            if (EnableAutoRotation && PreviewViewport != null)
            {
                PreviewViewport.SetAutoRotation(true);
            }
        }

        #region 光照设置

        /// <summary>
        /// 设置三点光照
        /// </summary>
        private void SetupLighting()
        {
            SetupKeyLight();
            SetupFillLight();
            SetupRimLight();
        }

        /// <summary>
        /// 设置主光源
        /// </summary>
        private void SetupKeyLight()
        {
            if (KeyLight == null) return;

            // 主光 - 45度侧向，45度俯角
            KeyLight.Orientation = Quaternion.Euler(-45f, -45f, 0f);
            KeyLight.Brightness = 3.0f;
            KeyLight.Color = GetColorTemperature(5500f); // 日光色温
        }

        /// <summary>
        /// 设置补光
        /// </summary>
        private void SetupFillLight()
        {
            if (FillLight == null) return;

            // 补光 - 主光对侧，较弱
            FillLight.Brightness = 1.5f;
            FillLight.Radius = 5f;
            FillLight.Color = GetColorTemperature(6000f);
        }

        /// <summary>
        /// 设置轮廓光
        /// </summary>
        private void SetupRimLight()
        {
            if (RimLight == null) return;

            // 轮廓光 - 角色背后上方
            RimLight.Brightness = 2.0f;
            RimLight.InnerConeAngle = 20f;
            RimLight.OuterConeAngle = 40f;
            RimLight.Color = GetColorTemperature(7000f); // 略冷
        }

        /// <summary>
        /// 根据色温获取颜色
        /// </summary>
        private Color GetColorTemperature(float kelvin)
        {
            float temp = kelvin / 100f;
            float red, green, blue;

            if (temp <= 66)
            {
                red = 1.0f;
                green = Mathf.Saturate(0.39f * Mathf.Log(temp) - 0.63f);
            }
            else
            {
                red = Mathf.Saturate(1.29f * Mathf.Pow(temp - 60, -0.13f));
                green = Mathf.Saturate(1.13f * Mathf.Pow(temp - 60, -0.08f));
            }

            if (temp >= 66)
            {
                blue = 1.0f;
            }
            else if (temp <= 19)
            {
                blue = 0f;
            }
            else
            {
                blue = Mathf.Saturate(0.54f * Mathf.Log(temp - 10) - 1.19f);
            }

            return new Color(red, green, blue);
        }

        #endregion

        #region 预览模式

        /// <summary>
        /// 应用预览模式
        /// </summary>
        public void ApplyPreviewMode(PreviewMode mode)
        {
            CurrentMode = mode;

            switch (mode)
            {
                case PreviewMode.FaceCloseUp:
                    SetFaceCloseUpMode();
                    break;
                case PreviewMode.UpperBody:
                    SetUpperBodyMode();
                    break;
                case PreviewMode.FullBody:
                    SetFullBodyMode();
                    break;
            }
        }

        /// <summary>
        /// 设置面部特写模式
        /// </summary>
        private void SetFaceCloseUpMode()
        {
            CameraDistance = 0.8f;
            CameraHeightOffset = 1.6f;
            CameraPitch = -5f;
            EnableDOF = true;
            EnableAutoRotation = false;

            // 应用到渲染系统
            RenderingSystem?.SetCloseUpMode();

            Debug.Log("[PreviewController] 已切换到面部特写模式");
        }

        /// <summary>
        /// 设置上半身模式
        /// </summary>
        private void SetUpperBodyMode()
        {
            CameraDistance = 1.5f;
            CameraHeightOffset = 1.2f;
            CameraPitch = -10f;
            EnableDOF = true;
            EnableAutoRotation = true;

            Debug.Log("[PreviewController] 已切换到上半身模式");
        }

        /// <summary>
        /// 设置全身模式
        /// </summary>
        private void SetFullBodyMode()
        {
            CameraDistance = 3.0f;
            CameraHeightOffset = 0.8f;
            CameraPitch = -15f;
            EnableDOF = false;
            EnableAutoRotation = true;

            // 应用到渲染系统
            RenderingSystem?.SetFullBodyMode();

            Debug.Log("[PreviewController] 已切换到全身模式");
        }

        #endregion

        #region 编辑器事件绑定

        /// <summary>
        /// 绑定编辑器事件
        /// </summary>
        private void BindEditorEvents()
        {
            if (AppearanceEditor == null) return;

            AppearanceEditor.OnAppearanceChanged += OnAppearanceChanged;
            AppearanceEditor.OnSkinChanged += OnSkinChanged;
            AppearanceEditor.OnEyeChanged += OnEyeChanged;
            AppearanceEditor.OnHairChanged += OnHairChanged;
        }

        /// <summary>
        /// 外观变化处理
        /// </summary>
        private void OnAppearanceChanged(CharacterAppearancePreset preset)
        {
            Debug.Log($"[PreviewController] 外观已更新: {preset?.PresetName}");
            RefreshPreview();
        }

        /// <summary>
        /// 皮肤变化处理
        /// </summary>
        private void OnSkinChanged()
        {
            Debug.Log("[PreviewController] 皮肤参数已更新");
        }

        /// <summary>
        /// 眼睛变化处理
        /// </summary>
        private void OnEyeChanged()
        {
            Debug.Log("[PreviewController] 眼睛参数已更新");
        }

        /// <summary>
        /// 毛发变化处理
        /// </summary>
        private void OnHairChanged()
        {
            Debug.Log("[PreviewController] 毛发参数已更新");
        }

        #endregion

        #region 预览控制

        /// <summary>
        /// 加载预览角色模型
        /// </summary>
        /// <param name="modelPath">模型路径</param>
        public void LoadPreviewCharacter(string modelPath)
        {
            if (PreviewViewport != null)
            {
                PreviewViewport.LoadAnimatedModel(modelPath);
            }
        }

        /// <summary>
        /// 播放预览动画
        /// </summary>
        /// <param name="animationName">动画名称</param>
        public void PlayPreviewAnimation(string animationName)
        {
            if (PreviewViewport != null)
            {
                PreviewViewport.PlayAnimation(animationName, true);
            }
        }

        /// <summary>
        /// 停止预览动画
        /// </summary>
        public void StopPreviewAnimation()
        {
            if (PreviewViewport != null)
            {
                PreviewViewport.StopAnimation();
            }
        }

        /// <summary>
        /// 设置角色旋转角度
        /// </summary>
        /// <param name="rotation">旋转角度</param>
        public void SetCharacterRotation(float rotation)
        {
            _currentRotation = rotation;
            if (_previewCharacter != null)
            {
                _previewCharacter.Orientation = Quaternion.Euler(0, rotation, 0);
            }
            if (PreviewViewport != null)
            {
                PreviewViewport.SetModelRotation(rotation);
            }
        }

        /// <summary>
        /// 刷新预览
        /// </summary>
        public void RefreshPreview()
        {
            // 刷新材质
            AppearanceEditor?.RefreshAllMaterials();

            // 刷新渲染系统
            RenderingSystem?.ApplySkinMaterialSettings();
            RenderingSystem?.ApplyEyeMaterialSettings();
            RenderingSystem?.ApplyHairMaterialSettings();
            RenderingSystem?.ApplyPostProcessSettings();
        }

        /// <summary>
        /// 设置自动旋转
        /// </summary>
        /// <param name="enabled">是否启用</param>
        public void SetAutoRotation(bool enabled)
        {
            EnableAutoRotation = enabled;
            if (PreviewViewport != null)
            {
                PreviewViewport.SetAutoRotation(enabled);
            }
        }

        /// <summary>
        /// 重置相机视角
        /// </summary>
        public void ResetCameraView()
        {
            _currentRotation = 0f;
            ApplyPreviewMode(CurrentMode);
        }

        #endregion

        #region 截图功能

        /// <summary>
        /// 截取预览图
        /// </summary>
        /// <param name="filePath">保存路径</param>
        /// <param name="width">宽度</param>
        /// <param name="height">高度</param>
        public void CapturePreviewImage(string filePath, int width = 512, int height = 512)
        {
            // TODO: 实现截图功能
            Debug.Log($"[PreviewController] 截图功能尚未实现: {filePath}");
        }

        #endregion

        public override void OnDisable()
        {
            // 取消订阅事件
            if (AppearanceEditor != null)
            {
                AppearanceEditor.OnAppearanceChanged -= OnAppearanceChanged;
                AppearanceEditor.OnSkinChanged -= OnSkinChanged;
                AppearanceEditor.OnEyeChanged -= OnEyeChanged;
                AppearanceEditor.OnHairChanged -= OnHairChanged;
            }
        }
    }
}
