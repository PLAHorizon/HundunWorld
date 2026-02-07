using System;
using FlaxEngine;

namespace HundunWorld.Game.Rendering.Materials
{
    /// <summary>
    /// 角色外观完整预设 - 包含皮肤、眼睛、毛发的所有材质参数
    /// </summary>
    [Serializable]
    public class CharacterAppearancePreset
    {
        /// <summary>
        /// 预设名称
        /// </summary>
        public string PresetName { get; set; } = "Default";

        /// <summary>
        /// 预设描述
        /// </summary>
        public string Description { get; set; } = "";

        /// <summary>
        /// 创建日期
        /// </summary>
        public string CreatedDate { get; set; } = "";

        /// <summary>
        /// 皮肤预设
        /// </summary>
        public SkinPreset Skin { get; set; } = new SkinPreset();

        /// <summary>
        /// 眼睛预设
        /// </summary>
        public EyePreset Eye { get; set; } = new EyePreset();

        /// <summary>
        /// 毛发预设
        /// </summary>
        public HairPreset Hair { get; set; } = new HairPreset();

        /// <summary>
        /// 创建默认预设
        /// </summary>
        public static CharacterAppearancePreset CreateDefault()
        {
            return new CharacterAppearancePreset
            {
                PresetName = "Default",
                Description = "默认角色外观预设",
                CreatedDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                Skin = SkinPreset.CreateDefault(),
                Eye = EyePreset.CreateDefault(),
                Hair = HairPreset.CreateDefault()
            };
        }

        /// <summary>
        /// 创建亚洲人预设
        /// </summary>
        public static CharacterAppearancePreset CreateAsian()
        {
            return new CharacterAppearancePreset
            {
                PresetName = "Asian",
                Description = "亚洲人外观预设",
                CreatedDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                Skin = SkinPreset.CreateAsian(),
                Eye = EyePreset.CreateAsian(),
                Hair = HairPreset.CreateAsian()
            };
        }

        /// <summary>
        /// 创建欧洲人预设
        /// </summary>
        public static CharacterAppearancePreset CreateEuropean()
        {
            return new CharacterAppearancePreset
            {
                PresetName = "European",
                Description = "欧洲人外观预设",
                CreatedDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                Skin = SkinPreset.CreateEuropean(),
                Eye = EyePreset.CreateEuropean(),
                Hair = HairPreset.CreateEuropean()
            };
        }
    }

    /// <summary>
    /// 皮肤材质预设
    /// </summary>
    [Serializable]
    public class SkinPreset
    {
        #region 基础属性

        /// <summary>
        /// 基础肤色
        /// </summary>
        public Color BaseColor { get; set; } = new Color(1.0f, 0.85f, 0.75f, 1.0f);

        /// <summary>
        /// 颜色变化（叠加）
        /// </summary>
        public Color ColorVariation { get; set; } = Color.White;

        /// <summary>
        /// 基础粗糙度
        /// </summary>
        public float BaseRoughness { get; set; } = 0.35f;

        /// <summary>
        /// 高光强度
        /// </summary>
        public float SpecularIntensity { get; set; } = 0.5f;

        #endregion

        #region SSS三层参数

        /// <summary>
        /// 是否启用SSS
        /// </summary>
        public bool EnableSSS { get; set; } = true;

        /// <summary>
        /// 表皮层颜色
        /// </summary>
        public Color EpidermisColor { get; set; } = new Color(1.0f, 0.9f, 0.85f, 1.0f);

        /// <summary>
        /// 真皮层颜色
        /// </summary>
        public Color DermisColor { get; set; } = new Color(0.9f, 0.3f, 0.2f, 1.0f);

        /// <summary>
        /// 皮下组织颜色
        /// </summary>
        public Color SubcutisColor { get; set; } = new Color(0.8f, 0.15f, 0.1f, 1.0f);

        /// <summary>
        /// 表皮层权重
        /// </summary>
        public float EpidermisWeight { get; set; } = 0.3f;

        /// <summary>
        /// 真皮层权重
        /// </summary>
        public float DermisWeight { get; set; } = 0.5f;

