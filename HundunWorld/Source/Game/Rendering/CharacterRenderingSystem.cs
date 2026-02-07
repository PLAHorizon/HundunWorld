using FlaxEngine;

namespace HundunWorld.Game.Rendering
{
    /// <summary>
    /// 高质量角色渲染系统 - MetaHuman级别渲染效果管理
    /// 负责管理皮肤材质、光照、后期处理等效果
    /// </summary>
    public class CharacterRenderingSystem : Script
    {
        #region 皮肤材质设置

        /// <summary>
        /// 皮肤材质实例
        /// </summary>
        [Header("皮肤材质")]
        [Tooltip("应用于角色皮肤的SSS材质")]
        public MaterialInstance SkinMaterial;

        /// <summary>
        /// 次表面散射强度
        /// </summary>
        [Range(0f, 1f)]
        [Tooltip("控制皮肤的半透明效果强度")]
        public float SubsurfaceScatteringIntensity = 0.5f;

        /// <summary>
        /// 皮肤颜色
        /// </summary>
        [Tooltip("皮肤的基础颜色")]
        public Color SkinColor = new Color(1.0f, 0.85f, 0.75f, 1.0f);

        /// <summary>
        /// 血管层颜色
        /// </summary>
        [Tooltip("皮下血管层的颜色")]
        public Color SubsurfaceColor = new Color(0.8f, 0.2f, 0.1f, 1.0f);

        /// <summary>
        /// 皮肤粗糙度
        /// </summary>
        [Range(0f, 1f)]
        [Tooltip("皮肤表面的粗糙度")]
        public float SkinRoughness = 0.35f;

        /// <summary>
        /// 毛孔细节强度
        /// </summary>
        [Range(0f, 1f)]
        [Tooltip("皮肤毛孔细节的显著程度")]
        public float PoreDetailIntensity = 0.5f;

        /// <summary>
        /// 法线贴图强度
        /// </summary>
        [Range(0f, 2f)]
        [Tooltip("皮肤法线贴图的影响强度")]
        public float NormalMapStrength = 1.0f;

        #endregion

        #region 眼睛材质设置

        /// <summary>
        /// 眼睛材质实例
        /// </summary>
        [Header("眼睛材质")]
        [Tooltip("应用于角色眼睛的材质")]
        public MaterialInstance EyeMaterial;

        /// <summary>
        /// 虹膜颜色
        /// </summary>
        [Tooltip("虹膜的颜色")]
        public Color IrisColor = new Color(0.4f, 0.3f, 0.2f, 1.0f);

        /// <summary>
        /// 瞳孔大小
        /// </summary>
        [Range(0.1f, 0.8f)]
        [Tooltip("瞳孔相对大小")]
        public float PupilSize = 0.3f;

        /// <summary>
        /// 眼球湿润度
        /// </summary>
        [Range(0f, 1f)]
        [Tooltip("眼球表面的湿润反射效果")]
        public float EyeWetness = 0.8f;

        /// <summary>
        /// 角膜折射强度
        /// </summary>
        [Range(0f, 1f)]
        [Tooltip("角膜的折射效果强度")]
        public float CorneaRefraction = 0.5f;

        #endregion

        #region 毛发材质设置

        /// <summary>
        /// 毛发材质实例
        /// </summary>
        [Header("毛发材质")]
        [Tooltip("应用于角色头发的材质")]
        public MaterialInstance HairMaterial;

        /// <summary>
        /// 毛发基础颜色
        /// </summary>
        [Tooltip("头发的基础颜色")]
        public Color HairBaseColor = new Color(0.1f, 0.08f, 0.05f, 1.0f);

        /// <summary>
        /// 毛发高光颜色
        /// </summary>
        [Tooltip("头发高光的颜色")]
        public Color HairSpecularColor = new Color(0.8f, 0.7f, 0.6f, 1.0f);

        /// <summary>
        /// 毛发各向异性
        /// </summary>
        [Range(0f, 1f)]
        [Tooltip("毛发的各向异性高光效果")]
        public float HairAnisotropy = 0.8f;

        /// <summary>
        /// 毛发散射
        /// </summary>
        [Range(0f, 1f)]
        [Tooltip("毛发的光线散射效果")]
        public float HairScatter = 0.5f;

        #endregion

        #region 光照配置

        /// <summary>
        /// 主光源引用
        /// </summary>
        [Header("光照设置")]
        [Tooltip("场景主方向光")]
        public DirectionalLight MainLight;

        /// <summary>
        /// 主光强度
        /// </summary>
        [Range(0f, 10f)]
        [Tooltip("主光源的亮度")]
        public float MainLightIntensity = 3.0f;

