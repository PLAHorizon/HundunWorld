using System;
using FlaxEngine;
using FlaxEngine.GUI;

namespace HundunWorld.Game.UI.Components
{
    /// <summary>
    /// 飘落粒子系统（UI 层）
    /// 模拟花瓣/尘埃等轻柔飘落效果，适用于武侠游戏 UI 装饰
    /// 粒子从屏幕上方随机位置生成，受重力和风力影响自然飘落
    /// </summary>
    public class FloatingParticles : Control
    {
        /// <summary>
        /// 粒子数据结构
        /// 包含位置、速度、生命周期、大小、旋转、透明度等属性
        /// </summary>
        private struct Particle
        {
            /// <summary>粒子当前位置（本地坐标）</summary>
            public Float2 Position;

            /// <summary>粒子当前速度（像素/秒）</summary>
            public Float2 Velocity;

            /// <summary>粒子已存活时间（秒）</summary>
            public float Life;

            /// <summary>粒子最大生命周期（秒）</summary>
            public float MaxLife;

            /// <summary>粒子大小（像素）</summary>
            public float Size;

            /// <summary>粒子当前旋转角度（弧度）</summary>
            public float Rotation;

            /// <summary>粒子旋转速度（弧度/秒）</summary>
            public float RotationSpeed;

            /// <summary>粒子当前透明度（0-1）</summary>
            public float Alpha;

            /// <summary>粒子初始透明度</summary>
            public float InitialAlpha;

            /// <summary>粒子漂移相位（用于正弦波漂移）</summary>
            public float DriftPhase;

            /// <summary>粒子漂移频率</summary>
            public float DriftFrequency;
        }

        #region 配置参数

        /// <summary>
        /// 最大粒子数量限制
        /// 控制性能与视觉效果平衡，建议 50-150
        /// </summary>
        public int MaxParticles { get; set; } = 100;

        /// <summary>
        /// 粒子下落速度范围（最小值，像素/秒）
        /// 实际速度在此值和 MaxFallSpeed 之间随机
        /// </summary>
        public float MinFallSpeed { get; set; } = 25f;

        /// <summary>
        /// 粒子下落速度范围（最大值，像素/秒）
        /// 实际速度在此值和 MinFallSpeed 之间随机
        /// </summary>
        public float MaxFallSpeed { get; set; } = 35f;

        /// <summary>
        /// 水平漂移幅度（像素/秒）
        /// 粒子左右摇摆的最大速度
        /// </summary>
        public float DriftAmplitude { get; set; } = 5f;

        /// <summary>
        /// 粒子大小范围（最小值，像素）
        /// </summary>
        public float MinParticleSize { get; set; } = 2f;

        /// <summary>
        /// 粒子大小范围（最大值，像素）
        /// </summary>
        public float MaxParticleSize { get; set; } = 4f;

        /// <summary>
        /// 粒子颜色（默认粉白色，50% 透明度）
        /// 符合燕云十六声风格的轻柔色调
        /// </summary>
        public Color ParticleColor { get; set; } = new Color(1.0f, 0.85f, 0.9f, 0.5f);

        /// <summary>
        /// 每帧生成粒子的最小数量
        /// </summary>
        public int MinSpawnPerFrame { get; set; } = 1;

        /// <summary>
        /// 每帧生成粒子的最大数量
        /// </summary>
        public int MaxSpawnPerFrame { get; set; } = 2;

        /// <summary>
        /// 粒子生命周期范围（最小值，秒）
        /// </summary>
        public float MinLifeTime { get; set; } = 8f;

        /// <summary>
        /// 粒子生命周期范围（最大值，秒）
        /// </summary>
        public float MaxLifeTime { get; set; } = 15f;

        /// <summary>
        /// 旋转速度范围（弧度/秒）
        /// </summary>
        public float MaxRotationSpeed { get; set; } = 1.5f;

        #endregion

        #region 内部状态

        // 使用数组而非 List，避免频繁内存分配
        private Particle[] _particles;
        private int _activeParticleCount;

        // 随机数生成器
        private Random _random;

        // 时间累积器，用于控制粒子生成频率
        private float _spawnAccumulator;

        // 全局时间，用于计算漂移效果
        private float _globalTime;

        // 缓存的控件尺寸，避免频繁访问属性
        private float _cachedWidth;
        private float _cachedHeight;

        #endregion

        /// <summary>
        /// 构造函数，初始化粒子系统
        /// </summary>
        public FloatingParticles()
        {
            // 设置透明背景，不拦截输入事件
            BackgroundColor = Color.Transparent;
            IsScrollable = false;

            // 预分配粒子数组，避免运行时频繁分配内存
            _particles = new Particle[MaxParticles];
            _activeParticleCount = 0;

            // 初始化随机数生成器
            _random = new Random();

            // 初始化缓存尺寸
            _cachedWidth = 1f;
            _cachedHeight = 1f;
        }