        /// <summary>
        /// 皮下组织权重
        /// </summary>
        public float SubcutisWeight { get; set; } = 0.2f;

        /// <summary>
        /// SSS强度
        /// </summary>
        public float SSSIntensity { get; set; } = 1.0f;

        /// <summary>
        /// SSS半径（单位：厘米）
        /// </summary>
        public float SSSRadius { get; set; } = 1.2f;

        /// <summary>
        /// 透射强度
        /// </summary>
        public float TransmissionIntensity { get; set; } = 0.5f;

        /// <summary>
        /// 透射颜色
        /// </summary>
        public Color TransmissionColor { get; set; } = new Color(1.0f, 0.4f, 0.2f, 1.0f);

        #endregion

        #region 细节参数

        /// <summary>
        /// 法线贴图强度
        /// </summary>
        public float NormalStrength { get; set; } = 1.0f;

        /// <summary>
        /// 毛孔法线强度
        /// </summary>
        public float PoreNormalStrength { get; set; } = 0.5f;

        /// <summary>
        /// 毛孔纹理平铺
        /// </summary>
        public float PoreTiling { get; set; } = 10f;

        /// <summary>
        /// 皱纹深度
        /// </summary>
        public float WrinkleDepth { get; set; } = 0.5f;

        /// <summary>
        /// 微表面细节强度
        /// </summary>
        public float MicroDetailStrength { get; set; } = 0.3f;

        #endregion

        #region 特殊区域参数

        /// <summary>
        /// 嘴唇颜色
        /// </summary>
        public Color LipColor { get; set; } = new Color(0.8f, 0.4f, 0.4f, 1.0f);

        /// <summary>
        /// 嘴唇湿润度
        /// </summary>
        public float LipWetness { get; set; } = 0.6f;

        /// <summary>
        /// 脸颊血色强度
        /// </summary>
        public float BlushIntensity { get; set; } = 0.3f;

        /// <summary>
        /// 脸颊血色颜色
        /// </summary>
        public Color BlushColor { get; set; } = new Color(0.9f, 0.5f, 0.5f, 1.0f);

        /// <summary>
        /// 眼周暗沉
        /// </summary>
        public float EyeSocketDarkening { get; set; } = 0.2f;

        #endregion

        #region 菲涅尔参数

        /// <summary>
        /// 菲涅尔强度
        /// </summary>
        public float FresnelIntensity { get; set; } = 0.5f;

        /// <summary>
        /// 菲涅尔指数
        /// </summary>
        public float FresnelPower { get; set; } = 5f;

        /// <summary>
        /// 菲涅尔颜色
        /// </summary>
        public Color FresnelColor { get; set; } = new Color(1.0f, 0.95f, 0.9f, 1.0f);

        #endregion

        #region 工厂方法

        /// <summary>
        /// 创建默认皮肤预设
        /// </summary>
        public static SkinPreset CreateDefault()
        {
            return new SkinPreset();
        }

        /// <summary>
        /// 创建亚洲人皮肤预设
        /// </summary>
        public static SkinPreset CreateAsian()
        {
            return new SkinPreset
            {
                BaseColor = new Color(1.0f, 0.88f, 0.78f, 1.0f),
                BaseRoughness = 0.32f,
                SSSIntensity = 1.1f,
                EpidermisColor = new Color(1.0f, 0.92f, 0.88f, 1.0f),
                BlushIntensity = 0.25f,
                LipColor = new Color(0.85f, 0.45f, 0.45f, 1.0f)
            };
        }

        /// <summary>
        /// 创建欧洲人皮肤预设
        /// </summary>
        public static SkinPreset CreateEuropean()
        {
            return new SkinPreset
            {
                BaseColor = new Color(1.0f, 0.82f, 0.72f, 1.0f),
                BaseRoughness = 0.38f,
                SSSIntensity = 1.2f,
                EpidermisColor = new Color(1.0f, 0.88f, 0.82f, 1.0f),
                BlushIntensity = 0.4f,
                LipColor = new Color(0.75f, 0.35f, 0.35f, 1.0f)
            };
        }

