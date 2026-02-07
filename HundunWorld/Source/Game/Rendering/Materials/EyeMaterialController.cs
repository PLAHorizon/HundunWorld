using FlaxEngine;

namespace HundunWorld.Game.Rendering.Materials
{
    /// <summary>
    /// 眼睛材质控制器 - 管理高质量眼球渲染效果
    /// 实现包括虹膜、瞳孔、角膜、巩膜等真实眼球效果
    /// </summary>
    public class EyeMaterialController : Script
    {
        #region 材质资源

        /// <summary>
        /// 眼球材质实例
        /// </summary>
        [Header("材质资源")]
        [Tooltip("眼球材质实例")]
        public MaterialInstance EyeMaterialInstance;

        /// <summary>
        /// 虹膜纹理
        /// </summary>
        [Tooltip("虹膜纹理贴图")]
        public Texture IrisTexture;

        /// <summary>
        /// 虹膜法线贴图
        /// </summary>
        [Tooltip("虹膜法线贴图")]
        public Texture IrisNormalMap;

        /// <summary>
        /// 巩膜纹理
        /// </summary>
        [Tooltip("巩膜（眼白）纹理")]
        public Texture ScleraTexture;

        /// <summary>
        /// 巩膜血丝贴图
        /// </summary>
        [Tooltip("巩膜血丝纹理")]
        public Texture ScleraVeinsTexture;

        /// <summary>
        /// 泪腺/湿润贴图
        /// </summary>
        [Tooltip("眼球湿润区域贴图")]
        public Texture WetnessMap;

        #endregion

        #region 虹膜参数

        /// <summary>
        /// 虹膜颜色
        /// </summary>
        [Header("虹膜设置")]
        [Tooltip("虹膜基础颜色")]
        public Color IrisColor = new Color(0.4f, 0.3f, 0.2f, 1.0f);

        /// <summary>
        /// 虹膜外缘颜色
        /// </summary>
        [Tooltip("虹膜外圈颜色")]
        public Color IrisLimbusColor = new Color(0.15f, 0.1f, 0.05f, 1.0f);

        /// <summary>
        /// 虹膜亮度
        /// </summary>
        [Range(0f, 2f)]
        [Tooltip("虹膜的亮度")]
        public float IrisBrightness = 1.0f;

        /// <summary>
        /// 虹膜饱和度
        /// </summary>
        [Range(0f, 2f)]
        [Tooltip("虹膜颜色饱和度")]
        public float IrisSaturation = 1.0f;

        /// <summary>
        /// 虹膜细节强度
        /// </summary>
        [Range(0f, 1f)]
        [Tooltip("虹膜纹理细节强度")]
        public float IrisDetailIntensity = 0.8f;

        /// <summary>
        /// 虹膜纤维密度
        /// </summary>
        [Range(0f, 1f)]
        [Tooltip("虹膜纤维纹理的密度")]
        public float IrisFiberDensity = 0.7f;

        /// <summary>
        /// 虹膜凹凸强度
        /// </summary>
        [Range(0f, 1f)]
        [Tooltip("虹膜法线的强度")]
        public float IrisNormalStrength = 0.5f;

        #endregion

        #region 瞳孔参数

        /// <summary>
        /// 瞳孔颜色
        /// </summary>
        [Header("瞳孔设置")]
        [Tooltip("瞳孔颜色")]
        public Color PupilColor = Color.Black;

        /// <summary>
        /// 瞳孔大小
        /// </summary>
        [Range(0.1f, 0.8f)]
        [Tooltip("瞳孔相对虹膜的大小")]
        public float PupilSize = 0.35f;

        /// <summary>
        /// 瞳孔形状（用于猫眼等特殊效果）
        /// </summary>
        [Range(0f, 1f)]
        [Tooltip("0=圆形，1=竖缝形")]
        public float PupilSlitness = 0f;

        /// <summary>
        /// 瞳孔对光反应（动态）
        /// </summary>
        [Tooltip("是否启用瞳孔对光线的动态反应")]
        public bool EnablePupilLightReaction = true;

