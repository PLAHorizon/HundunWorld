using System;
using FlaxEngine;

namespace HundunWorld.Game.Rendering.Atmospheric
{
    /// <summary>
    /// 竹叶/羽毛飘落粒子效果
    /// 为武侠角色创建场景提供氛围感的飘落粒子效果
    /// 使用场景 Actor（StaticModel 平面）渲染，可在游戏运行时可见
    /// </summary>
    public class AtmosphericParticleEffect : Script
    {
        [Header("粒子配置")]
        [Tooltip("粒子数量（最大100）")]
        [Limit(1, 100)]
        public int ParticleCount = 30;

        [Tooltip("下落速度")]
        public float FallSpeed = 0.3f;

        [Tooltip("水平摆动幅度")]
        public float SwayAmplitude = 0.5f;

        [Tooltip("风向")]
        public Vector3 WindDirection = new Vector3(0.1f, 0, 0);

        [Tooltip("粒子颜色 - 竹叶绿")]
        public Color ParticleColor = new Color(0.55f, 0.72f, 0.45f, 0.55f);

        [Tooltip("粒子辅助色 - 淡金色")]
        public Color ParticleColorB = new Color(0.82f, 0.72f, 0.42f, 0.45f);

        [Tooltip("粒子大小")]
        [Limit(0.01f, 0.5f)]
        public float ParticleSize = 0.05f;

        [Tooltip("发射半径")]
        public float EmissionRadius = 3.0f;

        [Tooltip("发射高度")]
        public float EmissionHeight = 5.0f;

        [Tooltip("叶片长度比例")]
        public float LeafLengthScale = 3.0f;

        [Tooltip("叶片宽度比例")]
        public float LeafWidthScale = 0.8f;

        private struct Particle
        {
            public Vector3 Position;
            public Vector3 Velocity;
            public float Size;
            public float Rotation;
            public float RotationX;  // X轴旋转（翻滚）
            public float Alpha;
            public float SwayPhase;
            public float SwaySpeed;
            public bool UseColorB;   // 使用辅助色
        }

        private Particle[] _particles;
        private Actor[] _particleActors;
        private StaticModel[] _particleModels;
        private bool _isInitialized = false;
        private bool _actorsCreated = false;
        private Random _random;

        /// <summary>
        /// 脚本启用时初始化粒子
        /// </summary>
        public override void OnEnable()
        {
            _random = new Random();
            InitializeParticles();
            CreateParticleActors();
            _isInitialized = true;
        }

        /// <summary>
        /// 脚本禁用时清理
        /// </summary>
        public override void OnDisable()
        {
            CleanupParticleActors();
            _isInitialized = false;
            _particles = null;
        }

        /// <summary>
        /// 创建粒子场景 Actor
        /// </summary>
        private void CreateParticleActors()
        {
            if (_particles == null) return;

            int count = _particles.Length;
            _particleActors = new Actor[count];
            _particleModels = new StaticModel[count];

            var scene = Actor?.Scene;

            for (int i = 0; i < count; i++)
            {
                try
                {
                    // 创建叶片 Actor（使用细长 Box 模拟竹叶形状）
                    var leafActor = new EmptyActor();
                    leafActor.Name = $"LeafParticle_{i}";

                    // 创建 StaticModel 组件
                    var model = leafActor.AddChild<StaticModel>();
                    model.Name = $"LeafModel_{i}";

                    // 设置叶片比例：长而窄
                    float leafLen = _particles[i].Size * LeafLengthScale;
                    float leafWid = _particles[i].Size * LeafWidthScale;
                    float leafThk = _particles[i].Size * 0.15f;
                    model.LocalScale = new Vector3(leafLen, leafThk, leafWid);

                    // 使用默认材质（引擎内置），颜色通过 DebugDraw 叠加实现
                    // Flax 不支持运行时编程创建 Material，使用默认材质即可

                    // 添加到场景
                    if (scene != null)
                    {
                        Level.SpawnActor(leafActor, scene);
                    }
                    else
                    {
                        Level.SpawnActor(leafActor);
                    }

                    // 设置初始位置
                    Vector3 worldPos = _particles[i].Position + Transform.Translation;
                    leafActor.Position = worldPos;
                    leafActor.Orientation = Quaternion.Euler(
                        _random.NextSingle() * 30f - 15f,
                        _particles[i].Rotation * Mathf.RadiansToDegrees,
                        _particles[i].RotationX * Mathf.RadiansToDegrees
                    );

                    _particleActors[i] = leafActor;
                    _particleModels[i] = model;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[AtmosphericParticleEffect] 创建粒子Actor[{i}]失败: {ex.Message}");
                }
            }

            _actorsCreated = true;
            Debug.Log($"[AtmosphericParticleEffect] 粒子Actor创建完成，数量: {count}");
        }

        /// <summary>
        /// 清理粒子 Actor
        /// </summary>
        private void CleanupParticleActors()
        {
            if (_particleActors != null)
            {
                for (int i = 0; i < _particleActors.Length; i++)
                {
                    if (_particleActors[i] != null)
                    {
                        Actor.Destroy(_particleActors[i]);
                        _particleActors[i] = null;
                    }
                }
                _particleActors = null;
                _particleModels = null;
            }

            _actorsCreated = false;
        }

        /// <summary>
        /// 初始化粒子数据
        /// </summary>
        private void InitializeParticles()
        {
            int count = Mathf.Clamp(ParticleCount, 1, 100);
            _particles = new Particle[count];

            for (int i = 0; i < count; i++)
            {
                _particles[i] = CreateRandomParticle(randomizeHeight: true);
            }
        }