        /// <summary>
        /// 创建年轻皮肤预设
        /// </summary>
        public static SkinPreset CreateYoung()
        {
            return new SkinPreset
            {
                BaseRoughness = 0.3f,
                PoreNormalStrength = 0.3f,
                WrinkleDepth = 0.1f,
                SSSIntensity = 1.2f,
                BlushIntensity = 0.4f,
                LipWetness = 0.7f
            };
        }

        /// <summary>
        /// 创建成熟皮肤预设
        /// </summary>
        public static SkinPreset CreateMature()
        {
            return new SkinPreset
            {
                BaseRoughness = 0.4f,
                PoreNormalStrength = 0.6f,
                WrinkleDepth = 0.5f,
                SSSIntensity = 0.9f,
                BlushIntensity = 0.2f,
                LipWetness = 0.4f,
                EyeSocketDarkening = 0.3f
            };
        }

        #endregion
    }

    /// <summary>
    /// 眼睛材质预设
    /// </summary>
    [Serializable]
    public class EyePreset
    {
        #region 虹膜参数

        /// <summary>
        /// 虹膜颜色
        /// </summary>
        public Color IrisColor { get; set; } = new Color(0.4f, 0.3f, 0.2f, 1.0f);

        /// <summary>
        /// 虹膜外缘颜色
        /// </summary>
        public Color IrisLimbusColor { get; set; } = new Color(0.15f, 0.1f, 0.05f, 1.0f);

        /// <summary>
        /// 虹膜亮度
        /// </summary>
        public float IrisBrightness { get; set; } = 1.0f;

        /// <summary>
        /// 虹膜饱和度
        /// </summary>
        public float IrisSaturation { get; set; } = 1.0f;

        /// <summary>
        /// 虹膜细节强度
        /// </summary>
        public float IrisDetailIntensity { get; set; } = 0.8f;

        /// <summary>
        /// 虹膜纤维密度
        /// </summary>
        public float IrisFiberDensity { get; set; } = 0.7f;

        /// <summary>
        /// 虹膜法线强度
        /// </summary>
        public float IrisNormalStrength { get; set; } = 0.5f;

        #endregion

        #region 瞳孔参数

        /// <summary>
        /// 瞳孔颜色
        /// </summary>
        public Color PupilColor { get; set; } = Color.Black;

        /// <summary>
        /// 瞳孔大小
        /// </summary>
        public float PupilSize { get; set; } = 0.35f;

        /// <summary>
        /// 瞳孔形状（0=圆形，1=竖缝形）
        /// </summary>
        public float PupilSlitness { get; set; } = 0f;

        /// <summary>
        /// 启用瞳孔光反应
        /// </summary>
        public bool EnablePupilLightReaction { get; set; } = true;

        /// <summary>
        /// 瞳孔反应速度
        /// </summary>
        public float PupilReactionSpeed { get; set; } = 0.5f;

        #endregion

        #region 角膜参数

        /// <summary>
        /// 角膜折射率
        /// </summary>
        public float CorneaIOR { get; set; } = 1.376f;

        /// <summary>
        /// 角膜曲率
        /// </summary>
        public float CorneaCurvature { get; set; } = 0.5f;

        /// <summary>
        /// 角膜高光强度
        /// </summary>
        public float CorneaSpecular { get; set; } = 0.9f;

        /// <summary>
        /// 角膜粗糙度
        /// </summary>
        public float CorneaRoughness { get; set; } = 0.05f;

        /// <summary>
        /// 角膜折射深度
        /// </summary>
        public float CorneaDepth { get; set; } = 0.1f;

        #endregion

        #region 巩膜参数

        /// <summary>
        /// 巩膜颜色
        /// </summary>
        public Color ScleraColor { get; set; } = new Color(0.98f, 0.97f, 0.95f, 1.0f);

        /// <summary>
        /// 巩膜血丝强度
        /// </summary>
        public float ScleraVeinsIntensity { get; set; } = 0.3f;

        /// <summary>
        /// 巩膜血丝颜色
        /// </summary>
        public Color ScleraVeinsColor { get; set; } = new Color(0.8f, 0.2f, 0.15f, 1.0f);

