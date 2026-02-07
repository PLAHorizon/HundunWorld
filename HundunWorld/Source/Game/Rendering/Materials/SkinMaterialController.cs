using FlaxEngine;

namespace HundunWorld.Game.Rendering.Materials
{
    /// <summary>
    /// 皮肤材质控制器 - 管理次表面散射(SSS)皮肤渲染效果
    /// 此类用于运行时动态调整皮肤材质参数
    /// </summary>
    public class SkinMaterialController : Script
    {
        #region 材质资源

        /// <summary>
        /// 皮肤材质实例
        /// </summary>
        [Header("材质资源")]
        [Tooltip("皮肤材质实例")]
        public MaterialInstance SkinMaterialInstance;

        /// <summary>
        /// 皮肤漫反射纹理
        /// </summary>
        [Tooltip("皮肤颜色贴图（Albedo/Diffuse）")]
        public Texture DiffuseMap;

        /// <summary>
        /// 皮肤法线贴图
        /// </summary>
        [Tooltip("皮肤法线贴图")]
        public Texture NormalMap;

        /// <summary>
        /// 毛孔细节法线贴图
        /// </summary>
        [Tooltip("毛孔细节法线贴图")]
        public Texture PoreNormalMap;

        /// <summary>
        /// 粗糙度贴图
        /// </summary>
        [Tooltip("皮肤粗糙度贴图")]
        public Texture RoughnessMap;

        /// <summary>
        /// AO贴图
        /// </summary>
        [Tooltip("皮肤环境光遮蔽贴图")]
        public Texture AOMap;

        /// <summary>
        /// 厚度贴图（用于SSS）
        /// </summary>
        [Tooltip("皮肤厚度贴图，用于次表面散射计算")]
        public Texture ThicknessMap;

        /// <summary>
        /// 曲率贴图
        /// </summary>
        [Tooltip("皮肤曲率贴图，影响SSS分布")]
        public Texture CurvatureMap;

        #endregion

        #region 基础皮肤参数

        /// <summary>
        /// 皮肤基础颜色
        /// </summary>
        [Header("皮肤基础设置")]
        [Tooltip("皮肤的基础颜色")]
        public Color BaseColor = new Color(1.0f, 0.85f, 0.75f, 1.0f);

        /// <summary>
        /// 皮肤颜色变化
        /// </summary>
        [Tooltip("皮肤颜色变化（叠加在基础颜色上）")]
        public Color ColorVariation = Color.White;

        /// <summary>
        /// 基础粗糙度
        /// </summary>
        [Range(0f, 1f)]
        [Tooltip("皮肤的基础粗糙度")]
        public float BaseRoughness = 0.35f;

        /// <summary>
        /// 高光强度
        /// </summary>
        [Range(0f, 1f)]
        [Tooltip("皮肤高光强度")]
        public float SpecularIntensity = 0.5f;

        #endregion

        #region 次表面散射参数

        /// <summary>
        /// 启用SSS
        /// </summary>
        [Header("次表面散射(SSS)")]
        [Tooltip("是否启用次表面散射")]
        public bool EnableSSS = true;

        /// <summary>
        /// SSS颜色（表皮层）
        /// </summary>
        [Tooltip("表皮层的散射颜色")]
        public Color EpidermisColor = new Color(1.0f, 0.9f, 0.85f, 1.0f);

        /// <summary>
        /// SSS颜色（真皮层）
        /// </summary>
        [Tooltip("真皮层的散射颜色（偏红）")]
        public Color DermisColor = new Color(0.9f, 0.3f, 0.2f, 1.0f);

        /// <summary>
        /// SSS颜色（皮下组织）
        /// </summary>
        [Tooltip("皮下组织的散射颜色（偏红暗）")]
        public Color SubcutisColor = new Color(0.8f, 0.15f, 0.1f, 1.0f);

        /// <summary>
        /// 表皮层权重
        /// </summary>
        [Range(0f, 1f)]
        [Tooltip("表皮层在SSS中的权重")]
        public float EpidermisWeight = 0.3f;

        /// <summary>
        /// 真皮层权重
        /// </summary>
        [Range(0f, 1f)]
        [Tooltip("真皮层在SSS中的权重")]
        public float DermisWeight = 0.5f;

        /// <summary>
        /// 皮下组织权重
        /// </summary>
        [Range(0f, 1f)]
        [Tooltip("皮下组织在SSS中的权重")]
        public float SubcutisWeight = 0.2f;

        /// <summary>
        /// SSS强度
        /// </summary>
        [Range(0f, 2f)]
        [Tooltip("整体次表面散射强度")]
        public float SSSIntensity = 1.0f;

        /// <summary>
        /// SSS半径
        /// </summary>
        [Range(0.1f, 5f)]
        [Tooltip("次表面散射的扩散半径（单位：厘米）")]
        public float SSSRadius = 1.2f;