        /// <summary>
        /// 瞳孔反应速度
        /// </summary>
        [Range(0.1f, 2f)]
        [Tooltip("瞳孔对光线变化的反应速度")]
        public float PupilReactionSpeed = 0.5f;

        #endregion

        #region 角膜参数

        /// <summary>
        /// 角膜折射率
        /// </summary>
        [Header("角膜设置")]
        [Range(1f, 2f)]
        [Tooltip("角膜的折射率")]
        public float CorneaIOR = 1.376f;

        /// <summary>
        /// 角膜曲率
        /// </summary>
        [Range(0f, 1f)]
        [Tooltip("角膜的曲率强度")]
        public float CorneaCurvature = 0.5f;

        /// <summary>
        /// 角膜高光强度
        /// </summary>
        [Range(0f, 1f)]
        [Tooltip("角膜表面的高光强度")]
        public float CorneaSpecular = 0.9f;

        /// <summary>
        /// 角膜粗糙度
        /// </summary>
        [Range(0f, 0.3f)]
        [Tooltip("角膜表面的粗糙度")]
        public float CorneaRoughness = 0.05f;

        /// <summary>
        /// 角膜折射深度
        /// </summary>
        [Range(0f, 0.5f)]
        [Tooltip("虹膜相对角膜表面的深度")]
        public float CorneaDepth = 0.1f;

        #endregion

        #region 巩膜参数

        /// <summary>
        /// 巩膜颜色
        /// </summary>
        [Header("巩膜（眼白）设置")]
        [Tooltip("巩膜基础颜色")]
        public Color ScleraColor = new Color(0.98f, 0.97f, 0.95f, 1.0f);

        /// <summary>
        /// 巩膜血丝强度
        /// </summary>
        [Range(0f, 1f)]
        [Tooltip("巩膜血丝的可见程度")]
        public float ScleraVeinsIntensity = 0.3f;

        /// <summary>
        /// 巩膜血丝颜色
        /// </summary>
        [Tooltip("巩膜血丝的颜色")]
        public Color ScleraVeinsColor = new Color(0.8f, 0.2f, 0.15f, 1.0f);

        /// <summary>
        /// 巩膜SSS颜色
        /// </summary>
        [Tooltip("巩膜的次表面散射颜色")]
        public Color ScleraSSSColor = new Color(0.9f, 0.3f, 0.2f, 1.0f);

        /// <summary>
        /// 巩膜SSS强度
        /// </summary>
        [Range(0f, 1f)]
        [Tooltip("巩膜的次表面散射强度")]
        public float ScleraSSSIntensity = 0.5f;

        /// <summary>
        /// 巩膜粗糙度
        /// </summary>
        [Range(0f, 0.5f)]
        [Tooltip("巩膜表面的粗糙度")]
        public float ScleraRoughness = 0.15f;

        #endregion

        #region 湿润效果

        /// <summary>
        /// 湿润度
        /// </summary>
        [Header("湿润效果")]
        [Range(0f, 1f)]
        [Tooltip("眼球整体湿润程度")]
        public float Wetness = 0.8f;

        /// <summary>
        /// 泪线强度
        /// </summary>
        [Range(0f, 1f)]
        [Tooltip("下眼睑泪线的可见程度")]
        public float TearLineIntensity = 0.3f;

        /// <summary>
        /// 环境反射强度
        /// </summary>
        [Range(0f, 1f)]
        [Tooltip("眼球表面的环境反射强度")]
        public float EnvironmentReflection = 0.7f;

        #endregion

        #region 焦散和光泽

        /// <summary>
        /// 焦散强度
        /// </summary>
        [Header("光泽效果")]
        [Range(0f, 1f)]
        [Tooltip("虹膜上的光线焦散效果")]
        public float CausticIntensity = 0.3f;

        /// <summary>
        /// 光斑大小
        /// </summary>
        [Range(0.01f, 0.2f)]
        [Tooltip("角膜高光光斑的大小")]
        public float SpecularSize = 0.05f;

