using System;
using System.Collections.Generic;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

namespace Horizon.Game.GengDi.Core.Helpers
{
    /// <summary>
    /// 花卉市场图表辅助类 — 基于 LiveChartsCore 2.0 生成模拟数据系列。
    /// 颜色值严格对应 GdTheme.axaml 中的 token，确保图表与 UI 风格统一。
    /// 所有 Paint 均设置中文字体 SKTypeface，避免中文标签渲染为方块。
    /// </summary>
    public static class FlowerChartHelper
    {
        // ==== GdTheme 颜色常量（ARGB → SKColor） ====
        public static readonly SKColor Brand = new(0x29, 0x62, 0xFF);       // GdBrand500 / GdPrimary / GdInfo
        public static readonly SKColor Brand400 = new(0x3A, 0x78, 0xFF);    // GdBrand400
        public static readonly SKColor Brand600 = new(0x1F, 0x4F, 0xD6);    // GdBrand600
        public static readonly SKColor Success = new(0x26, 0xA6, 0x9A);     // GdSuccess / GdUp
        public static readonly SKColor Warning = new(0xFF, 0x98, 0x00);     // GdWarning
        public static readonly SKColor Error = new(0xEF, 0x53, 0x50);       // GdError / GdDown
        public static readonly SKColor MutedForeground = new(0x78, 0x7B, 0x86); // GdMutedForeground

        // ==== 中文字体 SKTypeface ====
        // SkiaSharp 默认字体不含中文字形，必须显式指定支持中文的字体。
        // 优先尝试 Microsoft YaHei（Windows），回退 Noto Sans SC（Linux），再回退 SimSun。
        public static readonly SKTypeface CjkTypeface = LoadCjkTypeface();

        private static SKTypeface LoadCjkTypeface()
        {
            // 按优先级尝试多个中文字体
            string[] families = { "Microsoft YaHei", "Noto Sans SC", "SimSun", "PingFang SC", "WenQuanYi Micro Hei" };
            foreach (var family in families)
            {
                var tf = SKTypeface.FromFamilyName(family);
                if (tf != null)
                    return tf;
            }
            return SKTypeface.Default;
        }

        /// <summary>创建带中文字体的 SolidColorPaint</summary>
        private static SolidColorPaint Paint(SKColor color, float strokeWidth = 0)
        {
            var p = new SolidColorPaint(color) { SKTypeface = CjkTypeface };
            if (strokeWidth > 0)
                p.StrokeThickness = strokeWidth;
            return p;
        }

        /// <summary>创建带中文字体的透明分隔线 Paint</summary>
        private static SolidColorPaint SeparatorPaint => new(new SKColor(0xFF, 0xFF, 0xFF, 0x0F));

        // ==== Tooltip 画笔（带中文字体，修复提示文字显示为方框） ====
        // LiveCharts2 的 Tooltip 默认使用 SkiaSharp 系统字体，不含中文字形。
        // 通过 TooltipTextPaint AvaloniaProperty 注入带 CjkTypeface 的画笔。

        /// <summary>Tooltip 文字画笔（GdForeground #E0E6ED + 中文字体）</summary>
        public static SolidColorPaint DefaultTooltipTextPaint { get; } = new(new SKColor(0xE0, 0xE6, 0xED))
        {
            SKTypeface = CjkTypeface
        };

        /// <summary>Tooltip 背景画笔（GdPopover #1A1F2E）</summary>
        public static SolidColorPaint DefaultTooltipBackgroundPaint { get; } = new(new SKColor(0x1A, 0x1F, 0x2E));

        /// <summary>默认 X 轴数组（含1个空 Axis），避免 CartesianChart 绑定空数组抛异常</summary>
        public static Axis[] DefaultXAxes => new Axis[] { new Axis() };

        /// <summary>默认 Y 轴数组（含1个空 Axis），避免 CartesianChart 绑定空数组抛异常</summary>
        public static Axis[] DefaultYAxes => new Axis[] { new Axis() };

        /// <summary>创建标签轴（X 轴），使用 GdMutedForeground 颜色 + 中文字体</summary>
        public static Axis[] CreateLabelAxis(string[] labels)
        {
            return new Axis[]
            {
                new Axis
                {
                    Labels = labels,
                    LabelsPaint = Paint(MutedForeground),
                    TextSize = 11,
                    SeparatorsPaint = SeparatorPaint,
                    TicksPaint = Paint(MutedForeground),
                }
            };
        }

        /// <summary>创建数值轴（Y 轴），使用 GdMutedForeground 颜色 + 中文字体</summary>
        public static Axis[] CreateValueAxis(string? labeler = null)
        {
            var axis = new Axis
            {
                LabelsPaint = Paint(MutedForeground),
                TextSize = 11,
                SeparatorsPaint = SeparatorPaint,
                TicksPaint = Paint(MutedForeground),
            };
            if (labeler != null)
                axis.Labeler = v => labeler;
            return new Axis[] { axis };
        }

