using FlaxEngine;
using System.Collections.Generic;

namespace HundunWorld.Game.Rendering.Lighting
{
    /// <summary>
    /// 专业角色光照系统 - 实现摄影棚级别的人物光照
    /// 包括三点光照、区域光源、面部光照优化等
    /// </summary>
    public class CharacterLightingSystem : Script
    {
        #region 光照类型枚举

        /// <summary>
        /// 光照方案类型
        /// </summary>
        public enum LightingScheme
        {
            /// <summary>标准三点光照</summary>
            ThreePoint,
            /// <summary>蝴蝶光（派拉蒙光）</summary>
            Butterfly,
            /// <summary>伦勃朗光</summary>
            Rembrandt,
            /// <summary>分割光</summary>
            Split,
            /// <summary>环形光</summary>
            Loop,
            /// <summary>边缘光</summary>
            Rim,
            /// <summary>自定义</summary>
            Custom
        }

        /// <summary>
        /// 光照氛围类型
        /// </summary>
        public enum LightingMood
        {
            /// <summary>中性自然</summary>
            Neutral,
            /// <summary>温暖</summary>
            Warm,
            /// <summary>冷色</summary>
            Cool,
            /// <summary>戏剧性</summary>
            Dramatic,
            /// <summary>柔和</summary>
            Soft,
            /// <summary>高对比</summary>
            HighContrast
        }

        #endregion

        #region 光照方案设置

        /// <summary>
        /// 当前光照方案
        /// </summary>
        [Header("光照方案")]
        [Tooltip("选择预设的光照方案")]
        public LightingScheme CurrentScheme = LightingScheme.ThreePoint;

        /// <summary>
        /// 光照氛围
        /// </summary>
        [Tooltip("光照的整体氛围")]
        public LightingMood CurrentMood = LightingMood.Neutral;

        /// <summary>
        /// 角色目标
        /// </summary>
        [Tooltip("光照系统跟踪的角色")]
        public Actor CharacterTarget;

        /// <summary>
        /// 面部焦点偏移
        /// </summary>
        [Tooltip("相对于角色位置的面部高度偏移")]
        public Vector3 FaceFocusOffset = new Vector3(0, 1.7f, 0);

        #endregion

        #region 主光源（Key Light）

        /// <summary>
        /// 主光源
        /// </summary>
        [Header("主光源 (Key Light)")]
        [Tooltip("主光源引用")]
        public DirectionalLight KeyLight;

        /// <summary>
        /// 主光强度
        /// </summary>
        [Range(0f, 10f)]
        [Tooltip("主光源的亮度")]
        public float KeyLightIntensity = 3.0f;

        /// <summary>
        /// 主光色温
        /// </summary>
        [Range(2000f, 10000f)]
        [Tooltip("主光源色温（开尔文）")]
        public float KeyLightTemperature = 5500f;

        /// <summary>
        /// 主光水平角度
        /// </summary>
        [Range(-180f, 180f)]
        [Tooltip("主光源水平方向角度")]
        public float KeyLightAzimuth = -45f;

        /// <summary>
        /// 主光垂直角度
        /// </summary>
        [Range(0f, 90f)]
        [Tooltip("主光源垂直方向角度")]
        public float KeyLightElevation = 45f;

        /// <summary>
        /// 主光柔化
        /// </summary>
        [Range(0f, 1f)]
        [Tooltip("主光源的柔化程度（影响阴影）")]
        public float KeyLightSoftness = 0.3f;

        #endregion

        #region 补光（Fill Light）

        /// <summary>
        /// 补光
        /// </summary>
        [Header("补光 (Fill Light)")]
        [Tooltip("补光源引用")]
        public PointLight FillLight;

        /// <summary>
        /// 补光强度
        /// </summary>
        [Range(0f, 5f)]
        [Tooltip("补光的亮度")]
        public float FillLightIntensity = 1.0f;

        /// <summary>
        /// 补光色温
        /// </summary>
        [Range(2000f, 10000f)]
        [Tooltip("补光色温")]
        public float FillLightTemperature = 6000f;

        /// <summary>
        /// 补光距离
        /// </summary>
        [Range(0.5f, 10f)]
        [Tooltip("补光到角色的距离")]
        public float FillLightDistance = 2.5f;

        /// <summary>
        /// 补光水平偏移
        /// </summary>
        [Range(-180f, 180f)]
        [Tooltip("补光相对主光的水平偏移")]
        public float FillLightAzimuthOffset = 90f;

        /// <summary>
        /// 主/补光比
        /// </summary>
        [Range(1f, 8f)]
        [Tooltip("主光与补光的亮度比例")]
        public float KeyToFillRatio = 3f;

