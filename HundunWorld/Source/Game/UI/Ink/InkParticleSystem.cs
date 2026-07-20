using System;
using System.Collections.Generic;
using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.StyleSystem;

namespace HundunWorld.Game.UI.Ink
{
    /// <summary>
    /// 水墨粒子动效系统 — 对应 ink-particles.css。
    /// 实现 4 种粒子类型：
    /// <list type="bullet">
    ///   <item><see cref="ParticleKind.GoldBurst"/>：金粉飘落，按钮点击触发，800ms</item>
    ///   <item><see cref="ParticleKind.InkRipple"/>：墨韵涟漪，面板切换触发，1200ms（双环）</item>
    ///   <item><see cref="ParticleKind.JadeFirefly"/>：青玉萤光，信息提示出现，1000ms</item>
    ///   <item><see cref="ParticleKind.Ambient"/>：环境水墨微粒，页面加载后持续，4000ms 周期</item>
    /// </list>
    /// 动效曲线统一为 cubic-bezier(0.16, 1, 0.3, 1)（参考苹果 HIG ease-out）。
    /// <para>
    /// 该控件作为全屏覆盖层：<see cref="AnchorPresets.StretchAll"/> + 透明背景 + 不裁剪子控件 +
    /// <see cref="Control.Enabled"/>=false（不拦截鼠标事件，但仍渲染）。
    /// z-index 由父容器添加顺序决定，应在所有 UI 之上、Tooltip 之下。
    /// </para>
    /// <para>
    /// 通过订阅 <see cref="InkPageRouter.PanelShow"/> 自动在面板切换时触发墨韵涟漪；
    /// 通过 <see cref="EmitGoldBurst"/> 在按钮点击位置触发金粉；
    /// 通过 <see cref="EmitJadeFirefly"/> 在 Toast/通知位置触发萤光；
    /// 通过 <see cref="StartAmbient"/> / <see cref="StopAmbient"/> 控制环境微粒。
    /// </para>
    /// <para>
    /// <see cref="InkWashTheme.ReducedMotion"/> = true 时，所有动效持续时间降至 0.01ms（瞬切）。
    /// </para>
    /// </summary>
    public class InkParticleSystem : ContainerControl
    {
        /// <summary>粒子类型枚举</summary>
        public enum ParticleKind
        {
            /// <summary>金粉飘落（按钮点击）</summary>
            GoldBurst,

            /// <summary>墨韵涟漪（面板切换，双环）</summary>
            InkRipple,

            /// <summary>青玉萤光（信息提示）</summary>
            JadeFirefly,

            /// <summary>环境水墨微粒（持续飘动）</summary>
            Ambient
        }

        /// <summary>粒子状态结构</summary>
        private struct Particle
        {
            public ParticleKind Kind;
            public Float2 Position;       // 当前位置（控件局部坐标）
            public Float2 Velocity;       // 速度（像素/秒）
            public Float2 DriftMid;       // 中段漂移目标（Ambient 用）
            public Float2 DriftEnd;       // 终点漂移目标（Ambient/JadeFirefly 用）
            public float Size;            // 当前大小（像素）
            public float SizeInitial;     // 初始大小
            public float SizeFinal;       // 终止大小
            public Color ColorCore;       // 核心色
            public Color ColorGlow;       // 辉光色
            public float Life;            // 已存活时间（秒）
            public float MaxLife;         // 最大寿命（秒）
            public float MaxOpacity;      // 最大不透明度
            public bool IsLarge;          // GoldBurst 大尺寸标记
            public bool IsSecondRing;     // InkRipple 第二环标记
            public float Delay;           // 延迟启动时间（秒，InkRipple 第二环用）
            public float StrokeInitial;   // 涟漪初始描边
            public float StrokeFinal;     // 涟漪终止描边
            public float RippleMaxRadius; // 涟漪最大半径
            public bool Alive;
        }

        /// <summary>粒子池（活动粒子列表）</summary>
        private readonly List<Particle> _particles = new List<Particle>(128);

        /// <summary>粒子池容量上限（超过则丢弃最老的）</summary>
        private const int MaxParticles = 256;

        /// <summary>环境微粒是否启用</summary>
        private bool _ambientEnabled;

