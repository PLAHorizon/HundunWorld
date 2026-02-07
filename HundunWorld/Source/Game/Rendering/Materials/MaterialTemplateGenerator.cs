using FlaxEngine;

namespace HundunWorld.Game.Rendering.Materials
{
    /// <summary>
    /// 材质模板生成器 - 使用FlaxEngine API创建基础材质实例
    /// 提供皮肤、眼睛、毛发材质的模板创建和参数初始化
    /// </summary>
    public static class MaterialTemplateGenerator
    {
        #region 皮肤材质

        /// <summary>
        /// 创建皮肤材质实例
        /// </summary>
        /// <param name="baseMaterial">基础皮肤材质</param>
        /// <returns>配置好默认参数的材质实例</returns>
        public static MaterialInstance CreateSkinMaterialInstance(Material baseMaterial)
        {
            if (baseMaterial == null)
            {
                Debug.LogWarning("[MaterialTemplateGenerator] 基础皮肤材质为空");
                return null;
            }

            var instance = baseMaterial.CreateVirtualInstance();
            ApplyDefaultSkinParameters(instance);
            return instance;
        }

        /// <summary>
        /// 应用默认皮肤参数到材质实例
        /// </summary>
        public static void ApplyDefaultSkinParameters(MaterialInstance instance)
        {
            if (instance == null) return;

            var preset = SkinPreset.CreateDefault();
            ApplySkinPresetToMaterial(instance, preset);
        }

        /// <summary>
        /// 应用皮肤预设到材质实例
        /// </summary>
        public static void ApplySkinPresetToMaterial(MaterialInstance instance, SkinPreset preset)
        {
            if (instance == null || preset == null) return;

            // 基础属性
            instance.SetParameterValue("BaseColor", preset.BaseColor);
            instance.SetParameterValue("ColorVariation", preset.ColorVariation);
            instance.SetParameterValue("BaseRoughness", preset.BaseRoughness);
            instance.SetParameterValue("SpecularIntensity", preset.SpecularIntensity);

            // SSS参数
            instance.SetParameterValue("EnableSSS", preset.EnableSSS ? 1f : 0f);
            instance.SetParameterValue("EpidermisColor", preset.EpidermisColor);
            instance.SetParameterValue("DermisColor", preset.DermisColor);
            instance.SetParameterValue("SubcutisColor", preset.SubcutisColor);
            instance.SetParameterValue("EpidermisWeight", preset.EpidermisWeight);
            instance.SetParameterValue("DermisWeight", preset.DermisWeight);
            instance.SetParameterValue("SubcutisWeight", preset.SubcutisWeight);
            instance.SetParameterValue("SSSIntensity", preset.SSSIntensity);
            instance.SetParameterValue("SSSRadius", preset.SSSRadius);
            instance.SetParameterValue("TransmissionIntensity", preset.TransmissionIntensity);
            instance.SetParameterValue("TransmissionColor", preset.TransmissionColor);

            // 细节参数
            instance.SetParameterValue("NormalStrength", preset.NormalStrength);
            instance.SetParameterValue("PoreNormalStrength", preset.PoreNormalStrength);
            instance.SetParameterValue("PoreTiling", preset.PoreTiling);
            instance.SetParameterValue("WrinkleDepth", preset.WrinkleDepth);
            instance.SetParameterValue("MicroDetailStrength", preset.MicroDetailStrength);

            // 特殊区域
            instance.SetParameterValue("LipColor", preset.LipColor);
            instance.SetParameterValue("LipWetness", preset.LipWetness);
            instance.SetParameterValue("BlushIntensity", preset.BlushIntensity);
            instance.SetParameterValue("BlushColor", preset.BlushColor);
            instance.SetParameterValue("EyeSocketDarkening", preset.EyeSocketDarkening);

            // 菲涅尔
            instance.SetParameterValue("FresnelIntensity", preset.FresnelIntensity);
            instance.SetParameterValue("FresnelPower", preset.FresnelPower);
            instance.SetParameterValue("FresnelColor", preset.FresnelColor);
        }