        /// <summary>
        /// 主光颜色温度
        /// </summary>
        [Range(2000f, 10000f)]
        [Tooltip("主光源的色温（开尔文）")]
        public float MainLightTemperature = 5500f;

        /// <summary>
        /// 补光灯配置
        /// </summary>
        [Tooltip("面部补光点光源")]
        public PointLight FillLight;

        /// <summary>
        /// 补光强度
        /// </summary>
        [Range(0f, 5f)]
        [Tooltip("补光灯的强度")]
        public float FillLightIntensity = 1.5f;

        /// <summary>
        /// 背光/轮廓光
        /// </summary>
        [Tooltip("角色背光")]
        public SpotLight RimLight;

        /// <summary>
        /// 背光强度
        /// </summary>
        [Range(0f, 5f)]
        [Tooltip("背光的强度")]
        public float RimLightIntensity = 2.0f;

        /// <summary>
        /// 环境探针
        /// </summary>
        [Tooltip("环境反射探针")]
        public EnvironmentProbe EnvironmentProbe;

        #endregion

        #region 后期处理配置

        /// <summary>
        /// 后期处理体积
        /// </summary>
        [Header("后期处理")]
        [Tooltip("角色渲染后期处理体积")]
        public PostFxVolume PostProcessVolume;

        /// <summary>
        /// 景深开启
        /// </summary>
        [Tooltip("是否启用景深效果")]
        public bool EnableDepthOfField = true;

        /// <summary>
        /// 景深焦点距离
        /// </summary>
        [Range(0.1f, 100f)]
        [Tooltip("景深焦点距离")]
        public float FocusDistance = 2.0f;

        /// <summary>
        /// 景深光圈大小
        /// </summary>
        [Range(0.1f, 32f)]
        [Tooltip("光圈大小（影响景深程度）")]
        public float ApertureSize = 2.8f;

        /// <summary>
        /// 启用泛光
        /// </summary>
        [Tooltip("是否启用泛光效果")]
        public bool EnableBloom = true;

        /// <summary>
        /// 泛光强度
        /// </summary>
        [Range(0f, 5f)]
        [Tooltip("泛光效果强度")]
        public float BloomIntensity = 0.5f;

        /// <summary>
        /// 色调映射类型
        /// </summary>
        [Tooltip("色调映射算法")]
        public ToneMappingMode ToneMapping = ToneMappingMode.ACES;

        /// <summary>
        /// 色彩校正曝光
        /// </summary>
        [Range(-5f, 5f)]
        [Tooltip("曝光补偿")]
        public float ExposureCompensation = 0f;

        /// <summary>
        /// 色彩饱和度
        /// </summary>
        [Range(0f, 2f)]
        [Tooltip("整体色彩饱和度")]
        public float Saturation = 1.0f;

        /// <summary>
        /// 对比度
        /// </summary>
        [Range(0f, 2f)]
        [Tooltip("整体对比度")]
        public float Contrast = 1.0f;

        /// <summary>
        /// 启用SSR
        /// </summary>
        [Tooltip("是否启用屏幕空间反射")]
        public bool EnableSSR = true;

        /// <summary>
        /// SSR质量
        /// </summary>
        [Tooltip("屏幕空间反射质量等级")]
        public SSRQuality ScreenSpaceReflectionQuality = SSRQuality.High;

        /// <summary>
        /// 启用SSAO
        /// </summary>
        [Tooltip("是否启用屏幕空间环境光遮蔽")]
        public bool EnableSSAO = true;

        /// <summary>
        /// SSAO强度
        /// </summary>
        [Range(0f, 3f)]
        [Tooltip("SSAO效果强度")]
        public float SSAOIntensity = 1.0f;

        /// <summary>
        /// 启用色差
        /// </summary>
        [Tooltip("是否启用色差效果")]
        public bool EnableChromaticAberration = false;

        /// <summary>
        /// 色差强度
        /// </summary>
        [Range(0f, 1f)]
        [Tooltip("色差效果强度")]
        public float ChromaticAberrationIntensity = 0.1f;

        /// <summary>
        /// 启用暗角
        /// </summary>
        [Tooltip("是否启用暗角效果")]
        public bool EnableVignette = true;

        /// <summary>
        /// 暗角强度
        /// </summary>
        [Range(0f, 1f)]
        [Tooltip("暗角效果强度")]
        public float VignetteIntensity = 0.3f;

        #endregion

        #region 渲染质量设置

        /// <summary>
        /// 抗锯齿模式
        /// </summary>
        [Header("渲染质量")]
        [Tooltip("抗锯齿模式")]
        public AntialiasingMode AntiAliasing = AntialiasingMode.TemporalAntialiasing;