        // ================================================================
        //  商家管理 — 营收趋势（7 日折线图）
        // ================================================================
        public static ISeries[] CreateRevenueTrendSeries()
        {
            var values = new double[] { 2860, 3120, 2980, 3540, 3280, 3920, 3860 };
            return new ISeries[]
            {
                new LineSeries<double>
                {
                    Values = values,
                    Name = "营收",
                    Stroke = Paint(Brand, strokeWidth: 2),
                    Fill = new SolidColorPaint(new SKColor(0x29, 0x62, 0xFF, 0x33)),
                    GeometrySize = 6,
                    GeometryStroke = Paint(Brand, strokeWidth: 2),
                    GeometryFill = new SolidColorPaint(SKColors.White),
                    LineSmoothness = 0.3,
                }
            };
        }

        public static string[] RevenueTrendLabels => new[] { "7/20", "7/21", "7/22", "7/23", "7/24", "7/25", "7/26" };

        // ================================================================
        //  商家管理 — 品类销售占比（饼图）
        // ================================================================
        public static ISeries[] CreateCategoryPieSeries()
        {
            return new ISeries[]
            {
                new PieSeries<double>
                {
                    Values = new double[] { 38 },
                    Name = "红玫瑰",
                    Fill = new SolidColorPaint(Error),
                    DataLabelsPaint = Paint(SKColors.White),
                    DataLabelsSize = 12,
                    DataLabelsPosition = LiveChartsCore.Measure.PolarLabelsPosition.Middle,
                },
                new PieSeries<double>
                {
                    Values = new double[] { 25 },
                    Name = "百合",
                    Fill = new SolidColorPaint(Brand),
                    DataLabelsPaint = Paint(SKColors.White),
                    DataLabelsSize = 12,
                    DataLabelsPosition = LiveChartsCore.Measure.PolarLabelsPosition.Middle,
                },
                new PieSeries<double>
                {
                    Values = new double[] { 22 },
                    Name = "混合花束",
                    Fill = new SolidColorPaint(Success),
                    DataLabelsPaint = Paint(SKColors.White),
                    DataLabelsSize = 12,
                    DataLabelsPosition = LiveChartsCore.Measure.PolarLabelsPosition.Middle,
                },
                new PieSeries<double>
                {
                    Values = new double[] { 15 },
                    Name = "康乃馨",
                    Fill = new SolidColorPaint(Warning),
                    DataLabelsPaint = Paint(SKColors.White),
                    DataLabelsSize = 12,
                    DataLabelsPosition = LiveChartsCore.Measure.PolarLabelsPosition.Middle,
                }
            };
        }

        // ================================================================
        //  数据大屏 — 价格走势图（多品种折线图）
        // ================================================================
        public static ISeries[] CreatePriceTrendSeries()
        {
            return new ISeries[]
            {
                new LineSeries<double>
                {
                    Values = new double[] { 8.2, 8.5, 8.3, 8.6, 8.8, 8.5, 8.5 },
                    Name = "红玫瑰",
                    Stroke = Paint(Error, strokeWidth: 2),
                    GeometrySize = 4,
                    LineSmoothness = 0.4,
                },
                new LineSeries<double>
                {
                    Values = new double[] { 15.0, 15.2, 14.8, 15.5, 15.3, 15.2, 15.2 },
                    Name = "百合",
                    Stroke = Paint(Brand, strokeWidth: 2),
                    GeometrySize = 4,
                    LineSmoothness = 0.4,
                },
                new LineSeries<double>
                {
                    Values = new double[] { 6.5, 6.8, 6.7, 6.9, 6.8, 6.8, 6.8 },
                    Name = "康乃馨",
                    Stroke = Paint(Success, strokeWidth: 2),
                    GeometrySize = 4,
                    LineSmoothness = 0.4,
                },
            };
        }

        public static string[] PriceTrendLabels => new[] { "7/20", "7/21", "7/22", "7/23", "7/24", "7/25", "7/26" };

        // ================================================================
        //  数据大屏 — 品类占比图（饼图）
        // ================================================================
        public static ISeries[] CreateScreenCategoryPieSeries()
        {
            return new ISeries[]
            {
                new PieSeries<double>
                {
                    Values = new double[] { 35 },
                    Name = "红玫瑰",
                    Fill = new SolidColorPaint(Error),
                    DataLabelsPaint = Paint(SKColors.White),
                    DataLabelsSize = 11,
                    DataLabelsPosition = LiveChartsCore.Measure.PolarLabelsPosition.Middle,
                },
                new PieSeries<double>
                {
                    Values = new double[] { 28 },
                    Name = "百合",
                    Fill = new SolidColorPaint(Brand),
                    DataLabelsPaint = Paint(SKColors.White),
                    DataLabelsSize = 11,
                    DataLabelsPosition = LiveChartsCore.Measure.PolarLabelsPosition.Middle,
                },
                new PieSeries<double>
                {
                    Values = new double[] { 20 },
                    Name = "混合花束",
                    Fill = new SolidColorPaint(Success),
                    DataLabelsPaint = Paint(SKColors.White),
                    DataLabelsSize = 11,
                    DataLabelsPosition = LiveChartsCore.Measure.PolarLabelsPosition.Middle,
                },
                new PieSeries<double>
                {
                    Values = new double[] { 17 },
                    Name = "康乃馨",
                    Fill = new SolidColorPaint(Warning),
                    DataLabelsPaint = Paint(SKColors.White),
                    DataLabelsSize = 11,
                    DataLabelsPosition = LiveChartsCore.Measure.PolarLabelsPosition.Middle,
                }
            };
        }

