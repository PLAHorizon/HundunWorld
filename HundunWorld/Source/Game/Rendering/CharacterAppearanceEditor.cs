using System;
using System.IO;
using FlaxEngine;
using HundunWorld.Game.Rendering.Materials;

namespace HundunWorld.Game.Rendering
{
    /// <summary>
    /// 角色外观编辑器 - 统一管理皮肤、眼睛、毛发材质控制器
    /// 提供预设加载/保存、参数同步、快速预设切换等功能
    /// </summary>
    public class CharacterAppearanceEditor : Script
    {
        #region 材质控制器引用

        /// <summary>
        /// 皮肤材质控制器
        /// </summary>
        [Header("材质控制器")]
        [Tooltip("皮肤材质控制器引用")]
        public SkinMaterialController SkinController;

        /// <summary>
        /// 眼睛材质控制器
        /// </summary>
        [Tooltip("眼睛材质控制器引用")]
        public EyeMaterialController EyeController;

        /// <summary>
        /// 毛发材质控制器
        /// </summary>
        [Tooltip("毛发材质控制器引用")]
        public HairMaterialController HairController;

        #endregion

        #region 预设配置

        /// <summary>
        /// 当前加载的预设
        /// </summary>
        public CharacterAppearancePreset CurrentPreset { get; private set; }

        /// <summary>
        /// 默认预设文件路径
        /// </summary>
        [Header("预设配置")]
        [Tooltip("默认预设JSON文件路径")]
        public string DefaultPresetPath = "Content/Presets/MetaHuman/Preset_Default.json";

        /// <summary>
        /// 预设文件目录
        /// </summary>
        [Tooltip("预设文件存储目录")]
        public string PresetDirectory = "Content/Presets/MetaHuman";

        #endregion

        #region 实时更新设置

        /// <summary>
        /// 启用实时更新
        /// </summary>
        [Header("更新设置")]
        [Tooltip("是否在编辑器中实时更新材质")]
        public bool EnableRealtimeUpdate = true;

        /// <summary>
        /// 更新频率（秒）
        /// </summary>
        [Range(0.016f, 0.5f)]
        [Tooltip("材质更新的间隔时间")]
        public float UpdateInterval = 0.05f;

        private float _updateTimer;

        #endregion

        #region 事件

        /// <summary>
        /// 外观参数变化事件
        /// </summary>
        public event Action<CharacterAppearancePreset> OnAppearanceChanged;

        /// <summary>
        /// 预设加载完成事件
        /// </summary>
        public event Action<string> OnPresetLoaded;

        /// <summary>
        /// 预设保存完成事件
        /// </summary>
        public event Action<string> OnPresetSaved;

        /// <summary>
        /// 皮肤参数变化事件
        /// </summary>
        public event Action OnSkinChanged;

        /// <summary>
        /// 眼睛参数变化事件
        /// </summary>
        public event Action OnEyeChanged;

        /// <summary>
        /// 毛发参数变化事件
        /// </summary>
        public event Action OnHairChanged;

        #endregion

        #region 生命周期

        public override void OnStart()
        {
            // 尝试自动绑定控制器
            AutoBindControllers();
            
            ValidateControllers();
            LoadDefaultPreset();
            
            // 如果仍有控制器未分配，输出警告信息
            if (SkinController == null)
            {
                Debug.LogWarning($"[CharacterAppearanceEditor] 无法找到 SkinMaterialController，请确保在Actor '{Actor.Name}' 或其子对象中添加该组件");
            }
            if (EyeController == null)
            {
                Debug.LogWarning($"[CharacterAppearanceEditor] 无法找到 EyeMaterialController，请确保在Actor '{Actor.Name}' 或其子对象中添加该组件");
            }
            if (HairController == null)
            {
                Debug.LogWarning($"[CharacterAppearanceEditor] 无法找到 HairMaterialController，请确保在Actor '{Actor.Name}' 或其子对象中添加该组件");
            }
        }

