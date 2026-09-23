using System;
using Avalonia.Media;

namespace Horizon.Game.GengDi.Core.Helpers
{
    /// <summary>
    /// 单个花卉品种的视觉/产地映射结果。
    /// </summary>
    public readonly struct FlowerSpeciesVisual
    {
        /// <summary>品种主色（封面渐变起点 / 品种标签底色）。</summary>
        public Color Accent { get; init; }

        /// <summary>品种深色（封面渐变终点）。</summary>
        public Color AccentDeep { get; init; }

        /// <summary>主产区，用于卡片封面产地文案。</summary>
        public string Origin { get; init; }

        /// <summary>常规时效，用于卡片封面时效文案。</summary>
        public string Eta { get; init; }
    }

    /// <summary>
    /// 花卉品种视觉映射表 — 品种主色（无图降级封面渐变）+ 主产区 + 常规时效。
    /// 色值与 GdTheme.axaml 的 GdSpecies* 令牌保持一致；产区与时效为业务常量，
    /// 集中在此处便于随供应链调整。
    /// </summary>
    public static class FlowerSpeciesPalette
    {
        private static readonly (string Keyword, FlowerSpeciesVisual Visual)[] Table =
        {
            ("玫瑰", new FlowerSpeciesVisual
            {
                Accent = Color.Parse("#E8617D"), AccentDeep = Color.Parse("#C2435E"),
                Origin = "云南昆明", Eta = "24h 直达"
            }),
            ("百合", new FlowerSpeciesVisual
            {
                Accent = Color.Parse("#D98CB8"), AccentDeep = Color.Parse("#A85F8C"),
                Origin = "福建漳州", Eta = "24h 直达"
            }),
            ("康乃馨", new FlowerSpeciesVisual
            {
                Accent = Color.Parse("#F08A5D"), AccentDeep = Color.Parse("#C25F33"),
                Origin = "辽宁凌源", Eta = "48h 直达"
            }),
            ("郁金香", new FlowerSpeciesVisual
            {
                Accent = Color.Parse("#C86BE8"), AccentDeep = Color.Parse("#8E44AD"),
                Origin = "云南昆明", Eta = "48h 直达"
            }),
            ("向日葵", new FlowerSpeciesVisual
            {
                Accent = Color.Parse("#F2B33D"), AccentDeep = Color.Parse("#C98A16"),
                Origin = "云南昆明", Eta = "24h 直达"
            }),
            ("蝴蝶兰", new FlowerSpeciesVisual
            {
                Accent = Color.Parse("#B07BE8"), AccentDeep = Color.Parse("#7B4FA8"),
                Origin = "广东汕头", Eta = "48h 直达"
            }),
            ("菊", new FlowerSpeciesVisual
            {
                Accent = Color.Parse("#E8B45D"), AccentDeep = Color.Parse("#B8862F"),
                Origin = "广东广州", Eta = "48h 直达"
            }),
            ("尤加利", new FlowerSpeciesVisual
            {
                Accent = Color.Parse("#3FA37A"), AccentDeep = Color.Parse("#26735A"),
                Origin = "云南昆明", Eta = "24h 直达"
            }),
            ("叶材", new FlowerSpeciesVisual
            {
                Accent = Color.Parse("#3FA37A"), AccentDeep = Color.Parse("#26735A"),
                Origin = "云南昆明", Eta = "24h 直达"
            }),
            ("配草", new FlowerSpeciesVisual
            {
                Accent = Color.Parse("#4FA383"), AccentDeep = Color.Parse("#2A7360"),
                Origin = "云南昆明", Eta = "24h 直达"
            }),
            ("满天星", new FlowerSpeciesVisual
            {
                Accent = Color.Parse("#8FA8D9"), AccentDeep = Color.Parse("#5F76A8"),
                Origin = "云南昆明", Eta = "24h 直达"
            }),
        };

        private static readonly FlowerSpeciesVisual Fallback = new()
        {
            Accent = Color.Parse("#5A90FF"), AccentDeep = Color.Parse("#1F4FD6"),
            Origin = "云南昆明", Eta = "24h 直达"
        };

        /// <summary>
        /// 按品种名关键词解析视觉映射，未命中时返回默认（品牌蓝 + 昆明 24h）。
        /// </summary>
        public static FlowerSpeciesVisual Resolve(string? speciesName)
        {
            if (!string.IsNullOrWhiteSpace(speciesName))
            {
                foreach (var (keyword, visual) in Table)
                {
                    if (speciesName!.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                        return visual;
                }
            }

            return Fallback;
        }
    }
}