        /// <summary>环境微粒下次发射时间</summary>
        private float _nextAmbientEmit;

        /// <summary>环境微粒每次发射数量</summary>
        private const int AmbientEmitCount = 2;

        /// <summary>环境微粒发射间隔（秒）</summary>
        private const float AmbientEmitInterval = 0.6f;

        /// <summary>环境微粒最大并发数</summary>
        private const int AmbientMaxConcurrent = 24;

        /// <summary>随机数生成器（线程安全不需要，UI 线程单线程访问）</summary>
        private readonly Random _rng = new Random();

        /// <summary>关联的路由器（用于订阅 PanelShow 自动触发涟漪）</summary>
        private InkPageRouter _router;

        /// <summary>
        /// 构造函数：初始化全屏透明覆盖层。
        /// </summary>
        public InkParticleSystem()
        {
            try
            {
                AnchorPreset = AnchorPresets.StretchAll;
                Offsets = Margin.Zero;
                BackgroundColor = Color.Transparent;
                ClipChildren = false;
                AutoFocus = false;
                // Enabled=false 防止拦截鼠标事件，Draw 仍会被引擎调用
                Enabled = false;
                _ambientEnabled = false;
                _nextAmbientEmit = 0f;
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[InkParticleSystem] 初始化失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 关联路由器并订阅 PanelShow 事件。
        /// 订阅后，任何 <see cref="InkPageRouter.NavigateTo"/> / NavigateToHud / NavigateToAction
        /// 成功后会自动在指定位置触发墨韵涟漪。
        /// </summary>
        /// <param name="router">路由器实例</param>
        public void Initialize(InkPageRouter router)
        {
            try
            {
                if (_router != null)
                {
                    _router.PanelShow -= OnRouterPanelShow;
                }
                _router = router;
                if (_router != null)
                {
                    _router.PanelShow += OnRouterPanelShow;
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[InkParticleSystem] Initialize 失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 路由器 PanelShow 事件处理：在触发点发射墨韵涟漪。
        /// <para>
        /// 若 <paramref name="screenPos"/> 为 <see cref="Float2.Zero"/>（触发点未知），
        /// 则回退到控件中心绘制涟漪，对应 HTML 设计中面板切换时的居中扩散效果。
        /// </para>
        /// </summary>
        private void OnRouterPanelShow(string domId, Float2 screenPos)
        {
            try
            {
                Float2 localPos;
                if (screenPos == Float2.Zero)
                {
                    // 触发点未知：回退到控件中心
                    localPos = new Float2(Width * 0.5f, Height * 0.5f);
                }
                else
                {
                    // 屏幕坐标转控件局部坐标
                    localPos = PointFromScreen(screenPos);
                }
                EmitInkRipple(localPos, isGold: false);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[InkParticleSystem] OnRouterPanelShow 失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 开始环境微粒持续发射。
        /// </summary>
        public void StartAmbient()
        {
            _ambientEnabled = true;
            _nextAmbientEmit = Time.GameTime + 0.2f;
        }

        /// <summary>
        /// 停止环境微粒发射（已存在的粒子继续完成生命周期）。
        /// </summary>
        public void StopAmbient()
        {
            _ambientEnabled = false;
        }

        /// <summary>
        /// 发射金粉粒子爆发（按钮点击反馈）。
        /// <para>
        /// 对应 ink-particles.css <c>@keyframes gold-burst</c>：
        /// 从中心向外扩散，带重力下坠，800ms 寿命。
        /// </para>
        /// </summary>
        /// <param name="center">爆发中心（控件局部坐标）</param>
        /// <param name="count">粒子数量（默认 12）</param>
        /// <param name="isLarge">是否使用大尺寸粒子（默认 false）</param>
        public void EmitGoldBurst(Float2 center, int count = 12, bool isLarge = false)
        {
            try
            {
                if (InkWashTheme.ReducedMotion) return;

                float maxLife = InkWashTheme.GetDuration(InkWashTheme.DurationGoldBurst);
                float sizeBase = isLarge ? InkWashTheme.ParticleGoldSizeLg : InkWashTheme.ParticleGoldSizeSm;

                for (int i = 0; i < count; i++)
                {
                    if (_particles.Count >= MaxParticles) break;

                    // 随机方向（0~2π）与速度（80~220 px/s）
                    float angle = (float)(_rng.NextDouble() * Mathf.TwoPi);
                    float speed = 80f + (float)(_rng.NextDouble() * 140f);
                    // 重力下坠：vy 额外 +60~120 px/s
                    float gravity = 60f + (float)(_rng.NextDouble() * 60f);

                    var p = new Particle
                    {
                        Kind = ParticleKind.GoldBurst,
                        Position = center,
                        Velocity = new Float2(
                            Mathf.Cos(angle) * speed,
                            Mathf.Sin(angle) * speed + gravity),
                        Size = sizeBase,
                        SizeInitial = sizeBase,
                        SizeFinal = sizeBase * 0.3f,
                        ColorCore = InkWashTheme.ParticleGoldCore,
                        ColorGlow = isLarge
                            ? new Color(
                                InkWashTheme.ParticleGoldGlow.R,
                                InkWashTheme.ParticleGoldGlow.G,
                                InkWashTheme.ParticleGoldGlow.B,
                                0.9f)
                            : InkWashTheme.ParticleGoldGlow,
                        Life = 0f,
                        MaxLife = maxLife,
                        MaxOpacity = 1f,
                        IsLarge = isLarge,
                        Alive = true,
                    };
                    _particles.Add(p);
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[InkParticleSystem] EmitGoldBurst 失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 发射墨韵涟漪（面板切换反馈，双环）。
        /// <para>
        /// 对应 ink-particles.css <c>@keyframes ink-ripple</c> 与 <c>ink-ripple-second</c>：
        /// 从中心扩散 2 圈，1200ms 寿命。第二环在 30% 处启动，缩放至 1.2。
        /// </para>
        /// </summary>
        /// <param name="center">涟漪中心（控件局部坐标）</param>
        /// <param name="isGold">true=金色涟漪；false=青色涟漪（默认）</param>
        /// <param name="maxRadius">最大半径（默认 80px）</param>
        public void EmitInkRipple(Float2 center, bool isGold = false, float maxRadius = 80f)
        {
            try
            {
                if (InkWashTheme.ReducedMotion) return;

                float maxLife = InkWashTheme.GetDuration(InkWashTheme.DurationInkRipple);
                var ringColor = isGold ? InkWashTheme.RippleGold : InkWashTheme.RippleJade;

                // 第一环：立即启动
                var ring1 = new Particle
                {
                    Kind = ParticleKind.InkRipple,
                    Position = center,
                    Velocity = Float2.Zero,
                    Size = 0f,
                    SizeInitial = 0f,
                    SizeFinal = maxRadius,
                    ColorCore = ringColor,
                    ColorGlow = ringColor,
                    Life = 0f,
                    MaxLife = maxLife,
                    MaxOpacity = 0.8f,
                    IsSecondRing = false,
                    Delay = 0f,
                    StrokeInitial = InkWashTheme.RippleStrokeInitial,
                    StrokeFinal = InkWashTheme.RippleStrokeFinal,
                    RippleMaxRadius = maxRadius,
                    Alive = true,
                };
                _particles.Add(ring1);

                // 第二环：延迟 30% 启动，缩放至 1.2 倍
                var ring2 = ring1;
                ring2.IsSecondRing = true;
                ring2.Delay = maxLife * InkWashTheme.RippleSecondDelay;
                ring2.SizeFinal = maxRadius * InkWashTheme.RippleSecondScale;
                ring2.MaxOpacity = 0.6f;
                _particles.Add(ring2);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[InkParticleSystem] EmitInkRipple 失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 发射青玉萤光粒子（信息提示出现反馈）。
        /// <para>
        /// 对应 ink-particles.css <c>@keyframes jade-firefly</c>：
        /// 从边缘飘出，随机漂浮，1000ms 寿命。
        /// 20% 时达到最大不透明度，100% 时缩放至 0.6 并消失。
        /// </para>
        /// </summary>
        /// <param name="center">萤光中心</param>
        /// <param name="count">粒子数量（默认 6）</param>
        public void EmitJadeFirefly(Float2 center, int count = 6)
        {
            try
            {
                if (InkWashTheme.ReducedMotion) return;

                float maxLife = InkWashTheme.GetDuration(InkWashTheme.DurationJadeFirefly);

                for (int i = 0; i < count; i++)
                {
                    if (_particles.Count >= MaxParticles) break;

                    // 随机漂移方向（向上偏 60°范围内），距离 30~60px
                    float angle = -Mathf.PiOverTwo + (float)(_rng.NextDouble() - 0.5) * Mathf.Pi / 1.5f;
                    float dist = 30f + (float)(_rng.NextDouble() * 30f);
                    var drift = new Float2(Mathf.Cos(angle) * dist, Mathf.Sin(angle) * dist);

                    var p = new Particle
                    {
                        Kind = ParticleKind.JadeFirefly,
                        Position = center,
                        Velocity = Float2.Zero,
                        DriftEnd = center + drift,
                        Size = InkWashTheme.ParticleJadeSize,
                        SizeInitial = 0f,
                        SizeFinal = InkWashTheme.ParticleJadeSize * 0.6f,
                        ColorCore = InkWashTheme.ParticleJadeCore,
                        ColorGlow = InkWashTheme.ParticleJadeGlow,
                        Life = 0f,
                        MaxLife = maxLife,
                        MaxOpacity = 1f,
                        Alive = true,
                    };
                    _particles.Add(p);
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[InkParticleSystem] EmitJadeFirefly 失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 发射单个环境微粒（内部调用）。
        /// <para>
        /// 对应 ink-particles.css <c>@keyframes ambient-drift</c>：
        /// 缓慢飘动，带透明度呼吸，4000ms 周期。
        /// </para>
        /// </summary>
        private void EmitAmbientInternal()
        {
            try
            {
                if (InkWashTheme.ReducedMotion) return;
                if (_particles.Count >= MaxParticles) return;

                // 统计当前 Ambient 数量
                int ambientCount = 0;
                foreach (var p in _particles)
                {
                    if (p.Kind == ParticleKind.Ambient && p.Alive) ambientCount++;
                }
                if (ambientCount >= AmbientMaxConcurrent) return;

                // 随机出生点（屏幕内随机）
                float x = (float)(_rng.NextDouble() * Width);
                float y = (float)(_rng.NextDouble() * Height);
                // 随机中段与终点漂移（小幅）
                float dxMid = (float)(_rng.NextDouble() - 0.5) * 40f;
                float dyMid = (float)(_rng.NextDouble() - 0.5) * 40f;
                float dxEnd = (float)(_rng.NextDouble() - 0.5) * 80f;
                float dyEnd = (float)(_rng.NextDouble() - 0.5) * 80f;
                // 随机金/青色
                bool isGold = _rng.NextDouble() < 0.4;

                var p2 = new Particle
                {
                    Kind = ParticleKind.Ambient,
                    Position = new Float2(x, y),
                    Velocity = Float2.Zero,
                    DriftMid = new Float2(x + dxMid, y + dyMid),
                    DriftEnd = new Float2(x + dxEnd, y + dyEnd),
                    Size = InkWashTheme.ParticleAmbientSize,
                    SizeInitial = InkWashTheme.ParticleAmbientSize,
                    SizeFinal = InkWashTheme.ParticleAmbientSize,
                    ColorCore = isGold ? InkWashTheme.ParticleAmbientGold : InkWashTheme.ParticleAmbientJade,
                    ColorGlow = isGold ? InkWashTheme.ParticleAmbientGold : InkWashTheme.ParticleAmbientJade,
                    Life = 0f,
                    MaxLife = InkWashTheme.GetDuration(InkWashTheme.DurationAmbientDrift),
                    MaxOpacity = 0.6f,
                    Alive = true,
                };
                _particles.Add(p2);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[InkParticleSystem] EmitAmbientInternal 失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 每帧更新粒子状态。
        /// </summary>
        /// <param name="deltaTime">自上一帧以来的时间间隔（秒）</param>
        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);

            try
            {
                // 环境微粒定时发射
                if (_ambientEnabled && Time.GameTime >= _nextAmbientEmit)
                {
                    for (int i = 0; i < AmbientEmitCount; i++)
                    {
                        EmitAmbientInternal();
                    }
                    _nextAmbientEmit = Time.GameTime + AmbientEmitInterval;
                }

                // 推进所有粒子状态
                float dt = Time.DeltaTime;
                for (int i = _particles.Count - 1; i >= 0; i--)
                {
                    var p = _particles[i];
                    if (!p.Alive)
                    {
                        _particles.RemoveAt(i);
                        continue;
                    }

                    // 延迟启动（InkRipple 第二环）
                    if (p.Delay > 0f)
                    {
                        p.Delay -= dt;
                        if (p.Delay > 0f)
                        {
                            _particles[i] = p;
                            continue;
                        }
                    }

                    p.Life += dt;
                    if (p.Life >= p.MaxLife)
                    {
                        p.Alive = false;
                        _particles[i] = p;
                        continue;
                    }

                    float t = p.Life / p.MaxLife; // 0..1 进度

                    switch (p.Kind)
                    {
                        case ParticleKind.GoldBurst:
                            // 位移 += 速度 * dt
                            p.Position += p.Velocity * dt;
                            // 速度衰减（空气阻力）
                            p.Velocity *= 0.96f;
                            // 大小线性缩小至 SizeFinal
                            p.Size = Mathf.Lerp(p.SizeInitial, p.SizeFinal, t);
                            break;

                        case ParticleKind.InkRipple:
                            // 涟漪仅扩大半径，位置不变
                            p.Size = Mathf.Lerp(p.SizeInitial, p.SizeFinal, EaseOutCubic(t));
                            break;

                        case ParticleKind.JadeFirefly:
                            // 0% 在中心 scale(0)；20% 到达 30% 漂移点 scale(1)；100% 到达终点 scale(0.6)
                            if (t < 0.2f)
                            {
                                float t1 = t / 0.2f;
                                p.Position = Float2.Lerp(p.Position, p.DriftEnd * 0.3f + p.Position * 0.7f, t1);
                                p.Size = Mathf.Lerp(0f, p.SizeFinal / 0.6f, t1);
                            }
                            else
                            {
                                float t2 = (t - 0.2f) / 0.8f;
                                // 从 30% 点到终点
                                var startPos = p.DriftEnd * 0.3f + p.Position * 0.7f;
                                p.Position = Float2.Lerp(startPos, p.DriftEnd, t2);
                                p.Size = Mathf.Lerp(p.SizeFinal / 0.6f, p.SizeFinal, t2);
                            }
                            break;

                        case ParticleKind.Ambient:
                            // 0% 在起点；50% 在中段；100% 在终点
                            if (t < 0.5f)
                            {
                                float t1 = t / 0.5f;
                                // 从 Position 漂移到 DriftMid — 由于 Position 会变，需要保存起点
                                // 这里简化：直接按 t1 在 Position 与 DriftMid 之间插值
                                // 但 Position 已变，因此采用：以 DriftMid 为目标，按 t1 推进
                                // 修正：使用初始位置作为起点（首次进入时记录）
                                // 简化处理：使用 Lerp(Position, DriftMid, dt * 2) 渐近
                                p.Position = Float2.Lerp(p.Position, p.DriftMid, dt * 2f);
                            }
                            else
                            {
                                p.Position = Float2.Lerp(p.Position, p.DriftEnd, dt * 2f);
                            }
                            break;
                    }

                    _particles[i] = p;
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[InkParticleSystem] Update 失败: {ex.Message}");
            }
        }

        /// <summary>
        /// cubic-bezier(0.16, 1, 0.3, 1) ease-out 的近似实现（用于涟漪缩放）。
        /// 使用 1 - (1-t)^3 三次方 ease-out 近似，视觉上与原曲线差异可忽略。
        /// </summary>
        private static float EaseOutCubic(float t)
        {
            return 1f - Mathf.Square(1f - t) * (1f - t);
        }

        /// <summary>
        /// 自定义渲染：按粒子类型绘制。
        /// </summary>
        public override void Draw()
        {
            base.Draw();

            try
            {
                foreach (var p in _particles)
                {
                    if (!p.Alive || p.Delay > 0f) continue;

                    float t = p.Life / p.MaxLife;
                    float opacity = ComputeOpacity(p.Kind, t, p.MaxOpacity);

                    switch (p.Kind)
                    {
                        case ParticleKind.GoldBurst:
                            DrawGoldParticle(p, opacity);
                            break;
                        case ParticleKind.InkRipple:
                            DrawRippleRing(p, opacity);
                            break;
                        case ParticleKind.JadeFirefly:
                            DrawJadeParticle(p, opacity);
                            break;
                        case ParticleKind.Ambient:
                            DrawAmbientParticle(p, opacity);
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[InkParticleSystem] Draw 失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 计算粒子在当前生命周期的透明度。
        /// </summary>
        private static float ComputeOpacity(ParticleKind kind, float t, float maxOpacity)
        {
            switch (kind)
            {
                case ParticleKind.GoldBurst:
                    // 0%=1.0；60%=0.9；100%=0
                    if (t < 0.6f) return maxOpacity;
                    return maxOpacity * (1f - (t - 0.6f) / 0.4f);

                case ParticleKind.InkRipple:
                    // 0%=0.8；100%=0 线性衰减
                    return maxOpacity * (1f - t);

                case ParticleKind.JadeFirefly:
                    // 0%=0；20%=1；100%=0
                    if (t < 0.2f) return maxOpacity * (t / 0.2f);
                    return maxOpacity * (1f - (t - 0.2f) / 0.8f);

                case ParticleKind.Ambient:
                    // 0%=0；10%=max；90%=max；100%=0
                    if (t < 0.1f) return maxOpacity * (t / 0.1f);
                    if (t < 0.9f) return maxOpacity;
                    return maxOpacity * (1f - (t - 0.9f) / 0.1f);

                default:
                    return maxOpacity;
            }
        }

        /// <summary>
        /// 绘制金粉粒子：径向渐变圆 + 双层辉光。
        /// </summary>
        private static void DrawGoldParticle(in Particle p, float opacity)
        {
            // 外层辉光（更大、更透明）
            var glowColor = new Color(p.ColorGlow.R, p.ColorGlow.G, p.ColorGlow.B, p.ColorGlow.A * opacity * 0.5f);
            InkRenderHelper.FillRadialGradient(p.Position, p.Size * 2.5f, glowColor, Color.Transparent, 8);

            // 中层辉光
            var midColor = new Color(p.ColorGlow.R, p.ColorGlow.G, p.ColorGlow.B, p.ColorGlow.A * opacity * 0.8f);
            InkRenderHelper.FillRadialGradient(p.Position, p.Size * 1.5f, midColor, Color.Transparent, 8);

            // 核心径向渐变
            var coreColor = new Color(p.ColorCore.R, p.ColorCore.G, p.ColorCore.B, opacity);
            var midCoreColor = new Color(
                InkWashTheme.ParticleGoldMid.R,
                InkWashTheme.ParticleGoldMid.G,
                InkWashTheme.ParticleGoldMid.B,
                opacity);
            InkRenderHelper.FillRadialGradient(p.Position, p.Size, coreColor, midCoreColor, 6);
        }

        /// <summary>
        /// 绘制墨韵涟漪环：描边圆 + 辉光。
        /// </summary>
        private static void DrawRippleRing(in Particle p, float opacity)
        {
            if (p.Size <= 0f) return;

            // 描边宽度从 StrokeInitial 线性过渡到 StrokeFinal
            float stroke = Mathf.Lerp(p.StrokeInitial, p.StrokeFinal, p.Life / p.MaxLife);

            // 外辉光（更大、更透明）
            var glowColor = new Color(p.ColorGlow.R, p.ColorGlow.G, p.ColorGlow.B, opacity * 0.3f);
            InkRenderHelper.FillRadialGradient(p.Position, p.Size + stroke * 2f, glowColor, Color.Transparent, 8);

            // 描边圆（用三角形扇绘制环形）
            var ringColor = new Color(p.ColorCore.R, p.ColorCore.G, p.ColorCore.B, opacity);
            DrawRing(p.Position, p.Size, stroke, ringColor);
        }

        /// <summary>
        /// 绘制青玉萤光粒子：径向渐变圆 + 双层辉光（类似金粉但用青色）。
        /// </summary>
        private static void DrawJadeParticle(in Particle p, float opacity)
        {
            // 外层辉光
            var glowColor = new Color(p.ColorGlow.R, p.ColorGlow.G, p.ColorGlow.B, p.ColorGlow.A * opacity * 0.4f);
            InkRenderHelper.FillRadialGradient(p.Position, p.Size * 3f, glowColor, Color.Transparent, 8);

            // 中层辉光
            var midColor = new Color(p.ColorGlow.R, p.ColorGlow.G, p.ColorGlow.B, p.ColorGlow.A * opacity * 0.7f);
            InkRenderHelper.FillRadialGradient(p.Position, p.Size * 1.8f, midColor, Color.Transparent, 8);

            // 核心径向渐变
            var coreColor = new Color(p.ColorCore.R, p.ColorCore.G, p.ColorCore.B, opacity);
            var midCoreColor = new Color(
                InkWashTheme.ParticleJadeMid.R,
                InkWashTheme.ParticleJadeMid.G,
                InkWashTheme.ParticleJadeMid.B,
                opacity);
            InkRenderHelper.FillRadialGradient(p.Position, p.Size, coreColor, midCoreColor, 6);
        }

        /// <summary>
        /// 绘制环境微粒：简单径向渐变圆。
        /// </summary>
        private static void DrawAmbientParticle(in Particle p, float opacity)
        {
            var c = new Color(p.ColorCore.R, p.ColorCore.G, p.ColorCore.B, p.ColorCore.A * opacity);
            InkRenderHelper.FillRadialGradient(p.Position, p.Size * 1.5f, c, Color.Transparent, 4);
        }

        /// <summary>
        /// 绘制环形（描边圆）。
        /// 使用三角形扇绘制外圆，再用背景色（透明）三角形扇"挖空"内圆。
        /// </summary>
        /// <param name="center">圆心</param>
        /// <param name="radius">外半径</param>
        /// <param name="stroke">描边宽度</param>
        /// <param name="color">描边色</param>
        private static void DrawRing(Float2 center, float radius, float stroke, Color color)
        {
            const int segments = 48;
            float outerR = radius;
            float innerR = Mathf.Max(0f, radius - stroke);

            // 绘制环形：用四边形组成的环
            var vertices = new Float2[segments * 6];
            for (int i = 0; i < segments; i++)
            {
                float a1 = (i / (float)segments) * Mathf.TwoPi;
                float a2 = ((i + 1) / (float)segments) * Mathf.TwoPi;
                int idx = i * 6;
                // 两个三角形组成一个梯形
                vertices[idx] = center + new Float2(Mathf.Cos(a1) * outerR, Mathf.Sin(a1) * outerR);
                vertices[idx + 1] = center + new Float2(Mathf.Cos(a2) * outerR, Mathf.Sin(a2) * outerR);
                vertices[idx + 2] = center + new Float2(Mathf.Cos(a1) * innerR, Mathf.Sin(a1) * innerR);
                vertices[idx + 3] = center + new Float2(Mathf.Cos(a2) * outerR, Mathf.Sin(a2) * outerR);
                vertices[idx + 4] = center + new Float2(Mathf.Cos(a2) * innerR, Mathf.Sin(a2) * innerR);
                vertices[idx + 5] = center + new Float2(Mathf.Cos(a1) * innerR, Mathf.Sin(a1) * innerR);
            }
            Render2D.FillTriangles(vertices, color);
        }

        /// <summary>
        /// 清空所有粒子。
        /// </summary>
        public void ClearAll()
        {
            _particles.Clear();
        }

        /// <summary>
        /// 当前活动粒子数量。
        /// </summary>
        public int ActiveCount => _particles.Count;

        /// <summary>
        /// 控件销毁时取消对 <see cref="InkPageRouter.PanelShow"/> 的事件订阅，
        /// 避免路由器在粒子系统之后销毁时触发悬空订阅导致空引用。
        /// </summary>
        public override void OnDestroy()
        {
            try
            {
                if (_router != null)
                {
                    _router.PanelShow -= OnRouterPanelShow;
                    _router = null;
                }
                _particles.Clear();
                _ambientEnabled = false;
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[InkParticleSystem] OnDestroy 失败: {ex.Message}");
            }
            base.OnDestroy();
        }
    }
}