        public override void OnUpdate()
        {
            if (!EnableRealtimeUpdate || !Engine.IsEditor)
                return;

            _updateTimer += Time.DeltaTime;
            if (_updateTimer >= UpdateInterval)
            {
                _updateTimer = 0f;
                RefreshAllMaterials();
            }
        }

        #endregion

        #region 控制器验证

        /// <summary>
        /// 验证控制器是否正确引用
        /// </summary>
        private void ValidateControllers()
        {
            if (SkinController == null)
                Debug.LogWarning("[CharacterAppearanceEditor] SkinController未分配");
            if (EyeController == null)
                Debug.LogWarning("[CharacterAppearanceEditor] EyeController未分配");
            if (HairController == null)
                Debug.LogWarning("[CharacterAppearanceEditor] HairController未分配");
        }

        /// <summary>
        /// 自动查找并绑定控制器
        /// </summary>
        public void AutoBindControllers()
        {
            if (SkinController == null)
                SkinController = FindControllerInChildren<SkinMaterialController>();
            if (EyeController == null)
                EyeController = FindControllerInChildren<EyeMaterialController>();
            if (HairController == null)
                HairController = FindControllerInChildren<HairMaterialController>();

            ValidateControllers();
        }

        /// <summary>
        /// 在子对象中查找指定类型的控制器
        /// </summary>
        /// <typeparam name="T">控制器类型</typeparam>
        /// <returns>找到的控制器，如果没找到则返回null</returns>
        private T FindControllerInChildren<T>() where T : Script
        {
            // 首先尝试直接获取
            T controller = Actor.GetScript<T>();
            if (controller != null)
                return controller;
            
            // 如果直接获取不到，在子对象中搜索
            var children = Actor.Children;
            foreach (var child in children)
            {
                controller = child.GetScript<T>();
                if (controller != null)
                    return controller;
            }
            
            // 如果在直接子对象中没找到，递归搜索
            foreach (var child in children)
            {
                controller = FindControllerInDescendants<T>(child);
                if (controller != null)
                    return controller;
            }
            
            return null;
        }
        
        /// <summary>
        /// 在后代对象中递归查找指定类型的控制器
        /// </summary>
        /// <typeparam name="T">控制器类型</typeparam>
        /// <param name="actor">要搜索的起始Actor</param>
        /// <returns>找到的控制器，如果没找到则返回null</returns>
        private T FindControllerInDescendants<T>(Actor actor) where T : Script
        {
            // 搜索当前actor
            var controller = actor.GetScript<T>();
            if (controller != null)
                return controller;
            
            // 递归搜索子对象
            foreach (var child in actor.Children)
            {
                controller = FindControllerInDescendants<T>(child);
                if (controller != null)
                    return controller;
            }
            
            return null;
        }

        #endregion

        #region 预设管理

        /// <summary>
        /// 加载默认预设
        /// </summary>
        private void LoadDefaultPreset()
        {
            if (string.IsNullOrEmpty(DefaultPresetPath))
            {
                // 使用代码生成的默认预设
                CurrentPreset = CharacterAppearancePreset.CreateDefault();
                ApplyPreset(CurrentPreset);
                return;
            }

            if (!LoadPreset(DefaultPresetPath))
            {
                // 加载失败时使用代码生成的默认预设
                CurrentPreset = CharacterAppearancePreset.CreateDefault();
                ApplyPreset(CurrentPreset);
            }
        }

        /// <summary>
        /// 加载预设文件
        /// </summary>
        /// <param name="filePath">预设文件路径</param>
        /// <returns>是否加载成功</returns>
        public bool LoadPreset(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    Debug.LogWarning($"[CharacterAppearanceEditor] 预设文件不存在: {filePath}");
                    return false;
                }

                var jsonContent = File.ReadAllText(filePath);
                var preset = FlaxEngine.Json.JsonSerializer.Deserialize<CharacterAppearancePreset>(jsonContent);

                if (preset != null)
                {
                    CurrentPreset = preset;
                    ApplyPreset(preset);
                    OnPresetLoaded?.Invoke(filePath);
                    Debug.Log($"[CharacterAppearanceEditor] 预设加载成功: {filePath}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CharacterAppearanceEditor] 预设加载失败: {ex.Message}");
            }

