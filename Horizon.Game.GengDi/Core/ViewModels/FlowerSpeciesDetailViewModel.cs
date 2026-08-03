using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Horizon.Game.GengDi.Core.Helpers;
using Horizon.Game.GengDi.Core.Services;
using Horizon.Game.GengDi.Core.Views;
using Horizon.Game.Message.Network;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.Painting.Effects;
using SkiaSharp;

namespace Horizon.Game.GengDi.Core.ViewModels
{
    public class FlowerSpeciesDetailViewModel : ViewModelBase
    {
        private readonly FlowerMarketService _marketService;
        private readonly FlowerSpeciesLookupService _speciesLookup = FlowerSpeciesLookupService.Instance;
        private int _speciesId;
        private string _speciesName = "";
        private string _currentPrice = "--";
        private string _priceChange = "--";
        private string _priceChangeDirection = "";
        private string _volumeInfo = "--";
        private string _forecastConfidence = "--";
        private bool _isLoading;
        private bool _hasData;
        private ObservableCollection<FlowerPriceSnapshot> _priceHistory = new();
        private ObservableCollection<AlertMessage> _recentAlerts = new();
        private ObservableCollection<RelatedProduct> _relatedProducts = new();
        private ISeries[] _candlestickSeries = Array.Empty<ISeries>();
        private ISeries[] _forecastSeries = Array.Empty<ISeries>();
        private Axis[] _xAxes = new[] { new Axis() };
        private Axis[] _yAxes = new[] { new Axis() };
        private Axis[] _forecastXAxes = new[] { new Axis() };
        private Axis[] _forecastYAxes = new[] { new Axis() };
        private ISeries[] _growthCycleSeries = Array.Empty<ISeries>();
        private Axis[] _growthCycleXAxes = new[] { new Axis() };
        private Axis[] _growthCycleYAxes = new[] { new Axis() };
        private int _selectedTimeRange = 30;

        // 品种详情百科 - 头部信息
        private string _scientificName = "";
        private string _alias = "";
        private string _origin = "";
        private string _flowerLanguage = "";
        private string _suitableSeason = "";
        private string _floweringPeriod = "";
        private string _colors = "";
        private string _difficultyLevel = "";
        private string _gradeLevel = "A级";

        // 品种详情百科 - 筛选/搜索
        private string _searchKeyword = "";
        private string _selectedCategoryChip = "全部";
        private ObservableCollection<string> _categoryChips = new();
        private ObservableCollection<string> _aliasChips = new();
        private ObservableCollection<string> _familyTags = new();

        // 品种详情百科 - 百科列表 / 相关品种
        private ObservableCollection<SpeciesCardItem> _speciesList = new();
        private ObservableCollection<RelatedSpeciesItem> _relatedSpecies = new();

        public int SpeciesId
        {
            get => _speciesId;
            set => SetProperty(ref _speciesId, value);
        }

        public string SpeciesName
        {
            get => _speciesName;
            set => SetProperty(ref _speciesName, value);
        }

        public string CurrentPrice
        {
            get => _currentPrice;
            set => SetProperty(ref _currentPrice, value);
        }

        public string PriceChange
        {
            get => _priceChange;
            set => SetProperty(ref _priceChange, value);
        }

        public string PriceChangeDirection
        {
            get => _priceChangeDirection;
            set => SetProperty(ref _priceChangeDirection, value);
        }

        public string VolumeInfo
        {
            get => _volumeInfo;
            set => SetProperty(ref _volumeInfo, value);
        }

        public string ForecastConfidence
        {
            get => _forecastConfidence;
            set => SetProperty(ref _forecastConfidence, value);
        }

        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        public bool HasData
        {
            get => _hasData;
            set => SetProperty(ref _hasData, value);
        }

        public ObservableCollection<FlowerPriceSnapshot> PriceHistory
        {
            get => _priceHistory;
            set => SetProperty(ref _priceHistory, value);
        }

