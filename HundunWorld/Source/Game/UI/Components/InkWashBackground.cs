using System;
using System.Collections.Generic;
using FlaxEngine;
using FlaxEngine.GUI;

namespace HundunWorld.Game.UI.Components
{
    /// <summary>
    /// 水墨动态背景控件
    /// 
    /// 实现原理：
    /// 1. 使用多个半透明笔触层（Image 子控件）叠加，营造水墨晕染效果
    /// 2. 在 Update 中缓慢漂移位置（默认 0.01/s），实现动态流动效果
    /// 3. 支持从指定路径加载贴图，如果贴图不可用则使用纯色笔刷作为占位
    /// 4. 支持配置漂移速度、透明度、混合模式等参数
    /// 
    /// 性能优化：
    /// - 贴图资源复用，避免重复加载
    /// - 位置漂移使用简单的数学运算，开销极低
    /// - 默认 8 层笔触，可在 60fps 下流畅运行
    /// </summary>
    public class InkWashBackground : Panel
    {
        #region 配置参数

        /// <summary>
        /// 水墨笔触贴图路径列表
        /// 支持 .flax 格式的纹理资源路径
        /// 如果路径为空或加载失败，将使用纯色笔刷作为后备
        /// </summary>
        public string[] TexturePaths
        {
            get => _texturePaths;
            set
            {
                _texturePaths = value ?? Array.Empty<string>();
                ReloadTextures();
            }
        }
        private string[] _texturePaths = Array.Empty<string>();

        /// <summary>
        /// UV/位置漂移速度（单位/秒）
        /// 控制水墨笔触的流动速度，默认 0.01
        /// </summary>
        public float DriftSpeed
        {
            get => _driftSpeed;
            set => _driftSpeed = Mathf.Max(0f, value);
        }
        private float _driftSpeed = 0.01f;

        /// <summary>
        /// 整体透明度（0.0 ~ 1.0）
        /// 控制所有笔触层的整体不透明度
        /// </summary>
        public float Opacity
        {
            get => _opacity;
            set
            {
                _opacity = Mathf.Clamp(value, 0f, 1f);
                UpdateLayerColors();
            }
        }
        private float _opacity = 0.15f;

        /// <summary>
        /// 混合模式
        /// 控制笔触层与背景的混合方式
        /// </summary>
        public InkBlendMode BlendMode
        {
            get => _blendMode;
            set
            {
                _blendMode = value;
                ApplyBlendMode();
            }
        }
        private InkBlendMode _blendMode = InkBlendMode.AlphaBlend;

        /// <summary>
        /// 笔触基础颜色（RGB 通道）
        /// Alpha 通道由 Opacity 属性控制
        /// </summary>
        public Color InkColor
        {
            get => _inkColor;
            set
            {
                _inkColor = value;
                UpdateLayerColors();
            }
        }
        private Color _inkColor = new Color(20f / 255f, 20f / 255f, 30f / 255f, 1f);

        /// <summary>
        /// 笔触层数量
        /// 默认为 8 层，可在构造函数后调整
        /// </summary>
        public int LayerCount
        {
            get => _layerCount;
            set
            {
                if (value != _layerCount && value > 0 && value <= 16)
                {
                    _layerCount = value;
                    RecreateLayers();
                }
            }
        }
        private int _layerCount = 8;

        #endregion

        #region 混合模式枚举

        /// <summary>
        /// 水墨混合模式
        /// </summary>
        public enum InkBlendMode
        {
            /// <summary>标准 Alpha 混合</summary>
            AlphaBlend,
            /// <summary>加法混合（更亮的叠加）</summary>
            Additive,
            /// <summary>乘法混合（更暗的叠加）</summary>
            Multiply,
        }

        #endregion

        #region 内部状态

        // 笔触层数据
        private struct InkLayer
        {
            public Image Image;
            public TextureBrush Brush;
            public Float2 BaseLocation;
            public Float2 BaseSize;
            public float Phase;
            public float LayerAlpha;
        }

        private InkLayer[] _layers;
        private float _time;

        // 默认贴图路径（可选的水墨笔触资源）
        private static readonly string[] DefaultTexturePaths = new string[]
        {
            // 这些路径是示例，实际项目中可替换为真实的水墨笔触贴图
            // 如果路径无效，会自动使用纯色笔刷
        };