            return false;
        }

        /// <summary>
        /// 保存当前配置为预设
        /// </summary>
        /// <param name="filePath">保存路径</param>
        /// <param name="presetName">预设名称</param>
        /// <returns>是否保存成功</returns>
        public bool SavePreset(string filePath, string presetName = "Custom")
        {
            try
            {
                var preset = CaptureCurrentAppearance();
                preset.PresetName = presetName;
                preset.CreatedDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

                // 确保目录存在
                var directory = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var jsonContent = FlaxEngine.Json.JsonSerializer.Serialize(preset);
                File.WriteAllText(filePath, jsonContent);

                CurrentPreset = preset;
                OnPresetSaved?.Invoke(filePath);
                Debug.Log($"[CharacterAppearanceEditor] 预设保存成功: {filePath}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[CharacterAppearanceEditor] 预设保存失败: {ex.Message}");
            }

            return false;
        }

        /// <summary>
        /// 应用预设到所有材质控制器
        /// </summary>
        /// <param name="preset">要应用的预设</param>
        public void ApplyPreset(CharacterAppearancePreset preset)
        {
            if (preset == null) return;

            // 应用皮肤预设
            if (SkinController != null && preset.Skin != null)
            {
                ApplySkinPreset(preset.Skin);
            }

            // 应用眼睛预设
            if (EyeController != null && preset.Eye != null)
            {
                ApplyEyePreset(preset.Eye);
            }

            // 应用毛发预设
            if (HairController != null && preset.Hair != null)
            {
                ApplyHairPreset(preset.Hair);
            }

            OnAppearanceChanged?.Invoke(preset);
        }

        /// <summary>
        /// 捕获当前外观配置
        /// </summary>
        /// <returns>当前外观预设</returns>
        public CharacterAppearancePreset CaptureCurrentAppearance()
        {
            return new CharacterAppearancePreset
            {
                PresetName = "Custom",
                Description = "用户自定义外观",
                CreatedDate = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                Skin = CaptureSkinPreset(),
                Eye = CaptureEyePreset(),
                Hair = CaptureHairPreset()
            };
        }

        #endregion

        #region 皮肤预设应用和捕获

        /// <summary>
        /// 应用皮肤预设
        /// </summary>
        private void ApplySkinPreset(SkinPreset preset)
        {
            if (SkinController == null || preset == null) return;

            // 基础属性
            SkinController.BaseColor = preset.BaseColor;
            SkinController.ColorVariation = preset.ColorVariation;
            SkinController.BaseRoughness = preset.BaseRoughness;
            SkinController.SpecularIntensity = preset.SpecularIntensity;

            // SSS参数
            SkinController.EnableSSS = preset.EnableSSS;
            SkinController.EpidermisColor = preset.EpidermisColor;
            SkinController.DermisColor = preset.DermisColor;
            SkinController.SubcutisColor = preset.SubcutisColor;
            SkinController.EpidermisWeight = preset.EpidermisWeight;
            SkinController.DermisWeight = preset.DermisWeight;
            SkinController.SubcutisWeight = preset.SubcutisWeight;
            SkinController.SSSIntensity = preset.SSSIntensity;
            SkinController.SSSRadius = preset.SSSRadius;
            SkinController.TransmissionIntensity = preset.TransmissionIntensity;
            SkinController.TransmissionColor = preset.TransmissionColor;

            // 细节参数
            SkinController.NormalStrength = preset.NormalStrength;
            SkinController.PoreNormalStrength = preset.PoreNormalStrength;
            SkinController.PoreTiling = preset.PoreTiling;
            SkinController.WrinkleDepth = preset.WrinkleDepth;
            SkinController.MicroDetailStrength = preset.MicroDetailStrength;

            // 特殊区域
            SkinController.LipColor = preset.LipColor;
            SkinController.LipWetness = preset.LipWetness;
            SkinController.BlushIntensity = preset.BlushIntensity;
            SkinController.BlushColor = preset.BlushColor;
            SkinController.EyeSocketDarkening = preset.EyeSocketDarkening;

            // 菲涅尔
            SkinController.FresnelIntensity = preset.FresnelIntensity;
            SkinController.FresnelPower = preset.FresnelPower;
            SkinController.FresnelColor = preset.FresnelColor;

            SkinController.ApplyAllParameters();
            OnSkinChanged?.Invoke();
        }

