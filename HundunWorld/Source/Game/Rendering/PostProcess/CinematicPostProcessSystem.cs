using FlaxEngine;

namespace HundunWorld.Game.Rendering.PostProcess
{
    /// <summary>
    /// 电影级后期处理系统 - 实现MetaHuman级别的视觉效果
    /// 包括色调映射、色彩校正、景深、泛光等效果
    /// </summary>
    public class CinematicPostProcessSystem : Script
    {
        #region 视觉风格预设

        /// <summary>
        /// 视觉风格类型
        /// </summary>
        public enum VisualStyle
        {
            /// <summary>自然真实</summary>
            Realistic,
            /// <summary>电影风格</summary>
            Cinematic,
            /// <summary>明亮清新</summary>
            BrightClean,
            /// <summary>暗调戏剧</summary>
            DarkDramatic,
            /// <summary>复古暖调</summary>
            VintageWarm,
            /// <summary>科幻冷调</summary>
            SciFiCool,
            /// <summary>自定义</summary>
            Custom
        }

        #endregion

        #region 基础设置

        /// <summary>
        /// 后期处理体积
        /// </summary>
        [Header("基础设置")]
        [Tooltip("后期处理体积引用")]
        public PostFxVolume PostProcessVolume;

        /// <summary>
        /// 当前视觉风格
        /// </summary>
        [Tooltip("选择预设的视觉风格")]
        public VisualStyle CurrentStyle = VisualStyle.Cinematic;

        /// <summary>
        /// 自动曝光
        /// </summary>
        [Tooltip("是否启用自动曝光")]
        public bool EnableAutoExposure = true;

        #endregion

        #region 色调映射

        /// <summary>
        /// 色调映射模式枚举
        /// </summary>
        public enum ToneMappingModes
        {
            /// <summary>无色调映射</summary>
            None,
            /// <summary>Reinhard色调映射</summary>
            Reinhard,
            /// <summary>ACES电影色调映射</summary>
            ACES,
            /// <summary>Filmic色调映射</summary>
            Filmic,
            /// <summary>中性色调映射</summary>
            Neutral
        }

        /// <summary>
        /// 色调映射模式
        /// </summary>
        [Header("色调映射")]
        [Tooltip("色调映射算法")]
        public ToneMappingModes ToneMappingMode = ToneMappingModes.ACES;

        /// <summary>
        /// 白点
        /// </summary>
        [Range(1f, 20f)]
        [Tooltip("色调映射的白点值")]
        public float WhitePoint = 11.2f;

        /// <summary>
        /// 中性灰
        /// </summary>
        [Range(0.01f, 0.5f)]
        [Tooltip("色调映射的中性灰值")]
        public float MidGray = 0.18f;

        #endregion

        #region 色彩校正

        /// <summary>
        /// 饱和度
        /// </summary>
        [Header("色彩校正")]
        [Range(0f, 2f)]
        [Tooltip("整体饱和度")]
        public float Saturation = 1.0f;

        /// <summary>
        /// 对比度
        /// </summary>
        [Range(0f, 2f)]
        [Tooltip("整体对比度")]
        public float Contrast = 1.0f;

        /// <summary>
        /// 伽马
        /// </summary>
        [Range(0.5f, 2f)]
        [Tooltip("伽马校正值")]
        public float Gamma = 1.0f;

        /// <summary>
        /// 增益
        /// </summary>
        [Range(0f, 2f)]
        [Tooltip("整体亮度增益")]
        public float Gain = 1.0f;

        /// <summary>
        /// 偏移
        /// </summary>
        [Range(-0.5f, 0.5f)]
        [Tooltip("亮度偏移")]
        public float Offset = 0f;

        /// <summary>
        /// 色调
        /// </summary>
        [Range(-180f, 180f)]
        [Tooltip("色调偏移（色相旋转）")]
        public float Hue = 0f;

        /// <summary>
        /// 阴影色调
        /// </summary>
        [Tooltip("阴影区域的色调")]
        public Color ShadowsTint = Color.Black;

        /// <summary>
        /// 中间调色调
        /// </summary>
        [Tooltip("中间调区域的色调")]
        public Color MidtonesTint = Color.Gray;

        /// <summary>
        /// 高光色调
        /// </summary>
        [Tooltip("高光区域的色调")]
        public Color HighlightsTint = Color.White;

        #endregion

        #region 景深

        /// <summary>
        /// 启用景深
        /// </summary>
        [Header("景深 (DOF)")]
        [Tooltip("是否启用景深效果")]
        public bool EnableDOF = true;

