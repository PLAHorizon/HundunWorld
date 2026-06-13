using System;
using FlaxEngine;

namespace HundunWorld.Game.Rendering.Atmospheric
{
    /// <summary>
    /// 地面阴影平面
    /// 在角色脚下绘制柔和的圆形半透明阴影，边缘自然衰减
    /// 用于武侠角色创建场景的地面阴影效果
    /// </summary>
    public class GroundShadowPlane : Script
    {
        #region 配置参数

        /// <summary>
        /// 阴影半径
        /// </summary>
        [Header("阴影设置")]
        [Tooltip("阴影半径")]
        [Range(0.1f, 5f)]
        [Serialize]
        public float ShadowRadius = 0.8f;

        /// <summary>
        /// 阴影颜色
        /// </summary>
        [Tooltip("阴影颜色（含透明度）")]
        [Serialize]
        public Color ShadowColor = new Color(0.0f, 0.0f, 0.0f, 0.5f);

        /// <summary>
        /// 阴影Y轴偏移（避免z-fighting）
        /// </summary>
        [Tooltip("阴影Y轴偏移，避免与地面穿插")]
        [Range(0f, 0.1f)]
        [Serialize]
        public float ShadowOffset = 0.01f;

        /// <summary>
        /// 跟随目标角色
        /// </summary>
        [Tooltip("阴影跟随的目标角色，为空时自动查找")]
        [Serialize]
        public Actor TargetActor;

        /// <summary>
        /// 边缘衰减指数
        /// </summary>
        [Tooltip("边缘衰减曲线指数，值越大边缘衰减越陡峭")]
        [Range(0.5f, 8f)]
        [Serialize]
        public float FalloffPower = 2.0f;

        /// <summary>
        /// 同心圆层数（用于模拟渐变衰减）
        /// </summary>
        [Tooltip("绘制同心圆的层数，越多越平滑")]
        [Range(4, 32)]
        [Serialize]
        public int RingCount = 12;

        #endregion

        #region 内部状态

        private Vector3 _shadowPosition;
        private StaticModel _shadowModel;

        #endregion

        /// <summary>
        /// 脚本启用时调用
        /// </summary>
        public override void OnEnable()
        {
            if (TargetActor == null)
            {
                FindTargetActor();
            }

            if (TargetActor != null)
            {
                _shadowPosition = TargetActor.Position;
            }
            else
            {
                _shadowPosition = Actor.Position;
            }

            CreateShadowModel();
        }

        /// <summary>
        /// 创建运行时可见的阴影模型
        /// 使用扁平化的内置圆柱模型替代 DebugDraw 渲染
        /// </summary>
        private void CreateShadowModel()
        {
            try
            {
                _shadowModel = new StaticModel();
                _shadowModel.Name = "GroundShadowVisual";

                // 设置为子Actor
                _shadowModel.Parent = Actor;

                // 加载内置圆柱模型作为阴影圆盘
                var model = Content.LoadAsync<Model>("Content/Editor/Primitives/Cylinder");
                if (model != null)
                {
                    _shadowModel.Model = model;
                }
                else
                {
                    Debug.LogWarning("[GroundShadowPlane] 无法加载内置圆柱模型，阴影可能不可见");
                }

                // 极扁缩放形成圆盘：Y极小，X/Z匹配阴影半径
                // 内置圆柱半径约50单位，缩放 = ShadowRadius / 50
                _shadowModel.LocalScale = new Vector3(
                    ShadowRadius / 50f,
                    0.001f,
                    ShadowRadius / 50f
                );
                _shadowModel.LocalPosition = new Vector3(0, ShadowOffset, 0);

                // 使用默认材质（引擎自动分配），阴影通过模型颜色和光照体现
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[GroundShadowPlane] 创建运行时阴影模型失败: {ex.Message}");
                _shadowModel = null;
            }
        }

        /// <summary>
        /// 每帧更新，同步阴影位置到目标角色
        /// </summary>
        public override void OnUpdate()
        {
            if (TargetActor != null)
            {
                _shadowPosition = TargetActor.Position;
            }
            else
            {
                _shadowPosition = Actor.Position;
            }

            // 同步运行时阴影模型位置
            if (_shadowModel != null)
            {
                _shadowModel.LocalPosition = new Vector3(0, ShadowOffset, 0);
            }
        }

        /// <summary>
        /// 脚本禁用时清理运行时阴影模型
        /// </summary>
        public override void OnDisable()
        {
            if (_shadowModel != null)
            {
                Destroy(ref _shadowModel);
            }
        }

        /// <summary>
        /// 调试绘制，渲染地面阴影（编辑器预览辅助）
        /// </summary>
        public override void OnDebugDraw()
        {
            if (TargetActor == null && Actor == null)
                return;

            DrawGroundShadow();
        }

        /// <summary>
        /// 绘制地面阴影
        /// 使用多层同心圆模拟从中心到边缘的透明度衰减
        /// </summary>
        private void DrawGroundShadow()
        {
            Vector3 center = _shadowPosition + new Vector3(0, ShadowOffset, 0);

            for (int i = 0; i < RingCount; i++)
            {
                // 从外圈到内圈绘制，外圈先画（更透明），内圈后画（更不透明）
                float t = (float)i / RingCount; // 0 = 最外圈, 1 = 最内圈
                float ringRadius = ShadowRadius * (1.0f - t);

                if (ringRadius < 0.01f)
                    continue;

                // 计算衰减：距离中心越远（t越小），alpha越低
                // 使用归一化距离计算衰减因子
                float normalizedDist = 1.0f - t; // 0=中心, 1=边缘
                float falloff = Mathf.Pow(1.0f - normalizedDist, FalloffPower);

                // 最终alpha = 基础alpha * 衰减因子
                float alpha = ShadowColor.A * falloff;

                var ringColor = new Color(ShadowColor.R, ShadowColor.G, ShadowColor.B, alpha);

                DebugDraw.DrawCircle(center, Vector3.Up, ringRadius, ringColor, 0.0f);
            }
        }

        /// <summary>
        /// 自动查找目标角色
        /// 搜索场景中带有AnimatedModel或SkinnedModel组件的Actor
        /// </summary>
        private void FindTargetActor()
        {
            // 优先从自身及子对象查找
            var animatedModel = Actor.GetChild<AnimatedModel>();
            if (animatedModel != null)
            {
                TargetActor = animatedModel;
                return;
            }

            // 从父级查找
            if (Actor.Parent != null)
            {
                animatedModel = Actor.Parent.GetChild<AnimatedModel>();
                if (animatedModel != null)
                {
                    TargetActor = animatedModel;
                    return;
                }
            }

            // 全局搜索场景
            var allAnimatedModels = Level.GetActors<AnimatedModel>();
            if (allAnimatedModels != null && allAnimatedModels.Length > 0)
            {
                TargetActor = allAnimatedModels[0];
                return;
            }

            Debug.LogWarning("[GroundShadowPlane] 未找到带有AnimatedModel的目标角色");
        }
    }
}