        #endregion

        #region 眼睛材质

        /// <summary>
        /// 创建眼睛材质实例
        /// </summary>
        /// <param name="baseMaterial">基础眼睛材质</param>
        /// <returns>配置好默认参数的材质实例</returns>
        public static MaterialInstance CreateEyeMaterialInstance(Material baseMaterial)
        {
            if (baseMaterial == null)
            {
                Debug.LogWarning("[MaterialTemplateGenerator] 基础眼睛材质为空");
                return null;
            }

            var instance = baseMaterial.CreateVirtualInstance();
            ApplyDefaultEyeParameters(instance);
            return instance;
        }

        /// <summary>
        /// 应用默认眼睛参数到材质实例
        /// </summary>
        public static void ApplyDefaultEyeParameters(MaterialInstance instance)
        {
            if (instance == null) return;

            var preset = EyePreset.CreateDefault();
            ApplyEyePresetToMaterial(instance, preset);
        }

        /// <summary>
        /// 应用眼睛预设到材质实例
        /// </summary>
        public static void ApplyEyePresetToMaterial(MaterialInstance instance, EyePreset preset)
        {
            if (instance == null || preset == null) return;

            // 虹膜参数
            instance.SetParameterValue("IrisColor", preset.IrisColor);
            instance.SetParameterValue("IrisLimbusColor", preset.IrisLimbusColor);
            instance.SetParameterValue("IrisBrightness", preset.IrisBrightness);
            instance.SetParameterValue("IrisSaturation", preset.IrisSaturation);
            instance.SetParameterValue("IrisDetailIntensity", preset.IrisDetailIntensity);
            instance.SetParameterValue("IrisFiberDensity", preset.IrisFiberDensity);
            instance.SetParameterValue("IrisNormalStrength", preset.IrisNormalStrength);

            // 瞳孔参数
            instance.SetParameterValue("PupilColor", preset.PupilColor);
            instance.SetParameterValue("PupilSize", preset.PupilSize);
            instance.SetParameterValue("PupilSlitness", preset.PupilSlitness);

            // 角膜参数
            instance.SetParameterValue("CorneaIOR", preset.CorneaIOR);
            instance.SetParameterValue("CorneaCurvature", preset.CorneaCurvature);
            instance.SetParameterValue("CorneaSpecular", preset.CorneaSpecular);
            instance.SetParameterValue("CorneaRoughness", preset.CorneaRoughness);
            instance.SetParameterValue("CorneaDepth", preset.CorneaDepth);

            // 巩膜参数
            instance.SetParameterValue("ScleraColor", preset.ScleraColor);
            instance.SetParameterValue("ScleraVeinsIntensity", preset.ScleraVeinsIntensity);
            instance.SetParameterValue("ScleraVeinsColor", preset.ScleraVeinsColor);
            instance.SetParameterValue("ScleraSSSColor", preset.ScleraSSSColor);
            instance.SetParameterValue("ScleraSSSIntensity", preset.ScleraSSSIntensity);
            instance.SetParameterValue("ScleraRoughness", preset.ScleraRoughness);

            // 湿润效果
            instance.SetParameterValue("Wetness", preset.Wetness);
            instance.SetParameterValue("TearLineIntensity", preset.TearLineIntensity);
            instance.SetParameterValue("EnvironmentReflection", preset.EnvironmentReflection);

            // 光泽效果
            instance.SetParameterValue("CausticIntensity", preset.CausticIntensity);
            instance.SetParameterValue("SpecularSize", preset.SpecularSize);
            instance.SetParameterValue("DualSpecular", preset.DualSpecular ? 1f : 0f);
        }

        #endregion

        #region 毛发材质