        /// <summary>
        /// 透射强度
        /// </summary>
        [Range(0f, 1f)]
        [Tooltip("背光透射的强度")]
        public float TransmissionIntensity = 0.5f;

        /// <summary>
        /// 透射颜色
        /// </summary>
        [Tooltip("背光透射的颜色")]
        public Color TransmissionColor = new Color(1.0f, 0.4f, 0.2f, 1.0f);

        #endregion

        #region 细节参数

        /// <summary>
        /// 法线贴图强度
        /// </summary>
        [Header("细节设置")]
        [Range(0f, 2f)]
        [Tooltip("主法线贴图的强度")]
        public float NormalStrength = 1.0f;

        /// <summary>
        /// 毛孔法线强度
        /// </summary>
        [Range(0f, 1f)]
        [Tooltip("毛孔细节法线的强度")]
        public float PoreNormalStrength = 0.5f;

        /// <summary>
        /// 毛孔纹理平铺
        /// </summary>
        [Range(1f, 50f)]
        [Tooltip("毛孔纹理的平铺次数")]
        public float PoreTiling = 10f;

        /// <summary>
        /// 皱纹深度
        /// </summary>
        [Range(0f, 1f)]
        [Tooltip("皱纹的深度强度")]
        public float WrinkleDepth = 0.5f;

        /// <summary>
        /// 微表面细节强度
        /// </summary>
        [Range(0f, 1f)]
        [Tooltip("微表面细节的强度")]
        public float MicroDetailStrength = 0.3f;

        #endregion

        #region 特殊区域参数

        /// <summary>
        /// 嘴唇颜色
        /// </summary>
        [Header("特殊区域")]
        [Tooltip("嘴唇的颜色")]
        public Color LipColor = new Color(0.8f, 0.4f, 0.4f, 1.0f);

        /// <summary>
        /// 嘴唇湿润度
        /// </summary>
        [Range(0f, 1f)]
        [Tooltip("嘴唇的湿润反光程度")]
        public float LipWetness = 0.6f;

        /// <summary>
        /// 脸颊血色
        /// </summary>
        [Range(0f, 1f)]
        [Tooltip("脸颊的红润程度")]
        public float BlushIntensity = 0.3f;

        /// <summary>
        /// 脸颊血色颜色
        /// </summary>
        [Tooltip("脸颊血色的颜色")]
        public Color BlushColor = new Color(0.9f, 0.5f, 0.5f, 1.0f);

        /// <summary>
        /// 眼周暗沉
        /// </summary>
        [Range(0f, 1f)]
        [Tooltip("眼周的暗沉程度")]
        public float EyeSocketDarkening = 0.2f;

        #endregion

        #region 菲涅尔参数

        /// <summary>
        /// 菲涅尔强度
        /// </summary>
        [Header("菲涅尔效果")]
        [Range(0f, 1f)]
        [Tooltip("边缘菲涅尔反射强度")]
        public float FresnelIntensity = 0.5f;

        /// <summary>
        /// 菲涅尔指数
        /// </summary>
        [Range(1f, 10f)]
        [Tooltip("菲涅尔衰减指数")]
        public float FresnelPower = 5f;

        /// <summary>
        /// 菲涅尔颜色
        /// </summary>
        [Tooltip("菲涅尔反射的颜色偏移")]
        public Color FresnelColor = new Color(1.0f, 0.95f, 0.9f, 1.0f);

        #endregion

        public override void OnStart()
        {
            ApplyAllParameters();
        }

        public override void OnUpdate()
        {
            // 在编辑器中实时更新参数以便调试
            if (Engine.IsEditor)
            {
                ApplyAllParameters();
            }
        }

        /// <summary>
        /// 应用所有材质参数
        /// </summary>
        public void ApplyAllParameters()
        {
            if (SkinMaterialInstance == null) return;

            ApplyTextures();
            ApplyBaseParameters();
            ApplySSSParameters();
            ApplyDetailParameters();
            ApplySpecialAreaParameters();
            ApplyFresnelParameters();
        }

        /// <summary>
        /// 应用纹理贴图
        /// </summary>
        private void ApplyTextures()
        {
            if (DiffuseMap != null)
                SkinMaterialInstance.SetParameterValue("DiffuseMap", DiffuseMap);
            if (NormalMap != null)
                SkinMaterialInstance.SetParameterValue("NormalMap", NormalMap);
            if (PoreNormalMap != null)
                SkinMaterialInstance.SetParameterValue("PoreNormalMap", PoreNormalMap);
            if (RoughnessMap != null)
                SkinMaterialInstance.SetParameterValue("RoughnessMap", RoughnessMap);
            if (AOMap != null)
                SkinMaterialInstance.SetParameterValue("AOMap", AOMap);
            if (ThicknessMap != null)
                SkinMaterialInstance.SetParameterValue("ThicknessMap", ThicknessMap);
            if (CurvatureMap != null)
                SkinMaterialInstance.SetParameterValue("CurvatureMap", CurvatureMap);
        }