        /// <summary>
        /// 每帧更新粒子系统状态
        /// </summary>
        /// <param name="deltaTime">帧间隔时间（秒）</param>
        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);

            // 缓存控件尺寸，避免在循环中频繁访问
            _cachedWidth = Width > 0f ? Width : 1f;
            _cachedHeight = Height > 0f ? Height : 1f;

            // 更新全局时间
            _globalTime += deltaTime;

            // 生成新粒子
            SpawnParticles(deltaTime);

            // 更新所有活跃粒子
            UpdateParticles(deltaTime);
        }

        /// <summary>
        /// 生成新粒子
        /// </summary>
        /// <param name="deltaTime">帧间隔时间（秒）</param>
        private void SpawnParticles(float deltaTime)
        {
            // 如果已达到最大粒子数，不再生成
            if (_activeParticleCount >= MaxParticles)
            {
                return;
            }

            // 计算本帧应生成的粒子数量（1-2 个）
            int spawnCount = MinSpawnPerFrame;
            if (MaxSpawnPerFrame > MinSpawnPerFrame)
            {
                spawnCount += _random.Next(MaxSpawnPerFrame - MinSpawnPerFrame + 1);
            }

            // 生成指定数量的粒子
            for (int i = 0; i < spawnCount && _activeParticleCount < MaxParticles; i++)
            {
                SpawnSingleParticle();
            }
        }

        /// <summary>
        /// 生成单个粒子
        /// </summary>
        private void SpawnSingleParticle()
        {
            // 在屏幕上方随机 X 坐标生成
            float x = (float)(_random.NextDouble() * _cachedWidth);
            float y = -10f; // 从屏幕上方稍外位置生成

            // 随机下落速度
            float fallSpeed = Mathf.Lerp(MinFallSpeed, MaxFallSpeed, (float)_random.NextDouble());

            // 随机漂移参数（正弦波模拟）
            float driftPhase = (float)(_random.NextDouble() * Mathf.TwoPi);
            float driftFrequency = 0.5f + (float)(_random.NextDouble() * 1.5f); // 0.5-2.0 Hz

            // 随机大小
            float size = Mathf.Lerp(MinParticleSize, MaxParticleSize, (float)_random.NextDouble());

            // 随机旋转速度
            float rotationSpeed = ((float)_random.NextDouble() * 2f - 1f) * MaxRotationSpeed;

            // 随机生命周期
            float maxLife = Mathf.Lerp(MinLifeTime, MaxLifeTime, (float)_random.NextDouble());

            // 创建粒子
            var particle = new Particle
            {
                Position = new Float2(x, y),
                Velocity = new Float2(0f, fallSpeed),
                Life = 0f,
                MaxLife = maxLife,
                Size = size,
                Rotation = (float)(_random.NextDouble() * Mathf.TwoPi),
                RotationSpeed = rotationSpeed,
                Alpha = ParticleColor.A,
                InitialAlpha = ParticleColor.A,
                DriftPhase = driftPhase,
                DriftFrequency = driftFrequency
            };

            // 添加到活跃粒子数组
            _particles[_activeParticleCount] = particle;
            _activeParticleCount++;
        }

        /// <summary>
        /// 更新所有活跃粒子
        /// </summary>
        /// <param name="deltaTime">帧间隔时间（秒）</param>
        private void UpdateParticles(float deltaTime)
        {
            // 从后向前遍历，便于移除失效粒子
            for (int i = _activeParticleCount - 1; i >= 0; i--)
            {
                ref Particle p = ref _particles[i];

                // 更新生命周期
                p.Life += deltaTime;

                // 检查是否应该移除粒子
                bool shouldRemove = false;

                // 条件1：超过最大生命周期
                if (p.Life >= p.MaxLife)
                {
                    shouldRemove = true;
                }

                // 条件2：超出屏幕底部
                if (p.Position.Y > _cachedHeight + 20f)
                {
                    shouldRemove = true;
                }

                if (shouldRemove)
                {
                    // 用最后一个活跃粒子替换当前位置，实现 O(1) 移除
                    _activeParticleCount--;
                    if (i < _activeParticleCount)
                    {
                        _particles[i] = _particles[_activeParticleCount];
                    }
                    continue;
                }

                // 计算水平漂移（正弦波模拟）
                float driftX = Mathf.Sin(_globalTime * p.DriftFrequency + p.DriftPhase) * DriftAmplitude;

                // 更新位置
                p.Position.X += driftX * deltaTime;
                p.Position.Y += p.Velocity.Y * deltaTime;

                // 更新旋转
                p.Rotation += p.RotationSpeed * deltaTime;

                // 计算透明度渐变
                // 生命周期前 10% 淡入，后 30% 淡出
                float lifeRatio = p.Life / p.MaxLife;
                if (lifeRatio < 0.1f)
                {
                    // 淡入阶段
                    p.Alpha = p.InitialAlpha * (lifeRatio / 0.1f);
                }
                else if (lifeRatio > 0.7f)
                {
                    // 淡出阶段
                    float fadeOutRatio = (lifeRatio - 0.7f) / 0.3f;
                    p.Alpha = p.InitialAlpha * (1f - fadeOutRatio);
                }
                else
                {
                    // 正常显示阶段
                    p.Alpha = p.InitialAlpha;
                }
            }
        }

        /// <summary>
        /// 绘制所有粒子
        /// 使用 Render2D API 绘制小圆点或花瓣形状
        /// </summary>
        public override void Draw()
        {
            base.Draw();

            // 如果没有活跃粒子，直接返回
            if (_activeParticleCount == 0)
            {
                return;
            }

            // 遍历所有活跃粒子并绘制
            for (int i = 0; i < _activeParticleCount; i++)
            {
                ref Particle p = ref _particles[i];
                DrawParticle(ref p);
            }
        }

        /// <summary>
        /// 绘制单个粒子
        /// 支持透明度渐变和旋转效果
        /// </summary>
        /// <param name="p">要绘制的粒子引用</param>
        private void DrawParticle(ref Particle p)
        {
            // 跳过透明粒子
            if (p.Alpha <= 0.001f)
            {
                return;
            }

            // 构建带透明度的颜色
            var color = new Color(
                ParticleColor.R,
                ParticleColor.G,
                ParticleColor.B,
                p.Alpha
            );

            // 计算绘制矩形（以粒子位置为中心）
            float halfSize = p.Size;
            var rect = new Rectangle(
                p.Position.X - halfSize,
                p.Position.Y - halfSize,
                halfSize * 2f,
                halfSize * 2f
            );

            // 使用 FillRectangle 绘制小方块作为粒子
            // 如果需要更复杂的花瓣形状，可以替换为精灵渲染
            Render2D.FillRectangle(rect, color);
        }

        /// <summary>
        /// 清空所有粒子
        /// </summary>
        public void Clear()
        {
            _activeParticleCount = 0;
        }

        /// <summary>
        /// 获取当前活跃粒子数量
        /// </summary>
        /// <returns>活跃粒子数量</returns>
        public int GetActiveParticleCount()
        {
            return _activeParticleCount;
        }

        /// <summary>
        /// 调整最大粒子数时重新分配数组
        /// </summary>
        /// <param name="newMax">新的最大粒子数</param>
        public void SetMaxParticles(int newMax)
        {
            if (newMax <= 0)
            {
                newMax = 1;
            }

            if (newMax != MaxParticles)
            {
                MaxParticles = newMax;

                // 重新分配数组
                var newParticles = new Particle[MaxParticles];

                // 复制现有粒子（如果新容量小于当前数量，截断）
                int copyCount = Math.Min(_activeParticleCount, MaxParticles);
                Array.Copy(_particles, newParticles, copyCount);
                _particles = newParticles;
                _activeParticleCount = copyCount;
            }
        }

        /// <summary>
        /// 设置粒子颜色
        /// </summary>
        /// <param name="color">新的粒子颜色</param>
        public void SetParticleColor(Color color)
        {
            ParticleColor = color;
        }

        /// <summary>
        /// 设置下落速度范围
        /// </summary>
        /// <param name="min">最小下落速度（像素/秒）</param>
        /// <param name="max">最大下落速度（像素/秒）</param>
        public void SetFallSpeedRange(float min, float max)
        {
            MinFallSpeed = Mathf.Min(min, max);
            MaxFallSpeed = Mathf.Max(min, max);
        }

        /// <summary>
        /// 设置漂移幅度
        /// </summary>
        /// <param name="amplitude">漂移幅度（像素/秒）</param>
        public void SetDriftAmplitude(float amplitude)
        {
            DriftAmplitude = Mathf.Max(0f, amplitude);
        }

        /// <summary>
        /// 设置粒子大小范围
        /// </summary>
        /// <param name="min">最小大小（像素）</param>
        /// <param name="max">最大大小（像素）</param>
        public void SetParticleSizeRange(float min, float max)
        {
            MinParticleSize = Mathf.Min(min, max);
            MaxParticleSize = Mathf.Max(min, max);
        }
    }
}