        public ObservableCollection<AlertMessage> RecentAlerts
        {
            get => _recentAlerts;
            set => SetProperty(ref _recentAlerts, value);
        }

        public ObservableCollection<RelatedProduct> RelatedProducts
        {
            get => _relatedProducts;
            set => SetProperty(ref _relatedProducts, value);
        }

        public ISeries[] CandlestickSeries
        {
            get => _candlestickSeries;
            set => SetProperty(ref _candlestickSeries, value);
        }

        public ISeries[] ForecastSeries
        {
            get => _forecastSeries;
            set => SetProperty(ref _forecastSeries, value);
        }

        public Axis[] XAxes
        {
            get => _xAxes;
            set => SetProperty(ref _xAxes, value);
        }

        public Axis[] YAxes
        {
            get => _yAxes;
            set => SetProperty(ref _yAxes, value);
        }

        public Axis[] ForecastXAxes
        {
            get => _forecastXAxes;
            set => SetProperty(ref _forecastXAxes, value);
        }

        public Axis[] ForecastYAxes
        {
            get => _forecastYAxes;
            set => SetProperty(ref _forecastYAxes, value);
        }

        public ISeries[] GrowthCycleSeries
        {
            get => _growthCycleSeries;
            set => SetProperty(ref _growthCycleSeries, value);
        }

        public Axis[] GrowthCycleXAxes
        {
            get => _growthCycleXAxes;
            set => SetProperty(ref _growthCycleXAxes, value);
        }

        public Axis[] GrowthCycleYAxes
        {
            get => _growthCycleYAxes;
            set => SetProperty(ref _growthCycleYAxes, value);
        }

        public int SelectedTimeRange
        {
            get => _selectedTimeRange;
            set
            {
                if (SetProperty(ref _selectedTimeRange, value))
                {
                    _ = LoadDataAsync();
                }
            }
        }

        /// <summary>学名</summary>
        public string ScientificName { get => _scientificName; set => SetProperty(ref _scientificName, value); }

        /// <summary>别名（完整字符串，如"月季、玫瑰、长春花"）</summary>
        public string Alias { get => _alias; set => SetProperty(ref _alias, value); }

        /// <summary>原产地</summary>
        public string Origin { get => _origin; set => SetProperty(ref _origin, value); }

        /// <summary>花语</summary>
        public string FlowerLanguage { get => _flowerLanguage; set => SetProperty(ref _flowerLanguage, value); }

        /// <summary>适宜季节</summary>
        public string SuitableSeason { get => _suitableSeason; set => SetProperty(ref _suitableSeason, value); }

        /// <summary>花期</summary>
        public string FloweringPeriod { get => _floweringPeriod; set => SetProperty(ref _floweringPeriod, value); }

        /// <summary>颜色</summary>
        public string Colors { get => _colors; set => SetProperty(ref _colors, value); }

        /// <summary>难度等级</summary>
        public string DifficultyLevel { get => _difficultyLevel; set => SetProperty(ref _difficultyLevel, value); }

        /// <summary>等级标签（如 A级 / S级）</summary>
        public string GradeLevel { get => _gradeLevel; set => SetProperty(ref _gradeLevel, value); }

        /// <summary>搜索关键词</summary>
        public string SearchKeyword { get => _searchKeyword; set => SetProperty(ref _searchKeyword, value); }

        /// <summary>当前选中的分类筛选标签</summary>
        public string SelectedCategoryChip { get => _selectedCategoryChip; set => SetProperty(ref _selectedCategoryChip, value); }

        /// <summary>分类筛选标签集合</summary>
        public ObservableCollection<string> CategoryChips { get => _categoryChips; set => SetProperty(ref _categoryChips, value); }

        /// <summary>别名拆分后的 chip 集合（供头部别名 chip 展示）</summary>
        public ObservableCollection<string> AliasChips { get => _aliasChips; set => SetProperty(ref _aliasChips, value); }