        /// <summary>
        /// 阴影质量
        /// </summary>
        [Tooltip("阴影贴图分辨率")]
        public ShadowQuality ShadowsQuality = ShadowQuality.Ultra;

        /// <summary>
        /// 全局光照模式
        /// </summary>
        [Tooltip("全局光照类型")]
        public GlobalIlluminationMode GIMode = GlobalIlluminationMode.DDGI;

        #endregion

        private Camera _mainCamera;

        public override void OnStart()
        {
            _mainCamera = Camera.MainCamera;
            
            ApplySkinMaterialSettings();
            ApplyEyeMaterialSettings();
            ApplyHairMaterialSettings();
            ApplyLightingSettings();
            ApplyPostProcessSettings();
            ApplyRenderQualitySettings();
        }

        public override void OnUpdate()
        {
            // 动态更新材质参数（可选，用于实时调试）
            if (Engine.IsEditor)
            {
                ApplySkinMaterialSettings();
                ApplyLightingSettings();
            }
        }

        /// <summary>
        /// 应用皮肤材质设置
        /// </summary>
        public void ApplySkinMaterialSettings()
        {
            if (SkinMaterial == null) return;

            // 设置SSS参数
            SkinMaterial.SetParameterValue("SubsurfaceColor", SubsurfaceColor);
            SkinMaterial.SetParameterValue("SubsurfaceIntensity", SubsurfaceScatteringIntensity);
            SkinMaterial.SetParameterValue("BaseColor", SkinColor);
            SkinMaterial.SetParameterValue("Roughness", SkinRoughness);
            SkinMaterial.SetParameterValue("PoreIntensity", PoreDetailIntensity);
            SkinMaterial.SetParameterValue("NormalStrength", NormalMapStrength);
        }

        /// <summary>
        /// 应用眼睛材质设置
        /// </summary>
        public void ApplyEyeMaterialSettings()
        {
            if (EyeMaterial == null) return;

            EyeMaterial.SetParameterValue("IrisColor", IrisColor);
            EyeMaterial.SetParameterValue("PupilSize", PupilSize);
            EyeMaterial.SetParameterValue("Wetness", EyeWetness);
            EyeMaterial.SetParameterValue("CorneaRefraction", CorneaRefraction);
        }

        /// <summary>
        /// 应用毛发材质设置
        /// </summary>
        public void ApplyHairMaterialSettings()
        {
            if (HairMaterial == null) return;

            HairMaterial.SetParameterValue("BaseColor", HairBaseColor);
            HairMaterial.SetParameterValue("SpecularColor", HairSpecularColor);
            HairMaterial.SetParameterValue("Anisotropy", HairAnisotropy);
            HairMaterial.SetParameterValue("Scatter", HairScatter);
        }

        /// <summary>
        /// 应用光照设置
        /// </summary>
        public void ApplyLightingSettings()
        {
            // 主光源设置
            if (MainLight != null)
            {
                MainLight.Brightness = MainLightIntensity;
                MainLight.Color = GetColorTemperature(MainLightTemperature);
            }

            // 补光设置
            if (FillLight != null)
            {
                FillLight.Brightness = FillLightIntensity;
            }

            // 背光设置
            if (RimLight != null)
            {
                RimLight.Brightness = RimLightIntensity;
            }
        }

        /// <summary>
        /// 应用后期处理设置
        /// </summary>
        public void ApplyPostProcessSettings()
        {
            if (PostProcessVolume == null) return;

            // 景深设置
            var depthOfField = PostProcessVolume.DepthOfField;
            depthOfField.Enabled = EnableDepthOfField;
            if (EnableDepthOfField)
            {
                depthOfField.FocalDistance = FocusDistance;
                depthOfField.BokehBrightness = 1.0f;
            }
            PostProcessVolume.DepthOfField = depthOfField;

            // 泛光设置
            var bloom = PostProcessVolume.Bloom;
            bloom.Enabled = EnableBloom;
            if (EnableBloom)
            {
                bloom.Intensity = BloomIntensity;
                bloom.Threshold = 1.0f;
            }
            PostProcessVolume.Bloom = bloom;

            // 色调映射
            var toneMapping = PostProcessVolume.ToneMapping;
            toneMapping.Mode = (FlaxEngine.ToneMappingMode)(int)ToneMapping;
            PostProcessVolume.ToneMapping = toneMapping;

            // 色彩校正 - Flax 1.11 使用不同的属性结构
            // ColorGrading 通过 ColorGradingTrackball 控制
            // 简化处理：暂时跳过直接属性设置

            // SSR
            var ssr = PostProcessVolume.ScreenSpaceReflections;
            ssr.Intensity = EnableSSR ? 1.0f : 0f;
            PostProcessVolume.ScreenSpaceReflections = ssr;

            // SSAO
            var ao = PostProcessVolume.AmbientOcclusion;
            ao.Enabled = EnableSSAO;
            if (EnableSSAO)
            {
                ao.Intensity = SSAOIntensity;
            }
            PostProcessVolume.AmbientOcclusion = ao;

            // 色差
            var cameraArtifacts = PostProcessVolume.CameraArtifacts;
            cameraArtifacts.ChromaticDistortion = EnableChromaticAberration ? ChromaticAberrationIntensity : 0f;
            PostProcessVolume.CameraArtifacts = cameraArtifacts;

            // 暗角 - Flax 1.11 使用 CameraArtifacts.VignetteIntensity
            cameraArtifacts.VignetteIntensity = EnableVignette ? VignetteIntensity : 0f;
            PostProcessVolume.CameraArtifacts = cameraArtifacts;
        }

