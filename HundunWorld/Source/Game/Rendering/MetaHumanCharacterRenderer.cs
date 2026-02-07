using FlaxEngine;
using HundunWorld.Game.Rendering.Materials;
using HundunWorld.Game.Rendering.Lighting;
using HundunWorld.Game.Rendering.PostProcess;

namespace HundunWorld.Game.Rendering
{
    /// <summary>
    /// MetaHuman级别角色渲染管理器
    /// 整合皮肤、眼睛、毛发材质以及光照和后期处理系统
    /// </summary>
    public class MetaHumanCharacterRenderer : Script
    {
        #region 渲染质量预设

        /// <summary>
        /// 渲染质量等级
        /// </summary>
        public enum RenderQuality
        {
            /// <summary>低质量（移动端）</summary>
            Low,
            /// <summary>中等质量</summary>
            Medium,
            /// <summary>高质量</summary>
            High,
            /// <summary>超高质量（影视级）</summary>
            Ultra,
            /// <summary>自定义</summary>
            Custom
        }

        /// <summary>
        /// 角色渲染模式
        /// </summary>
        public enum CharacterRenderMode
        {
            /// <summary>游戏模式（实时，优化性能）</summary>
            Gameplay,
            /// <summary>过场动画模式（高质量）</summary>
            Cutscene,
            /// <summary>角色展示模式（最高质量）</summary>
            Showcase,
            /// <summary>照片模式</summary>
            PhotoMode
        }

        #endregion

        #region 基础设置

        /// <summary>
        /// 角色模型
        /// </summary>
        [Header("角色设置")]
        [Tooltip("角色的根Actor")]
        public Actor CharacterActor;

        /// <summary>
        /// 头部模型
        /// </summary>
        [Tooltip("角色头部模型")]
        public StaticModel HeadModel;

        /// <summary>
        /// 身体模型
        /// </summary>
        [Tooltip("角色身体模型")]
        public StaticModel BodyModel;

        /// <summary>
        /// 眼球模型
        /// </summary>
        [Tooltip("角色眼球模型")]
        public StaticModel EyeModel;

        /// <summary>
        /// 毛发模型
        /// </summary>
        [Tooltip("角色头发模型")]
        public StaticModel HairModel;

        /// <summary>
        /// 动画骨骼
        /// </summary>
        [Tooltip("角色的动画模型")]
        public AnimatedModel AnimatedCharacter;

        /// <summary>
        /// 渲染质量
        /// </summary>
        [Tooltip("当前渲染质量等级")]
        public RenderQuality Quality = RenderQuality.High;

        /// <summary>
        /// 渲染模式
        /// </summary>
        [Tooltip("当前渲染模式")]
        public CharacterRenderMode RenderMode = CharacterRenderMode.Gameplay;

        #endregion

        #region 材质控制器

        /// <summary>
        /// 皮肤材质控制器
        /// </summary>
        [Header("材质控制")]
        [Tooltip("皮肤材质控制器组件")]
        public SkinMaterialController SkinController;

        /// <summary>
        /// 眼睛材质控制器
        /// </summary>
        [Tooltip("眼睛材质控制器组件")]
        public EyeMaterialController EyeController;

        /// <summary>
        /// 毛发材质控制器
        /// </summary>
        [Tooltip("毛发材质控制器组件")]
        public HairMaterialController HairController;

        #endregion

        #region 系统引用

        /// <summary>
        /// 光照系统
        /// </summary>
        [Header("渲染系统")]
        [Tooltip("角色光照系统")]
        public CharacterLightingSystem LightingSystem;

        /// <summary>
        /// 后期处理系统
        /// </summary>
        [Tooltip("后期处理系统")]
        public CinematicPostProcessSystem PostProcessSystem;

        /// <summary>
        /// 渲染系统（基础）
        /// </summary>
        [Tooltip("基础渲染系统")]
        public CharacterRenderingSystem RenderingSystem;

        #endregion

        #region LOD设置

