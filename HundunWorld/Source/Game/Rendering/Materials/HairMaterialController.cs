using FlaxEngine;

namespace HundunWorld.Game.Rendering.Materials
{
    /// <summary>
    /// 毛发材质控制器 - 管理高质量毛发渲染效果
    /// 实现各向异性高光、多层散射等真实毛发效果
    /// </summary>
    public class HairMaterialController : Script
    {
        #region 材质资源

        /// <summary>
        /// 毛发材质实例
        /// </summary>
        [Header("材质资源")]
        [Tooltip("毛发材质实例")]
        public MaterialInstance HairMaterialInstance;

        /// <summary>
        /// 毛发漫反射贴图
        /// </summary>
        [Tooltip("毛发颜色贴图")]
        public Texture DiffuseMap;

        /// <summary>
        /// 毛发法线贴图
        /// </summary>
        [Tooltip("毛发法线贴图")]
        public Texture NormalMap;

        /// <summary>
        /// 毛发流向贴图
        /// </summary>
        [Tooltip("毛发流向贴图（用于各向异性高光）")]
        public Texture FlowMap;

        /// <summary>
        /// 毛发Alpha贴图
        /// </summary>
        [Tooltip("毛发透明度贴图")]
        public Texture AlphaMap;

        /// <summary>
        /// AO贴图
        /// </summary>
        [Tooltip("毛发环境光遮蔽贴图")]
        public Texture AOMap;

        /// <summary>
        /// 根部遮蔽贴图
        /// </summary>
        [Tooltip("发根遮蔽贴图")]
        public Texture RootOcclusionMap;

        /// <summary>
        /// 高度渐变贴图
        /// </summary>
        [Tooltip("从发根到发尖的高度渐变贴图")]
        public Texture HeightGradientMap;

        #endregion

        #region 基础颜色参数

        /// <summary>
        /// 发根颜色
        /// </summary>
        [Header("颜色设置")]
        [Tooltip("发根的颜色（通常较深）")]
        public Color RootColor = new Color(0.05f, 0.04f, 0.03f, 1.0f);

        /// <summary>
        /// 发尖颜色
        /// </summary>
        [Tooltip("发尖的颜色（通常较浅）")]
        public Color TipColor = new Color(0.15f, 0.12f, 0.08f, 1.0f);

        /// <summary>
        /// 主色调
        /// </summary>
        [Tooltip("毛发的主色调（叠加）")]
        public Color BaseColor = new Color(0.1f, 0.08f, 0.05f, 1.0f);

        /// <summary>
        /// 染色颜色
        /// </summary>
        [Tooltip("额外的染发颜色")]
        public Color DyeColor = Color.White;

        /// <summary>
        /// 染色强度
        /// </summary>
        [Range(0f, 1f)]
        [Tooltip("染发颜色的强度")]
        public float DyeIntensity = 0f;

        /// <summary>
        /// 黑色素含量
        /// </summary>
        [Range(0f, 1f)]
        [Tooltip("毛发的黑色素含量（影响自然发色）")]
        public float Melanin = 0.5f;

        /// <summary>
        /// 红色素含量
        /// </summary>
        [Range(0f, 1f)]
        [Tooltip("毛发的红色素含量")]
        public float Pheomelanin = 0.3f;

        #endregion

        #region 各向异性高光参数

        /// <summary>
        /// 启用各向异性
        /// </summary>
        [Header("各向异性高光")]
        [Tooltip("是否启用各向异性高光")]
        public bool EnableAnisotropy = true;

        /// <summary>
        /// 各向异性强度
        /// </summary>
        [Range(0f, 1f)]
        [Tooltip("各向异性效果的强度")]
        public float AnisotropyIntensity = 0.8f;

        /// <summary>
        /// 主高光偏移
        /// </summary>
        [Range(-1f, 1f)]
        [Tooltip("主高光在切线方向的偏移")]
        public float PrimarySpecularShift = -0.1f;

        /// <summary>
        /// 主高光颜色
        /// </summary>
        [Tooltip("主高光的颜色")]
        public Color PrimarySpecularColor = new Color(1.0f, 1.0f, 1.0f, 1.0f);

        /// <summary>
        /// 主高光强度
        /// </summary>
        [Range(0f, 2f)]
        [Tooltip("主高光的强度")]
        public float PrimarySpecularIntensity = 0.5f;

        /// <summary>
        /// 主高光宽度
        /// </summary>
        [Range(0f, 1f)]
        [Tooltip("主高光的宽度")]
        public float PrimarySpecularWidth = 0.3f;

        /// <summary>
        /// 次高光偏移
        /// </summary>
        [Range(-1f, 1f)]
        [Tooltip("次高光在切线方向的偏移")]
        public float SecondarySpecularShift = 0.1f;