        /// <summary>科属标签集合（供头部 badge 展示）</summary>
        public ObservableCollection<string> FamilyTags { get => _familyTags; set => SetProperty(ref _familyTags, value); }

        /// <summary>品种百科列表</summary>
        public ObservableCollection<SpeciesCardItem> SpeciesList { get => _speciesList; set => SetProperty(ref _speciesList, value); }

        /// <summary>底部相关品种推荐</summary>
        public ObservableCollection<RelatedSpeciesItem> RelatedSpecies { get => _relatedSpecies; set => SetProperty(ref _relatedSpecies, value); }

        public FlowerSpeciesDetailViewModel() : this(1)
        {
        }

        public FlowerSpeciesDetailViewModel(int speciesId)
        {
            _marketService = new FlowerMarketService();
            SpeciesId = speciesId;
            SpeciesName = GetSpeciesName(speciesId);
            SelectTimeRangeCommand = new SimpleRelayCommand(OnSelectTimeRange);
            NavigateToPlantingAdviceCommand = new SimpleRelayCommand(OnNavigateToPlantingAdvice);
            SelectCategoryCommand = new SimpleRelayCommand(OnSelectCategory);
            ViewSpeciesDetailCommand = new SimpleRelayCommand(OnViewSpeciesDetail);
            SearchCommand = new SimpleRelayCommand(OnSearch);
            InitEncyclopediaData();
        }

        public System.Windows.Input.ICommand SelectTimeRangeCommand { get; }
        public System.Windows.Input.ICommand NavigateToPlantingAdviceCommand { get; }
        public System.Windows.Input.ICommand SelectCategoryCommand { get; }
        public System.Windows.Input.ICommand ViewSpeciesDetailCommand { get; }
        public System.Windows.Input.ICommand SearchCommand { get; }

        private void OnSelectTimeRange(object parameter)
        {
            if (parameter is string rangeStr && int.TryParse(rangeStr, out var range))
            {
                SelectedTimeRange = range;
            }
        }

        private void OnNavigateToPlantingAdvice(object parameter)
        {
            if (App.MainWindow?.Content is Views.MainView mainView && mainView.DataContext is MainViewModel viewModel)
            {
                viewModel.NavigateToFlowerPlantingAdviceWithSpecies(SpeciesId);
            }
        }

        private void OnSelectCategory(object parameter)
        {
            if (parameter is string chip)
            {
                SelectedCategoryChip = chip;
            }
        }

        private void OnViewSpeciesDetail(object parameter)
        {
            // 预留：跳转到指定品种详情
            if (parameter is SpeciesCardItem card)
            {
                SpeciesName = card.Name;
            }
        }

        private void OnSearch(object parameter)
        {
            // 预留：按 SearchKeyword 过滤 SpeciesList
        }