        /// <summary>
        /// 启用LOD
        /// </summary>
        [Header("LOD设置")]
        [Tooltip("是否启用LOD切换")]
        public bool EnableLOD = true;

        /// <summary>
        /// LOD偏移
        /// </summary>
        [Range(-2f, 2f)]
        [Tooltip("LOD切换距离的偏移")]
        public float LODBias = 0f;

        /// <summary>
        /// 最高LOD距离
        /// </summary>
        [Range(0f, 50f)]
        [Tooltip("使用最高质量LOD的最大距离")]
        public float HighLODDistance = 5f;

        /// <summary>
        /// 中等LOD距离
        /// </summary>
        [Range(0f, 100f)]
        [Tooltip("使用中等质量LOD的最大距离")]
        public float MediumLODDistance = 15f;

        /// <summary>
        /// 低LOD距离
        /// </summary>
        [Range(0f, 200f)]
        [Tooltip("使用低质量LOD的最大距离")]
        public float LowLODDistance = 30f;

        #endregion

        #region 性能设置

        /// <summary>
        /// 启用SSS
        /// </summary>
        [Header("性能优化")]
        [Tooltip("是否启用次表面散射")]
        public bool EnableSSS = true;

        /// <summary>
        /// 阴影质量
        /// </summary>
        [Tooltip("角色阴影质量")]
        public ShadowQualityLevel ShadowQuality = ShadowQualityLevel.High;

        /// <summary>
        /// 反射质量
        /// </summary>
        [Tooltip("角色反射质量")]
        public ReflectionQualityLevel ReflectionQuality = ReflectionQualityLevel.High;

        /// <summary>
        /// 毛发渲染质量
        /// </summary>
        [Tooltip("毛发渲染质量")]
        public HairQualityLevel HairQuality = HairQualityLevel.High;

        /// <summary>
        /// 目标帧率
        /// </summary>
        [Range(30, 144)]
        [Tooltip("目标帧率，用于动态质量调节")]
        public int TargetFrameRate = 60;

        /// <summary>
        /// 自适应质量
        /// </summary>
        [Tooltip("是否根据帧率自动调节质量")]
        public bool AdaptiveQuality = false;

        #endregion

        private Camera _mainCamera;
        private float _distanceToCamera;
        private int _currentLOD = 0;

        public override void OnStart()
        {
            _mainCamera = Camera.MainCamera;
            
            InitializeMaterialControllers();
            ApplyQualitySettings();
            ApplyRenderMode();
        }

        public override void OnUpdate()
        {
            UpdateDistanceToCamera();
            
            if (EnableLOD)
            {
                UpdateLOD();
            }

            if (AdaptiveQuality)
            {
                AdaptQualityToPerformance();
            }
        }

        /// <summary>
        /// 初始化材质控制器
        /// </summary>
        private void InitializeMaterialControllers()
        {
            // 如果没有指定控制器，尝试在子对象中查找
            if (SkinController == null)
            {
                SkinController = Actor.GetScript<SkinMaterialController>();
            }
            if (EyeController == null)
            {
                EyeController = Actor.GetScript<EyeMaterialController>();
            }
            if (HairController == null)
            {
                HairController = Actor.GetScript<HairMaterialController>();
            }
        }

        /// <summary>
        /// 更新到摄像机的距离
        /// </summary>
        private void UpdateDistanceToCamera()
        {
            if (_mainCamera == null || CharacterActor == null) return;
            
            _distanceToCamera = Vector3.Distance(
                _mainCamera.Position, 
                CharacterActor.Position);
        }

        /// <summary>
        /// 更新LOD级别
        /// </summary>
        private void UpdateLOD()
        {
            float adjustedDistance = _distanceToCamera + LODBias;
            int newLOD;

            if (adjustedDistance < HighLODDistance)
            {
                newLOD = 0; // 最高质量
            }
            else if (adjustedDistance < MediumLODDistance)
            {
                newLOD = 1; // 中等质量
            }
            else if (adjustedDistance < LowLODDistance)
            {
                newLOD = 2; // 低质量
            }
            else
            {
                newLOD = 3; // 最低质量/剔除
            }

            if (newLOD != _currentLOD)
            {
                _currentLOD = newLOD;
                ApplyLODSettings(newLOD);
            }
        }