        /// <summary>
        /// 巩膜SSS颜色
        /// </summary>
        public Color ScleraSSSColor { get; set; } = new Color(0.9f, 0.3f, 0.2f, 1.0f);

        /// <summary>
        /// 巩膜SSS强度
        /// </summary>
        public float ScleraSSSIntensity { get; set; } = 0.5f;

        /// <summary>
        /// 巩膜粗糙度
        /// </summary>
        public float ScleraRoughness { get; set; } = 0.15f;

        #endregion

        #region 湿润效果

        /// <summary>
        /// 湿润度
        /// </summary>
        public float Wetness { get; set; } = 0.8f;

        /// <summary>
        /// 泪线强度
        /// </summary>
        public float TearLineIntensity { get; set; } = 0.3f;

        /// <summary>
        /// 环境反射强度
        /// </summary>
        public float EnvironmentReflection { get; set; } = 0.7f;

        #endregion

        #region 光泽效果

        /// <summary>
        /// 焦散强度
        /// </summary>
        public float CausticIntensity { get; set; } = 0.3f;

        /// <summary>
        /// 光斑大小
        /// </summary>
        public float SpecularSize { get; set; } = 0.05f;

        /// <summary>
        /// 双光斑效果
        /// </summary>
        public bool DualSpecular { get; set; } = true;

        #endregion

        #region 工厂方法

        /// <summary>
        /// 创建默认眼睛预设
        /// </summary>
        public static EyePreset CreateDefault()
        {
            return new EyePreset();
        }

        /// <summary>
        /// 创建亚洲人眼睛预设（深棕色）
        /// </summary>
        public static EyePreset CreateAsian()
        {
            return new EyePreset
            {
                IrisColor = new Color(0.15f, 0.1f, 0.05f, 1.0f),
                IrisLimbusColor = new Color(0.08f, 0.05f, 0.02f, 1.0f),
                PupilSize = 0.32f
            };
        }

        /// <summary>
        /// 创建欧洲人眼睛预设（蓝色）
        /// </summary>
        public static EyePreset CreateEuropean()
        {
            return new EyePreset
            {
                IrisColor = new Color(0.2f, 0.4f, 0.7f, 1.0f),
                IrisLimbusColor = new Color(0.1f, 0.15f, 0.3f, 1.0f),
                PupilSize = 0.35f
            };
        }

        /// <summary>
        /// 创建棕色眼睛预设
        /// </summary>
        public static EyePreset CreateBrown()
        {
            return new EyePreset
            {
                IrisColor = new Color(0.4f, 0.25f, 0.1f, 1.0f),
                IrisLimbusColor = new Color(0.15f, 0.08f, 0.03f, 1.0f)
            };
        }

        /// <summary>
        /// 创建蓝色眼睛预设
        /// </summary>
        public static EyePreset CreateBlue()
        {
            return new EyePreset
            {
                IrisColor = new Color(0.2f, 0.4f, 0.7f, 1.0f),
                IrisLimbusColor = new Color(0.1f, 0.15f, 0.3f, 1.0f)
            };
        }

        /// <summary>
        /// 创建绿色眼睛预设
        /// </summary>
        public static EyePreset CreateGreen()
        {
            return new EyePreset
            {
                IrisColor = new Color(0.3f, 0.5f, 0.3f, 1.0f),
                IrisLimbusColor = new Color(0.1f, 0.2f, 0.1f, 1.0f)
            };
        }

        #endregion
    }

    /// <summary>
    /// 毛发材质预设
    /// </summary>
    [Serializable]
    public class HairPreset
    {
        #region 颜色参数

        /// <summary>
        /// 发根颜色
        /// </summary>
        public Color RootColor { get; set; } = new Color(0.05f, 0.04f, 0.03f, 1.0f);

        /// <summary>
        /// 发尖颜色
        /// </summary>
        public Color TipColor { get; set; } = new Color(0.15f, 0.12f, 0.08f, 1.0f);

        /// <summary>
        /// 主色调
        /// </summary>
        public Color BaseColor { get; set; } = new Color(0.1f, 0.08f, 0.05f, 1.0f);