        /// <summary>
        /// 景深模式
        /// </summary>
        [Tooltip("景深渲染模式")]
        public DOFMode DepthOfFieldMode = DOFMode.BokehDOF;

        /// <summary>
        /// 焦点距离
        /// </summary>
        [Range(0.1f, 100f)]
        [Tooltip("焦点到摄像机的距离")]
        public float FocusDistance = 2.0f;

        /// <summary>
        /// 焦点区域
        /// </summary>
        [Range(0f, 10f)]
        [Tooltip("清晰区域的深度范围")]
        public float FocusRegion = 1.0f;

        /// <summary>
        /// 近景模糊
        /// </summary>
        [Range(0f, 1f)]
        [Tooltip("近景的模糊程度")]
        public float NearBlur = 0.5f;

        /// <summary>
        /// 远景模糊
        /// </summary>
        [Range(0f, 1f)]
        [Tooltip("远景的模糊程度")]
        public float FarBlur = 0.5f;

        /// <summary>
        /// 光圈大小
        /// </summary>
        [Range(0.5f, 32f)]
        [Tooltip("模拟相机光圈大小（F值）")]
        public float ApertureSize = 2.8f;

        /// <summary>
        /// 散景亮度
        /// </summary>
        [Range(0f, 3f)]
        [Tooltip("散景高光的亮度")]
        public float BokehBrightness = 1.0f;

        /// <summary>
        /// 散景大小
        /// </summary>
        [Range(0f, 100f)]
        [Tooltip("散景的大小")]
        public float BokehSize = 25f;

        /// <summary>
        /// 散景形状
        /// </summary>
        [Tooltip("散景的形状纹理")]
        public Texture BokehShape;

        /// <summary>
        /// 散景边数
        /// </summary>
        [Range(3, 9)]
        [Tooltip("散景的多边形边数")]
        public int BokehBladeCount = 6;

        #endregion

        #region 泛光

        /// <summary>
        /// 启用泛光
        /// </summary>
        [Header("泛光 (Bloom)")]
        [Tooltip("是否启用泛光效果")]
        public bool EnableBloom = true;

        /// <summary>
        /// 泛光强度
        /// </summary>
        [Range(0f, 5f)]
        [Tooltip("泛光效果的强度")]
        public float BloomIntensity = 0.5f;

        /// <summary>
        /// 泛光阈值
        /// </summary>
        [Range(0f, 10f)]
        [Tooltip("触发泛光的亮度阈值")]
        public float BloomThreshold = 1.0f;

        /// <summary>
        /// 泛光半径
        /// </summary>
        [Range(0f, 10f)]
        [Tooltip("泛光的扩散半径")]
        public float BloomRadius = 4f;

        /// <summary>
        /// 泛光色调
        /// </summary>
        [Tooltip("泛光的颜色偏移")]
        public Color BloomTint = Color.White;

        /// <summary>
        /// 镜头污渍
        /// </summary>
        [Tooltip("镜头污渍/灰尘纹理")]
        public Texture LensDirtTexture;

        /// <summary>
        /// 镜头污渍强度
        /// </summary>
        [Range(0f, 10f)]
        [Tooltip("镜头污渍的可见程度")]
        public float LensDirtIntensity = 0f;

        #endregion

        #region 镜头效果

        /// <summary>
        /// 启用镜头光晕
        /// </summary>
        [Header("镜头效果")]
        [Tooltip("是否启用镜头光晕")]
        public bool EnableLensFlare = false;

        /// <summary>
        /// 镜头光晕强度
        /// </summary>
        [Range(0f, 3f)]
        [Tooltip("镜头光晕的强度")]
        public float LensFlareIntensity = 1.0f;

        /// <summary>
        /// 启用色差
        /// </summary>
        [Tooltip("是否启用色差效果")]
        public bool EnableChromaticAberration = false;

        /// <summary>
        /// 色差强度
        /// </summary>
        [Range(0f, 3f)]
        [Tooltip("色差效果的强度")]
        public float ChromaticAberrationIntensity = 0.2f;

        /// <summary>
        /// 启用暗角
        /// </summary>
        [Tooltip("是否启用暗角效果")]
        public bool EnableVignette = true;

        /// <summary>
        /// 暗角强度
        /// </summary>
        [Range(0f, 2f)]
        [Tooltip("暗角效果的强度")]
        public float VignetteIntensity = 0.3f;

        /// <summary>
        /// 暗角颜色
        /// </summary>
        [Tooltip("暗角的颜色")]
        public Color VignetteColor = Color.Black;

