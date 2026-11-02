// PCG 插件 - 纹理采样节点
// 属于 FlaxEngine PCG 插件。见 docs/PCG_PLUGIN_DESIGN.md §5.1.22。

using System;
using FlaxEngine;
using PCG.Execution;

namespace PCG.Nodes
{
    /// <summary>
    /// 在每个点的位置上读一张纹理，把结果写成属性——一张手绘的掩码，驱动一片
    /// 散布。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 这是唯一一个“输入是画出来的、不是算出来的”采样器：手绘的密度图、
    /// 别的工具导出的植被掩码、一张不是地形自己的高度图。算术在
    /// <see cref="PCGTextureSampling"/> 里，它从不碰引擎、离线就有测试；本类
    /// 只是适配器：把像素从资产里取出来，再把世界坐标映射到纹理坐标。
    /// </para>
    /// <para>
    /// 映射用的是本插件一贯的那套：图像的一份完整拷贝在 X 与 Z 上各占 <see cref="TileSize"/> 个世界单位，
    /// 以原点为中心，图像第一行在负 Z。图像各行朝 +Z 方向排。
    /// </para>
    /// <para>
    /// <b>成本，直说：</b>每次执行都重新读并提取像素，所以在分区或
    /// 流式体积里，本节点每格都要付这笔钱。它刻意不做缓存：跨节点共享的
    /// 缓存会竞争，而只属于一个节点的缓存在多格并行跑时照样会被重入。
    /// 加缓存之前先量。
    /// </para>
    /// </remarks>
    public sealed class PCGTextureSamplerNode : PCGNodeBase
    {
        /// <summary>稳定的类型 id。</summary>
        public const string Id = "TextureSampler";

        // 参数下标。顺序是已存图格式的一部分；只许追加。
        private const int PTexturePath = 0;
        private const int PAttribute = 1;
        private const int PChannel = 2;
        private const int PAddress = 3;
        private const int PFilter = 4;
        private const int PTileSize = 5;
        private const int PInvert = 6;

        /// <summary>本节点读取的参数个数。</summary>
        public const int ParameterCount = 7;

        /// <summary>没有反序列化值时建节点所写的列。</summary>
        public const string DefaultAttribute = "texture";

        private readonly string _typeId;
        private readonly PCGNodeParameters _params;

        /// <summary>要读的纹理的内容路径。</summary>
        public string TexturePath = string.Empty;

        /// <summary>采样值写进哪一列。留空则写点的密度。</summary>
        public string AttributeName = DefaultAttribute;

        /// <summary>读哪条通道。</summary>
        public PCGTextureChannel Channel = PCGTextureChannel.Luminance;

        /// <summary>图像之外的坐标怎么折回来。</summary>
        public PCGTextureAddress Address = PCGTextureAddress.Clamp;

        /// <summary>取最近的纹素，或对四个做双线性混合。</summary>
        public PCGTextureFilter Filter = PCGTextureFilter.Bilinear;

        /// <summary>图像一份完整拷贝占多少世界单位。</summary>
        public float TileSize = 1000.0f;

        /// <summary>打开时把采样值翻转，于是白色表示“否”而不是“是”。</summary>
        public bool Invert;

        /// <summary>用反序列化出来的参数建节点。</summary>
        public PCGTextureSamplerNode(PCGNodeRegistry.PCGNodeSerializationData data)
        {
            _typeId = Id;
            _params = new PCGNodeParameters(data.Values, data.TypeId);

            TexturePath = _params.Get(PTexturePath, string.Empty) ?? string.Empty;
            AttributeName = _params.Get(PAttribute, DefaultAttribute) ?? string.Empty;
            Channel = _params.Get(PChannel, PCGTextureChannel.Luminance);
            Address = _params.Get(PAddress, PCGTextureAddress.Clamp);
            Filter = _params.Get(PFilter, PCGTextureFilter.Bilinear);
            TileSize = Math.Max(1e-3f, _params.Get(PTileSize, 1000.0f));
            Invert = _params.Get(PInvert, false);
        }

        /// <inheritdoc />
        public override string TypeId => _typeId;

        /// <inheritdoc />
        public override string DisplayName => "Texture Sampler";

        /// <inheritdoc />
        public override bool IsThreadSafe => true;

        /// <inheritdoc />
        public override PCGPinDefinition[] InputPins => new[]
        {
            new PCGPinDefinition("In", PCGPinDirection.Input, isRequired: true,
                tooltip: "Points whose positions are sampled against the texture."),
        };