        #endregion

        #region 背光/轮廓光（Rim Light）

        /// <summary>
        /// 背光
        /// </summary>
        [Header("背光 (Rim Light)")]
        [Tooltip("背光源引用")]
        public SpotLight RimLight;

        /// <summary>
        /// 背光强度
        /// </summary>
        [Range(0f, 5f)]
        [Tooltip("背光的亮度")]
        public float RimLightIntensity = 2.0f;

        /// <summary>
        /// 背光色温
        /// </summary>
        [Range(2000f, 10000f)]
        [Tooltip("背光色温")]
        public float RimLightTemperature = 5500f;

        /// <summary>
        /// 背光角度
        /// </summary>
        [Range(0f, 60f)]
        [Tooltip("背光的聚光角度")]
        public float RimLightAngle = 30f;

        /// <summary>
        /// 背光距离
        /// </summary>
        [Range(1f, 10f)]
        [Tooltip("背光到角色的距离")]
        public float RimLightDistance = 3f;

        /// <summary>
        /// 背光高度偏移
        /// </summary>
        [Range(0f, 3f)]
        [Tooltip("背光相对角色的高度偏移")]
        public float RimLightHeightOffset = 1.5f;

        #endregion

        #region 眼神光（Eye Light）

        /// <summary>
        /// 眼神光
        /// </summary>
        [Header("眼神光 (Eye Light)")]
        [Tooltip("眼神光源引用（小型点光源）")]
        public PointLight EyeLight;

        /// <summary>
        /// 眼神光强度
        /// </summary>
        [Range(0f, 2f)]
        [Tooltip("眼神光的亮度")]
        public float EyeLightIntensity = 0.5f;

        /// <summary>
        /// 眼神光范围
        /// </summary>
        [Range(0.1f, 2f)]
        [Tooltip("眼神光的影响范围")]
        public float EyeLightRadius = 0.5f;

        /// <summary>
        /// 启用眼神光
        /// </summary>
        [Tooltip("是否启用眼神光")]
        public bool EnableEyeLight = true;

        #endregion

        #region 区域光源（Area Lights）

        /// <summary>
        /// 区域光列表
        /// </summary>
        [Header("区域光源")]
        [Tooltip("场景中的区域光源")]
        public List<PointLight> AreaLights = new List<PointLight>();

        /// <summary>
        /// 区域光整体强度
        /// </summary>
        [Range(0f, 3f)]
        [Tooltip("所有区域光的整体强度倍增")]
        public float AreaLightIntensityMultiplier = 1.0f;

        #endregion

        #region 环境光设置

        /// <summary>
        /// 天空光
        /// </summary>
        [Header("环境光")]
        [Tooltip("天空光源引用")]
        public SkyLight SkyLightSource;

        /// <summary>
        /// 天空光强度
        /// </summary>
        [Range(0f, 5f)]
        [Tooltip("天空光的亮度")]
        public float SkyLightIntensity = 1.0f;

        /// <summary>
        /// 环境探针
        /// </summary>
        [Tooltip("环境反射探针")]
        public EnvironmentProbe EnvProbe;

        /// <summary>
        /// 环境反射强度
        /// </summary>
        [Range(0f, 2f)]
        [Tooltip("环境反射的强度")]
        public float EnvironmentReflectionIntensity = 1.0f;

        /// <summary>
        /// 使用HDRI
        /// </summary>
        [Tooltip("是否使用HDRI环境贴图")]
        public bool UseHDRI = false;

        /// <summary>
        /// HDRI纹理
        /// </summary>
        [Tooltip("HDRI环境贴图")]
        public CubeTexture HDRITexture;

        /// <summary>
        /// HDRI旋转
        /// </summary>
        [Range(0f, 360f)]
        [Tooltip("HDRI的旋转角度")]
        public float HDRIRotation = 0f;

        #endregion

        #region 全局光照设置

        /// <summary>
        /// 启用DDGI
        /// </summary>
        [Header("全局光照")]
        [Tooltip("是否启用动态漫反射全局光照")]
        public bool EnableDDGI = true;

        /// <summary>
        /// GI强度
        /// </summary>
        [Range(0f, 2f)]
        [Tooltip("全局光照的强度")]
        public float GIIntensity = 1.0f;

        /// <summary>
        /// 间接光反弹次数
        /// </summary>
        [Range(1, 4)]
        [Tooltip("间接光的反弹次数")]
        public int IndirectBounces = 2;

        #endregion

        private Camera _mainCamera;

        public override void OnStart()
        {
            _mainCamera = Camera.MainCamera;
            ApplyLightingScheme();
        }