        /// <summary>
        /// 捕获当前皮肤预设
        /// </summary>
        private SkinPreset CaptureSkinPreset()
        {
            if (SkinController == null) return new SkinPreset();

            return new SkinPreset
            {
                BaseColor = SkinController.BaseColor,
                ColorVariation = SkinController.ColorVariation,
                BaseRoughness = SkinController.BaseRoughness,
                SpecularIntensity = SkinController.SpecularIntensity,
                EnableSSS = SkinController.EnableSSS,
                EpidermisColor = SkinController.EpidermisColor,
                DermisColor = SkinController.DermisColor,
                SubcutisColor = SkinController.SubcutisColor,
                EpidermisWeight = SkinController.EpidermisWeight,
                DermisWeight = SkinController.DermisWeight,
                SubcutisWeight = SkinController.SubcutisWeight,
                SSSIntensity = SkinController.SSSIntensity,
                SSSRadius = SkinController.SSSRadius,
                TransmissionIntensity = SkinController.TransmissionIntensity,
                TransmissionColor = SkinController.TransmissionColor,
                NormalStrength = SkinController.NormalStrength,
                PoreNormalStrength = SkinController.PoreNormalStrength,
                PoreTiling = SkinController.PoreTiling,
                WrinkleDepth = SkinController.WrinkleDepth,
                MicroDetailStrength = SkinController.MicroDetailStrength,
                LipColor = SkinController.LipColor,
                LipWetness = SkinController.LipWetness,
                BlushIntensity = SkinController.BlushIntensity,
                BlushColor = SkinController.BlushColor,
                EyeSocketDarkening = SkinController.EyeSocketDarkening,
                FresnelIntensity = SkinController.FresnelIntensity,
                FresnelPower = SkinController.FresnelPower,
                FresnelColor = SkinController.FresnelColor
            };
        }

        #endregion

        #region 眼睛预设应用和捕获

        /// <summary>
        /// 应用眼睛预设
        /// </summary>
        private void ApplyEyePreset(EyePreset preset)
        {
            if (EyeController == null || preset == null) return;

            // 虹膜参数
            EyeController.IrisColor = preset.IrisColor;
            EyeController.IrisLimbusColor = preset.IrisLimbusColor;
            EyeController.IrisBrightness = preset.IrisBrightness;
            EyeController.IrisSaturation = preset.IrisSaturation;
            EyeController.IrisDetailIntensity = preset.IrisDetailIntensity;
            EyeController.IrisFiberDensity = preset.IrisFiberDensity;
            EyeController.IrisNormalStrength = preset.IrisNormalStrength;

            // 瞳孔参数
            EyeController.PupilColor = preset.PupilColor;
            EyeController.PupilSize = preset.PupilSize;
            EyeController.PupilSlitness = preset.PupilSlitness;
            EyeController.EnablePupilLightReaction = preset.EnablePupilLightReaction;
            EyeController.PupilReactionSpeed = preset.PupilReactionSpeed;

            // 角膜参数
            EyeController.CorneaIOR = preset.CorneaIOR;
            EyeController.CorneaCurvature = preset.CorneaCurvature;
            EyeController.CorneaSpecular = preset.CorneaSpecular;
            EyeController.CorneaRoughness = preset.CorneaRoughness;
            EyeController.CorneaDepth = preset.CorneaDepth;

            // 巩膜参数
            EyeController.ScleraColor = preset.ScleraColor;
            EyeController.ScleraVeinsIntensity = preset.ScleraVeinsIntensity;
            EyeController.ScleraVeinsColor = preset.ScleraVeinsColor;
            EyeController.ScleraSSSColor = preset.ScleraSSSColor;
            EyeController.ScleraSSSIntensity = preset.ScleraSSSIntensity;
            EyeController.ScleraRoughness = preset.ScleraRoughness;

            // 湿润效果
            EyeController.Wetness = preset.Wetness;
            EyeController.TearLineIntensity = preset.TearLineIntensity;
            EyeController.EnvironmentReflection = preset.EnvironmentReflection;

            // 光泽效果
            EyeController.CausticIntensity = preset.CausticIntensity;
            EyeController.SpecularSize = preset.SpecularSize;
            EyeController.DualSpecular = preset.DualSpecular;

            EyeController.ApplyAllParameters();
            OnEyeChanged?.Invoke();
        }