        /// <summary>
        /// 创建随机粒子
        /// </summary>
        private Particle CreateRandomParticle(bool randomizeHeight = false)
        {
            var particle = new Particle();

            // 在圆柱形区域内随机分布
            float angle = _random.NextSingle() * Mathf.TwoPi;
            float radius = Mathf.Sqrt(_random.NextSingle()) * EmissionRadius;

            particle.Position = new Vector3(
                Mathf.Cos(angle) * radius,
                randomizeHeight
                    ? _random.NextSingle() * EmissionHeight
                    : EmissionHeight + _random.NextSingle() * 0.5f,
                Mathf.Sin(angle) * radius
            );

            // 初始速度：缓慢下落 + 微弱水平漂移
            particle.Velocity = new Vector3(
                (_random.NextSingle() - 0.5f) * 0.05f,
                0,
                (_random.NextSingle() - 0.5f) * 0.05f
            );

            particle.Size = ParticleSize * (0.7f + _random.NextSingle() * 0.6f);
            particle.Rotation = _random.NextSingle() * Mathf.TwoPi;
            particle.RotationX = _random.NextSingle() * Mathf.TwoPi;
            particle.Alpha = 0.3f + _random.NextSingle() * 0.5f;
            particle.SwayPhase = _random.NextSingle() * Mathf.TwoPi;
            particle.SwaySpeed = 1.0f + _random.NextSingle() * 2.0f;
            particle.UseColorB = _random.NextSingle() > 0.6f; // 40%概率使用金色

            return particle;
        }

        /// <summary>
        /// 每帧更新粒子状态和 Actor 位置
        /// </summary>
        public override void OnUpdate()
        {
            if (!_isInitialized || _particles == null) return;

            float deltaTime = Time.DeltaTime;
            Vector3 actorPos = Transform.Translation;

            for (int i = 0; i < _particles.Length; i++)
            {
                ref var p = ref _particles[i];

                // 更新摆动相位
                p.SwayPhase += deltaTime * p.SwaySpeed;

                // 计算水平摆动偏移（正弦波）
                float swayOffset = Mathf.Sin(p.SwayPhase) * SwayAmplitude * deltaTime;
                Vector3 swayDir = Vector3.Cross(Vector3.Up, WindDirection);
                if (swayDir.LengthSquared < 0.001f)
                    swayDir = Vector3.Right;
                swayDir.Normalize();

                // 更新位置：下落 + 风向 + 摆动
                p.Position += new Vector3(0, -FallSpeed * deltaTime, 0);
                p.Position += WindDirection * deltaTime;
                p.Position += swayDir * swayOffset;

                // 更新旋转（Y轴旋转 + X轴翻滚）
                p.Rotation += deltaTime * 0.5f;
                p.RotationX += deltaTime * 0.3f;

                // 粒子到达底部时重置到顶部
                if (p.Position.Y < -0.5f)
                {
                    _particles[i] = CreateRandomParticle(randomizeHeight: false);
                }

                // 更新对应 Actor 的位置和旋转
                if (_actorsCreated && i < _particleActors?.Length && _particleActors[i] != null)
                {
                    Vector3 worldPos = _particles[i].Position + actorPos;
                    _particleActors[i].Position = worldPos;

                    float rotY = _particles[i].Rotation * Mathf.RadiansToDegrees;
                    float rotX = _particles[i].RotationX * Mathf.RadiansToDegrees;
                    float rotZ = Mathf.Sin(_particles[i].SwayPhase) * 15f;
                    _particleActors[i].Orientation = Quaternion.Euler(rotZ, rotY, rotX);
                }
            }


        }

        /// <summary>
        /// 编辑器调试绘制（保留作为编辑器预览）
        /// </summary>
        public override void OnDebugDraw()
        {
            if (!_isInitialized || _particles == null) return;

            // 如果 Actor 创建成功，不需要 DebugDraw（游戏运行时可见）
            if (_actorsCreated && _particleActors != null)
            {
                // 仅在编辑器中绘制发射区域边界（辅助调试）
                if (Engine.IsEditor)
                {
                    Vector3 actorPos = Transform.Translation;
                    var centerColor = new Color(0.5f, 0.8f, 0.4f, 0.15f);
                    DebugDraw.DrawWireSphere(
                        new BoundingSphere(actorPos + new Vector3(0, EmissionHeight * 0.5f, 0), EmissionRadius),
                        centerColor
                    );
                }
                return;
            }

            // 降级模式：Actor 创建失败时使用 DebugDraw
            Vector3 pos = Transform.Translation;
            for (int i = 0; i < _particles.Length; i++)
            {
                var p = _particles[i];
                Vector3 worldPos = p.Position + pos;

                var color = p.UseColorB
                    ? new Color(ParticleColorB.R, ParticleColorB.G, ParticleColorB.B, ParticleColorB.A * p.Alpha)
                    : new Color(ParticleColor.R, ParticleColor.G, ParticleColor.B, ParticleColor.A * p.Alpha);

                float cosR = Mathf.Cos(p.Rotation);
                float sinR = Mathf.Sin(p.Rotation);
                float leafLength = p.Size * LeafLengthScale;
                float leafWidth = p.Size * LeafWidthScale;

                Vector3 tip1 = worldPos + new Vector3(cosR, sinR, 0) * leafLength;
                Vector3 tip2 = worldPos - new Vector3(cosR, sinR, 0) * leafLength;

                DebugDraw.DrawLine(tip1, tip2, color, leafWidth);
                var sphere = new BoundingSphere(worldPos, p.Size * 0.5f);
                DebugDraw.DrawSphere(sphere, color);
            }
        }
    }
}