        public override void OnUpdate()
        {
            // 跟随角色更新光照位置
            if (CharacterTarget != null)
            {
                UpdateLightPositions();
            }

            if (Engine.IsEditor)
            {
                ApplyLightingScheme();
            }
        }

        /// <summary>
        /// 应用当前光照方案
        /// </summary>
        public void ApplyLightingScheme()
        {
            switch (CurrentScheme)
            {
                case LightingScheme.ThreePoint:
                    SetupThreePointLighting();
                    break;
                case LightingScheme.Butterfly:
                    SetupButterflyLighting();
                    break;
                case LightingScheme.Rembrandt:
                    SetupRembrandtLighting();
                    break;
                case LightingScheme.Split:
                    SetupSplitLighting();
                    break;
                case LightingScheme.Loop:
                    SetupLoopLighting();
                    break;
                case LightingScheme.Rim:
                    SetupRimLighting();
                    break;
                case LightingScheme.Custom:
                    // 自定义模式，使用用户设置的值
                    break;
            }

            ApplyMoodSettings();
            ApplyLightSettings();
        }

        /// <summary>
        /// 设置三点光照
        /// </summary>
        private void SetupThreePointLighting()
        {
            KeyLightAzimuth = -45f;
            KeyLightElevation = 45f;
            KeyLightIntensity = 3.0f;

            FillLightAzimuthOffset = 90f;
            FillLightIntensity = 1.0f;

            RimLightIntensity = 2.0f;
            RimLightHeightOffset = 1.5f;
        }

        /// <summary>
        /// 设置蝴蝶光（派拉蒙光）
        /// </summary>
        private void SetupButterflyLighting()
        {
            KeyLightAzimuth = 0f;
            KeyLightElevation = 60f;
            KeyLightIntensity = 3.5f;

            FillLightAzimuthOffset = 180f;
            FillLightIntensity = 0.8f;

            RimLightIntensity = 1.5f;
        }

        /// <summary>
        /// 设置伦勃朗光
        /// </summary>
        private void SetupRembrandtLighting()
        {
            KeyLightAzimuth = -45f;
            KeyLightElevation = 45f;
            KeyLightIntensity = 4.0f;

            FillLightAzimuthOffset = 135f;
            FillLightIntensity = 0.5f;

            RimLightIntensity = 1.0f;
        }

        /// <summary>
        /// 设置分割光
        /// </summary>
        private void SetupSplitLighting()
        {
            KeyLightAzimuth = -90f;
            KeyLightElevation = 30f;
            KeyLightIntensity = 3.5f;

            FillLightIntensity = 0.3f;

            RimLightIntensity = 2.5f;
        }

        /// <summary>
        /// 设置环形光
        /// </summary>
        private void SetupLoopLighting()
        {
            KeyLightAzimuth = -30f;
            KeyLightElevation = 40f;
            KeyLightIntensity = 3.0f;

            FillLightAzimuthOffset = 60f;
            FillLightIntensity = 1.2f;

            RimLightIntensity = 1.5f;
        }

        /// <summary>
        /// 设置边缘光优先
        /// </summary>
        private void SetupRimLighting()
        {
            KeyLightAzimuth = -45f;
            KeyLightElevation = 40f;
            KeyLightIntensity = 2.0f;

            FillLightIntensity = 0.8f;

            RimLightIntensity = 4.0f;
            RimLightHeightOffset = 2.0f;
        }

        /// <summary>
        /// 应用氛围设置
        /// </summary>
        private void ApplyMoodSettings()
        {
            switch (CurrentMood)
            {
                case LightingMood.Neutral:
                    KeyLightTemperature = 5500f;
                    FillLightTemperature = 5500f;
                    RimLightTemperature = 5500f;
                    break;

                case LightingMood.Warm:
                    KeyLightTemperature = 4500f;
                    FillLightTemperature = 4000f;
                    RimLightTemperature = 5000f;
                    break;

                case LightingMood.Cool:
                    KeyLightTemperature = 6500f;
                    FillLightTemperature = 7000f;
                    RimLightTemperature = 6000f;
                    break;

                case LightingMood.Dramatic:
                    KeyLightIntensity *= 1.3f;
                    FillLightIntensity *= 0.5f;
                    KeyToFillRatio = 6f;
                    break;

                case LightingMood.Soft:
                    KeyLightSoftness = 0.6f;
                    KeyLightIntensity *= 0.8f;
                    FillLightIntensity *= 1.2f;
                    KeyToFillRatio = 2f;
                    break;

                case LightingMood.HighContrast:
                    KeyLightIntensity *= 1.5f;
                    FillLightIntensity *= 0.3f;
                    RimLightIntensity *= 1.5f;
                    KeyToFillRatio = 8f;
                    break;
            }
        }