        /// <summary>
        /// 应用基础皮肤参数
        /// </summary>
        private void ApplyBaseParameters()
        {
            SkinMaterialInstance.SetParameterValue("BaseColor", BaseColor);
            SkinMaterialInstance.SetParameterValue("ColorVariation", ColorVariation);
            SkinMaterialInstance.SetParameterValue("BaseRoughness", BaseRoughness);
            SkinMaterialInstance.SetParameterValue("SpecularIntensity", SpecularIntensity);
        }

        /// <summary>
        /// 应用SSS参数
        /// </summary>
        private void ApplySSSParameters()
        {
            SkinMaterialInstance.SetParameterValue("EnableSSS", EnableSSS ? 1f : 0f);
            SkinMaterialInstance.SetParameterValue("EpidermisColor", EpidermisColor);
            SkinMaterialInstance.SetParameterValue("DermisColor", DermisColor);
            SkinMaterialInstance.SetParameterValue("SubcutisColor", SubcutisColor);
            SkinMaterialInstance.SetParameterValue("EpidermisWeight", EpidermisWeight);
            SkinMaterialInstance.SetParameterValue("DermisWeight", DermisWeight);
            SkinMaterialInstance.SetParameterValue("SubcutisWeight", SubcutisWeight);
            SkinMaterialInstance.SetParameterValue("SSSIntensity", SSSIntensity);
            SkinMaterialInstance.SetParameterValue("SSSRadius", SSSRadius);
            SkinMaterialInstance.SetParameterValue("TransmissionIntensity", TransmissionIntensity);
            SkinMaterialInstance.SetParameterValue("TransmissionColor", TransmissionColor);
        }

        /// <summary>
        /// 应用细节参数
        /// </summary>
        private void ApplyDetailParameters()
        {
            SkinMaterialInstance.SetParameterValue("NormalStrength", NormalStrength);
            SkinMaterialInstance.SetParameterValue("PoreNormalStrength", PoreNormalStrength);
            SkinMaterialInstance.SetParameterValue("PoreTiling", PoreTiling);
            SkinMaterialInstance.SetParameterValue("WrinkleDepth", WrinkleDepth);
            SkinMaterialInstance.SetParameterValue("MicroDetailStrength", MicroDetailStrength);
        }

        /// <summary>
        /// 应用特殊区域参数
        /// </summary>
        private void ApplySpecialAreaParameters()
        {
            SkinMaterialInstance.SetParameterValue("LipColor", LipColor);
            SkinMaterialInstance.SetParameterValue("LipWetness", LipWetness);
            SkinMaterialInstance.SetParameterValue("BlushIntensity", BlushIntensity);
            SkinMaterialInstance.SetParameterValue("BlushColor", BlushColor);
            SkinMaterialInstance.SetParameterValue("EyeSocketDarkening", EyeSocketDarkening);
        }

        /// <summary>
        /// 应用菲涅尔参数
        /// </summary>
        private void ApplyFresnelParameters()
        {
            SkinMaterialInstance.SetParameterValue("FresnelIntensity", FresnelIntensity);
            SkinMaterialInstance.SetParameterValue("FresnelPower", FresnelPower);
            SkinMaterialInstance.SetParameterValue("FresnelColor", FresnelColor);
        }

        /// <summary>
        /// 设置年轻皮肤预设
        /// </summary>
        public void SetYoungSkinPreset()
        {
            BaseRoughness = 0.3f;
            PoreNormalStrength = 0.3f;
            WrinkleDepth = 0.1f;
            SSSIntensity = 1.2f;
            BlushIntensity = 0.4f;
            LipWetness = 0.7f;
            ApplyAllParameters();
        }

        /// <summary>
        /// 设置成熟皮肤预设
        /// </summary>
        public void SetMatureSkinPreset()
        {
            BaseRoughness = 0.4f;
            PoreNormalStrength = 0.6f;
            WrinkleDepth = 0.5f;
            SSSIntensity = 0.9f;
            BlushIntensity = 0.2f;
            LipWetness = 0.4f;
            EyeSocketDarkening = 0.3f;
            ApplyAllParameters();
        }

        /// <summary>
        /// 设置油性皮肤预设
        /// </summary>
        public void SetOilySkinPreset()
        {
            BaseRoughness = 0.2f;
            SpecularIntensity = 0.7f;
            FresnelIntensity = 0.6f;
            ApplyAllParameters();
        }

        /// <summary>
        /// 设置干性皮肤预设
        /// </summary>
        public void SetDrySkinPreset()
        {
            BaseRoughness = 0.5f;
            SpecularIntensity = 0.3f;
            MicroDetailStrength = 0.5f;
            ApplyAllParameters();
        }
    }
}