        /// <summary>
        /// 初始化品种详情百科的模拟数据（头部信息、筛选标签、品种列表、相关品种）。
        /// 数据参考设计原型中的品种示例（高原红玫瑰、雪山白百合等）。
        /// </summary>
        private void InitEncyclopediaData()
        {
            // 头部信息
            ScientificName = "Rosa chinensis Jacq.";
            Alias = "月季、玫瑰、长春花";
            Origin = "中国西南地区";
            FlowerLanguage = "爱情与勇气";
            SuitableSeason = "春季 / 秋季";
            FloweringPeriod = "5–10 月";
            Colors = "红 / 粉 / 白";
            DifficultyLevel = "中等";
            GradeLevel = "A级";

            // 别名 chips
            AliasChips = new ObservableCollection<string> { "月季", "玫瑰", "长春花" };

            // 科属标签
            FamilyTags = new ObservableCollection<string> { "蔷薇科", "蔷薇属" };

            // 分类筛选
            CategoryChips = new ObservableCollection<string>
            {
                "全部", "蔷薇科", "百合科", "菊科", "兰科", "混合花束"
            };
            SelectedCategoryChip = "全部";

            // 品种百科列表
            SpeciesList = new ObservableCollection<SpeciesCardItem>
            {
                new SpeciesCardItem
                {
                    Name = "高原红玫瑰",
                    IconEmoji = "🌸",
                    Description = "蔷薇科经典品种，花色艳丽饱满，适合节庆与高端花艺供应。",
                    Tags = new ObservableCollection<string> { "红色", "春季", "A级" },
                    HeaderBrush = MakeGradientBrush("GdError", "GdBrand600")
                },
                new SpeciesCardItem
                {
                    Name = "雪山白百合",
                    IconEmoji = "🌼",
                    Description = "花型优雅，清香宜人，婚礼与庆典用花的首选品种。",
                    Tags = new ObservableCollection<string> { "白色", "夏季", "A级" },
                    HeaderBrush = MakeGradientBrush("GdInfo", "GdBrand400")
                },
                new SpeciesCardItem
                {
                    Name = "金穗向日葵",
                    IconEmoji = "🌻",
                    Description = "阳光活力，生长快速，大宗鲜切花市场的主力品种。",
                    Tags = new ObservableCollection<string> { "黄色", "夏季", "B级" },
                    HeaderBrush = MakeGradientBrush("GdWarning", "GdError")
                },
                new SpeciesCardItem
                {
                    Name = "蓝月风铃",
                    IconEmoji = "🔔",
                    Description = "稀有蓝色品种，花姿独特，适合高端花艺搭配。",
                    Tags = new ObservableCollection<string> { "蓝色", "春季", "S级" },
                    HeaderBrush = MakeGradientBrush("GdInfo", "GdBrand600")
                },
                new SpeciesCardItem
                {
                    Name = "粉黛康乃馨",
                    IconEmoji = "🌺",
                    Description = "母亲节主推品种，花期长、易养护，性价比极高。",
                    Tags = new ObservableCollection<string> { "粉色", "全年", "A级" },
                    HeaderBrush = MakeGradientBrush("GdError", "GdWarning")
                },
                new SpeciesCardItem
                {
                    Name = "紫恋鸢尾",
                    IconEmoji = "🌷",
                    Description = "优雅神秘，色彩浓郁，高端花艺与景观常用品种。",
                    Tags = new ObservableCollection<string> { "紫色", "春季", "A级" },
                    HeaderBrush = MakeGradientBrush("GdInfo", "GdError")
                }
            };

            // 底部相关品种推荐
            RelatedSpecies = new ObservableCollection<RelatedSpeciesItem>
            {
                new RelatedSpeciesItem
                {
                    Name = "高原粉玫瑰",
                    IconEmoji = "🌸",
                    Description = "粉色系玫瑰，浪漫柔美，花期持久。",
                    HeaderBrush = MakeGradientBrush("GdError", "GdWarning")
                },
                new RelatedSpeciesItem
                {
                    Name = "香水百合",
                    IconEmoji = "🌼",
                    Description = "浓香型百合，花型硕大，适合大型花艺。",
                    HeaderBrush = MakeGradientBrush("GdInfo", "GdBrand400")
                },
                new RelatedSpeciesItem
                {
                    Name = "多头向日葵",
                    IconEmoji = "🌻",
                    Description = "一枝多花，色彩明亮，性价比突出。",
                    HeaderBrush = MakeGradientBrush("GdWarning", "GdSuccess")
                },
                new RelatedSpeciesItem
                {
                    Name = "迷你康乃馨",
                    IconEmoji = "🌺",
                    Description = "小巧精致，色彩丰富，日常花束百搭。",
                    HeaderBrush = MakeGradientBrush("GdError", "GdInfo")
                }
            };

            // 生长周期图（LiveChartsCore 2.0）
            GrowthCycleSeries = FlowerChartHelper.CreateGrowthCycleSeries();
            GrowthCycleXAxes = FlowerChartHelper.CreateLabelAxis(FlowerChartHelper.GrowthCycleLabels);
            GrowthCycleYAxes = FlowerChartHelper.CreateValueAxis();
        }