        /// <summary>
        /// 染色颜色
        /// </summary>
        public Color DyeColor { get; set; } = Color.White;

        /// <summary>
        /// 染色强度
        /// </summary>
        public float DyeIntensity { get; set; } = 0f;

        /// <summary>
        /// 黑色素含量
        /// </summary>
        public float Melanin { get; set; } = 0.5f;

        /// <summary>
        /// 红色素含量
        /// </summary>
        public float Pheomelanin { get; set; } = 0.3f;

        #endregion

        #region 各向异性高光参数

        /// <summary>
        /// 启用各向异性
        /// </summary>
        public bool EnableAnisotropy { get; set; } = true;

        /// <summary>
        /// 各向异性强度
        /// </summary>
        public float AnisotropyIntensity { get; set; } = 0.8f;

        /// <summary>
        /// 主高光偏移
        /// </summary>
        public float PrimarySpecularShift { get; set; } = -0.1f;

        /// <summary>
        /// 主高光颜色
        /// </summary>
        public Color PrimarySpecularColor { get; set; } = new Color(1.0f, 1.0f, 1.0f, 1.0f);

        /// <summary>
        /// 主高光强度
        /// </summary>
        public float PrimarySpecularIntensity { get; set; } = 0.5f;

        /// <summary>
        /// 主高光宽度
        /// </summary>
        public float PrimarySpecularWidth { get; set; } = 0.3f;

        /// <summary>
        /// 次高光偏移
        /// </summary>
        public float SecondarySpecularShift { get; set; } = 0.1f;

        /// <summary>
        /// 次高光颜色
        /// </summary>
        public Color SecondarySpecularColor { get; set; } = new Color(0.8f, 0.6f, 0.4f, 1.0f);

        /// <summary>
        /// 次高光强度
        /// </summary>
        public float SecondarySpecularIntensity { get; set; } = 0.3f;

        /// <summary>
        /// 次高光宽度
        /// </summary>
        public float SecondarySpecularWidth { get; set; } = 0.5f;

        #endregion

        #region 散射参数

        /// <summary>
        /// 启用多重散射
        /// </summary>
        public bool EnableMultipleScattering { get; set; } = true;

        /// <summary>
        /// 散射强度
        /// </summary>
        public float ScatterIntensity { get; set; } = 0.5f;

        /// <summary>
        /// 散射颜色
        /// </summary>
        public Color ScatterColor { get; set; } = new Color(1.0f, 0.9f, 0.8f, 1.0f);

        /// <summary>
        /// 透射强度
        /// </summary>
        public float TransmissionIntensity { get; set; } = 0.3f;

        /// <summary>
        /// 透射颜色
        /// </summary>
        public Color TransmissionColor { get; set; } = new Color(1.0f, 0.6f, 0.3f, 1.0f);

        /// <summary>
        /// 环境光散射
        /// </summary>
        public float AmbientScatter { get; set; } = 0.5f;

        #endregion

        #region 表面参数

        /// <summary>
        /// 粗糙度
        /// </summary>
        public float Roughness { get; set; } = 0.4f;

        /// <summary>
        /// 切向粗糙度
        /// </summary>
        public float TangentRoughness { get; set; } = 0.3f;

        /// <summary>
        /// 法线强度
        /// </summary>
        public float NormalStrength { get; set; } = 1.0f;

        #endregion

        #region 透明度参数

        /// <summary>
        /// Alpha裁切阈值
        /// </summary>
        public float AlphaCutoff { get; set; } = 0.5f;

        /// <summary>
        /// Alpha边缘柔化
        /// </summary>
        public float AlphaSoftness { get; set; } = 0.1f;

        /// <summary>
        /// 双面渲染
        /// </summary>
        public bool DoubleSided { get; set; } = true;

        #endregion

        #region 细节参数

        /// <summary>
        /// 流向偏移强度
        /// </summary>
        public float FlowMapStrength { get; set; } = 0.5f;

        /// <summary>
        /// 发根暗化
        /// </summary>
        public float RootDarkening { get; set; } = 0.3f;

        /// <summary>
        /// AO强度
        /// </summary>
        public float AOIntensity { get; set; } = 0.5f;