        /// <summary>
        /// 应用渲染质量设置
        /// </summary>
        public void ApplyRenderQualitySettings()
        {
            if (_mainCamera == null) return;

            // 抗锯齿设置 - 需要在Graphics Settings中配置
            // FlaxEngine在渲染管线级别处理AA
        }

        /// <summary>
        /// 根据色温获取颜色
        /// </summary>
        private Color GetColorTemperature(float kelvin)
        {
            // 将开尔文色温转换为RGB颜色
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
        /// 创建三点光照配置（专业摄影棚风格）
        /// </summary>
        public void SetupThreePointLighting(Actor characterRoot)
        {
            if (characterRoot == null) return;

            var characterPos = characterRoot.Position;
            var cameraPos = _mainCamera?.Position ?? Vector3.Zero;
            var lookDir = (characterPos - cameraPos).Normalized;

            // 主光（Key Light）- 45度侧光
            if (MainLight != null)
            {
                var keyLightDir = Vector3.Transform(Vector3.Forward, Quaternion.Euler(-45f, -45f, 0f));
                MainLight.Orientation = Quaternion.LookRotation(keyLightDir, Vector3.Up);
            }

            // 补光（Fill Light）- 主光对侧，较弱
            if (FillLight != null)
            {
                var fillPos = characterPos + Vector3.Transform(lookDir * 2f, Quaternion.Euler(0, 45f, 0));
                fillPos.Y = characterPos.Y + 0.5f;
                FillLight.Position = fillPos;
            }

            // 背光（Rim Light）- 角色后方
            if (RimLight != null)
            {
                var rimPos = characterPos - lookDir * 2f;
                rimPos.Y = characterPos.Y + 1.5f;
                RimLight.Position = rimPos;
                RimLight.Orientation = Quaternion.LookRotation((characterPos - rimPos).Normalized, Vector3.Up);
            }
        }

        /// <summary>
        /// 设置面部特写渲染模式
        /// </summary>
        public void SetCloseUpMode()
        {
            SubsurfaceScatteringIntensity = 0.7f;
            SkinRoughness = 0.3f;
            PoreDetailIntensity = 0.7f;
            NormalMapStrength = 1.2f;
            
            EnableDepthOfField = true;
            FocusDistance = 1.0f;
            ApertureSize = 1.8f;
            
            EnableBloom = true;
            BloomIntensity = 0.3f;
            
            ApplySkinMaterialSettings();
            ApplyPostProcessSettings();
        }

        /// <summary>
        /// 设置全身渲染模式
        /// </summary>
        public void SetFullBodyMode()
        {
            SubsurfaceScatteringIntensity = 0.5f;
            SkinRoughness = 0.35f;
            PoreDetailIntensity = 0.4f;
            NormalMapStrength = 1.0f;
            
            EnableDepthOfField = true;
            FocusDistance = 3.0f;
            ApertureSize = 4.0f;
            
            ApplySkinMaterialSettings();
            ApplyPostProcessSettings();
        }
    }

    #region 枚举定义

    /// <summary>
    /// 色调映射模式
    /// </summary>
    public enum ToneMappingMode
    {
        None,
        Neutral,
        ACES
    }

    /// <summary>
    /// SSR质量等级
    /// </summary>
    public enum SSRQuality
    {
        Low,
        Medium,
        High,
        Ultra
    }

    /// <summary>
    /// 抗锯齿模式
    /// </summary>
    public enum AntialiasingMode
    {
        None,
        FXAA,
        SMAA,
        TemporalAntialiasing
    }

    /// <summary>
    /// 阴影质量
    /// </summary>
    public enum ShadowQuality
    {
        Low,
        Medium,
        High,
        Ultra
    }

    /// <summary>
    /// 全局光照模式
    /// </summary>
    public enum GlobalIlluminationMode
    {
        None,
        SSGI,
        DDGI,
        Lightmap
    }

    #endregion
}