        /// <summary>
        /// 双光斑
        /// </summary>
        [Tooltip("是否显示双光斑效果（模拟室内照明）")]
        public bool DualSpecular = true;

        #endregion

        private float _currentPupilSize;
        private float _targetPupilSize;

        public override void OnStart()
        {
            _currentPupilSize = PupilSize;
            _targetPupilSize = PupilSize;
            ApplyAllParameters();
        }

        public override void OnUpdate()
        {
            if (EnablePupilLightReaction)
            {
                UpdatePupilSize();
            }

            if (Engine.IsEditor)
            {
                ApplyAllParameters();
            }
        }

        /// <summary>
        /// 根据光照条件更新瞳孔大小
        /// </summary>
        private void UpdatePupilSize()
        {
            // 获取眼球位置的光照强度
            float lightIntensity = CalculateLocalLightIntensity();
            
            // 光线越强，瞳孔越小
            float minPupil = 0.15f;
            float maxPupil = 0.7f;
            _targetPupilSize = Mathf.Lerp(maxPupil, minPupil, lightIntensity);
            
            // 平滑过渡
            _currentPupilSize = Mathf.Lerp(_currentPupilSize, _targetPupilSize, 
                Time.DeltaTime * PupilReactionSpeed);
            
            if (EyeMaterialInstance != null)
            {
                EyeMaterialInstance.SetParameterValue("PupilSize", _currentPupilSize);
            }
        }

        /// <summary>
        /// 计算局部光照强度
        /// </summary>
        private float CalculateLocalLightIntensity()
        {
            // 简化的光照计算，实际项目中可以使用更精确的方法
            var lights = Level.GetActors<Light>();
            float totalIntensity = 0f;
            var eyePos = Actor.Position;

            foreach (var light in lights)
            {
                if (light is DirectionalLight dirLight)
                {
                    totalIntensity += dirLight.Brightness * 0.5f;
                }
                else if (light is PointLight pointLight)
                {
                    float dist = Vector3.Distance(eyePos, pointLight.Position);
                    float attenuation = 1f / (1f + dist * dist * 0.01f);
                    totalIntensity += pointLight.Brightness * attenuation;
                }
            }

            return Mathf.Saturate(totalIntensity / 5f);
        }

        /// <summary>
        /// 应用所有参数到材质
        /// </summary>
        public void ApplyAllParameters()
        {
            if (EyeMaterialInstance == null) return;

            ApplyTextures();
            ApplyIrisParameters();
            ApplyPupilParameters();
            ApplyCorneaParameters();
            ApplyScleraParameters();
            ApplyWetnessParameters();
            ApplyCausticParameters();
        }

        private void ApplyTextures()
        {
            if (IrisTexture != null)
                EyeMaterialInstance.SetParameterValue("IrisTexture", IrisTexture);
            if (IrisNormalMap != null)
                EyeMaterialInstance.SetParameterValue("IrisNormalMap", IrisNormalMap);
            if (ScleraTexture != null)
                EyeMaterialInstance.SetParameterValue("ScleraTexture", ScleraTexture);
            if (ScleraVeinsTexture != null)
                EyeMaterialInstance.SetParameterValue("ScleraVeinsTexture", ScleraVeinsTexture);
            if (WetnessMap != null)
                EyeMaterialInstance.SetParameterValue("WetnessMap", WetnessMap);
        }

        private void ApplyIrisParameters()
        {
            EyeMaterialInstance.SetParameterValue("IrisColor", IrisColor);
            EyeMaterialInstance.SetParameterValue("IrisLimbusColor", IrisLimbusColor);
            EyeMaterialInstance.SetParameterValue("IrisBrightness", IrisBrightness);
            EyeMaterialInstance.SetParameterValue("IrisSaturation", IrisSaturation);
            EyeMaterialInstance.SetParameterValue("IrisDetailIntensity", IrisDetailIntensity);
            EyeMaterialInstance.SetParameterValue("IrisFiberDensity", IrisFiberDensity);
            EyeMaterialInstance.SetParameterValue("IrisNormalStrength", IrisNormalStrength);
        }