        /// <summary>
        /// 自阴影强度
        /// </summary>
        public float SelfShadowIntensity { get; set; } = 0.3f;

        #endregion

        #region 工厂方法

        /// <summary>
        /// 创建默认毛发预设
        /// </summary>
        public static HairPreset CreateDefault()
        {
            return new HairPreset();
        }

        /// <summary>
        /// 创建亚洲人毛发预设（黑发）
        /// </summary>
        public static HairPreset CreateAsian()
        {
            return new HairPreset
            {
                RootColor = new Color(0.02f, 0.02f, 0.02f, 1.0f),
                TipColor = new Color(0.05f, 0.05f, 0.05f, 1.0f),
                BaseColor = new Color(0.03f, 0.03f, 0.03f, 1.0f),
                Melanin = 0.9f,
                Pheomelanin = 0.1f,
                SecondarySpecularColor = new Color(0.6f, 0.5f, 0.4f, 1.0f)
            };
        }

        /// <summary>
        /// 创建欧洲人毛发预设（金发）
        /// </summary>
        public static HairPreset CreateEuropean()
        {
            return new HairPreset
            {
                RootColor = new Color(0.3f, 0.25f, 0.15f, 1.0f),
                TipColor = new Color(0.5f, 0.45f, 0.3f, 1.0f),
                BaseColor = new Color(0.4f, 0.35f, 0.2f, 1.0f),
                Melanin = 0.2f,
                Pheomelanin = 0.6f,
                SecondarySpecularColor = new Color(1.0f, 0.95f, 0.8f, 1.0f)
            };
        }

        /// <summary>
        /// 创建黑发预设
        /// </summary>
        public static HairPreset CreateBlack()
        {
            return new HairPreset
            {
                RootColor = new Color(0.02f, 0.02f, 0.02f, 1.0f),
                TipColor = new Color(0.05f, 0.05f, 0.05f, 1.0f),
                BaseColor = new Color(0.03f, 0.03f, 0.03f, 1.0f),
                Melanin = 0.9f,
                Pheomelanin = 0.1f
            };
        }

        /// <summary>
        /// 创建棕发预设
        /// </summary>
        public static HairPreset CreateBrown()
        {
            return new HairPreset
            {
                RootColor = new Color(0.08f, 0.05f, 0.03f, 1.0f),
                TipColor = new Color(0.15f, 0.1f, 0.06f, 1.0f),
                BaseColor = new Color(0.1f, 0.07f, 0.04f, 1.0f),
                Melanin = 0.6f,
                Pheomelanin = 0.4f
            };
        }

        /// <summary>
        /// 创建金发预设
        /// </summary>
        public static HairPreset CreateBlonde()
        {
            return new HairPreset
            {
                RootColor = new Color(0.3f, 0.25f, 0.15f, 1.0f),
                TipColor = new Color(0.5f, 0.45f, 0.3f, 1.0f),
                BaseColor = new Color(0.4f, 0.35f, 0.2f, 1.0f),
                Melanin = 0.2f,
                Pheomelanin = 0.6f
            };
        }

        /// <summary>
        /// 创建红发预设
        /// </summary>
        public static HairPreset CreateRed()
        {
            return new HairPreset
            {
                RootColor = new Color(0.15f, 0.05f, 0.02f, 1.0f),
                TipColor = new Color(0.4f, 0.15f, 0.05f, 1.0f),
                BaseColor = new Color(0.25f, 0.1f, 0.04f, 1.0f),
                Melanin = 0.3f,
                Pheomelanin = 0.9f
            };
        }

        /// <summary>
        /// 创建白发/银发预设
        /// </summary>
        public static HairPreset CreateWhite()
        {
            return new HairPreset
            {
                RootColor = new Color(0.7f, 0.7f, 0.7f, 1.0f),
                TipColor = new Color(0.9f, 0.9f, 0.9f, 1.0f),
                BaseColor = new Color(0.85f, 0.85f, 0.85f, 1.0f),
                Melanin = 0.0f,
                Pheomelanin = 0.0f
            };
        }

        #endregion
    }
}