        /// <summary>
        /// 捕获当前眼睛预设
        /// </summary>
        private EyePreset CaptureEyePreset()
        {
            if (EyeController == null) return new EyePreset();

            return new EyePreset
            {
                IrisColor = EyeController.IrisColor,
                IrisLimbusColor = EyeController.IrisLimbusColor,
                IrisBrightness = EyeController.IrisBrightness,
                IrisSaturation = EyeController.IrisSaturation,
                IrisDetailIntensity = EyeController.IrisDetailIntensity,
                IrisFiberDensity = EyeController.IrisFiberDensity,
                IrisNormalStrength = EyeController.IrisNormalStrength,
                PupilColor = EyeController.PupilColor,
                PupilSize = EyeController.PupilSize,
                PupilSlitness = EyeController.PupilSlitness,
                EnablePupilLightReaction = EyeController.EnablePupilLightReaction,
                PupilReactionSpeed = EyeController.PupilReactionSpeed,
                CorneaIOR = EyeController.CorneaIOR,
                CorneaCurvature = EyeController.CorneaCurvature,
                CorneaSpecular = EyeController.CorneaSpecular,
                CorneaRoughness = EyeController.CorneaRoughness,
                CorneaDepth = EyeController.CorneaDepth,
                ScleraColor = EyeController.ScleraColor,
                ScleraVeinsIntensity = EyeController.ScleraVeinsIntensity,
                ScleraVeinsColor = EyeController.ScleraVeinsColor,
                ScleraSSSColor = EyeController.ScleraSSSColor,
                ScleraSSSIntensity = EyeController.ScleraSSSIntensity,
                ScleraRoughness = EyeController.ScleraRoughness,
                Wetness = EyeController.Wetness,
                TearLineIntensity = EyeController.TearLineIntensity,
                EnvironmentReflection = EyeController.EnvironmentReflection,
                CausticIntensity = EyeController.CausticIntensity,
                SpecularSize = EyeController.SpecularSize,
                DualSpecular = EyeController.DualSpecular
            };
        }

        #endregion

        #region 毛发预设应用和捕获

        /// <summary>
        /// 应用毛发预设
        /// </summary>
        private void ApplyHairPreset(HairPreset preset)
        {
            if (HairController == null || preset == null) return;

            // 颜色参数
            HairController.RootColor = preset.RootColor;
            HairController.TipColor = preset.TipColor;
            HairController.BaseColor = preset.BaseColor;
            HairController.DyeColor = preset.DyeColor;
            HairController.DyeIntensity = preset.DyeIntensity;
            HairController.Melanin = preset.Melanin;
            HairController.Pheomelanin = preset.Pheomelanin;

            // 各向异性高光
            HairController.EnableAnisotropy = preset.EnableAnisotropy;
            HairController.AnisotropyIntensity = preset.AnisotropyIntensity;
            HairController.PrimarySpecularShift = preset.PrimarySpecularShift;
            HairController.PrimarySpecularColor = preset.PrimarySpecularColor;
            HairController.PrimarySpecularIntensity = preset.PrimarySpecularIntensity;
            HairController.PrimarySpecularWidth = preset.PrimarySpecularWidth;
            HairController.SecondarySpecularShift = preset.SecondarySpecularShift;
            HairController.SecondarySpecularColor = preset.SecondarySpecularColor;
            HairController.SecondarySpecularIntensity = preset.SecondarySpecularIntensity;
            HairController.SecondarySpecularWidth = preset.SecondarySpecularWidth;

            // 散射参数
            HairController.EnableMultipleScattering = preset.EnableMultipleScattering;
            HairController.ScatterIntensity = preset.ScatterIntensity;
            HairController.ScatterColor = preset.ScatterColor;
            HairController.TransmissionIntensity = preset.TransmissionIntensity;
            HairController.TransmissionColor = preset.TransmissionColor;
            HairController.AmbientScatter = preset.AmbientScatter;

            // 表面参数
            HairController.Roughness = preset.Roughness;
            HairController.TangentRoughness = preset.TangentRoughness;
            HairController.NormalStrength = preset.NormalStrength;

            // 透明度参数
            HairController.AlphaCutoff = preset.AlphaCutoff;
            HairController.AlphaSoftness = preset.AlphaSoftness;
            HairController.DoubleSided = preset.DoubleSided;

            // 细节参数
            HairController.FlowMapStrength = preset.FlowMapStrength;
            HairController.RootDarkening = preset.RootDarkening;
            HairController.AOIntensity = preset.AOIntensity;
            HairController.SelfShadowIntensity = preset.SelfShadowIntensity;

            HairController.ApplyAllParameters();
            OnHairChanged?.Invoke();
        }