        /// <inheritdoc />
        public override PCGPinDefinition[] OutputPins => new[]
        {
            new PCGPinDefinition("Out", PCGPinDirection.Output,
                tooltip: "The same points, each carrying the sampled value."),
        };

        /// <inheritdoc />
        public override string DescribeParameters()
        {
            string name = string.IsNullOrEmpty(AttributeName) ? PCGAttributeSet.DensityName : AttributeName;
            string path = string.IsNullOrEmpty(TexturePath) ? "(none)" : TexturePath;
            return $"{name} = {Channel} of {path} ({Filter}/{Address}), tile={TileSize:F1}, invert={Invert}";
        }

        /// <inheritdoc />
        public override PCGData OnExecute(PCGExecutionContext context, PCGData[] inputs)
        {
            var input = inputs != null && inputs.Length > 0 ? inputs[0] : null;
            if (input == null || input.Count == 0)
                return PCGData.Empty;

            if (string.IsNullOrEmpty(TexturePath))
            {
                LastError = "Texture Sampler has no texture assigned. The input passes through unchanged.";
                return input.Clone();
            }

            // 同步取、取不到给 null，与各生成器的选择一致：一次图运行必须拿到答案
            // 才算完，而纹理没加载上是一个值得报出来的配置错误。
            var texture = Content.Load<Texture>(TexturePath);
            if (texture == null)
            {
                LastError = $"Texture Sampler could not load '{TexturePath}'. The input passes through unchanged.";
                return input.Clone();
            }

            // 引擎在这里失败时返回 true，读起来是反的；这个标志就是全部契约。
            if (!texture.GetPixels(out Color32[] pixels) || pixels == null)
            {
                LastError = $"Texture Sampler could not read '{TexturePath}': only uncompressed, single-plane " +
                            "textures have CPU-accessible pixels. The input passes through unchanged.";
                return input.Clone();
            }

            int width = texture.Width;
            int height = texture.Height;

            if (width <= 0 || height <= 0 || pixels.Length < width * height)
            {
                LastError = $"Texture Sampler read {pixels.Length} pixel(s) for a {width}x{height} texture. " +
                            "The input passes through unchanged.";
                return input.Clone();
            }

            if ((long)width * height > PCGTextureSampling.MaxPixels)
            {
                LastError = $"Texture Sampler refuses a {width}x{height} texture (cap " +
                            $"{PCGTextureSampling.MaxPixels} pixel(s)). The input passes through unchanged.";
                return input.Clone();
            }

            var grid = PCGTextureSampling.ExtractChannel(pixels, Channel);

            bool toDensity = string.IsNullOrEmpty(AttributeName) ||
                             string.Equals(AttributeName, PCGAttributeSet.DensityName, StringComparison.Ordinal);

            if (toDensity)
            {
                var result = input.Clone();
                var targetPoints = result.Points;

                for (int i = 0; i < targetPoints.Length; i++)
                {
                    context.ThrowIfCancelled(i);
                    targetPoints[i].Density = Read(grid, width, height, targetPoints[i].Position);
                }

                return result;
            }

            var cloned = input.Clone();
            var attributes = EnsureAttributes(cloned);

            if (!attributes.Contains(AttributeName))
                attributes.DeclareAttributeUnsafe(AttributeName, PCGAttributeType.Float);

            var column = attributes.GetWritableFloatArray(AttributeName);
            var points = cloned.Points;

            for (int i = 0; i < points.Length; i++)
            {
                context.ThrowIfCancelled(i);
                column[i] = Read(grid, width, height, points[i].Position);
            }

            return cloned;
        }

        /// <summary>把世界坐标映射到图像上并采样。</summary>
        private float Read(float[] grid, int width, int height, in Vector3 position)
        {
            var uv = PCGTextureSampling.UvFromWorld(position, TileSize);
            float value = PCGTextureSampling.Sample(grid, width, height, uv.X, uv.Y, Address, Filter);

            return Invert ? 1.0f - value : value;
        }

        /// <summary>数据集里的属性存储；缺失时创建并盖到它的点上。</summary>
        private static PCGAttributeSet EnsureAttributes(PCGData data)
        {
            var attributes = data.SharedAttributes;

            if (attributes == null)
            {
                attributes = new PCGAttributeSet(data.Count);
                data.SharedAttributes = attributes;

                for (int i = 0; i < data.Points.Length; i++)
                    data.Points[i].Attributes = attributes;
            }

            return attributes;
        }
    }
}