        /// <summary>
        /// 应用LOD设置
        /// </summary>
        private void ApplyLODSettings(int lodLevel)
        {
            switch (lodLevel)
            {
                case 0: // 最高质量
                    EnableSSS = true;
                    if (SkinController != null)
                    {
                        SkinController.PoreNormalStrength = 0.5f;
                        SkinController.MicroDetailStrength = 0.3f;
                    }
                    break;

                case 1: // 中等质量
                    EnableSSS = true;
                    if (SkinController != null)
                    {
                        SkinController.PoreNormalStrength = 0.3f;
                        SkinController.MicroDetailStrength = 0.1f;
                    }
                    break;

                case 2: // 低质量
                    EnableSSS = false;
                    if (SkinController != null)
                    {
                        SkinController.PoreNormalStrength = 0f;
                        SkinController.MicroDetailStrength = 0f;
                    }
                    break;

                case 3: // 最低质量
                    EnableSSS = false;
                    // 可选：隐藏细节模型
                    break;
            }
        }

        /// <summary>
        /// 应用质量设置
        /// </summary>
        public void ApplyQualitySettings()
        {
            switch (Quality)
            {
                case RenderQuality.Low:
                    SetLowQuality();
                    break;
                case RenderQuality.Medium:
                    SetMediumQuality();
                    break;
                case RenderQuality.High:
                    SetHighQuality();
                    break;
                case RenderQuality.Ultra:
                    SetUltraQuality();
                    break;
            }
        }

        /// <summary>
        /// 设置低质量
        /// </summary>
        private void SetLowQuality()
        {
            EnableSSS = false;
            ShadowQuality = ShadowQualityLevel.Low;
            ReflectionQuality = ReflectionQualityLevel.Low;
            HairQuality = HairQualityLevel.Low;

            if (SkinController != null)
            {
                SkinController.PoreNormalStrength = 0f;
                SkinController.MicroDetailStrength = 0f;
                SkinController.SSSIntensity = 0f;
            }

            if (EyeController != null)
            {
                EyeController.EnablePupilLightReaction = false;
                EyeController.CausticIntensity = 0f;
            }

            if (HairController != null)
            {
                HairController.EnableMultipleScattering = false;
                HairController.EnableAnisotropy = false;
            }

            if (PostProcessSystem != null)
            {
                PostProcessSystem.EnableDOF = false;
                PostProcessSystem.EnableSSAO = false;
                PostProcessSystem.EnableSSR = false;
            }
        }

        /// <summary>
        /// 设置中等质量
        /// </summary>
        private void SetMediumQuality()
        {
            EnableSSS = true;
            ShadowQuality = ShadowQualityLevel.Medium;
            ReflectionQuality = ReflectionQualityLevel.Medium;
            HairQuality = HairQualityLevel.Medium;

            if (SkinController != null)
            {
                SkinController.PoreNormalStrength = 0.3f;
                SkinController.MicroDetailStrength = 0.1f;
                SkinController.SSSIntensity = 0.7f;
            }

            if (EyeController != null)
            {
                EyeController.EnablePupilLightReaction = true;
                EyeController.CausticIntensity = 0.1f;
            }

            if (HairController != null)
            {
                HairController.EnableMultipleScattering = false;
                HairController.EnableAnisotropy = true;
            }

            if (PostProcessSystem != null)
            {
                PostProcessSystem.EnableDOF = false;
                PostProcessSystem.EnableSSAO = true;
                PostProcessSystem.SSAOIntensity = 0.5f;
                PostProcessSystem.EnableSSR = false;
            }
        }