        /// <summary>
        /// 捕获当前毛发预设
        /// </summary>
        private HairPreset CaptureHairPreset()
        {
            if (HairController == null) return new HairPreset();

            return new HairPreset
            {
                RootColor = HairController.RootColor,
                TipColor = HairController.TipColor,
                BaseColor = HairController.BaseColor,
                DyeColor = HairController.DyeColor,
                DyeIntensity = HairController.DyeIntensity,
                Melanin = HairController.Melanin,
                Pheomelanin = HairController.Pheomelanin,
                EnableAnisotropy = HairController.EnableAnisotropy,
                AnisotropyIntensity = HairController.AnisotropyIntensity,
                PrimarySpecularShift = HairController.PrimarySpecularShift,
                PrimarySpecularColor = HairController.PrimarySpecularColor,
                PrimarySpecularIntensity = HairController.PrimarySpecularIntensity,
                PrimarySpecularWidth = HairController.PrimarySpecularWidth,
                SecondarySpecularShift = HairController.SecondarySpecularShift,
                SecondarySpecularColor = HairController.SecondarySpecularColor,
                SecondarySpecularIntensity = HairController.SecondarySpecularIntensity,
                SecondarySpecularWidth = HairController.SecondarySpecularWidth,
                EnableMultipleScattering = HairController.EnableMultipleScattering,
                ScatterIntensity = HairController.ScatterIntensity,
                ScatterColor = HairController.ScatterColor,
                TransmissionIntensity = HairController.TransmissionIntensity,
                TransmissionColor = HairController.TransmissionColor,
                AmbientScatter = HairController.AmbientScatter,
                Roughness = HairController.Roughness,
                TangentRoughness = HairController.TangentRoughness,
                NormalStrength = HairController.NormalStrength,
                AlphaCutoff = HairController.AlphaCutoff,
                AlphaSoftness = HairController.AlphaSoftness,
                DoubleSided = HairController.DoubleSided,
                FlowMapStrength = HairController.FlowMapStrength,
                RootDarkening = HairController.RootDarkening,
                AOIntensity = HairController.AOIntensity,
                SelfShadowIntensity = HairController.SelfShadowIntensity
            };
        }

        #endregion

        #region 单参数设置接口 - 皮肤

        /// <summary>
        /// 设置皮肤基础颜色
        /// </summary>
        public void SetSkinBaseColor(Color color)
        {
            if (SkinController == null) return;
            SkinController.BaseColor = color;
            SkinController.ApplyAllParameters();
            OnSkinChanged?.Invoke();
        }

        /// <summary>
        /// 设置皮肤粗糙度
        /// </summary>
        public void SetSkinRoughness(float roughness)
        {
            if (SkinController == null) return;
            SkinController.BaseRoughness = roughness;
            SkinController.ApplyAllParameters();
            OnSkinChanged?.Invoke();
        }