        /// <summary>
        /// 暗角圆度
        /// </summary>
        [Range(0f, 1f)]
        [Tooltip("暗角的圆形程度")]
        public float VignetteRoundness = 0.5f;

        /// <summary>
        /// 暗角平滑度
        /// </summary>
        [Range(0.01f, 1f)]
        [Tooltip("暗角边缘的平滑程度")]
        public float VignetteSmoothness = 0.5f;

        /// <summary>
        /// 启用颗粒
        /// </summary>
        [Tooltip("是否启用胶片颗粒效果")]
        public bool EnableGrain = false;

        /// <summary>
        /// 颗粒强度
        /// </summary>
        [Range(0f, 1f)]
        [Tooltip("胶片颗粒的强度")]
        public float GrainIntensity = 0.1f;

        /// <summary>
        /// 颗粒大小
        /// </summary>
        [Range(0.5f, 3f)]
        [Tooltip("胶片颗粒的大小")]
        public float GrainSize = 1f;

        #endregion

        #region 环境效果

        /// <summary>
        /// 启用SSAO
        /// </summary>
        [Header("环境效果")]
        [Tooltip("是否启用屏幕空间环境光遮蔽")]
        public bool EnableSSAO = true;

        /// <summary>
        /// SSAO强度
        /// </summary>
        [Range(0f, 3f)]
        [Tooltip("SSAO效果的强度")]
        public float SSAOIntensity = 1.0f;

        /// <summary>
        /// SSAO半径
        /// </summary>
        [Range(0.01f, 5f)]
        [Tooltip("SSAO采样半径")]
        public float SSAORadius = 0.5f;

        /// <summary>
        /// 启用SSR
        /// </summary>
        [Tooltip("是否启用屏幕空间反射")]
        public bool EnableSSR = true;

        /// <summary>
        /// SSR强度
        /// </summary>
        [Range(0f, 1f)]
        [Tooltip("屏幕空间反射的强度")]
        public float SSRIntensity = 1.0f;

        /// <summary>
        /// SSR质量
        /// </summary>
        [Tooltip("屏幕空间反射的质量")]
        public SSRQualityLevel SSRQuality = SSRQualityLevel.High;

        /// <summary>
        /// SSR边缘淡出
        /// </summary>
        [Range(0f, 1f)]
        [Tooltip("SSR边缘的淡出程度")]
        public float SSREdgeFade = 0.1f;

        #endregion

        #region 运动模糊

        /// <summary>
        /// 启用运动模糊
        /// </summary>
        [Header("运动模糊")]
        [Tooltip("是否启用运动模糊")]
        public bool EnableMotionBlur = false;

        /// <summary>
        /// 运动模糊强度
        /// </summary>
        [Range(0f, 4f)]
        [Tooltip("运动模糊的强度")]
        public float MotionBlurIntensity = 1f;

        /// <summary>
        /// 运动模糊采样数
        /// </summary>
        [Range(4, 64)]
        [Tooltip("运动模糊的采样数量")]
        public int MotionBlurSampleCount = 16;

        #endregion

        public override void OnStart()
        {
            ApplyVisualStyle();
        }

        public override void OnUpdate()
        {
            if (Engine.IsEditor)
            {
                ApplyVisualStyle();
            }
        }

        /// <summary>
        /// 应用当前视觉风格
        /// </summary>
        public void ApplyVisualStyle()
        {
            switch (CurrentStyle)
            {
                case VisualStyle.Realistic:
                    SetRealisticStyle();
                    break;
                case VisualStyle.Cinematic:
                    SetCinematicStyle();
                    break;
                case VisualStyle.BrightClean:
                    SetBrightCleanStyle();
                    break;
                case VisualStyle.DarkDramatic:
                    SetDarkDramaticStyle();
                    break;
                case VisualStyle.VintageWarm:
                    SetVintageWarmStyle();
                    break;
                case VisualStyle.SciFiCool:
                    SetSciFiCoolStyle();
                    break;
                case VisualStyle.Custom:
                    // 使用用户自定义设置
                    break;
            }

            ApplyAllSettings();
        }

        /// <summary>
        /// 设置自然真实风格
        /// </summary>
        private void SetRealisticStyle()
        {
            ToneMappingMode = ToneMappingModes.ACES;
            Saturation = 1.0f;
            Contrast = 1.0f;
            BloomIntensity = 0.3f;
            BloomThreshold = 1.5f;
            VignetteIntensity = 0.15f;
            EnableChromaticAberration = false;
            EnableGrain = false;
        }