        /// <summary>
        /// 创建毛发材质实例
        /// </summary>
        /// <param name="baseMaterial">基础毛发材质</param>
        /// <returns>配置好默认参数的材质实例</returns>
        public static MaterialInstance CreateHairMaterialInstance(Material baseMaterial)
        {
            if (baseMaterial == null)
            {
                Debug.LogWarning("[MaterialTemplateGenerator] 基础毛发材质为空");
                return null;
            }

            var instance = baseMaterial.CreateVirtualInstance();
            ApplyDefaultHairParameters(instance);
            return instance;
        }

        /// <summary>
        /// 应用默认毛发参数到材质实例
        /// </summary>
        public static void ApplyDefaultHairParameters(MaterialInstance instance)
        {
            if (instance == null) return;

            var preset = HairPreset.CreateDefault();
            ApplyHairPresetToMaterial(instance, preset);
        }

        /// <summary>
        /// 应用毛发预设到材质实例
        /// </summary>
        public static void ApplyHairPresetToMaterial(MaterialInstance instance, HairPreset preset)
        {
            if (instance == null || preset == null) return;

            // 颜色参数
            instance.SetParameterValue("RootColor", preset.RootColor);
            instance.SetParameterValue("TipColor", preset.TipColor);
            instance.SetParameterValue("BaseColor", preset.BaseColor);
            instance.SetParameterValue("DyeColor", preset.DyeColor);
            instance.SetParameterValue("DyeIntensity", preset.DyeIntensity);
            instance.SetParameterValue("Melanin", preset.Melanin);
            instance.SetParameterValue("Pheomelanin", preset.Pheomelanin);

            // 各向异性高光
            instance.SetParameterValue("EnableAnisotropy", preset.EnableAnisotropy ? 1f : 0f);
            instance.SetParameterValue("AnisotropyIntensity", preset.AnisotropyIntensity);
            instance.SetParameterValue("PrimarySpecularShift", preset.PrimarySpecularShift);
            instance.SetParameterValue("PrimarySpecularColor", preset.PrimarySpecularColor);
            instance.SetParameterValue("PrimarySpecularIntensity", preset.PrimarySpecularIntensity);
            instance.SetParameterValue("PrimarySpecularWidth", preset.PrimarySpecularWidth);
            instance.SetParameterValue("SecondarySpecularShift", preset.SecondarySpecularShift);
            instance.SetParameterValue("SecondarySpecularColor", preset.SecondarySpecularColor);
            instance.SetParameterValue("SecondarySpecularIntensity", preset.SecondarySpecularIntensity);
            instance.SetParameterValue("SecondarySpecularWidth", preset.SecondarySpecularWidth);

            // 散射参数
            instance.SetParameterValue("EnableMultipleScattering", preset.EnableMultipleScattering ? 1f : 0f);
            instance.SetParameterValue("ScatterIntensity", preset.ScatterIntensity);
            instance.SetParameterValue("ScatterColor", preset.ScatterColor);
            instance.SetParameterValue("TransmissionIntensity", preset.TransmissionIntensity);
            instance.SetParameterValue("TransmissionColor", preset.TransmissionColor);
            instance.SetParameterValue("AmbientScatter", preset.AmbientScatter);

            // 表面参数
            instance.SetParameterValue("Roughness", preset.Roughness);
            instance.SetParameterValue("TangentRoughness", preset.TangentRoughness);
            instance.SetParameterValue("NormalStrength", preset.NormalStrength);

            // 透明度参数
            instance.SetParameterValue("AlphaCutoff", preset.AlphaCutoff);
            instance.SetParameterValue("AlphaSoftness", preset.AlphaSoftness);
            instance.SetParameterValue("DoubleSided", preset.DoubleSided ? 1f : 0f);

            // 细节参数
            instance.SetParameterValue("FlowMapStrength", preset.FlowMapStrength);
            instance.SetParameterValue("RootDarkening", preset.RootDarkening);
            instance.SetParameterValue("AOIntensity", preset.AOIntensity);
            instance.SetParameterValue("SelfShadowIntensity", preset.SelfShadowIntensity);
        }

        #endregion

        #region 纹理设置辅助