        /// <summary>
        /// 设置皮肤SSS强度
        /// </summary>
        public void SetSkinSSSIntensity(float intensity)
        {
            if (SkinController == null) return;
            SkinController.SSSIntensity = intensity;
            SkinController.ApplyAllParameters();
            OnSkinChanged?.Invoke();
        }

        /// <summary>
        /// 设置皮肤SSS半径
        /// </summary>
        public void SetSkinSSSRadius(float radius)
        {
            if (SkinController == null) return;
            SkinController.SSSRadius = radius;
            SkinController.ApplyAllParameters();
            OnSkinChanged?.Invoke();
        }

        /// <summary>
        /// 设置表皮层颜色
        /// </summary>
        public void SetSkinEpidermisColor(Color color)
        {
            if (SkinController == null) return;
            SkinController.EpidermisColor = color;
            SkinController.ApplyAllParameters();
            OnSkinChanged?.Invoke();
        }

        /// <summary>
        /// 设置真皮层颜色
        /// </summary>
        public void SetSkinDermisColor(Color color)
        {
            if (SkinController == null) return;
            SkinController.DermisColor = color;
            SkinController.ApplyAllParameters();
            OnSkinChanged?.Invoke();
        }

        /// <summary>
        /// 设置皮下组织颜色
        /// </summary>
        public void SetSkinSubcutisColor(Color color)
        {
            if (SkinController == null) return;
            SkinController.SubcutisColor = color;
            SkinController.ApplyAllParameters();
            OnSkinChanged?.Invoke();
        }

        /// <summary>
        /// 设置嘴唇颜色
        /// </summary>
        public void SetSkinLipColor(Color color)
        {
            if (SkinController == null) return;
            SkinController.LipColor = color;
            SkinController.ApplyAllParameters();
            OnSkinChanged?.Invoke();
        }

        /// <summary>
        /// 设置腮红强度
        /// </summary>
        public void SetSkinBlushIntensity(float intensity)
        {
            if (SkinController == null) return;
            SkinController.BlushIntensity = intensity;
            SkinController.ApplyAllParameters();
            OnSkinChanged?.Invoke();
        }

        #endregion

        #region 单参数设置接口 - 眼睛

        /// <summary>
        /// 设置虹膜颜色
        /// </summary>
        public void SetEyeIrisColor(Color color)
        {
            if (EyeController == null) return;
            EyeController.IrisColor = color;
            EyeController.ApplyAllParameters();
            OnEyeChanged?.Invoke();
        }

        /// <summary>
        /// 设置瞳孔大小
        /// </summary>
        public void SetEyePupilSize(float size)
        {
            if (EyeController == null) return;
            EyeController.PupilSize = size;
            EyeController.ApplyAllParameters();
            OnEyeChanged?.Invoke();
        }

        /// <summary>
        /// 设置眼球湿润度
        /// </summary>
        public void SetEyeWetness(float wetness)
        {
            if (EyeController == null) return;
            EyeController.Wetness = wetness;
            EyeController.ApplyAllParameters();
            OnEyeChanged?.Invoke();
        }

        /// <summary>
        /// 设置巩膜血丝强度
        /// </summary>
        public void SetEyeScleraVeinsIntensity(float intensity)
        {
            if (EyeController == null) return;
            EyeController.ScleraVeinsIntensity = intensity;
            EyeController.ApplyAllParameters();
            OnEyeChanged?.Invoke();
        }

        #endregion

        #region 单参数设置接口 - 毛发

        /// <summary>
        /// 设置发根颜色
        /// </summary>
        public void SetHairRootColor(Color color)
        {
            if (HairController == null) return;
            HairController.RootColor = color;
            HairController.ApplyAllParameters();
            OnHairChanged?.Invoke();
        }

        /// <summary>
        /// 设置发尖颜色
        /// </summary>
        public void SetHairTipColor(Color color)
        {
            if (HairController == null) return;
            HairController.TipColor = color;
            HairController.ApplyAllParameters();
            OnHairChanged?.Invoke();
        }