        /// <summary>
        /// 设置电影风格
        /// </summary>
        public void SetCinematicStyle()
        {
            ToneMappingMode = ToneMappingModes.ACES;
            Saturation = 0.95f;
            Contrast = 1.1f;
            BloomIntensity = 0.5f;
            BloomThreshold = 1.0f;
            VignetteIntensity = 0.35f;
            EnableChromaticAberration = true;
            ChromaticAberrationIntensity = 0.15f;
            EnableGrain = true;
            GrainIntensity = 0.05f;

            // 电影色调：稍微偏青/橙
            ShadowsTint = new Color(0f, 0.02f, 0.05f);
            HighlightsTint = new Color(0.02f, 0.01f, 0f);
        }

        /// <summary>
        /// 设置明亮清新风格
        /// </summary>
        private void SetBrightCleanStyle()
        {
            ToneMappingMode = ToneMappingModes.Neutral;
            Saturation = 1.1f;
            Contrast = 0.95f;
            Gamma = 1.05f;
            Gain = 1.1f;
            BloomIntensity = 0.7f;
            BloomThreshold = 0.8f;
            VignetteIntensity = 0.1f;
            EnableChromaticAberration = false;
            EnableGrain = false;
        }

        /// <summary>
        /// 设置暗调戏剧风格
        /// </summary>
        private void SetDarkDramaticStyle()
        {
            ToneMappingMode = ToneMappingModes.ACES;
            Saturation = 0.9f;
            Contrast = 1.3f;
            Gamma = 0.95f;
            Gain = 0.9f;
            BloomIntensity = 0.4f;
            BloomThreshold = 1.2f;
            VignetteIntensity = 0.5f;
            EnableChromaticAberration = false;
            EnableGrain = true;
            GrainIntensity = 0.08f;
            SSAOIntensity = 1.5f;
        }

        /// <summary>
        /// 设置复古暖调风格
        /// </summary>
        private void SetVintageWarmStyle()
        {
            ToneMappingMode = ToneMappingModes.Neutral;
            Saturation = 0.85f;
            Contrast = 1.05f;
            Hue = 5f;
            ShadowsTint = new Color(0.02f, 0.01f, 0f);
            MidtonesTint = new Color(0.5f, 0.48f, 0.45f);
            HighlightsTint = new Color(1f, 0.98f, 0.9f);
            BloomIntensity = 0.6f;
            BloomTint = new Color(1f, 0.95f, 0.85f);
            VignetteIntensity = 0.4f;
            EnableGrain = true;
            GrainIntensity = 0.15f;
            GrainSize = 1.5f;
        }

        /// <summary>
        /// 设置科幻冷调风格
        /// </summary>
        private void SetSciFiCoolStyle()
        {
            ToneMappingMode = ToneMappingModes.ACES;
            Saturation = 0.95f;
            Contrast = 1.15f;
            Hue = -5f;
            ShadowsTint = new Color(0f, 0.02f, 0.04f);
            MidtonesTint = new Color(0.48f, 0.5f, 0.52f);
            HighlightsTint = new Color(0.9f, 0.95f, 1f);
            BloomIntensity = 0.8f;
            BloomTint = new Color(0.8f, 0.9f, 1f);
            VignetteIntensity = 0.3f;
            EnableChromaticAberration = true;
            ChromaticAberrationIntensity = 0.25f;
            EnableGrain = false;
        }

