using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
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
        private Axis[] _xAxes = Array.Empty<Axis>();
        private Axis[] _yAxes = Array.Empty<Axis>();
        private Axis[] _forecastXAxes = Array.Empty<Axis>();
        private Axis[] _forecastYAxes = Array.Empty<Axis>();
        private int _selectedTimeRange = 30;

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
        }

        public System.Windows.Input.ICommand SelectTimeRangeCommand { get; }
        public System.Windows.Input.ICommand NavigateToPlantingAdviceCommand { get; }

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
                    LabelsPaint = new SolidColorPaint(SKColor.Parse("#AAAAAA")),
                }
            };

            YAxes = new Axis[]
            {
                new Axis
                {
                    Name = "价格 (¥)",
                    TextSize = 11,
                    SeparatorsPaint = new SolidColorPaint(SKColor.Parse("#20FFFFFF")),
                    LabelsPaint = new SolidColorPaint(SKColor.Parse("#AAAAAA")),
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
                    LabelsPaint = new SolidColorPaint(SKColor.Parse("#AAAAAA")),
                }
            };

            ForecastYAxes = new Axis[]
            {
                new Axis
                {
                    Name = "价格 (¥)",
                    TextSize = 11,
                    SeparatorsPaint = new SolidColorPaint(SKColor.Parse("#20FFFFFF")),
                    LabelsPaint = new SolidColorPaint(SKColor.Parse("#AAAAAA")),
                }
            };
        }

        private string GetSpeciesName(int speciesId) => _speciesLookup.GetSpeciesName(speciesId);
    }
}