        /// <summary>从主题资源 key 解析出实际画刷（找不到时回退到 SlateGray）。</summary>
        private static Avalonia.Media.IBrush ResolveThemeBrush(string resourceKey)
        {
            if (Avalonia.Application.Current?.TryGetResource(resourceKey, null, out var res) == true
                && res is Avalonia.Media.IBrush b)
            {
                return b;
            }
            return Avalonia.Media.Brushes.SlateGray;
        }

        /// <summary>用两个主题 Color 资源 key 构造对角线渐变画刷。</summary>
        private static Avalonia.Media.IBrush MakeGradientBrush(string startColorKey, string endColorKey)
        {
            var start = ResolveColor(startColorKey, Avalonia.Media.Colors.SlateGray);
            var end = ResolveColor(endColorKey, Avalonia.Media.Colors.SlateBlue);
            var brush = new Avalonia.Media.LinearGradientBrush
            {
                StartPoint = new Avalonia.RelativePoint(0, 0, Avalonia.RelativeUnit.Relative),
                EndPoint = new Avalonia.RelativePoint(1, 1, Avalonia.RelativeUnit.Relative),
            };
            brush.GradientStops.Add(new Avalonia.Media.GradientStop { Color = start, Offset = 0 });
            brush.GradientStops.Add(new Avalonia.Media.GradientStop { Color = end, Offset = 1 });
            return brush;
        }

        private static Avalonia.Media.Color ResolveColor(string resourceKey, Avalonia.Media.Color fallback)
        {
            if (Avalonia.Application.Current?.TryGetResource(resourceKey, null, out var res) == true
                && res is Avalonia.Media.Color c)
            {
                return c;
            }
            return fallback;
        }