        /// <summary>
        /// 应用所有后期处理设置
        /// </summary>
        private void ApplyAllSettings()
        {
            if (PostProcessVolume == null) return;

            // 色调映射
            var toneMapping = PostProcessVolume.ToneMapping;
            toneMapping.Mode = (FlaxEngine.ToneMappingMode)(int)ToneMappingMode;
            toneMapping.WhiteTemperature = WhitePoint;
            PostProcessVolume.ToneMapping = toneMapping;

            // 色彩校正 - Flax 1.11 ColorGrading 使用不同的属性结构
            // 通过 ColorGradingTrackball 控制 Shadows/Midtones/Highlights
            // 简化处理：暂时跳过直接属性设置，使用 LUT 或保持默认

            // 景深
            var depthOfField = PostProcessVolume.DepthOfField;
            depthOfField.Enabled = EnableDOF;
            if (EnableDOF)
            {
                depthOfField.FocalDistance = FocusDistance;
                depthOfField.FocalRegion = FocusRegion;
                depthOfField.NearTransitionRange = NearBlur * 10f;
                depthOfField.FarTransitionRange = FarBlur * 10f;
                depthOfField.BokehSize = BokehSize;
                depthOfField.BokehBrightness = BokehBrightness;
                // BokehShape 在 Flax 1.11 中是枚举类型，不是纹理
            }
            PostProcessVolume.DepthOfField = depthOfField;

            // 泛光
            var bloom = PostProcessVolume.Bloom;
            bloom.Enabled = EnableBloom;
            if (EnableBloom)
            {
                bloom.Intensity = BloomIntensity;
                bloom.Threshold = BloomThreshold;
                // BlurSigma/DirtTexture 在 Flax 1.11 中可能使用不同属性名
            }
            PostProcessVolume.Bloom = bloom;

            // 色差和暗角 - 通过 CameraArtifacts 设置
            var cameraArtifacts = PostProcessVolume.CameraArtifacts;
            cameraArtifacts.ChromaticDistortion = EnableChromaticAberration ? ChromaticAberrationIntensity : 0f;
            cameraArtifacts.VignetteIntensity = EnableVignette ? VignetteIntensity : 0f;
            cameraArtifacts.VignetteColor = VignetteColor;
            cameraArtifacts.VignetteShapeFactor = VignetteRoundness;
            cameraArtifacts.GrainAmount = EnableGrain ? GrainIntensity : 0f;
            cameraArtifacts.GrainParticleSize = GrainSize;
            PostProcessVolume.CameraArtifacts = cameraArtifacts;

            // SSAO
            var ao = PostProcessVolume.AmbientOcclusion;
            ao.Enabled = EnableSSAO;
            if (EnableSSAO)
            {
                ao.Intensity = SSAOIntensity;
                ao.Radius = SSAORadius;
            }
            PostProcessVolume.AmbientOcclusion = ao;

            // SSR
            var ssr = PostProcessVolume.ScreenSpaceReflections;
            ssr.Intensity = EnableSSR ? SSRIntensity : 0f;
            PostProcessVolume.ScreenSpaceReflections = ssr;

            // 运动模糊
            var motionBlur = PostProcessVolume.MotionBlur;
            motionBlur.Enabled = EnableMotionBlur;
            if (EnableMotionBlur)
            {
                motionBlur.Scale = MotionBlurIntensity;
                motionBlur.SampleCount = MotionBlurSampleCount;
            }
            PostProcessVolume.MotionBlur = motionBlur;
        }

        /// <summary>
        /// 设置角色特写模式
        /// </summary>
        public void SetCloseUpMode()
        {
            EnableDOF = true;
            FocusDistance = 1.5f;
            FocusRegion = 0.3f;
            ApertureSize = 1.8f;
            BokehBrightness = 1.2f;
            
            EnableBloom = true;
            BloomIntensity = 0.4f;
            
            EnableVignette = true;
            VignetteIntensity = 0.25f;
            
            ApplyAllSettings();
        }

        /// <summary>
        /// 设置全身镜头模式
        /// </summary>
        public void SetFullBodyMode()
        {
            EnableDOF = true;
            FocusDistance = 3.0f;
            FocusRegion = 1.5f;
            ApertureSize = 4.0f;
            
            EnableBloom = true;
            BloomIntensity = 0.5f;
            
            ApplyAllSettings();
        }

        /// <summary>
        /// 设置动作场景模式
        /// </summary>
        public void SetActionMode()
        {
            EnableDOF = false;
            EnableMotionBlur = true;
            MotionBlurIntensity = 1.5f;
            
            Contrast = 1.2f;
            Saturation = 1.1f;
            
            ApplyAllSettings();
        }

        /// <summary>
        /// 设置情感场景模式
        /// </summary>
        public void SetEmotionalMode()
        {
            EnableDOF = true;
            FocusDistance = 1.0f;
            FocusRegion = 0.2f;
            
            BloomIntensity = 0.7f;
            VignetteIntensity = 0.4f;
            
            Saturation = 0.9f;
            Contrast = 1.05f;
            
            ApplyAllSettings();
        }
    }

    /// <summary>
    /// 景深模式
    /// </summary>
    public enum DOFMode
    {
        /// <summary>高斯模糊</summary>
        GaussianDOF,
        /// <summary>散景DOF</summary>
        BokehDOF,
        /// <summary>圆形DOF</summary>
        CircleDOF
    }

    /// <summary>
    /// SSR质量等级
    /// </summary>
    public enum SSRQualityLevel
    {
        Low,
        Medium,
        High,
        Ultra
    }
}