        /// <summary>
        /// 设置黑色素含量
        /// </summary>
        public void SetHairMelanin(float melanin)
        {
            if (HairController == null) return;
            HairController.Melanin = melanin;
            HairController.ApplyAllParameters();
            OnHairChanged?.Invoke();
        }

        /// <summary>
        /// 设置毛发粗糙度
        /// </summary>
        public void SetHairRoughness(float roughness)
        {
            if (HairController == null) return;
            HairController.Roughness = roughness;
            HairController.ApplyAllParameters();
            OnHairChanged?.Invoke();
        }

        /// <summary>
        /// 设置各向异性强度
        /// </summary>
        public void SetHairAnisotropyIntensity(float intensity)
        {
            if (HairController == null) return;
            HairController.AnisotropyIntensity = intensity;
            HairController.ApplyAllParameters();
            OnHairChanged?.Invoke();
        }

        #endregion

        #region 快速预设

        /// <summary>
        /// 应用年轻皮肤预设
        /// </summary>
        public void ApplyYoungSkinPreset()
        {
            SkinController?.SetYoungSkinPreset();
            OnSkinChanged?.Invoke();
        }

        /// <summary>
        /// 应用成熟皮肤预设
        /// </summary>
        public void ApplyMatureSkinPreset()
        {
            SkinController?.SetMatureSkinPreset();
            OnSkinChanged?.Invoke();
        }

        /// <summary>
        /// 应用油性皮肤预设
        /// </summary>
        public void ApplyOilySkinPreset()
        {
            SkinController?.SetOilySkinPreset();
            OnSkinChanged?.Invoke();
        }

        /// <summary>
        /// 应用干性皮肤预设
        /// </summary>
        public void ApplyDrySkinPreset()
        {
            SkinController?.SetDrySkinPreset();
            OnSkinChanged?.Invoke();
        }

        /// <summary>
        /// 应用棕色眼睛预设
        /// </summary>
        public void ApplyBrownEyePreset()
        {
            EyeController?.SetBrownEyePreset();
            OnEyeChanged?.Invoke();
        }

        /// <summary>
        /// 应用蓝色眼睛预设
        /// </summary>
        public void ApplyBlueEyePreset()
        {
            EyeController?.SetBlueEyePreset();
            OnEyeChanged?.Invoke();
        }

        /// <summary>
        /// 应用绿色眼睛预设
        /// </summary>
        public void ApplyGreenEyePreset()
        {
            EyeController?.SetGreenEyePreset();
            OnEyeChanged?.Invoke();
        }

        /// <summary>
        /// 应用黑发预设
        /// </summary>
        public void ApplyBlackHairPreset()
        {
            HairController?.SetBlackHairPreset();
            OnHairChanged?.Invoke();
        }

        /// <summary>
        /// 应用棕发预设
        /// </summary>
        public void ApplyBrownHairPreset()
        {
            HairController?.SetBrownHairPreset();
            OnHairChanged?.Invoke();
        }

        /// <summary>
        /// 应用金发预设
        /// </summary>
        public void ApplyBlondeHairPreset()
        {
            HairController?.SetBlondeHairPreset();
            OnHairChanged?.Invoke();
        }

        /// <summary>
        /// 应用红发预设
        /// </summary>
        public void ApplyRedHairPreset()
        {
            HairController?.SetRedHairPreset();
            OnHairChanged?.Invoke();
        }

        /// <summary>
        /// 应用白发预设
        /// </summary>
        public void ApplyWhiteHairPreset()
        {
            HairController?.SetWhiteHairPreset();
            OnHairChanged?.Invoke();
        }

        #endregion

        #region 材质刷新

        /// <summary>
        /// 刷新所有材质
        /// </summary>
        public void RefreshAllMaterials()
        {
            SkinController?.ApplyAllParameters();
            EyeController?.ApplyAllParameters();
            HairController?.ApplyAllParameters();
        }

        /// <summary>
        /// 重置为默认值
        /// </summary>
        public void ResetToDefault()
        {
            var defaultPreset = CharacterAppearancePreset.CreateDefault();
            ApplyPreset(defaultPreset);
            CurrentPreset = defaultPreset;
        }

        #endregion
    }
}