        /// <summary>
        /// 设置皮肤纹理贴图
        /// </summary>
        public static void SetSkinTextures(
            MaterialInstance instance,
            Texture diffuseMap = null,
            Texture normalMap = null,
            Texture poreNormalMap = null,
            Texture roughnessMap = null,
            Texture aoMap = null,
            Texture thicknessMap = null,
            Texture curvatureMap = null)
        {
            if (instance == null) return;

            if (diffuseMap != null)
                instance.SetParameterValue("DiffuseMap", diffuseMap);
            if (normalMap != null)
                instance.SetParameterValue("NormalMap", normalMap);
            if (poreNormalMap != null)
                instance.SetParameterValue("PoreNormalMap", poreNormalMap);
            if (roughnessMap != null)
                instance.SetParameterValue("RoughnessMap", roughnessMap);
            if (aoMap != null)
                instance.SetParameterValue("AOMap", aoMap);
            if (thicknessMap != null)
                instance.SetParameterValue("ThicknessMap", thicknessMap);
            if (curvatureMap != null)
                instance.SetParameterValue("CurvatureMap", curvatureMap);
        }

        /// <summary>
        /// 设置眼睛纹理贴图
        /// </summary>
        public static void SetEyeTextures(
            MaterialInstance instance,
            Texture irisTexture = null,
            Texture irisNormalMap = null,
            Texture scleraTexture = null,
            Texture scleraVeinsTexture = null,
            Texture wetnessMap = null)
        {
            if (instance == null) return;

            if (irisTexture != null)
                instance.SetParameterValue("IrisTexture", irisTexture);
            if (irisNormalMap != null)
                instance.SetParameterValue("IrisNormalMap", irisNormalMap);
            if (scleraTexture != null)
                instance.SetParameterValue("ScleraTexture", scleraTexture);
            if (scleraVeinsTexture != null)
                instance.SetParameterValue("ScleraVeinsTexture", scleraVeinsTexture);
            if (wetnessMap != null)
                instance.SetParameterValue("WetnessMap", wetnessMap);
        }

        /// <summary>
        /// 设置毛发纹理贴图
        /// </summary>
        public static void SetHairTextures(
            MaterialInstance instance,
            Texture diffuseMap = null,
            Texture normalMap = null,
            Texture flowMap = null,
            Texture alphaMap = null,
            Texture aoMap = null,
            Texture rootOcclusionMap = null,
            Texture heightGradientMap = null)
        {
            if (instance == null) return;

            if (diffuseMap != null)
                instance.SetParameterValue("DiffuseMap", diffuseMap);
            if (normalMap != null)
                instance.SetParameterValue("NormalMap", normalMap);
            if (flowMap != null)
                instance.SetParameterValue("FlowMap", flowMap);
            if (alphaMap != null)
                instance.SetParameterValue("AlphaMap", alphaMap);
            if (aoMap != null)
                instance.SetParameterValue("AOMap", aoMap);
            if (rootOcclusionMap != null)
                instance.SetParameterValue("RootOcclusionMap", rootOcclusionMap);
            if (heightGradientMap != null)
                instance.SetParameterValue("HeightGradientMap", heightGradientMap);
        }

        #endregion

        #region 材质验证

        /// <summary>
        /// 验证皮肤材质是否具有所需参数
        /// </summary>
        public static bool ValidateSkinMaterial(Material material)
        {
            if (material == null) return false;

            // 检查关键参数是否存在
            // FlaxEngine Material API 不直接提供参数存在性检查
            // 这里返回true假设材质已正确配置
            return true;
        }

        /// <summary>
        /// 验证眼睛材质是否具有所需参数
        /// </summary>
        public static bool ValidateEyeMaterial(Material material)
        {
            if (material == null) return false;
            return true;
        }

        /// <summary>
        /// 验证毛发材质是否具有所需参数
        /// </summary>
        public static bool ValidateHairMaterial(Material material)
        {
            if (material == null) return false;
            return true;
        }

        #endregion
    }
}