        /// <summary>
        /// 设置高质量
        /// </summary>
        private void SetHighQuality()
        {
            EnableSSS = true;
            ShadowQuality = ShadowQualityLevel.High;
            ReflectionQuality = ReflectionQualityLevel.High;
            HairQuality = HairQualityLevel.High;

            if (SkinController != null)
            {
                SkinController.PoreNormalStrength = 0.5f;
                SkinController.MicroDetailStrength = 0.3f;
                SkinController.SSSIntensity = 1.0f;
            }

            if (EyeController != null)
            {
                EyeController.EnablePupilLightReaction = true;
                EyeController.CausticIntensity = 0.3f;
            }

            if (HairController != null)
            {
                HairController.EnableMultipleScattering = true;
                HairController.EnableAnisotropy = true;
            }

            if (PostProcessSystem != null)
            {
                PostProcessSystem.EnableDOF = true;
                PostProcessSystem.EnableSSAO = true;
                PostProcessSystem.SSAOIntensity = 1.0f;
                PostProcessSystem.EnableSSR = true;
            }
        }

        /// <summary>
        /// 设置超高质量
        /// </summary>
        private void SetUltraQuality()
        {
            EnableSSS = true;
            ShadowQuality = ShadowQualityLevel.Ultra;
            ReflectionQuality = ReflectionQualityLevel.Ultra;
            HairQuality = HairQualityLevel.Ultra;

            if (SkinController != null)
            {
                SkinController.PoreNormalStrength = 0.7f;
                SkinController.MicroDetailStrength = 0.5f;
                SkinController.SSSIntensity = 1.2f;
                SkinController.WrinkleDepth = 0.6f;
            }

            if (EyeController != null)
            {
                EyeController.EnablePupilLightReaction = true;
                EyeController.CausticIntensity = 0.5f;
                EyeController.DualSpecular = true;
            }

            if (HairController != null)
            {
                HairController.EnableMultipleScattering = true;
                HairController.EnableAnisotropy = true;
                HairController.ScatterIntensity = 0.7f;
            }

            if (PostProcessSystem != null)
            {
                PostProcessSystem.EnableDOF = true;
                PostProcessSystem.EnableSSAO = true;
                PostProcessSystem.SSAOIntensity = 1.5f;
                PostProcessSystem.EnableSSR = true;
                PostProcessSystem.SSRIntensity = 1.0f;
                PostProcessSystem.EnableChromaticAberration = true;
                PostProcessSystem.EnableGrain = true;
            }
        }

        /// <summary>
        /// 应用渲染模式
        /// </summary>
        public void ApplyRenderMode()
        {
            switch (RenderMode)
            {
                case CharacterRenderMode.Gameplay:
                    SetGameplayMode();
                    break;
                case CharacterRenderMode.Cutscene:
                    SetCutsceneMode();
                    break;
                case CharacterRenderMode.Showcase:
                    SetShowcaseMode();
                    break;
                case CharacterRenderMode.PhotoMode:
                    SetPhotoMode();
                    break;
            }
        }

        /// <summary>
        /// 设置游戏模式
        /// </summary>
        private void SetGameplayMode()
        {
            EnableLOD = true;
            AdaptiveQuality = true;
            
            if (PostProcessSystem != null)
            {
                PostProcessSystem.EnableMotionBlur = false;
            }
        }

        /// <summary>
        /// 设置过场动画模式
        /// </summary>
        private void SetCutsceneMode()
        {
            EnableLOD = false;
            AdaptiveQuality = false;
            _currentLOD = 0;
            ApplyLODSettings(0);
            
            if (LightingSystem != null)
            {
                LightingSystem.EnableEyeLight = true;
            }

            if (PostProcessSystem != null)
            {
                PostProcessSystem.SetCinematicStyle();
            }
        }

        /// <summary>
        /// 设置展示模式
        /// </summary>
        private void SetShowcaseMode()
        {
            EnableLOD = false;
            AdaptiveQuality = false;
            Quality = RenderQuality.Ultra;
            ApplyQualitySettings();
            
            if (LightingSystem != null)
            {
                LightingSystem.CurrentScheme = CharacterLightingSystem.LightingScheme.ThreePoint;
                LightingSystem.EnableEyeLight = true;
                LightingSystem.ApplyLightingScheme();
            }
        }