        public async Task LoadDataAsync()
        {
            IsLoading = true;
            try
            {
                var historyTask = _marketService.GetPriceHistoryAsync(SpeciesId, SelectedTimeRange);
                var forecastTask = _marketService.GetPriceForecastAsync(SpeciesId, 14);
                var productsTask = _marketService.GetRelatedProductsAsync(SpeciesId);

                await Task.WhenAll(historyTask, forecastTask, productsTask).ConfigureAwait(false);

                var history = historyTask.Result;
                var forecast = forecastTask.Result;
                var products = productsTask.Result;

                if (history != null && history.Count > 0)
                {
                    PriceHistory = new ObservableCollection<FlowerPriceSnapshot>(history);
                    UpdateCurrentPriceInfo(history);
                    BuildCandlestickChart(history);
                    HasData = true;
                }

                if (forecast != null)
                {
                    ForecastConfidence = $"{forecast.Confidence:P0}";
                    BuildForecastChart(forecast, history);
                }

                if (products != null && products.Count > 0)
                {
                    RelatedProducts = new ObservableCollection<RelatedProduct>(products);
                }
            }
            catch
            {
                HasData = false;
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void UpdateCurrentPriceInfo(List<FlowerPriceSnapshot> history)
        {
            var latest = history.OrderByDescending(h => h.SnapshotTime).FirstOrDefault();
            if (latest == null) return;

            CurrentPrice = $"¥{latest.AvgPrice:F2}";
            VolumeInfo = $"成交量 {latest.TradeCount} 笔 / {latest.Volume} 枝";

            if (history.Count >= 2)
            {
                var previous = history.OrderByDescending(h => h.SnapshotTime).Skip(1).First();
                var change = (double)((latest.AvgPrice - previous.AvgPrice) / previous.AvgPrice * 100);
                PriceChange = $"{(change >= 0 ? "+" : "")}{change:F2}%";
                PriceChangeDirection = change >= 0 ? "↑" : "↓";
            }
        }

        private void BuildCandlestickChart(List<FlowerPriceSnapshot> history)
        {
            var sorted = history.OrderBy(h => h.SnapshotTime).ToList();
            var candles = new List<FinancialPoint>();

            for (int i = 0; i < sorted.Count; i++)
            {
                var snap = sorted[i];
                var open = i > 0 ? (double)sorted[i - 1].AvgPrice : (double)snap.AvgPrice;
                var close = (double)snap.AvgPrice;
                var high = (double)snap.MaxPrice;
                var low = (double)snap.MinPrice;

                candles.Add(new FinancialPoint(snap.SnapshotTime, open, high, low, close));
            }

            CandlestickSeries = new ISeries[]
            {
                new CandlesticksSeries<FinancialPoint>
                {
                    Values = candles,
                    Name = SpeciesName,
                    UpFill = new SolidColorPaint(SKColor.Parse("#26A69A")),
                    UpStroke = new SolidColorPaint(SKColor.Parse("#26A69A")) { StrokeThickness = 1 },
                    DownFill = new SolidColorPaint(SKColor.Parse("#EF5350")),
                    DownStroke = new SolidColorPaint(SKColor.Parse("#EF5350")) { StrokeThickness = 1 },
                }
            };

            XAxes = new Axis[]
            {
                new Axis
                {
                    Labels = sorted.Select(s => s.SnapshotTime.ToString("MM/dd")).ToArray(),
                    LabelsRotation = 45,
                    TextSize = 10,
                    SeparatorsPaint = new SolidColorPaint(SKColor.Parse("#30FFFFFF")),
                    LabelsPaint = new SolidColorPaint(SKColor.Parse("#AAAAAA")) { SKTypeface = FlowerChartHelper.CjkTypeface },
                }
            };

            YAxes = new Axis[]
            {
                new Axis
                {
                    Name = "价格 (¥)",
                    TextSize = 11,
                    SeparatorsPaint = new SolidColorPaint(SKColor.Parse("#20FFFFFF")),
                    LabelsPaint = new SolidColorPaint(SKColor.Parse("#AAAAAA")) { SKTypeface = FlowerChartHelper.CjkTypeface },
                }
            };
        }

        private void BuildForecastChart(FlowerPriceForecast forecast, List<FlowerPriceSnapshot> history)
        {
            var predictedPoints = forecast.PredictedPrices;
            if (predictedPoints == null || predictedPoints.Count == 0) return;

            var predictedValues = predictedPoints.Select(p => (double)p.PredictedPrice).ToList();
            var upperValues = predictedPoints.Select(p => (double)p.UpperBound).ToList();
            var lowerValues = predictedPoints.Select(p => (double)p.LowerBound).ToList();

            var allLabels = new List<string>();
            var historyValues = new List<double?>();

            if (history != null)
            {
                var recentHistory = history
                    .OrderByDescending(h => h.SnapshotTime)
                    .Take(14)
                    .OrderBy(h => h.SnapshotTime)
                    .ToList();

                foreach (var h in recentHistory)
                {
                    allLabels.Add(h.SnapshotTime.ToString("MM/dd"));
                    historyValues.Add((double)h.AvgPrice);
                }
            }

            foreach (var p in predictedPoints)
            {
                allLabels.Add(p.Date.ToString("MM/dd"));
                historyValues.Add(null);
            }

            var forecastLine = new List<double?>();
            var upperLine = new List<double?>();
            var lowerLine = new List<double?>();

            for (int i = 0; i < historyValues.Count - predictedPoints.Count; i++)
            {
                forecastLine.Add(null);
                upperLine.Add(null);
                lowerLine.Add(null);
            }

            foreach (var p in predictedPoints)
            {
                forecastLine.Add((double)p.PredictedPrice);
                upperLine.Add((double)p.UpperBound);
                lowerLine.Add((double)p.LowerBound);
            }

            ForecastSeries = new ISeries[]
            {
                new LineSeries<double?>
                {
                    Values = historyValues,
                    Name = "历史均价",
                    Fill = null,
                    Stroke = new SolidColorPaint(SKColor.Parse("#42A5F5")) { StrokeThickness = 2 },
                    GeometrySize = 0,
                },
                new LineSeries<double?>
                {
                    Values = forecastLine,
                    Name = "预测价格",
                    Fill = null,
                    Stroke = new SolidColorPaint(SKColor.Parse("#FFA726")) { StrokeThickness = 2, PathEffect = new DashEffect(new float[] { 6, 3 }) },
                    GeometrySize = 0,
                },
                new LineSeries<double?>
                {
                    Values = upperValues.Cast<double?>().ToList(),
                    Name = "预测上界",
                    Fill = null,
                    Stroke = new SolidColorPaint(SKColor.Parse("#66BB6A40")) { StrokeThickness = 1 },
                    GeometrySize = 0,
                },
                new LineSeries<double?>
                {
                    Values = lowerValues.Cast<double?>().ToList(),
                    Name = "预测下界",
                    Fill = null,
                    Stroke = new SolidColorPaint(SKColor.Parse("#66BB6A40")) { StrokeThickness = 1 },
                    GeometrySize = 0,
                },
            };

            ForecastXAxes = new Axis[]
            {
                new Axis
                {
                    Labels = allLabels.ToArray(),
                    LabelsRotation = 45,
                    TextSize = 10,
                    SeparatorsPaint = new SolidColorPaint(SKColor.Parse("#30FFFFFF")),
                    LabelsPaint = new SolidColorPaint(SKColor.Parse("#AAAAAA")) { SKTypeface = FlowerChartHelper.CjkTypeface },
                }
            };

            ForecastYAxes = new Axis[]
            {
                new Axis
                {
                    Name = "价格 (¥)",
                    TextSize = 11,
                    SeparatorsPaint = new SolidColorPaint(SKColor.Parse("#20FFFFFF")),
                    LabelsPaint = new SolidColorPaint(SKColor.Parse("#AAAAAA")) { SKTypeface = FlowerChartHelper.CjkTypeface },
                }
            };
        }

        private string GetSpeciesName(int speciesId) => _speciesLookup.GetSpeciesName(speciesId);
    }

    /// <summary>
    /// 品种百科列表卡片项（品种详情百科 section 品种百科列表网格）。
    /// HeaderBrush 为运行时从主题资源解析得到的渐变画刷，供 XAML 头部背景绑定。
    /// </summary>
    public class SpeciesCardItem : ViewModelBase
    {
        private string _name = "";
        private string _iconEmoji = "";
        private string _description = "";
        private ObservableCollection<string> _tags = new();
        private Avalonia.Media.IBrush _headerBrush = Avalonia.Media.Brushes.SlateGray;

        public string Name { get => _name; set => SetProperty(ref _name, value); }
        public string IconEmoji { get => _iconEmoji; set => SetProperty(ref _iconEmoji, value); }
        public string Description { get => _description; set => SetProperty(ref _description, value); }
        public ObservableCollection<string> Tags { get => _tags; set => SetProperty(ref _tags, value); }
        public Avalonia.Media.IBrush HeaderBrush { get => _headerBrush; set => SetProperty(ref _headerBrush, value); }
    }

    /// <summary>
    /// 底部相关品种推荐项（品种详情百科 section 相关品种推荐）。
    /// </summary>
    public class RelatedSpeciesItem : ViewModelBase
    {
        private string _name = "";
        private string _iconEmoji = "";
        private string _description = "";
        private Avalonia.Media.IBrush _headerBrush = Avalonia.Media.Brushes.SlateGray;

        public string Name { get => _name; set => SetProperty(ref _name, value); }
        public string IconEmoji { get => _iconEmoji; set => SetProperty(ref _iconEmoji, value); }
        public string Description { get => _description; set => SetProperty(ref _description, value); }
        public Avalonia.Media.IBrush HeaderBrush { get => _headerBrush; set => SetProperty(ref _headerBrush, value); }
    }
}