        /// <summary>
        /// 次高光颜色
        /// </summary>
        [Tooltip("次高光的颜色（通常带有发色）")]
        public Color SecondarySpecularColor = new Color(0.8f, 0.6f, 0.4f, 1.0f);

        /// <summary>
        /// 次高光强度
        /// </summary>
        [Range(0f, 2f)]
        [Tooltip("次高光的强度")]
        public float SecondarySpecularIntensity = 0.3f;

        /// <summary>
        /// 次高光宽度
        /// </summary>
        [Range(0f, 1f)]
        [Tooltip("次高光的宽度")]
        public float SecondarySpecularWidth = 0.5f;

        #endregion

        #region 散射参数

        /// <summary>
        /// 启用多重散射
        /// </summary>
        [Header("散射效果")]
        [Tooltip("是否启用多重散射")]
        public bool EnableMultipleScattering = true;

        /// <summary>
        /// 散射强度
        /// </summary>
        [Range(0f, 1f)]
        [Tooltip("光线在毛发间散射的强度")]
        public float ScatterIntensity = 0.5f;

        /// <summary>
        /// 散射颜色
        /// </summary>
        [Tooltip("散射光的颜色偏移")]
        public Color ScatterColor = new Color(1.0f, 0.9f, 0.8f, 1.0f);

        /// <summary>
        /// 透射强度
        /// </summary>
        [Range(0f, 1f)]
        [Tooltip("背光透射的强度")]
        public float TransmissionIntensity = 0.3f;

        /// <summary>
        /// 透射颜色
        /// </summary>
        [Tooltip("背光透射的颜色")]
        public Color TransmissionColor = new Color(1.0f, 0.6f, 0.3f, 1.0f);

        /// <summary>
        /// 环境光散射
        /// </summary>
        [Range(0f, 1f)]
        [Tooltip("环境光的散射程度")]
        public float AmbientScatter = 0.5f;

        #endregion

        #region 表面参数

        /// <summary>
        /// 粗糙度
        /// </summary>
        [Header("表面属性")]
        [Range(0f, 1f)]
        [Tooltip("毛发表面的粗糙度")]
        public float Roughness = 0.4f;

        /// <summary>
        /// 切向粗糙度
        /// </summary>
        [Range(0f, 1f)]
        [Tooltip("沿毛发方向的粗糙度")]
        public float TangentRoughness = 0.3f;

        /// <summary>
        /// 表皮层厚度
        /// </summary>
        [Range(0f, 0.1f)]
        [Tooltip("毛发表皮层的厚度")]
        public float CuticleThickness = 0.01f;

        /// <summary>
        /// 表皮层倾斜角
        /// </summary>
        [Range(-10f, 10f)]
        [Tooltip("毛发鳞片的倾斜角度")]
        public float CuticleTiltAngle = 3f;

        /// <summary>
        /// 法线强度
        /// </summary>
        [Range(0f, 2f)]
        [Tooltip("法线贴图的强度")]
        public float NormalStrength = 1.0f;

        #endregion

        #region 透明度参数

        /// <summary>
        /// Alpha裁切阈值
        /// </summary>
        [Header("透明度")]
        [Range(0f, 1f)]
        [Tooltip("Alpha测试的裁切阈值")]
        public float AlphaCutoff = 0.5f;

        /// <summary>
        /// Alpha边缘柔化
        /// </summary>
        [Range(0f, 1f)]
        [Tooltip("Alpha边缘的柔化程度")]
        public float AlphaSoftness = 0.1f;

        /// <summary>
        /// 双面渲染
        /// </summary>
        [Tooltip("是否双面渲染")]
        public bool DoubleSided = true;

        /// <summary>
        /// 深度预通道
        /// </summary>
        [Tooltip("是否使用深度预通道（改善透明排序）")]
        public bool UseDepthPrepass = true;

        #endregion

        #region 细节参数

        /// <summary>
        /// 流向偏移强度
        /// </summary>
        [Header("细节设置")]
        [Range(0f, 1f)]
        [Tooltip("流向贴图对高光的影响强度")]
        public float FlowMapStrength = 0.5f;

        /// <summary>
        /// 发根暗化
        /// </summary>
        [Range(0f, 1f)]
        [Tooltip("发根的暗化程度")]
        public float RootDarkening = 0.3f;

        /// <summary>
        /// AO强度
        /// </summary>
        [Range(0f, 1f)]
        [Tooltip("环境光遮蔽的强度")]
        public float AOIntensity = 0.5f;

        /// <summary>
        /// 自阴影强度
        /// </summary>
        [Range(0f, 1f)]
        [Tooltip("毛发自阴影的强度")]
        public float SelfShadowIntensity = 0.3f;