        // ================================================================
        //  数据大屏 — 地区分布图（柱状图）
        // ================================================================
        public static ISeries[] CreateRegionColumnSeries()
        {
            return new ISeries[]
            {
                new ColumnSeries<double>
                {
                    Values = new double[] { 856, 620, 480, 350, 280, 180 },
                    Name = "交易额(万)",
                    Stroke = null,
                    Fill = new SolidColorPaint(Brand),
                    Rx = 4,
                    Ry = 4,
                }
            };
        }

        public static string[] RegionLabels => new[] { "昆明", "广州", "上海", "北京", "成都", "西安" };

        // ================================================================
        //  数据大屏 — 实时交易流（折线图，动态数据）
        // ================================================================
        public static ISeries[] CreateTradeFlowSeries()
        {
            var values = new double[] { 124, 138, 115, 142, 128, 156, 134, 148, 162, 140, 155, 168 };
            return new ISeries[]
            {
                new LineSeries<double>
                {
                    Values = values,
                    Name = "交易量",
                    Stroke = Paint(Success, strokeWidth: 2),
                    Fill = new SolidColorPaint(new SKColor(0x26, 0xA6, 0x9A, 0x33)),
                    GeometrySize = 4,
                    LineSmoothness = 0.5,
                }
            };
        }

        public static string[] TradeFlowLabels => new[]
        {
            "09:00", "09:30", "10:00", "10:30", "11:00", "11:30",
            "12:00", "12:30", "13:00", "13:30", "14:00", "14:30"
        };

        // ================================================================
        //  预警中心 — 预警趋势图（柱状图，近 7 日预警分布）
        // ================================================================
        public static ISeries[] CreateAlertTrendSeries()
        {
            return new ISeries[]
            {
                new ColumnSeries<double>
                {
                    Values = new double[] { 3, 5, 2, 7, 4, 6, 5 },
                    Name = "预警数",
                    Stroke = null,
                    Fill = new SolidColorPaint(Warning),
                    Rx = 4,
                    Ry = 4,
                }
            };
        }

        public static string[] AlertTrendLabels => new[] { "7/20", "7/21", "7/22", "7/23", "7/24", "7/25", "7/26" };

        // ================================================================
        //  商品详情 — 近 30 日价格走势（折线图）
        // ================================================================
        public static ISeries[] CreateProductPriceTrendSeries()
        {
            var values = new double[]
            {
                96, 94, 95, 92, 90, 88, 89, 87, 85, 86,
                84, 82, 83, 81, 80, 82, 79, 78, 80, 77,
                76, 78, 75, 74, 76, 73, 72, 74, 71, 88
            };
            return new ISeries[]
            {
                new LineSeries<double>
                {
                    Values = values,
                    Name = "成交均价",
                    Stroke = Paint(Brand, strokeWidth: 2),
                    Fill = new SolidColorPaint(new SKColor(0x29, 0x62, 0xFF, 0x22)),
                    GeometrySize = 0,
                    LineSmoothness = 0.3,
                }
            };
        }

        public static string[] ProductPriceTrendLabels =>
            GenerateDayLabels(30, new DateTime(2026, 6, 27));

        // ================================================================
        //  品种详情 — 生长周期图（柱状图，5 阶段）
        // ================================================================
        public static ISeries[] CreateGrowthCycleSeries()
        {
            return new ISeries[]
            {
                new ColumnSeries<double>
                {
                    Values = new double[] { 7, 14, 21, 28, 20 },
                    Name = "天数",
                    Stroke = null,
                    Fill = new SolidColorPaint(Brand400),
                    Rx = 4,
                    Ry = 4,
                    Padding = 8,
                }
            };
        }

        public static string[] GrowthCycleLabels => new[] { "播种", "育苗", "生长", "花蕾", "开花" };

        // ================================================================
        //  辅助方法
        // ================================================================

        /// <summary>生成 N 天的日期标签（M/d 格式）</summary>
        private static string[] GenerateDayLabels(int count, DateTime startDate)
        {
            var labels = new string[count];
            for (int i = 0; i < count; i++)
                labels[i] = startDate.AddDays(i).ToString("M/d");
            return labels;
        }
    }
}