        /// <summary>
        /// 应用光源设置
        /// </summary>
        private void ApplyLightSettings()
        {
            // 主光
            if (KeyLight != null)
            {
                KeyLight.Brightness = KeyLightIntensity;
                KeyLight.Color = GetColorFromTemperature(KeyLightTemperature);
                
                var rotation = Quaternion.Euler(-KeyLightElevation, KeyLightAzimuth, 0f);
                KeyLight.Orientation = rotation;
            }

            // 补光
            if (FillLight != null)
            {
                FillLight.Brightness = KeyLightIntensity / KeyToFillRatio;
                FillLight.Color = GetColorFromTemperature(FillLightTemperature);
            }

            // 背光
            if (RimLight != null)
            {
                RimLight.Brightness = RimLightIntensity;
                RimLight.Color = GetColorFromTemperature(RimLightTemperature);
                RimLight.OuterConeAngle = RimLightAngle;
            }

            // 眼神光
            if (EyeLight != null && EnableEyeLight)
            {
                EyeLight.Brightness = EyeLightIntensity;
                EyeLight.Radius = EyeLightRadius;
            }
            else if (EyeLight != null)
            {
                EyeLight.Brightness = 0f;
            }

            // 天空光
            if (SkyLightSource != null)
            {
                SkyLightSource.Brightness = SkyLightIntensity;
                
                if (UseHDRI && HDRITexture != null)
                {
                    SkyLightSource.CustomTexture = HDRITexture;
                }
            }

            // 区域光
            foreach (var areaLight in AreaLights)
            {
                if (areaLight != null)
                {
                    areaLight.Brightness *= AreaLightIntensityMultiplier;
                }
            }
        }

        /// <summary>
        /// 更新光源位置
        /// </summary>
        private void UpdateLightPositions()
        {
            if (CharacterTarget == null) return;

            var facePosition = CharacterTarget.Position + FaceFocusOffset;
            var cameraDirection = (_mainCamera?.Position ?? Vector3.Zero) - facePosition;
            if (cameraDirection.LengthSquared > 0.001f)
            {
                cameraDirection.Normalize();
            }
            else
            {
                cameraDirection = Vector3.Forward;
            }

            // 补光位置
            if (FillLight != null)
            {
                var fillAngle = Mathf.DegreesToRadians * FillLightAzimuthOffset;
                var fillDir = new Vector3(
                    Mathf.Sin(fillAngle) * cameraDirection.X - Mathf.Cos(fillAngle) * cameraDirection.Z,
                    0,
                    Mathf.Cos(fillAngle) * cameraDirection.X + Mathf.Sin(fillAngle) * cameraDirection.Z
                ).Normalized;
                
                FillLight.Position = facePosition + fillDir * FillLightDistance;
            }

            // 背光位置
            if (RimLight != null)
            {
                var rimPosition = facePosition - cameraDirection * RimLightDistance;
                rimPosition.Y = facePosition.Y + RimLightHeightOffset;
                RimLight.Position = rimPosition;
                RimLight.Orientation = Quaternion.LookRotation((facePosition - rimPosition).Normalized, Vector3.Up);
            }

            // 眼神光位置（靠近摄像机）
            if (EyeLight != null && EnableEyeLight)
            {
                var eyePos = facePosition + cameraDirection * 0.3f;
                eyePos.Y += 0.1f;
                EyeLight.Position = eyePos;
            }
        }

        /// <summary>
        /// 根据色温获取颜色
        /// </summary>
        private Color GetColorFromTemperature(float kelvin)
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

        /// <summary>
        /// 创建完整的光照设置
        /// </summary>
        public void CreateFullLightingRig()
        {
            if (CharacterTarget == null)
            {
                Debug.LogWarning("CharacterLightingSystem: 需要指定 CharacterTarget");
                return;
            }

            var parent = Actor;

            // 创建主光（方向光）
            if (KeyLight == null)
            {
                KeyLight = new DirectionalLight { Name = "KeyLight" };
                KeyLight.SetParent(parent, false);
            }

            // 创建补光（点光源）
            if (FillLight == null)
            {
                FillLight = new PointLight { Name = "FillLight" };
                FillLight.SetParent(parent, false);
            }

            // 创建背光（聚光灯）
            if (RimLight == null)
            {
                RimLight = new SpotLight { Name = "RimLight" };
                RimLight.SetParent(parent, false);
            }

            // 创建眼神光
            if (EyeLight == null && EnableEyeLight)
            {
                EyeLight = new PointLight { Name = "EyeLight" };
                EyeLight.SetParent(parent, false);
            }

            ApplyLightingScheme();
            UpdateLightPositions();
        }
    }
}