        #endregion

        public override void OnStart()
        {
            ApplyAllParameters();
        }

        public override void OnUpdate()
        {
            if (Engine.IsEditor)
            {
                ApplyAllParameters();
            }
        }

        /// <summary>
        /// 应用所有参数到材质
        /// </summary>
        public void ApplyAllParameters()
        {
            if (HairMaterialInstance == null) return;

            ApplyTextures();
            ApplyColorParameters();
            ApplyAnisotropyParameters();
            ApplyScatteringParameters();
            ApplySurfaceParameters();
            ApplyAlphaParameters();
            ApplyDetailParameters();
        }

        private void ApplyTextures()
        {
            if (DiffuseMap != null)
                HairMaterialInstance.SetParameterValue("DiffuseMap", DiffuseMap);
            if (NormalMap != null)
                HairMaterialInstance.SetParameterValue("NormalMap", NormalMap);
            if (FlowMap != null)
                HairMaterialInstance.SetParameterValue("FlowMap", FlowMap);
            if (AlphaMap != null)
                HairMaterialInstance.SetParameterValue("AlphaMap", AlphaMap);
            if (AOMap != null)
                HairMaterialInstance.SetParameterValue("AOMap", AOMap);
            if (RootOcclusionMap != null)
                HairMaterialInstance.SetParameterValue("RootOcclusionMap", RootOcclusionMap);
            if (HeightGradientMap != null)
                HairMaterialInstance.SetParameterValue("HeightGradientMap", HeightGradientMap);
        }

        private void ApplyColorParameters()
        {
            HairMaterialInstance.SetParameterValue("RootColor", RootColor);
            HairMaterialInstance.SetParameterValue("TipColor", TipColor);
            HairMaterialInstance.SetParameterValue("BaseColor", BaseColor);
            HairMaterialInstance.SetParameterValue("DyeColor", DyeColor);
            HairMaterialInstance.SetParameterValue("DyeIntensity", DyeIntensity);
            HairMaterialInstance.SetParameterValue("Melanin", Melanin);
            HairMaterialInstance.SetParameterValue("Pheomelanin", Pheomelanin);
        }

        private void ApplyAnisotropyParameters()
        {
            HairMaterialInstance.SetParameterValue("EnableAnisotropy", EnableAnisotropy ? 1f : 0f);
            HairMaterialInstance.SetParameterValue("AnisotropyIntensity", AnisotropyIntensity);
            HairMaterialInstance.SetParameterValue("PrimarySpecularShift", PrimarySpecularShift);
            HairMaterialInstance.SetParameterValue("PrimarySpecularColor", PrimarySpecularColor);
            HairMaterialInstance.SetParameterValue("PrimarySpecularIntensity", PrimarySpecularIntensity);
            HairMaterialInstance.SetParameterValue("PrimarySpecularWidth", PrimarySpecularWidth);
            HairMaterialInstance.SetParameterValue("SecondarySpecularShift", SecondarySpecularShift);
            HairMaterialInstance.SetParameterValue("SecondarySpecularColor", SecondarySpecularColor);
            HairMaterialInstance.SetParameterValue("SecondarySpecularIntensity", SecondarySpecularIntensity);
            HairMaterialInstance.SetParameterValue("SecondarySpecularWidth", SecondarySpecularWidth);
        }

        private void ApplyScatteringParameters()
        {
            HairMaterialInstance.SetParameterValue("EnableMultipleScattering", EnableMultipleScattering ? 1f : 0f);
            HairMaterialInstance.SetParameterValue("ScatterIntensity", ScatterIntensity);
            HairMaterialInstance.SetParameterValue("ScatterColor", ScatterColor);
            HairMaterialInstance.SetParameterValue("TransmissionIntensity", TransmissionIntensity);
            HairMaterialInstance.SetParameterValue("TransmissionColor", TransmissionColor);
            HairMaterialInstance.SetParameterValue("AmbientScatter", AmbientScatter);
        }

        private void ApplySurfaceParameters()
        {
            HairMaterialInstance.SetParameterValue("Roughness", Roughness);
            HairMaterialInstance.SetParameterValue("TangentRoughness", TangentRoughness);
            HairMaterialInstance.SetParameterValue("CuticleThickness", CuticleThickness);
            HairMaterialInstance.SetParameterValue("CuticleTiltAngle", CuticleTiltAngle);
            HairMaterialInstance.SetParameterValue("NormalStrength", NormalStrength);
        }

        private void ApplyAlphaParameters()
        {
            HairMaterialInstance.SetParameterValue("AlphaCutoff", AlphaCutoff);
            HairMaterialInstance.SetParameterValue("AlphaSoftness", AlphaSoftness);
            HairMaterialInstance.SetParameterValue("DoubleSided", DoubleSided ? 1f : 0f);
        }