        /// <summary>
        /// 设置照片模式
        /// </summary>
        private void SetPhotoMode()
        {
            EnableLOD = false;
            AdaptiveQuality = false;
            Quality = RenderQuality.Ultra;
            ApplyQualitySettings();
            
            // 启用所有视觉效果
            if (PostProcessSystem != null)
            {
                PostProcessSystem.EnableDOF = true;
                PostProcessSystem.EnableBloom = true;
                PostProcessSystem.EnableVignette = true;
                PostProcessSystem.EnableChromaticAberration = true;
            }
        }

        /// <summary>
        /// 根据性能自适应质量
        /// </summary>
        private void AdaptQualityToPerformance()
        {
            float currentFPS = 1.0f / Time.DeltaTime;
            
            if (currentFPS < TargetFrameRate * 0.8f)
            {
                // 帧率过低，降低质量
                if (Quality > RenderQuality.Low)
                {
                    Quality = (RenderQuality)((int)Quality - 1);
                    ApplyQualitySettings();
                }
            }
            else if (currentFPS > TargetFrameRate * 1.2f)
            {
                // 帧率富余，可以提高质量
                if (Quality < RenderQuality.Ultra)
                {
                    Quality = (RenderQuality)((int)Quality + 1);
                    ApplyQualitySettings();
                }
            }
        }

        /// <summary>
        /// 设置角色皮肤类型
        /// </summary>
        public void SetSkinType(SkinType type)
        {
            if (SkinController == null) return;

            switch (type)
            {
                case SkinType.Young:
                    SkinController.SetYoungSkinPreset();
                    break;
                case SkinType.Mature:
                    SkinController.SetMatureSkinPreset();
                    break;
                case SkinType.Oily:
                    SkinController.SetOilySkinPreset();
                    break;
                case SkinType.Dry:
                    SkinController.SetDrySkinPreset();
                    break;
            }
        }

        /// <summary>
        /// 设置眼睛颜色
        /// </summary>
        public void SetEyeColor(EyeColorType type)
        {
            if (EyeController == null) return;

            switch (type)
            {
                case EyeColorType.Brown:
                    EyeController.SetBrownEyePreset();
                    break;
                case EyeColorType.Blue:
                    EyeController.SetBlueEyePreset();
                    break;
                case EyeColorType.Green:
                    EyeController.SetGreenEyePreset();
                    break;
            }
        }

        /// <summary>
        /// 设置头发颜色
        /// </summary>
        public void SetHairColor(HairColorType type)
        {
            if (HairController == null) return;

            switch (type)
            {
                case HairColorType.Black:
                    HairController.SetBlackHairPreset();
                    break;
                case HairColorType.Brown:
                    HairController.SetBrownHairPreset();
                    break;
                case HairColorType.Blonde:
                    HairController.SetBlondeHairPreset();
                    break;
                case HairColorType.Red:
                    HairController.SetRedHairPreset();
                    break;
                case HairColorType.White:
                    HairController.SetWhiteHairPreset();
                    break;
            }
        }
    }

    #region 辅助枚举

    /// <summary>
    /// 阴影质量等级
    /// </summary>
    public enum ShadowQualityLevel
    {
        Low,
        Medium,
        High,
        Ultra
    }

    /// <summary>
    /// 反射质量等级
    /// </summary>
    public enum ReflectionQualityLevel
    {
        Low,
        Medium,
        High,
        Ultra
    }

    /// <summary>
    /// 毛发质量等级
    /// </summary>
    public enum HairQualityLevel
    {
        Low,
        Medium,
        High,
        Ultra
    }

    /// <summary>
    /// 皮肤类型
    /// </summary>
    public enum SkinType
    {
        Young,
        Mature,
        Oily,
        Dry
    }

    /// <summary>
    /// 眼睛颜色类型
    /// </summary>
    public enum EyeColorType
    {
        Brown,
        Blue,
        Green
    }

    /// <summary>
    /// 头发颜色类型
    /// </summary>
    public enum HairColorType
    {
        Black,
        Brown,
        Blonde,
        Red,
        White
    }

    #endregion
}