        private void ApplyPupilParameters()
        {
            EyeMaterialInstance.SetParameterValue("PupilColor", PupilColor);
            EyeMaterialInstance.SetParameterValue("PupilSize", EnablePupilLightReaction ? _currentPupilSize : PupilSize);
            EyeMaterialInstance.SetParameterValue("PupilSlitness", PupilSlitness);
        }

        private void ApplyCorneaParameters()
        {
            EyeMaterialInstance.SetParameterValue("CorneaIOR", CorneaIOR);
            EyeMaterialInstance.SetParameterValue("CorneaCurvature", CorneaCurvature);
            EyeMaterialInstance.SetParameterValue("CorneaSpecular", CorneaSpecular);
            EyeMaterialInstance.SetParameterValue("CorneaRoughness", CorneaRoughness);
            EyeMaterialInstance.SetParameterValue("CorneaDepth", CorneaDepth);
        }

        private void ApplyScleraParameters()
        {
            EyeMaterialInstance.SetParameterValue("ScleraColor", ScleraColor);
            EyeMaterialInstance.SetParameterValue("ScleraVeinsIntensity", ScleraVeinsIntensity);
            EyeMaterialInstance.SetParameterValue("ScleraVeinsColor", ScleraVeinsColor);
            EyeMaterialInstance.SetParameterValue("ScleraSSSColor", ScleraSSSColor);
            EyeMaterialInstance.SetParameterValue("ScleraSSSIntensity", ScleraSSSIntensity);
            EyeMaterialInstance.SetParameterValue("ScleraRoughness", ScleraRoughness);
        }

        private void ApplyWetnessParameters()
        {
            EyeMaterialInstance.SetParameterValue("Wetness", Wetness);
            EyeMaterialInstance.SetParameterValue("TearLineIntensity", TearLineIntensity);
            EyeMaterialInstance.SetParameterValue("EnvironmentReflection", EnvironmentReflection);
        }

        private void ApplyCausticParameters()
        {
            EyeMaterialInstance.SetParameterValue("CausticIntensity", CausticIntensity);
            EyeMaterialInstance.SetParameterValue("SpecularSize", SpecularSize);
            EyeMaterialInstance.SetParameterValue("DualSpecular", DualSpecular ? 1f : 0f);
        }

        /// <summary>
        /// 设置棕色眼睛预设
        /// </summary>
        public void SetBrownEyePreset()
        {
            IrisColor = new Color(0.4f, 0.25f, 0.1f, 1.0f);
            IrisLimbusColor = new Color(0.15f, 0.08f, 0.03f, 1.0f);
            ApplyAllParameters();
        }

        /// <summary>
        /// 设置蓝色眼睛预设
        /// </summary>
        public void SetBlueEyePreset()
        {
            IrisColor = new Color(0.2f, 0.4f, 0.7f, 1.0f);
            IrisLimbusColor = new Color(0.1f, 0.15f, 0.3f, 1.0f);
            ApplyAllParameters();
        }

        /// <summary>
        /// 设置绿色眼睛预设
        /// </summary>
        public void SetGreenEyePreset()
        {
            IrisColor = new Color(0.3f, 0.5f, 0.3f, 1.0f);
            IrisLimbusColor = new Color(0.1f, 0.2f, 0.1f, 1.0f);
            ApplyAllParameters();
        }

        /// <summary>
        /// 设置疲劳眼睛状态
        /// </summary>
        public void SetTiredState()
        {
            ScleraVeinsIntensity = 0.6f;
            Wetness = 0.5f;
            PupilSize = 0.5f;
            ApplyAllParameters();
        }

        /// <summary>
        /// 设置流泪状态
        /// </summary>
        public void SetCryingState()
        {
            Wetness = 1.0f;
            TearLineIntensity = 0.8f;
            ScleraVeinsIntensity = 0.5f;
            ApplyAllParameters();
        }
    }
}