        private void ApplyDetailParameters()
        {
            HairMaterialInstance.SetParameterValue("FlowMapStrength", FlowMapStrength);
            HairMaterialInstance.SetParameterValue("RootDarkening", RootDarkening);
            HairMaterialInstance.SetParameterValue("AOIntensity", AOIntensity);
            HairMaterialInstance.SetParameterValue("SelfShadowIntensity", SelfShadowIntensity);
        }

        #region 发色预设

        /// <summary>
        /// 设置黑发预设
        /// </summary>
        public void SetBlackHairPreset()
        {
            RootColor = new Color(0.02f, 0.02f, 0.02f, 1.0f);
            TipColor = new Color(0.05f, 0.05f, 0.05f, 1.0f);
            BaseColor = new Color(0.03f, 0.03f, 0.03f, 1.0f);
            Melanin = 0.9f;
            Pheomelanin = 0.1f;
            SecondarySpecularColor = new Color(0.6f, 0.5f, 0.4f, 1.0f);
            ApplyAllParameters();
        }

        /// <summary>
        /// 设置棕发预设
        /// </summary>
        public void SetBrownHairPreset()
        {
            RootColor = new Color(0.08f, 0.05f, 0.03f, 1.0f);
            TipColor = new Color(0.15f, 0.1f, 0.06f, 1.0f);
            BaseColor = new Color(0.1f, 0.07f, 0.04f, 1.0f);
            Melanin = 0.6f;
            Pheomelanin = 0.4f;
            SecondarySpecularColor = new Color(0.9f, 0.7f, 0.5f, 1.0f);
            ApplyAllParameters();
        }

        /// <summary>
        /// 设置金发预设
        /// </summary>
        public void SetBlondeHairPreset()
        {
            RootColor = new Color(0.3f, 0.25f, 0.15f, 1.0f);
            TipColor = new Color(0.5f, 0.45f, 0.3f, 1.0f);
            BaseColor = new Color(0.4f, 0.35f, 0.2f, 1.0f);
            Melanin = 0.2f;
            Pheomelanin = 0.6f;
            SecondarySpecularColor = new Color(1.0f, 0.95f, 0.8f, 1.0f);
            ApplyAllParameters();
        }

        /// <summary>
        /// 设置红发预设
        /// </summary>
        public void SetRedHairPreset()
        {
            RootColor = new Color(0.15f, 0.05f, 0.02f, 1.0f);
            TipColor = new Color(0.4f, 0.15f, 0.05f, 1.0f);
            BaseColor = new Color(0.25f, 0.1f, 0.04f, 1.0f);
            Melanin = 0.3f;
            Pheomelanin = 0.9f;
            SecondarySpecularColor = new Color(1.0f, 0.6f, 0.3f, 1.0f);
            ApplyAllParameters();
        }

        /// <summary>
        /// 设置白发/银发预设
        /// </summary>
        public void SetWhiteHairPreset()
        {
            RootColor = new Color(0.7f, 0.7f, 0.7f, 1.0f);
            TipColor = new Color(0.9f, 0.9f, 0.9f, 1.0f);
            BaseColor = new Color(0.85f, 0.85f, 0.85f, 1.0f);
            Melanin = 0.0f;
            Pheomelanin = 0.0f;
            SecondarySpecularColor = new Color(1.0f, 1.0f, 1.0f, 1.0f);
            ApplyAllParameters();
        }

        #endregion

        #region 发质预设

        /// <summary>
        /// 设置光滑发质预设
        /// </summary>
        public void SetSmoothHairPreset()
        {
            Roughness = 0.25f;
            TangentRoughness = 0.2f;
            PrimarySpecularIntensity = 0.7f;
            SecondarySpecularIntensity = 0.4f;
            ScatterIntensity = 0.3f;
            ApplyAllParameters();
        }

        /// <summary>
        /// 设置粗糙发质预设
        /// </summary>
        public void SetCoarseHairPreset()
        {
            Roughness = 0.6f;
            TangentRoughness = 0.5f;
            PrimarySpecularIntensity = 0.3f;
            SecondarySpecularIntensity = 0.2f;
            ScatterIntensity = 0.6f;
            ApplyAllParameters();
        }

        /// <summary>
        /// 设置湿发预设
        /// </summary>
        public void SetWetHairPreset()
        {
            Roughness = 0.15f;
            TangentRoughness = 0.1f;
            PrimarySpecularIntensity = 0.9f;
            SecondarySpecularIntensity = 0.6f;
            RootDarkening = 0.6f;
            ScatterIntensity = 0.2f;
            ApplyAllParameters();
        }

        #endregion
    }
}