        #endregion

        #region 构造函数

        public InkWashBackground()
        {
            BackgroundColor = Color.Transparent;
            ClipChildren = false;

            // 初始化默认贴图路径
            _texturePaths = DefaultTexturePaths;

            // 创建笔触层
            CreateLayers();
        }

        #endregion

        #region 生命周期

        /// <inheritdoc />
        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);

            if (_layers == null || _layers.Length == 0)
                return;

            // 累计时间，用于驱动漂移动画
            _time += deltaTime * DriftSpeed;

            // 更新每层的位置（模拟 UV 漂移效果）
            for (int i = 0; i < _layers.Length; i++)
            {
                ref var layer = ref _layers[i];
                if (layer.Image == null)
                    continue;

                float phase = layer.Phase;

                // 计算位置偏移（使用 sin/cos 实现平滑的椭圆漂移）
                // 不同层使用不同的频率和相位，避免同步运动
                float offsetX = Mathf.Sin(_time * 0.5f + phase) * 5f;
                float offsetY = Mathf.Cos(_time * 0.3f + phase) * 3f;

                // 叠加更慢的漂移，模拟水墨缓慢晕染
                offsetX += Mathf.Sin(_time * 0.13f + phase * 1.7f) * 2f;
                offsetY += Mathf.Cos(_time * 0.17f + phase * 1.3f) * 1.5f;

                // 更新位置
                layer.Image.Location = new Float2(
                    layer.BaseLocation.X + offsetX,
                    layer.BaseLocation.Y + offsetY
                );
            }
        }

        /// <inheritdoc />
        protected override void OnSizeChanged()
        {
            base.OnSizeChanged();
            RecalculateLayerPositions();
        }

        /// <inheritdoc />
        protected override void OnParentChangedInternal()
        {
            base.OnParentChangedInternal();
            RecalculateLayerPositions();
        }

        /// <inheritdoc />
        public override void DrawSelf()
        {
            // 应用混合模式后绘制子控件
            base.DrawSelf();
        }

        #endregion

        #region 笔触层管理

        /// <summary>
        /// 创建笔触层
        /// </summary>
        private void CreateLayers()
        {
            _layers = new InkLayer[LayerCount];

            // 各层的基础尺寸（不同尺寸营造水墨浓淡层次）
            var baseSizes = new Float2[]
            {
                new Float2(380f, 260f),   // 大块泼墨
                new Float2(300f, 400f),   // 纵向大笔触
                new Float2(440f, 220f),   // 横向扫笔
                new Float2(260f, 340f),   // 纵向中笔触
                new Float2(200f, 160f),   // 小块浓墨
                new Float2(320f, 180f),   // 横向中笔触
                new Float2(180f, 280f),   // 纵向细笔触
                new Float2(360f, 200f),   // 横向淡扫
                new Float2(280f, 320f),   // 方形笔触
                new Float2(400f, 150f),   // 超横向扫笔
                new Float2(150f, 350f),   // 超纵向笔触
                new Float2(240f, 240f),   // 正方形笔触
                new Float2(350f, 280f),   // 中大笔触
                new Float2(220f, 300f),   // 中纵向笔触
                new Float2(300f, 200f),   // 中横向笔触
                new Float2(260f, 260f),   // 中等方形
            };

            // 每层不同的基础透明度，营造浓淡变化
            float[] layerAlphas = {
                0.10f, 0.08f, 0.14f, 0.06f, 0.12f, 0.07f, 0.09f, 0.11f,
                0.08f, 0.10f, 0.06f, 0.13f, 0.07f, 0.09f, 0.11f, 0.08f
            };

            for (int i = 0; i < LayerCount; i++)
            {
                var layer = new InkLayer
                {
                    BaseSize = baseSizes[i % baseSizes.Length],
                    Phase = i * 1.3f,
                    LayerAlpha = layerAlphas[i % layerAlphas.Length],
                };

                // 创建 Image 控件作为笔触层
                var image = new Image
                {
                    Size = layer.BaseSize,
                    ClipChildren = false,
                    KeepAspectRatio = false,
                };

                layer.Image = image;
                _layers[i] = layer;
                AddChild(image);
            }

            // 加载贴图并应用到笔触层
            LoadAndApplyTextures();

            // 计算初始位置
            RecalculateLayerPositions();
        }

        /// <summary>
        /// 重新创建笔触层（当 LayerCount 变化时）
        /// </summary>
        private void RecreateLayers()
        {
            // 清理现有层
            if (_layers != null)
            {
                foreach (var layer in _layers)
                {
                    layer.Image?.Dispose();
                }
            }

            CreateLayers();
        }

        /// <summary>
        /// 重新计算笔触层位置
        /// </summary>
        private void RecalculateLayerPositions()
        {
            if (_layers == null || Width <= 0 || Height <= 0)
                return;

            for (int i = 0; i < _layers.Length; i++)
            {
                ref var layer = ref _layers[i];
                if (layer.Image == null)
                    continue;

                // 不规则分布：使用黄金分割比例散布在屏幕各处
                float ratio = (i + 0.5f) / _layers.Length;
                float baseX, baseY;

                // 使用螺旋分布避免规则排列
                float angle = ratio * Mathf.Pi * 2f * 2.4f; // 黄金角
                float radius = Mathf.Sqrt(ratio) * 0.4f;

                baseX = Width * (0.5f + Mathf.Cos(angle) * radius);
                baseY = Height * (0.5f + Mathf.Sin(angle) * radius);

                // 确保不超出边界
                baseX = Mathf.Clamp(baseX, 20f, Mathf.Max(20f, Width - layer.BaseSize.X - 20f));
                baseY = Mathf.Clamp(baseY, 10f, Mathf.Max(10f, Height - layer.BaseSize.Y - 10f));

                layer.BaseLocation = new Float2(baseX, baseY);
                layer.Image.Location = layer.BaseLocation;
            }
        }

        /// <summary>
        /// 更新所有层的颜色（当 InkColor 或 Opacity 变化时）
        /// </summary>
        private void UpdateLayerColors()
        {
            if (_layers == null)
                return;

            float[] layerAlphas = {
                0.10f, 0.08f, 0.14f, 0.06f, 0.12f, 0.07f, 0.09f, 0.11f,
                0.08f, 0.10f, 0.06f, 0.13f, 0.07f, 0.09f, 0.11f, 0.08f
            };

            for (int i = 0; i < _layers.Length; i++)
            {
                ref var layer = ref _layers[i];
                if (layer.Image == null)
                    continue;

                float baseAlpha = layerAlphas[i % layerAlphas.Length];
                layer.LayerAlpha = baseAlpha;

                // 最终透明度 = 基础透明度 * 整体透明度
                float finalAlpha = baseAlpha * Opacity;
                var color = new Color(InkColor.R, InkColor.G, InkColor.B, finalAlpha);

                layer.Image.Color = color;
            }
        }

        #endregion

        #region 贴图加载

        /// <summary>
        /// 加载贴图并应用到笔触层
        /// </summary>
        private void LoadAndApplyTextures()
        {
            if (_layers == null)
                return;

            bool anyTextureLoaded = false;

            for (int i = 0; i < _layers.Length; i++)
            {
                ref var layer = ref _layers[i];
                if (layer.Image == null)
                    continue;

                Texture texture = null;

                // 尝试从指定路径加载贴图
                if (_texturePaths != null && i < _texturePaths.Length && !string.IsNullOrEmpty(_texturePaths[i]))
                {
                    try
                    {
                        var loadedTexture = Content.LoadAsync<Texture>(_texturePaths[i]);
                        if (loadedTexture != null && loadedTexture.IsLoaded)
                        {
                            texture = loadedTexture;
                        }
                        else if (loadedTexture != null)
                        {
                            // 等待异步加载完成
                            loadedTexture.WaitForLoaded();
                            if (loadedTexture.IsLoaded)
                            {
                                texture = loadedTexture;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        FlaxEngine.Debug.LogWarning($"[InkWashBackground] 加载贴图失败 ({_texturePaths[i]}): {ex.Message}");
                    }
                }

                // 如果成功加载贴图，创建 TextureBrush
                if (texture != null)
                {
                    var brush = new TextureBrush(texture);
                    layer.Brush = brush;
                    layer.Image.Brush = brush;
                    anyTextureLoaded = true;
                }
                else
                {
                    // 使用纯色笔刷作为后备
                    float finalAlpha = layer.LayerAlpha * Opacity;
                    var brush = new SolidColorBrush(new Color(InkColor.R, InkColor.G, InkColor.B, finalAlpha));
                    layer.Image.Brush = brush;
                }

                // 设置初始颜色
                float layerFinalAlpha = layer.LayerAlpha * Opacity;
                layer.Image.Color = new Color(InkColor.R, InkColor.G, InkColor.B, layerFinalAlpha);
            }

            if (!anyTextureLoaded)
            {
                FlaxEngine.Debug.Log("[InkWashBackground] 使用纯色笔刷作为后备（未加载外部贴图）");
            }
        }

        /// <summary>
        /// 重新加载贴图（当 TexturePaths 变化时）
        /// </summary>
        private void ReloadTextures()
        {
            LoadAndApplyTextures();
        }

        #endregion

        #region 混合模式

        /// <summary>
        /// 应用混合模式到所有笔触层
        /// </summary>
        private void ApplyBlendMode()
        {
            // Flax Engine 的 Image 控件不直接支持混合模式设置
            // 这里通过调整颜色 Alpha 来模拟不同的混合效果

            if (_layers == null)
                return;

            for (int i = 0; i < _layers.Length; i++)
            {
                ref var layer = ref _layers[i];
                if (layer.Image == null)
                    continue;

                float baseAlpha = layer.LayerAlpha * Opacity;
                float adjustedAlpha = baseAlpha;

                switch (BlendMode)
                {
                    case InkBlendMode.AlphaBlend:
                        // 标准 Alpha 混合，不做调整
                        adjustedAlpha = baseAlpha;
                        break;

                    case InkBlendMode.Additive:
                        // 加法混合：降低基础 Alpha，避免过曝
                        adjustedAlpha = baseAlpha * 0.6f;
                        break;

                    case InkBlendMode.Multiply:
                        // 乘法混合：增加 Alpha，让效果更明显
                        adjustedAlpha = baseAlpha * 1.3f;
                        break;
                }

                layer.Image.Color = new Color(InkColor.R, InkColor.G, InkColor.B, adjustedAlpha);
            }
        }

        #endregion

        #region 公共方法

        /// <summary>
        /// 设置单个贴图路径
        /// </summary>
        /// <param name="path">贴图资源路径</param>
        public void SetTexturePath(string path)
        {
            TexturePaths = string.IsNullOrEmpty(path) ? Array.Empty<string>() : new string[] { path };
        }

        /// <summary>
        /// 添加一个贴图路径
        /// </summary>
        /// <param name="path">贴图资源路径</param>
        public void AddTexturePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return;

            var newList = new List<string>(_texturePaths ?? Array.Empty<string>());
            newList.Add(path);
            TexturePaths = newList.ToArray();
        }

        /// <summary>
        /// 清空所有贴图路径，使用纯色笔刷
        /// </summary>
        public void ClearTexturePaths()
        {
            TexturePaths = Array.Empty<string>();
        }

        /// <summary>
        /// 重置动画状态
        /// </summary>
        public void ResetAnimation()
        {
            _time = 0f;
            if (_layers != null)
            {
                for (int i = 0; i < _layers.Length; i++)
                {
                    if (_layers[i].Image != null)
                    {
                        _layers[i].Image.Location = _layers[i].BaseLocation;
                    }
                }
            }
        }

        #endregion

        #region 资源清理

        /// <summary>
        /// 清理资源
        /// 注意：Flax Engine 的 Control.Dispose() 不是虚方法，
        /// 因此使用 new 关键字隐藏基类方法。调用时应通过 InkWashBackground 引用调用。
        /// </summary>
        public new void Dispose()
        {
            // 清理笔触层
            if (_layers != null)
            {
                foreach (var layer in _layers)
                {
                    layer.Image?.Dispose();
                }
                _layers = null;
            }

            base.Dispose();
        }

        #endregion
    }
}
