using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Input;
using Horizon.Game.GengDi.Core.Services;
using Horizon.Game.Message.Network;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

namespace Horizon.Game.GengDi.Core.ViewModels
{
    public class FlowerPlantingAdviceViewModel : ViewModelBase
    {
        private readonly FlowerIoTService _iotService;
        private readonly FlowerShopService _shopService;
        private readonly FlowerAIService _aiService;
        private readonly FlowerMerchantService _merchantService;
        private readonly FlowerSpeciesLookupService _speciesLookup = FlowerSpeciesLookupService.Instance;
        private readonly FlowerMqttClientService _mqttService = FlowerMqttClientService.Instance;
        private SensorReadingData _sensorData = new();
        private ObservableCollection<HarvestAdviceItem> _harvestAdvices = new();
        private ObservableCollection<PestAlertItem> _pestAlerts = new();
        private bool _isLoading;
        private int _selectedTabIndex;
        private decimal _monthlyCost;
        private decimal _monthlyYield;
        private string _currentTimeRange = "24h";
        private string _currentTimeRangeDisplay = "24h";
        private ObservableCollection<ThresholdAlertItem> _thresholdAlerts = new();
        private ObservableCollection<AdviceDisplayItem> _activeAdvices = new();
        private ObservableCollection<CostDisplayItem> _costRecords = new();
        private ObservableCollection<YieldDisplayItem> _yieldRecords = new();
        private ObservableCollection<DeviceGroupDisplayItem> _deviceGroups = new();
        private string _currentGreenhouseId = "default";
        private ObservableCollection<string> _greenhouseList = new() { "default" };
        private string _selectedGreenhouse = "default";
        private long _currentBatchId;
        private ObservableCollection<PlantingBatchInfo> _batches = new();
        private PlantingBatchInfo _selectedBatch;
        private string _adviceFilter = "All";
        private string _speciesFilter = "";
        private bool _isCostDialogOpen;
        private bool _isYieldDialogOpen;
        private bool _isDeviceDialogOpen;
        private bool _isGroupDialogOpen;
        private bool _isBindDialogOpen;
        private string _bindDeviceCode = "";
        private string _bindGroupId = "";
        private bool _isChangeGroupDialogOpen;
        private ObservableCollection<GroupSelectItem> _availableGroups = new();
        private GroupSelectItem? _changeGroupTargetGroupId;
        private string _changeGroupDeviceCode = "";
        private ObservableCollection<DeviceComparisonItem> _comparisonDevices = new();
        private ObservableCollection<DeviceComparisonResultItem> _comparisonResults = new();
        private bool _isComparisonLoading;
        private HealthIndexInfo _healthIndex;
        private ObservableCollection<AnomalyInfo> _anomalies = new();
        private ObservableCollection<SignificantChangePointInfo> _significantChanges = new();
        private string _costCategory = "Seedling";
        private decimal _costAmount;
        private DateTime _costDate = DateTime.Now;
        private string _costRemark = "";
        private string _yieldSpeciesName = "";
        private decimal _yieldQuantity;
        private string _yieldGrade = "A";
        private DateTime _yieldHarvestDate = DateTime.Now;
        private string _yieldRemark = "";
        private string _deviceName = "";
        private string _deviceType = "Sensor";
        private string _deviceProtocol = "MQTT";
        private GroupSelectItem? _deviceGroupInput;
        private string _groupName = "";
        private string _groupDescription = "";
        private bool _isListingDialogOpen;
        private long _listingYieldRecordId;
        private string _listingSpeciesName = "";
        private string _listingGrade = "";
        private decimal _listingQuantity;
        private decimal _listingSuggestedPrice;
        private decimal _listingActualPrice;
        private long _listingId;
        private bool _isSelectAllYield;
        private bool _isBatchDetailDialogOpen;
        private BatchLifecycleInfo? _batchLifecycle;
        private BatchProfitAnalysisInfo? _batchProfitAnalysis;
        private ISeries[] _profitCostPieSeries = Array.Empty<ISeries>();
        private ISeries[] _profitRevenuePieSeries = Array.Empty<ISeries>();
        private PresaleFulfillmentStatusInfo? _presaleFulfillment;
        private ObservableCollection<PresaleOrderDisplayItem> _presaleOrders = new();
        private bool _isMqttConnected;
        private string _mqttConnectionStatus = "未连接";
        private bool _isSendingCommand;
        private DeviceTwinDisplayInfo _currentDeviceTwin;
        private string _selectedDeviceCode = "";
        private DeviceDisplayItem _selectedDeviceForControl;
        private bool _isSendingIrrigation;
        private bool _isSendingVentilation;
        private bool _isSendingLighting;
        private ObservableCollection<TwinPropertyDisplayItem> _deviceTwinDesiredProperties = new();
        private ObservableCollection<TwinPropertyDisplayItem> _deviceTwinReportedProperties = new();
        private ObservableCollection<TwinDiffDisplayItem> _deviceTwinDifferences = new();
        private bool _isDeviceTwinLoaded;
        private string _errorMessage = "";
        private string _sensorEmptyHint = "";
        private string _adviceEmptyHint = "";
        private bool _isThresholdDialogOpen;
        private double _thresholdTemperature = 35;
        private double _thresholdHumidity = 80;
        private double _thresholdCo2 = 500;
        private double _thresholdLight = 50000;
        private double _thresholdSoil = 40;
        private string _thresholdDeviceCode = "";
        private bool _isThresholdSaving;
        private bool _isManualReportDialogOpen;
        private double _manualTemperature;
        private double _manualHumidity;
        private double _manualLight;
        private double _manualCo2;
        private double _manualSoil;
        private string _manualReportDeviceCode = "";
        private string _renameGroupNewName = "";
        private bool _isRenameGroupDialogOpen = false;
        private string _renameGroupTargetId = "";

        public bool IsCostDialogOpen { get => _isCostDialogOpen; set => SetProperty(ref _isCostDialogOpen, value); }
        public bool IsYieldDialogOpen { get => _isYieldDialogOpen; set => SetProperty(ref _isYieldDialogOpen, value); }
        public bool IsDeviceDialogOpen { get => _isDeviceDialogOpen; set => SetProperty(ref _isDeviceDialogOpen, value); }
        public bool IsGroupDialogOpen { get => _isGroupDialogOpen; set => SetProperty(ref _isGroupDialogOpen, value); }
        public string CostCategory { get => _costCategory; set => SetProperty(ref _costCategory, value); }
        public decimal CostAmount { get => _costAmount; set => SetProperty(ref _costAmount, value); }
        public DateTime CostDate { get => _costDate; set => SetProperty(ref _costDate, value); }
        public string CostRemark { get => _costRemark; set => SetProperty(ref _costRemark, value); }
        public string YieldSpeciesName { get => _yieldSpeciesName; set => SetProperty(ref _yieldSpeciesName, value); }
        public decimal YieldQuantity { get => _yieldQuantity; set => SetProperty(ref _yieldQuantity, value); }
        public string YieldGrade { get => _yieldGrade; set => SetProperty(ref _yieldGrade, value); }
        public DateTime YieldHarvestDate { get => _yieldHarvestDate; set => SetProperty(ref _yieldHarvestDate, value); }
        public string YieldRemark { get => _yieldRemark; set => SetProperty(ref _yieldRemark, value); }
        public string DeviceNameInput { get => _deviceName; set => SetProperty(ref _deviceName, value); }
        public string DeviceTypeInput { get => _deviceType; set => SetProperty(ref _deviceType, value); }
        public string DeviceProtocolInput { get => _deviceProtocol; set => SetProperty(ref _deviceProtocol, value); }
        public GroupSelectItem? DeviceGroupInput { get => _deviceGroupInput; set => SetProperty(ref _deviceGroupInput, value); }
        private string _deviceLocationInput = "";
        public string DeviceLocationInput { get => _deviceLocationInput; set => SetProperty(ref _deviceLocationInput, value); }
        private string _deviceManufacturerInput = "";
        public string DeviceManufacturerInput { get => _deviceManufacturerInput; set => SetProperty(ref _deviceManufacturerInput, value); }
        private string _deviceModelInput = "";
        public string DeviceModelInput { get => _deviceModelInput; set => SetProperty(ref _deviceModelInput, value); }
        private string _deviceCapabilitiesInput = "";
        public string DeviceCapabilitiesInput { get => _deviceCapabilitiesInput; set => SetProperty(ref _deviceCapabilitiesInput, value); }
        private string _deviceRemarkInput = "";
        public string DeviceRemarkInput { get => _deviceRemarkInput; set => SetProperty(ref _deviceRemarkInput, value); }
        public string GroupNameInput { get => _groupName; set => SetProperty(ref _groupName, value); }
        public string GroupDescriptionInput { get => _groupDescription; set => SetProperty(ref _groupDescription, value); }
        public bool IsBindDialogOpen { get => _isBindDialogOpen; set => SetProperty(ref _isBindDialogOpen, value); }
        public string BindDeviceCodeInput { get => _bindDeviceCode; set => SetProperty(ref _bindDeviceCode, value); }
        public string BindGroupIdInput { get => _bindGroupId; set => SetProperty(ref _bindGroupId, value); }
        public bool IsChangeGroupDialogOpen { get => _isChangeGroupDialogOpen; set => SetProperty(ref _isChangeGroupDialogOpen, value); }
        public bool IsThresholdDialogOpen { get => _isThresholdDialogOpen; set => SetProperty(ref _isThresholdDialogOpen, value); }
        public double ThresholdTemperature { get => _thresholdTemperature; set => SetProperty(ref _thresholdTemperature, value); }
        public double ThresholdHumidity { get => _thresholdHumidity; set => SetProperty(ref _thresholdHumidity, value); }
        public double ThresholdCo2 { get => _thresholdCo2; set => SetProperty(ref _thresholdCo2, value); }
        public double ThresholdLight { get => _thresholdLight; set => SetProperty(ref _thresholdLight, value); }
        public double ThresholdSoil { get => _thresholdSoil; set => SetProperty(ref _thresholdSoil, value); }
        public bool IsThresholdSaving { get => _isThresholdSaving; set => SetProperty(ref _isThresholdSaving, value); }
        public bool IsManualReportDialogOpen { get => _isManualReportDialogOpen; set => SetProperty(ref _isManualReportDialogOpen, value); }
        public double ManualTemperature { get => _manualTemperature; set => SetProperty(ref _manualTemperature, value); }
        public double ManualHumidity { get => _manualHumidity; set => SetProperty(ref _manualHumidity, value); }
        public double ManualLight { get => _manualLight; set => SetProperty(ref _manualLight, value); }
        public double ManualCo2 { get => _manualCo2; set => SetProperty(ref _manualCo2, value); }
        public double ManualSoil { get => _manualSoil; set => SetProperty(ref _manualSoil, value); }
        public string RenameGroupNewName { get => _renameGroupNewName; set => SetProperty(ref _renameGroupNewName, value); }
        public bool IsRenameGroupDialogOpen { get => _isRenameGroupDialogOpen; set => SetProperty(ref _isRenameGroupDialogOpen, value); }
        public ObservableCollection<GroupSelectItem> AvailableGroups { get => _availableGroups; set => SetProperty(ref _availableGroups, value); }
        public GroupSelectItem? ChangeGroupTargetGroupId { get => _changeGroupTargetGroupId; set => SetProperty(ref _changeGroupTargetGroupId, value); }
        public ObservableCollection<DeviceComparisonItem> ComparisonDevices { get => _comparisonDevices; set => SetProperty(ref _comparisonDevices, value); }
        public ObservableCollection<DeviceComparisonResultItem> ComparisonResults { get => _comparisonResults; set => SetProperty(ref _comparisonResults, value); }
        public bool IsComparisonLoading { get => _isComparisonLoading; set => SetProperty(ref _isComparisonLoading, value); }
        public HealthIndexInfo HealthIndexData { get => _healthIndex; set => SetProperty(ref _healthIndex, value); }
        public ObservableCollection<AnomalyInfo> Anomalies { get => _anomalies; set => SetProperty(ref _anomalies, value); }
        public ObservableCollection<SignificantChangePointInfo> SignificantChanges { get => _significantChanges; set => SetProperty(ref _significantChanges, value); }

        public bool IsListingDialogOpen { get => _isListingDialogOpen; set => SetProperty(ref _isListingDialogOpen, value); }
        public long ListingYieldRecordId { get => _listingYieldRecordId; set => SetProperty(ref _listingYieldRecordId, value); }
        public string ListingSpeciesName { get => _listingSpeciesName; set => SetProperty(ref _listingSpeciesName, value); }
        public string ListingGrade { get => _listingGrade; set => SetProperty(ref _listingGrade, value); }
        public decimal ListingQuantity { get => _listingQuantity; set => SetProperty(ref _listingQuantity, value); }
        public decimal ListingSuggestedPrice { get => _listingSuggestedPrice; set => SetProperty(ref _listingSuggestedPrice, value); }
        public decimal ListingActualPrice { get => _listingActualPrice; set => SetProperty(ref _listingActualPrice, value); }
        public long ListingId { get => _listingId; set => SetProperty(ref _listingId, value); }
        public bool IsSelectAllYield
        {
            get => _isSelectAllYield;
            set
            {
                if (SetProperty(ref _isSelectAllYield, value))
                {
                    foreach (var item in _yieldRecords)
                        item.IsSelected = value;
                    OnPropertyChanged(nameof(HasSelectedYieldRecords));
                }
            }
        }
        public bool HasSelectedYieldRecords => _yieldRecords.Any(r => r.IsSelected);

        public bool IsBatchDetailDialogOpen { get => _isBatchDetailDialogOpen; set => SetProperty(ref _isBatchDetailDialogOpen, value); }
        public BatchLifecycleInfo? BatchLifecycle { get => _batchLifecycle; set => SetProperty(ref _batchLifecycle, value); }
        public BatchProfitAnalysisInfo? BatchProfitAnalysis { get => _batchProfitAnalysis; set => SetProperty(ref _batchProfitAnalysis, value); }
        public ISeries[] ProfitCostPieSeries { get => _profitCostPieSeries; set => SetProperty(ref _profitCostPieSeries, value); }
        public ISeries[] ProfitRevenuePieSeries { get => _profitRevenuePieSeries; set => SetProperty(ref _profitRevenuePieSeries, value); }
        public PresaleFulfillmentStatusInfo? PresaleFulfillment { get => _presaleFulfillment; set => SetProperty(ref _presaleFulfillment, value); }
        public ObservableCollection<PresaleOrderDisplayItem> PresaleOrders { get => _presaleOrders; set => SetProperty(ref _presaleOrders, value); }
        public bool HasPresaleOrders => _presaleOrders.Count > 0;
        public string PresaleProgressText => _presaleFulfillment != null
            ? $"预售需求: {_presaleFulfillment.TotalPresaleDemand} | 已收获: {_presaleFulfillment.TotalHarvested} | {(_presaleFulfillment.IsFulfilled ? "已满足" : "未满足")}"
            : "";

        public bool IsMqttConnected
        {
            get => _isMqttConnected;
            set
            {
                if (SetProperty(ref _isMqttConnected, value))
                {
                    OnPropertyChanged(nameof(MqttStatusText));
                    OnPropertyChanged(nameof(MqttStatusDotBrush));
                    OnPropertyChanged(nameof(CanSendCommands));
                }
            }
        }

        public string MqttStatusText => _isMqttConnected ? "MQTT: 已连接" : "MQTT: 未连接";
        public Avalonia.Media.IBrush MqttStatusDotBrush => _isMqttConnected
            ? Avalonia.Media.Brushes.Green
            : Avalonia.Media.Brushes.Red;
        public string PushFrequencyText => "推送频率: 10s/次";
        public string CurrentTimeRangeDisplay { get => _currentTimeRangeDisplay; set => SetProperty(ref _currentTimeRangeDisplay, value); }

        public DeviceDisplayItem SelectedDeviceForControl
        {
            get => _selectedDeviceForControl;
            set
            {
                if (SetProperty(ref _selectedDeviceForControl, value))
                {
                    OnPropertyChanged(nameof(CanSendCommands));
                    OnPropertyChanged(nameof(HasSelectedDevice));
                }
            }
        }

        public bool HasSelectedDevice => _selectedDeviceForControl != null;
        public bool CanSendCommands => _selectedDeviceForControl != null && _isMqttConnected;

        public bool IsSendingIrrigation
        {
            get => _isSendingIrrigation;
            set => SetProperty(ref _isSendingIrrigation, value);
        }

        public bool IsSendingVentilation
        {
            get => _isSendingVentilation;
            set => SetProperty(ref _isSendingVentilation, value);
        }

        public bool IsSendingLighting
        {
            get => _isSendingLighting;
            set => SetProperty(ref _isSendingLighting, value);
        }

        public ObservableCollection<TwinPropertyDisplayItem> DeviceTwinDesiredProperties
        {
            get => _deviceTwinDesiredProperties;
            set => SetProperty(ref _deviceTwinDesiredProperties, value);
        }

        public ObservableCollection<TwinPropertyDisplayItem> DeviceTwinReportedProperties
        {
            get => _deviceTwinReportedProperties;
            set => SetProperty(ref _deviceTwinReportedProperties, value);
        }

        public ObservableCollection<TwinDiffDisplayItem> DeviceTwinDifferences
        {
            get => _deviceTwinDifferences;
            set => SetProperty(ref _deviceTwinDifferences, value);
        }

        public bool IsDeviceTwinLoaded
        {
            get => _isDeviceTwinLoaded;
            set => SetProperty(ref _isDeviceTwinLoaded, value);
        }

        public string ErrorMessage { get => _errorMessage; set { if (SetProperty(ref _errorMessage, value)) OnPropertyChanged(nameof(HasError)); } }
        public bool HasError => !string.IsNullOrEmpty(_errorMessage);
        public string SensorEmptyHint { get => _sensorEmptyHint; set => SetProperty(ref _sensorEmptyHint, value); }
        public string AdviceEmptyHint { get => _adviceEmptyHint; set => SetProperty(ref _adviceEmptyHint, value); }

        public bool HasNoDesiredProperties => _isDeviceTwinLoaded && _deviceTwinDesiredProperties.Count == 0;
        public bool HasNoReportedProperties => _isDeviceTwinLoaded && _deviceTwinReportedProperties.Count == 0;
        public bool HasNoTwinDifferences => _isDeviceTwinLoaded && _deviceTwinDifferences.Count == 0;

        public string MqttConnectionStatus
        {
            get => _mqttConnectionStatus;
            set => SetProperty(ref _mqttConnectionStatus, value);
        }

        public bool IsSendingCommand
        {
            get => _isSendingCommand;
            set => SetProperty(ref _isSendingCommand, value);
        }

        public DeviceTwinDisplayInfo CurrentDeviceTwin
        {
            get => _currentDeviceTwin;
            set => SetProperty(ref _currentDeviceTwin, value);
        }

        public string SelectedDeviceCode
        {
            get => _selectedDeviceCode;
            set => SetProperty(ref _selectedDeviceCode, value);
        }

        public ObservableCollection<string> CostCategories { get; } = new() { "Seedling", "Fertilizer", "Pesticide", "Labor", "Utility", "Depreciation", "Other" };
        public ObservableCollection<string> YieldGrades { get; } = new() { "A", "B", "C" };
        public ObservableCollection<string> DeviceTypes { get; } = new() { "Sensor", "Gateway", "Controller" };
        public ObservableCollection<string> DeviceProtocols { get; } = new() { "MQTT", "HTTP", "CoAP" };

        public SensorReadingData SensorData
        {
            get => _sensorData;
            set => SetProperty(ref _sensorData, value);
        }

        public ObservableCollection<HarvestAdviceItem> HarvestAdvices
        {
            get => _harvestAdvices;
            set => SetProperty(ref _harvestAdvices, value);
        }

        public ObservableCollection<PestAlertItem> PestAlerts
        {
            get => _pestAlerts;
            set => SetProperty(ref _pestAlerts, value);
        }

        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        public int SelectedTabIndex
        {
            get => _selectedTabIndex;
            set => SetProperty(ref _selectedTabIndex, value);
        }

        public decimal MonthlyCost
        {
            get => _monthlyCost;
            set => SetProperty(ref _monthlyCost, value);
        }

        public decimal MonthlyYield
        {
            get => _monthlyYield;
            set => SetProperty(ref _monthlyYield, value);
        }

        public ObservableCollection<ThresholdAlertItem> ThresholdAlerts
        {
            get => _thresholdAlerts;
            set => SetProperty(ref _thresholdAlerts, value);
        }

        public bool HasNoThresholdAlerts => _thresholdAlerts.Count == 0;

        public ObservableCollection<AdviceDisplayItem> ActiveAdvices
        {
            get => _activeAdvices;
            set => SetProperty(ref _activeAdvices, value);
        }

        public bool IsEmptyAdvice => !_isLoading && _activeAdvices.Count == 0;
        public bool IsEmptyHarvest => !_isLoading && _harvestAdvices.Count == 0;
        public bool IsEmptyPest => !_isLoading && _pestAlerts.Count == 0;

        public ObservableCollection<CostDisplayItem> CostRecords
        {
            get => _costRecords;
            set => SetProperty(ref _costRecords, value);
        }

        public ObservableCollection<YieldDisplayItem> YieldRecords
        {
            get => _yieldRecords;
            set => SetProperty(ref _yieldRecords, value);
        }

        public ObservableCollection<DeviceGroupDisplayItem> DeviceGroups
        {
            get => _deviceGroups;
            set => SetProperty(ref _deviceGroups, value);
        }

        public string CurrentGreenhouseId
        {
            get => _currentGreenhouseId;
            set => SetProperty(ref _currentGreenhouseId, value);
        }

        public ObservableCollection<string> GreenhouseList { get => _greenhouseList; set => SetProperty(ref _greenhouseList, value); }
        public string SelectedGreenhouse
        {
            get => _selectedGreenhouse;
            set
            {
                if (SetProperty(ref _selectedGreenhouse, value) && value != CurrentGreenhouseId)
                {
                    CurrentGreenhouseId = value;
                    _ = RefreshForGreenhouseAsync();
                }
            }
        }

        public long CurrentBatchId
        {
            get => _currentBatchId;
            set => SetProperty(ref _currentBatchId, value);
        }

        public ObservableCollection<PlantingBatchInfo> Batches
        {
            get => _batches;
            set => SetProperty(ref _batches, value);
        }

        public PlantingBatchInfo SelectedBatch
        {
            get => _selectedBatch;
            set { if (SetProperty(ref _selectedBatch, value) && value != null) { CurrentBatchId = value.Id; _ = LoadBatchDataAsync(value.Id); } }
        }

        public string AdviceFilter
        {
            get => _adviceFilter;
            set => SetProperty(ref _adviceFilter, value);
        }

        public string SpeciesFilter
        {
            get => _speciesFilter;
            set => SetProperty(ref _speciesFilter, value);
        }

        public ISeries[] HistorySeries { get; set; }
        public Axis[] HistoryXAxes { get; set; }
        public Axis[] HistoryYAxes { get; set; }

        public ISeries[] TemperaturePieSeries { get; set; }
        public ISeries[] LightPieSeries { get; set; }

        public ISeries[] CostPieSeries { get; set; }
        public ISeries[] CostBarSeries { get; set; }
        public Axis[] CostBarXAxes { get; set; }
        public Axis[] CostBarYAxes { get; set; }

        public ISeries[] YieldLineSeries { get; set; }
        public Axis[] YieldLineXAxes { get; set; }
        public Axis[] YieldLineYAxes { get; set; }

        public ICommand RefreshCommand { get; }
        public ICommand AddCostCommand { get; }
        public ICommand AddYieldCommand { get; }
        public ICommand SetTimeRangeCommand { get; }
        public ICommand ExportDataCommand { get; }
        public ICommand FilterAdviceCommand { get; }
        public ICommand GenerateAdviceCommand { get; }
        public ICommand MarkExecutedCommand { get; }
        public ICommand CreateGroupCommand { get; }
        public ICommand AddDeviceCommand { get; }
        public ICommand SubmitCostCommand { get; }
        public ICommand SubmitYieldCommand { get; }
        public ICommand SubmitDeviceCommand { get; }
        public ICommand SubmitGroupCommand { get; }
        public ICommand CancelDialogCommand { get; }
        public ICommand ListFromYieldCommand { get; }
        public ICommand BatchListFromYieldCommand { get; }
        public ICommand ConfirmListingCommand { get; }
        public ICommand ShowBatchDetailCommand { get; }
        public ICommand CloseBatchDetailCommand { get; }
        public ICommand BindDeviceCommand { get; }
        public ICommand SubmitBindDeviceCommand { get; }
        public ICommand UnbindDeviceCommand { get; }
        public ICommand ChangeGroupCommand { get; }
        public ICommand SubmitChangeGroupCommand { get; }
        public ICommand CancelChangeGroupCommand { get; }
        public ICommand LoadComparisonDevicesCommand { get; }
        public ICommand LoadComparisonCommand { get; }
        public ICommand LoadAnalysisCommand { get; }
        public ICommand SendIrrigationCommandCommand { get; }
        public ICommand SendVentilationCommandCommand { get; }
        public ICommand SendLightingCommandCommand { get; }
        public ICommand LoadDeviceTwinCommand { get; }
        public ICommand ConnectMqttCommand { get; }
        public ICommand SelectDeviceForControlCommand { get; }
        public ICommand RetryCommand { get; }
        public ICommand OpenThresholdDialogCommand { get; }
        public ICommand SaveThresholdsCommand { get; }
        public ICommand CancelThresholdCommand { get; }
        public ICommand OpenManualReportCommand { get; }
        public ICommand SubmitManualReportCommand { get; }
        public ICommand CancelManualReportCommand { get; }
        public ICommand DeleteGroupCommand { get; }
        public ICommand RenameGroupCommand { get; }
        public ICommand SubmitRenameGroupCommand { get; }
        public ICommand CancelRenameGroupCommand { get; }

        public FlowerPlantingAdviceViewModel()
        {
            _iotService = new FlowerIoTService();
            _shopService = new FlowerShopService();
            _aiService = new FlowerAIService();
            _merchantService = new FlowerMerchantService();
            RefreshCommand = new AsyncCommand(LoadDataAsync);
            AddCostCommand = new AsyncCommand(AddCostAsync);
            AddYieldCommand = new AsyncCommand(AddYieldAsync);
            SetTimeRangeCommand = new ParameterAsyncCommand(SetTimeRangeAsync);
            ExportDataCommand = new AsyncCommand(ExportDataAsync);
            FilterAdviceCommand = new ParameterAsyncCommand(FilterAdviceAsync);
            GenerateAdviceCommand = new AsyncCommand(GenerateAdviceAsync);
            MarkExecutedCommand = new ParameterAsyncCommand(MarkExecutedAsync);
            CreateGroupCommand = new AsyncCommand(CreateGroupAsync);
            AddDeviceCommand = new AsyncCommand(AddDeviceAsync);
            SubmitCostCommand = new AsyncCommand(SubmitCostAsync);
            SubmitYieldCommand = new AsyncCommand(SubmitYieldAsync);
            SubmitDeviceCommand = new AsyncCommand(SubmitDeviceAsync);
            SubmitGroupCommand = new AsyncCommand(SubmitGroupAsync);
            CancelDialogCommand = new ParameterAsyncCommand(CancelDialogAsync);
            ListFromYieldCommand = new ParameterAsyncCommand(ListFromYieldAsync);
            BatchListFromYieldCommand = new AsyncCommand(BatchListFromYieldAsync);
            ConfirmListingCommand = new AsyncCommand(ConfirmListingAsync);
            ShowBatchDetailCommand = new ParameterAsyncCommand(ShowBatchDetailAsync);
            CloseBatchDetailCommand = new AsyncCommand(CloseBatchDetailAsync);
            BindDeviceCommand = new AsyncCommand(BindDeviceAsync);
            SubmitBindDeviceCommand = new AsyncCommand(SubmitBindDeviceAsync);
            UnbindDeviceCommand = new ParameterAsyncCommand(UnbindDeviceAsync);
            ChangeGroupCommand = new ParameterAsyncCommand(ChangeGroupAsync);
            SubmitChangeGroupCommand = new AsyncCommand(SubmitChangeGroupAsync);
            CancelChangeGroupCommand = new AsyncCommand(() => { IsChangeGroupDialogOpen = false; return Task.CompletedTask; });
            LoadComparisonDevicesCommand = new AsyncCommand(LoadComparisonDevicesAsync);
            LoadComparisonCommand = new AsyncCommand(LoadComparisonAsync);
            LoadAnalysisCommand = new AsyncCommand(LoadAnalysisDataAsync);
            SendIrrigationCommandCommand = new AsyncCommand(SendIrrigationCommandAsync);
            SendVentilationCommandCommand = new AsyncCommand(SendVentilationCommandAsync);
            SendLightingCommandCommand = new AsyncCommand(SendLightingCommandAsync);
            LoadDeviceTwinCommand = new AsyncCommand(LoadDeviceTwinAsync);
            ConnectMqttCommand = new AsyncCommand(ConnectMqttAsync);
            SelectDeviceForControlCommand = new ParameterAsyncCommand(SelectDeviceForControlAsync);
            RetryCommand = new AsyncCommand(RetryAsync);
            OpenThresholdDialogCommand = new ParameterAsyncCommand(OpenThresholdDialogAsync);
            SaveThresholdsCommand = new AsyncCommand(SaveThresholdsAsync);
            CancelThresholdCommand = new AsyncCommand(() => { IsThresholdDialogOpen = false; return Task.CompletedTask; });
            OpenManualReportCommand = new AsyncCommand(OpenManualReportDialogAsync);
            SubmitManualReportCommand = new AsyncCommand(SubmitManualReportAsync);
            CancelManualReportCommand = new AsyncCommand(() => { IsManualReportDialogOpen = false; return Task.CompletedTask; });
            DeleteGroupCommand = new ParameterAsyncCommand(DeleteGroupAsync);
            RenameGroupCommand = new ParameterAsyncCommand(OpenRenameGroupDialogAsync);
            SubmitRenameGroupCommand = new AsyncCommand(SubmitRenameGroupAsync);
            CancelRenameGroupCommand = new AsyncCommand(() => { IsRenameGroupDialogOpen = false; return Task.CompletedTask; });
            InitializeMqtt();
            _ = LoadDataAsync();
        }

        public async Task LoadDataAsync()
        {
            IsLoading = true;
            if (!_mqttService.IsConnected)
            {
                _ = ConnectMqttAsync();
            }
            try
            {
                await LoadGreenhousesAsync();
                await LoadSensorDataAsync();
                await LoadDeviceGroupsAsync();
                await LoadBatchesAsync();
                await LoadAdviceAsync();
                await LoadHistoryChartsAsync();
            }
            catch (HttpRequestException)
            {
                ErrorMessage = "网络连接失败，请检查网络设置";
            }
            catch (TaskCanceledException)
            {
                ErrorMessage = "请求超时，请稍后重试";
            }
            catch (Exception)
            {
                ErrorMessage = "数据加载失败，请稍后重试";
            }
            finally
            {
                IsLoading = false;
                OnPropertyChanged(nameof(IsEmptyHarvest));
                OnPropertyChanged(nameof(IsEmptyPest));
                OnPropertyChanged(nameof(HasNoThresholdAlerts));
                OnPropertyChanged(nameof(IsEmptyAdvice));
            }
        }

        private async Task RetryAsync()
        {
            ErrorMessage = "";
            await LoadDataAsync();
        }

        private async Task LoadGreenhousesAsync()
        {
            var greenhouses = await _iotService.GetGreenhousesAsync().ConfigureAwait(false);
            if (greenhouses != null && greenhouses.Count > 0)
            {
                GreenhouseList = new ObservableCollection<string>(greenhouses);
                if (!greenhouses.Contains(_selectedGreenhouse))
                {
                    SelectedGreenhouse = greenhouses[0];
                }
            }
        }

        private async Task RefreshForGreenhouseAsync()
        {
            IsLoading = true;
            try
            {
                await LoadSensorDataAsync();
                await LoadDeviceGroupsAsync();
                await LoadBatchesAsync();
                if (_mqttService.IsConnected)
                {
                    try
                    {
                        await _mqttService.UnsubscribeSensorDataAsync(CurrentGreenhouseId).ConfigureAwait(false);
                        await _mqttService.SubscribeSensorDataAsync(CurrentGreenhouseId).ConfigureAwait(false);
                    }
                    catch { }
                }
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task LoadSensorDataAsync()
        {
            SensorEmptyHint = "";
            var devices = await _iotService.GetIoTDevicesAsync(CurrentGreenhouseId).ConfigureAwait(false);
            if (devices == null || devices.Count == 0)
            {
                SensorEmptyHint = "请先添加 IoT 设备";
                return;
            }

            var onlineDevice = devices.FirstOrDefault(d => d.OnlineStatus == "Online") ?? devices[0];
            var reading = await _iotService.GetLatestSensorReadingAsync(onlineDevice.DeviceCode).ConfigureAwait(false);
            if (reading != null)
            {
                SensorData = new SensorReadingData
                {
                    Temperature = reading.Temperature,
                    Humidity = reading.Humidity,
                    LightIntensity = reading.LightIntensity,
                    Co2Concentration = reading.Co2Level,
                    SoilMoisture = reading.SoilMoisture,
                    TemperatureTrend = reading.Temperature > 25 ? "↑ 偏高" : reading.Temperature < 15 ? "↓ 偏低" : "→ 正常",
                    HumidityTrend = reading.Humidity > 80 ? "↑ 偏高" : reading.Humidity < 40 ? "↓ 偏低" : "→ 正常",
                    LightTrend = reading.LightIntensity > 20000 ? "↑ 强光" : reading.LightIntensity < 5000 ? "↓ 弱光" : "→ 适中",
                    Co2Trend = reading.Co2Level > 500 ? "↑ 偏高" : "→ 正常",
                    SoilTrend = reading.SoilMoisture < 40 ? "↓ 偏干" : "→ 正常",
                    Summary = "温室环境数据已从传感器实时获取。"
                };

                var alerts = new ObservableCollection<ThresholdAlertItem>();
                if (reading.SoilMoisture < 40) alerts.Add(new() { Message = $"土壤湿度 {reading.SoilMoisture:F1}%，低于40%阈值，建议灌溉" });
                if (reading.Co2Level > 500) alerts.Add(new() { Message = $"CO₂浓度 {reading.Co2Level:F0}ppm，高于500ppm阈值，建议通风" });
                if (reading.Temperature > 35) alerts.Add(new() { Message = $"温度 {reading.Temperature:F1}°C，超过35°C高温阈值" });
                ThresholdAlerts = alerts;
            }
            else
            {
                ErrorMessage = "传感器数据加载失败，请检查网络连接";
            }
        }

        private void SetDefaultSensorData()
        {
            SensorData = new SensorReadingData
            {
                Temperature = 24.5,
                Humidity = 68.2,
                LightIntensity = 12500,
                Co2Concentration = 420,
                SoilMoisture = 42.3,
                TemperatureTrend = "↑ 0.3℃/h",
                HumidityTrend = "↓ 1.2%/h",
                LightTrend = "→ 稳定",
                Co2Trend = "↑ 5ppm/h",
                SoilTrend = "↓ 0.5%/h",
                Summary = "暂无在线传感器，显示为示例数据。"
            };
            ThresholdAlerts = new ObservableCollection<ThresholdAlertItem>
            {
                new() { Message = "土壤湿度 35%，低于40%阈值，建议灌溉" },
                new() { Message = "CO₂浓度 520ppm，高于450ppm阈值，建议通风" }
            };
        }

        private async Task LoadDeviceGroupsAsync()
        {
            var groups = await _iotService.GetDeviceGroupsAsync(App.CurrentUser.PassportId).ConfigureAwait(false);
            var devices = await _iotService.GetIoTDevicesAsync(App.CurrentUser.PassportId).ConfigureAwait(false);

            var displayGroups = new ObservableCollection<DeviceGroupDisplayItem>();

            if (groups != null && groups.Count > 0)
            {
                foreach (var group in groups)
                {
                    var groupDevices = devices?.Where(d => d.GroupId == group.Id.ToString()).ToList() ?? new();
                    var displayItems = groupDevices.Select(d => new DeviceDisplayItem
                    {
                        DeviceName = d.DeviceName,
                        DeviceTypeDisplay = MapDeviceType(d.DeviceType),
                        StatusBrush = d.OnlineStatus == "Online" ? Avalonia.Media.Brushes.Green
                            : d.OnlineStatus == "Offline" ? Avalonia.Media.Brushes.Red
                            : Avalonia.Media.Brushes.Orange,
                        LastSeenText = d.LastHeartbeatTime.HasValue
                            ? (DateTime.UtcNow - d.LastHeartbeatTime.Value).TotalMinutes < 1 ? "刚刚"
                              : $"{(int)(DateTime.UtcNow - d.LastHeartbeatTime.Value).TotalMinutes}分钟前"
                            : "离线",
                        DeviceCode = d.DeviceCode,
                        BindingStatus = d.BindingStatus ?? "Unbound",
                        Location = d.Location,
                        Manufacturer = d.Manufacturer,
                        Model = d.Model,
                        Protocol = d.Protocol,
                        SensorCapabilities = d.SensorCapabilities,
                        Remark = d.Remark,
                    });
                    displayGroups.Add(new DeviceGroupDisplayItem
                    {
                        GroupName = group.GroupName,
                        GroupId = group.Id.ToString(),
                        Devices = new ObservableCollection<DeviceDisplayItem>(displayItems)
                    });
                }
            }

            var ungroupedDevices = devices?.Where(d => string.IsNullOrWhiteSpace(d.GroupId)).ToList() ?? new List<IoTDeviceInfo>();
            if (ungroupedDevices.Count > 0)
            {
                var ungroupedGroup = new DeviceGroupDisplayItem { GroupName = "未分组设备" };
                foreach (var device in ungroupedDevices)
                {
                    ungroupedGroup.Devices.Add(new DeviceDisplayItem
                    {
                        DeviceCode = device.DeviceCode,
                        DeviceName = device.DeviceName,
                        DeviceTypeDisplay = MapDeviceType(device.DeviceType),
                        StatusBrush = device.OnlineStatus == "Online" ? Avalonia.Media.Brushes.Green
                            : device.OnlineStatus == "Offline" ? Avalonia.Media.Brushes.Red
                            : Avalonia.Media.Brushes.Orange,
                        LastSeenText = device.LastHeartbeatTime.HasValue
                            ? (DateTime.UtcNow - device.LastHeartbeatTime.Value).TotalMinutes < 1 ? "刚刚"
                              : $"{(int)(DateTime.UtcNow - device.LastHeartbeatTime.Value).TotalMinutes}分钟前"
                            : "离线",
                        BindingStatus = device.BindingStatus ?? "Unbound",
                    });
                }
                displayGroups.Add(ungroupedGroup);
            }

            DeviceGroups = displayGroups;
        }

        private void SetDefaultDeviceGroups()
        {
            DeviceGroups = new ObservableCollection<DeviceGroupDisplayItem>
            {
                new()
                {
                    GroupName = "1号温室",
                    Devices = new ObservableCollection<DeviceDisplayItem>
                    {
                        new() { DeviceName = "温湿度传感器-01", DeviceTypeDisplay = "温湿度传感器", StatusBrush = Avalonia.Media.Brushes.Green, LastSeenText = "刚刚" },
                        new() { DeviceName = "光照传感器-01", DeviceTypeDisplay = "光照传感器", StatusBrush = Avalonia.Media.Brushes.Green, LastSeenText = "2分钟前" },
                        new() { DeviceName = "土壤传感器-01", DeviceTypeDisplay = "土壤传感器", StatusBrush = Avalonia.Media.Brushes.Orange, LastSeenText = "15分钟前" }
                    }
                },
                new()
                {
                    GroupName = "2号大棚",
                    Devices = new ObservableCollection<DeviceDisplayItem>
                    {
                        new() { DeviceName = "CO₂传感器-01", DeviceTypeDisplay = "CO₂传感器", StatusBrush = Avalonia.Media.Brushes.Green, LastSeenText = "1分钟前" },
                        new() { DeviceName = "网关-01", DeviceTypeDisplay = "网关设备", StatusBrush = Avalonia.Media.Brushes.Red, LastSeenText = "离线" }
                    }
                }
            };
        }

        private async Task LoadBatchesAsync()
        {
            var batches = await _iotService.GetPlantingBatchesAsync(CurrentGreenhouseId).ConfigureAwait(false);
            if (batches != null && batches.Count > 0)
            {
                Batches = new ObservableCollection<PlantingBatchInfo>(batches);
                if (SelectedBatch == null && Batches.Count > 0)
                {
                    SelectedBatch = Batches[0];
                }
            }
        }

        private async Task LoadBatchDataAsync(long batchId)
        {
            try
            {
                await LoadCostDataAsync(batchId);
                await LoadYieldDataAsync(batchId);
            }
            catch
            {
                ErrorMessage = "数据加载失败";
            }
        }

        private async Task LoadCostDataAsync(long batchId)
        {
            var records = await _iotService.GetCostRecordsAsync(batchId).ConfigureAwait(false);
            var stats = await _iotService.GetCostStatsAsync(batchId).ConfigureAwait(false);

            if (records != null && records.Count > 0)
            {
                CostRecords = new ObservableCollection<CostDisplayItem>(
                    records.Select(r => new CostDisplayItem
                    {
                        CategoryIcon = MapCategoryIcon(r.Category),
                        CategoryDisplay = MapCategoryDisplay(r.Category),
                        Amount = r.Amount,
                        CostDate = r.CostDate
                    }));
                MonthlyCost = records.Where(r => r.CostDate >= DateTime.Now.AddMonths(-1)).Sum(r => r.Amount);
            }
            else
            {
                SetDefaultCostData();
            }

            if (stats != null && stats.Count > 0)
            {
                CostPieSeries = stats.Select(s => new PieSeries<double>
                {
                    Values = new double[] { (double)s.TotalAmount },
                    Name = MapCategoryDisplay(s.Category)
                }).ToArray();
                OnPropertyChanged(nameof(CostPieSeries));
            }
            else
            {
                CostPieSeries = new ISeries[]
                {
                    new PieSeries<double> { Values = new double[] { 1200 }, Name = "种苗" },
                    new PieSeries<double> { Values = new double[] { 580 }, Name = "肥料" },
                    new PieSeries<double> { Values = new double[] { 320 }, Name = "农药" },
                    new PieSeries<double> { Values = new double[] { 800 }, Name = "人工" },
                    new PieSeries<double> { Values = new double[] { 380 }, Name = "水电" }
                };
                OnPropertyChanged(nameof(CostPieSeries));
            }

            var trend = await _iotService.GetCostMonthlyTrendAsync(CurrentGreenhouseId).ConfigureAwait(false);
            if (trend != null && trend.Count > 0)
            {
                var months = trend.Select(t => t.Month).ToArray();
                var amounts = trend.Select(t => (double)t.TotalAmount).ToArray();
                CostBarSeries = new ISeries[]
                {
                    new ColumnSeries<double> { Values = amounts, Name = "月度成本" }
                };
                CostBarXAxes = new Axis[] { new Axis { Labels = months } };
                CostBarYAxes = new Axis[] { new Axis { Labeler = v => $"¥{v:0}" } };
                OnPropertyChanged(nameof(CostBarSeries));
                OnPropertyChanged(nameof(CostBarXAxes));
                OnPropertyChanged(nameof(CostBarYAxes));
            }
        }

        private void SetDefaultCostData()
        {
            CostRecords = new ObservableCollection<CostDisplayItem>
            {
                new() { CategoryIcon = "🌱", CategoryDisplay = "种苗", Amount = 1200m, CostDate = DateTime.Now.AddDays(-15) },
                new() { CategoryIcon = "🧪", CategoryDisplay = "肥料", Amount = 580m, CostDate = DateTime.Now.AddDays(-10) },
                new() { CategoryIcon = "💊", CategoryDisplay = "农药", Amount = 320m, CostDate = DateTime.Now.AddDays(-7) },
                new() { CategoryIcon = "👷", CategoryDisplay = "人工", Amount = 800m, CostDate = DateTime.Now.AddDays(-3) },
                new() { CategoryIcon = "⚡", CategoryDisplay = "水电", Amount = 380m, CostDate = DateTime.Now.AddDays(-1) }
            };
            MonthlyCost = 3280m;
        }

        private async Task LoadYieldDataAsync(long batchId)
        {
            var records = await _iotService.GetYieldRecordsAsync(batchId).ConfigureAwait(false);

            if (records != null && records.Count > 0)
            {
                YieldRecords = new ObservableCollection<YieldDisplayItem>(
                    records.Select(r => new YieldDisplayItem
                    {
                        Id = r.Id,
                        GradeIcon = MapGradeIcon(r.Grade),
                        SpeciesName = r.SpeciesName,
                        Quantity = r.Quantity,
                        Grade = r.Grade,
                        HarvestDate = r.HarvestDate
                    }));
                MonthlyYield = records.Where(r => r.HarvestDate >= DateTime.Now.AddMonths(-1)).Sum(r => r.Quantity);
            }
            else
            {
                SetDefaultYieldData();
            }

            var yieldTrend = await _iotService.GetYieldTrendAsync(CurrentGreenhouseId).ConfigureAwait(false);
            if (yieldTrend != null && yieldTrend.Count > 0)
            {
                var months = yieldTrend.Select(t => t.Month).Distinct().OrderBy(m => m).ToArray();
                var thisYearData = months.Select(m => (double)yieldTrend.Where(t => t.Month == m).Sum(t => t.TotalQuantity)).ToArray();
                var lastYearData = months.Select(m => (double)yieldTrend.Where(t => t.Month == m).Sum(t => t.LastYearQuantity)).ToArray();
                YieldLineSeries = new ISeries[]
                {
                    new LineSeries<double> { Values = thisYearData, Name = "今年产量" },
                    new LineSeries<double> { Values = lastYearData, Name = "去年同期" }
                };
                YieldLineXAxes = new Axis[] { new Axis { Labels = months } };
                YieldLineYAxes = new Axis[] { new Axis { Labeler = v => $"{v:0}支" } };
                OnPropertyChanged(nameof(YieldLineSeries));
                OnPropertyChanged(nameof(YieldLineXAxes));
                OnPropertyChanged(nameof(YieldLineYAxes));
            }
        }

        private void SetDefaultYieldData()
        {
            YieldRecords = new ObservableCollection<YieldDisplayItem>
            {
                new() { Id = 1, GradeIcon = "🅰️", SpeciesName = _speciesLookup.GetSpeciesName(1), Quantity = 1200m, Grade = "A", HarvestDate = DateTime.Now.AddDays(-2) },
                new() { Id = 2, GradeIcon = "🅱️", SpeciesName = _speciesLookup.GetSpeciesName(1), Quantity = 800m, Grade = "B", HarvestDate = DateTime.Now.AddDays(-2) },
                new() { Id = 3, GradeIcon = "🅰️", SpeciesName = _speciesLookup.GetSpeciesName(2), Quantity = 600m, Grade = "A", HarvestDate = DateTime.Now.AddDays(-5) },
                new() { Id = 4, GradeIcon = "🅰️", SpeciesName = _speciesLookup.GetSpeciesName(3), Quantity = 1500m, Grade = "A", HarvestDate = DateTime.Now.AddDays(-8) }
            };
            MonthlyYield = 4500m;
        }

        private async Task LoadAdviceAsync()
        {
            if (CurrentBatchId <= 0)
            {
                ActiveAdvices = new ObservableCollection<AdviceDisplayItem>();
                AdviceEmptyHint = "请先选择种植批次";
                return;
            }

            AdviceEmptyHint = "";
            var advices = await _aiService.GetActiveAdviceAsync(CurrentBatchId).ConfigureAwait(false);
            if (advices != null && advices.Count > 0)
            {
                var filteredAdvices = !string.IsNullOrEmpty(_speciesFilter)
                    ? advices.Where(a => a.Title.Contains(_speciesFilter) || a.Content.Contains(_speciesFilter)).ToList()
                    : advices;

                ActiveAdvices = new ObservableCollection<AdviceDisplayItem>(
                    filteredAdvices.Select(a => new AdviceDisplayItem
                    {
                        Id = a.Id,
                        Title = a.Title,
                        AdviceType = a.AdviceType,
                        Content = a.Content,
                        Source = a.Source,
                        Priority = a.Priority,
                        Status = a.Status,
                        GeneratedTime = a.GeneratedTime
                    }));
            }
            else
            {
                ActiveAdvices = new ObservableCollection<AdviceDisplayItem>();
            }
        }

        public async Task LoadAdviceForSpeciesAsync(int speciesId)
        {
            IsLoading = true;
            try
            {
                var speciesName = GetSpeciesName(speciesId);
                SpeciesFilter = speciesName;

                if (CurrentBatchId <= 0)
                {
                    SetDefaultAdviceForSpecies(speciesId, speciesName);
                    return;
                }

                var advices = await _aiService.GetActiveAdviceAsync(CurrentBatchId).ConfigureAwait(false);
                if (advices != null && advices.Count > 0)
                {
                    var filteredAdvices = advices
                        .Where(a => a.Title.Contains(speciesName) || a.Content.Contains(speciesName) || a.AdviceType == "Harvest")
                        .ToList();

                    ActiveAdvices = new ObservableCollection<AdviceDisplayItem>(
                        filteredAdvices.Select(a => new AdviceDisplayItem
                        {
                            Id = a.Id,
                            Title = a.Title,
                            AdviceType = a.AdviceType,
                            Content = a.Content,
                            Source = a.Source,
                            Priority = a.Priority,
                            Status = a.Status,
                            GeneratedTime = a.GeneratedTime
                        }));
                }
                else
                {
                    SetDefaultAdviceForSpecies(speciesId, speciesName);
                }
            }
            catch
            {
                SetDefaultAdviceForSpecies(speciesId, GetSpeciesName(speciesId));
            }
            finally
            {
                IsLoading = false;
                OnPropertyChanged(nameof(IsEmptyAdvice));
            }
        }

        private void SetDefaultAdviceForSpecies(int speciesId, string speciesName)
        {
            ActiveAdvices = new ObservableCollection<AdviceDisplayItem>
            {
                new()
                {
                    Id = 1, Title = $"提前采收{speciesName}", AdviceType = "Harvest",
                    Content = $"当前温度22°C，预报未来3天温度将降至15°C，建议提前2天采收{speciesName}，预计可提升花材品质等级。",
                    Source = "综合分析", Priority = "High", Status = "Pending",
                    GeneratedTime = DateTime.Now.AddHours(-2)
                },
                new()
                {
                    Id = 2, Title = $"{speciesName}灌溉建议", AdviceType = "Irrigation",
                    Content = $"土壤湿度35%（低于40%阈值），结合预报未来2天无降水，建议今日对{speciesName}进行灌溉。",
                    Source = "传感器数据", Priority = "Normal", Status = "Pending",
                    GeneratedTime = DateTime.Now.AddHours(-1)
                }
            };
        }

        private string GetSpeciesName(int speciesId) => _speciesLookup.GetSpeciesName(speciesId);

        private void SetDefaultAdvice()
        {
            ActiveAdvices = new ObservableCollection<AdviceDisplayItem>
            {
                new()
                {
                    Id = 1, Title = "提前采收红玫瑰", AdviceType = "Harvest",
                    Content = "当前温度22°C，预报未来3天温度将降至15°C，建议提前2天采收，预计可提升花材品质等级。",
                    Source = "综合分析", Priority = "High", Status = "Pending",
                    GeneratedTime = DateTime.Now.AddHours(-2)
                },
                new()
                {
                    Id = 2, Title = "灌溉建议", AdviceType = "Irrigation",
                    Content = "土壤湿度35%（低于40%阈值），结合预报未来2天无降水，建议今日灌溉。",
                    Source = "传感器数据", Priority = "Normal", Status = "Pending",
                    GeneratedTime = DateTime.Now.AddHours(-1)
                },
                new()
                {
                    Id = 3, Title = "通风建议", AdviceType = "Ventilation",
                    Content = "当前温度32°C，CO₂浓度600ppm，建议开启通风设备，目标温度降至28°C以下。",
                    Source = "传感器数据", Priority = "High", Status = "Executed",
                    GeneratedTime = DateTime.Now.AddHours(-4)
                },
                new()
                {
                    Id = 4, Title = "灰霉病预警", AdviceType = "Pest",
                    Content = "连续3天湿度>85%，温度18-24°C，灰霉病高风险，建议加强通风，必要时喷洒杀菌剂。",
                    Source = "综合分析", Priority = "High", Status = "Pending",
                    GeneratedTime = DateTime.Now.AddMinutes(-30)
                }
            };
        }

        private void LoadFallbackData()
        {
            SetDefaultSensorData();
            SetDefaultDeviceGroups();
            SetDefaultCostData();
            SetDefaultYieldData();
            SetDefaultAdvice();
            HarvestAdvices = new ObservableCollection<HarvestAdviceItem>
            {
                new()
                {
                    SpeciesName = "红玫瑰",
                    AdviceText = "当前市场价格处于上升趋势，建议提前2天采收以获取最佳利润。",
                    SuggestedHarvestDate = DateTime.Now.AddDays(5),
                    PriceForecast = 12.80m
                },
                new()
                {
                    SpeciesName = "百合",
                    AdviceText = "近期供应充足，建议延后采收等待价格回升。",
                    SuggestedHarvestDate = DateTime.Now.AddDays(10),
                    PriceForecast = 8.50m
                }
            };
        }

        private async Task LoadHistoryChartsAsync()
        {
            try
            {
                var devices = await _iotService.GetIoTDevicesAsync(CurrentGreenhouseId).ConfigureAwait(false);
                if (devices == null || devices.Count == 0)
                {
                    InitFallbackCharts();
                    return;
                }

                var device = devices.FirstOrDefault(d => d.OnlineStatus == "Online") ?? devices[0];
                var (start, end) = GetTimeRange(_currentTimeRange);
                var history = await _iotService.GetSensorHistoryAsync(device.DeviceCode, start, end).ConfigureAwait(false);

                if (history != null && history.Count > 0)
                {
                    var labels = history.Select(h => h.ReadingTime.ToString("HH:mm")).ToArray();
                    var tempData = history.Select(h => h.Temperature).ToArray();
                    var humidData = history.Select(h => h.Humidity).ToArray();

                    HistorySeries = new ISeries[]
                    {
                        new LineSeries<double> { Values = tempData, Name = "温度(℃)", Fill = null },
                        new LineSeries<double> { Values = humidData, Name = "湿度(%)", Fill = null }
                    };
                    HistoryXAxes = new Axis[] { new Axis { Labels = labels, LabelsRotation = 45 } };
                    HistoryYAxes = new Axis[] { new Axis { MinLimit = 10, MaxLimit = 90 } };
                }
                else
                {
                    InitFallbackCharts();
                    return;
                }

                var tempRanges = new[] { "<10°C", "10-20°C", "20-30°C", "30-35°C", ">35°C" };
                var tempCounts = new[] { 0, 0, 0, 0, 0 };
                var lightRanges = new[] { "<5000lux", "5000-15000lux", "15000-30000lux", "30000-50000lux", ">50000lux" };
                var lightCounts = new[] { 0, 0, 0, 0, 0 };
                foreach (var r in history)
                {
                    var t = r.Temperature;
                    if (t < 10) tempCounts[0]++;
                    else if (t < 20) tempCounts[1]++;
                    else if (t < 30) tempCounts[2]++;
                    else if (t < 35) tempCounts[3]++;
                    else tempCounts[4]++;

                    var l = r.LightIntensity;
                    if (l < 5000) lightCounts[0]++;
                    else if (l < 15000) lightCounts[1]++;
                    else if (l < 30000) lightCounts[2]++;
                    else if (l < 50000) lightCounts[3]++;
                    else lightCounts[4]++;
                }

                if (tempCounts.Sum() > 0)
                {
                    TemperaturePieSeries = tempRanges.Select((label, i) => new PieSeries<double> { Values = new double[] { tempCounts[i] }, Name = label }).ToArray();
                }
                else
                {
                    TemperaturePieSeries = new ISeries[] { new PieSeries<double> { Values = new double[] { 1 }, Name = "暂无数据" } };
                }

                if (lightCounts.Sum() > 0)
                {
                    LightPieSeries = lightRanges.Select((label, i) => new PieSeries<double> { Values = new double[] { lightCounts[i] }, Name = label }).ToArray();
                }
                else
                {
                    LightPieSeries = new ISeries[] { new PieSeries<double> { Values = new double[] { 1 }, Name = "暂无数据" } };
                }
            }
            catch
            {
                InitFallbackCharts();
            }

            OnPropertyChanged(nameof(HistorySeries));
            OnPropertyChanged(nameof(HistoryXAxes));
            OnPropertyChanged(nameof(HistoryYAxes));
            OnPropertyChanged(nameof(TemperaturePieSeries));
            OnPropertyChanged(nameof(LightPieSeries));
        }

        private void InitFallbackCharts()
        {
            var hours = Enumerable.Range(0, 24).Select(h => $"{h:00}:00").ToArray();
            var random = new Random(42);
            var tempData = Enumerable.Range(0, 24).Select(_ => 18 + random.NextDouble() * 12).ToArray();
            var humidData = Enumerable.Range(0, 24).Select(_ => 50 + random.NextDouble() * 30).ToArray();

            HistorySeries = new ISeries[]
            {
                new LineSeries<double> { Values = tempData, Name = "温度(℃)", Fill = null },
                new LineSeries<double> { Values = humidData, Name = "湿度(%)", Fill = null }
            };
            HistoryXAxes = new Axis[] { new Axis { Labels = hours, LabelsRotation = 45 } };
            HistoryYAxes = new Axis[] { new Axis { MinLimit = 10, MaxLimit = 90 } };

            InitFallbackPieCharts();
        }

        private void InitFallbackPieCharts()
        {
            TemperaturePieSeries = new ISeries[]
            {
                new PieSeries<double> { Values = new double[] { 10 }, Name = "<15°C" },
                new PieSeries<double> { Values = new double[] { 60 }, Name = "15-25°C" },
                new PieSeries<double> { Values = new double[] { 25 }, Name = "25-35°C" },
                new PieSeries<double> { Values = new double[] { 5 }, Name = ">35°C" }
            };

            LightPieSeries = new ISeries[]
            {
                new PieSeries<double> { Values = new double[] { 30 }, Name = "强光(>20000)" },
                new PieSeries<double> { Values = new double[] { 45 }, Name = "适中(5000-20000)" },
                new PieSeries<double> { Values = new double[] { 20 }, Name = "弱光(<5000)" },
                new PieSeries<double> { Values = new double[] { 5 }, Name = "无光" }
            };
        }

        private async Task AddCostAsync()
        {
            if (CurrentBatchId <= 0) return;
            CostAmount = 0;
            CostCategory = "Seedling";
            CostDate = DateTime.Now;
            CostRemark = "";
            IsCostDialogOpen = true;
        }

        private async Task SubmitCostAsync()
        {
            if (CostAmount <= 0) return;
            var result = await _iotService.AddCostRecordAsync(new AddCostRecordRequest
            {
                BatchId = CurrentBatchId,
                Category = CostCategory,
                Amount = CostAmount,
                CostDate = CostDate,
                Remark = CostRemark
            }).ConfigureAwait(false);
            if (result == 0)
            {
                ErrorMessage = "成本录入失败，请重试";
                return;
            }
            IsCostDialogOpen = false;
            await LoadCostDataAsync(CurrentBatchId);
        }

        private async Task AddYieldAsync()
        {
            if (CurrentBatchId <= 0) return;
            YieldQuantity = 0;
            YieldSpeciesName = "";
            YieldGrade = "A";
            YieldHarvestDate = DateTime.Now;
            YieldRemark = "";
            IsYieldDialogOpen = true;
        }

        private async Task SubmitYieldAsync()
        {
            if (YieldQuantity <= 0 || string.IsNullOrWhiteSpace(YieldSpeciesName)) return;
            var result = await _iotService.AddYieldRecordAsync(new AddYieldRecordRequest
            {
                BatchId = CurrentBatchId,
                SpeciesName = YieldSpeciesName,
                Quantity = YieldQuantity,
                Grade = YieldGrade,
                HarvestDate = YieldHarvestDate,
                Remark = YieldRemark
            }).ConfigureAwait(false);
            if (result == 0)
            {
                ErrorMessage = "产量录入失败，请重试";
                return;
            }
            IsYieldDialogOpen = false;
            await LoadYieldDataAsync(CurrentBatchId);
        }

        private async Task SetTimeRangeAsync(object param)
        {
            _currentTimeRange = param?.ToString() ?? "24h";
            CurrentTimeRangeDisplay = _currentTimeRange switch
            {
                "24h" => "24h",
                "7d" => "7天",
                "30d" => "30天",
                _ => _currentTimeRange
            };
            await LoadHistoryChartsAsync();
        }

        private async Task ExportDataAsync()
        {
            try
            {
                var devices = await _iotService.GetIoTDevicesAsync(CurrentGreenhouseId).ConfigureAwait(false);
                if (devices == null || devices.Count == 0) return;

                var device = devices.FirstOrDefault(d => d.OnlineStatus == "Online") ?? devices[0];
                var (start, end) = GetTimeRange(_currentTimeRange);
                var history = await _iotService.GetSensorHistoryAsync(device.DeviceCode, start, end).ConfigureAwait(false);
                if (history == null || history.Count == 0) return;

                var sb = new System.Text.StringBuilder();
                sb.AppendLine("时间,设备ID,温度(℃),湿度(%),光照(lux),CO2(ppm),土壤湿度(%)");
                foreach (var r in history)
                {
                    sb.AppendLine($"{r.ReadingTime:yyyy-MM-dd HH:mm:ss},{r.DeviceId},{r.Temperature:F1},{r.Humidity:F1},{r.LightIntensity:F0},{r.Co2Level:F0},{r.SoilMoisture:F1}");
                }

                var fileName = $"传感器数据_{device.DeviceName}_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
                var filePath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), fileName);
                await System.IO.File.WriteAllTextAsync(filePath, sb.ToString(), System.Text.Encoding.UTF8).ConfigureAwait(false);
                ErrorMessage = "";
                MqttConnectionStatus = $"数据已导出到: {filePath}";
            }
            catch
            {
                ErrorMessage = "导出失败，请重试";
            }
        }

        private static (DateTime start, DateTime end) GetTimeRange(string timeRange) => timeRange switch
        {
            "1h" => (DateTime.UtcNow.AddHours(-1), DateTime.UtcNow),
            "6h" => (DateTime.UtcNow.AddHours(-6), DateTime.UtcNow),
            "24h" => (DateTime.UtcNow.AddHours(-24), DateTime.UtcNow),
            "7d" => (DateTime.UtcNow.AddDays(-7), DateTime.UtcNow),
            "30d" => (DateTime.UtcNow.AddDays(-30), DateTime.UtcNow),
            _ => (DateTime.UtcNow.AddHours(-24), DateTime.UtcNow)
        };

        private async Task FilterAdviceAsync(object param)
        {
            var filter = param?.ToString() ?? "All";
            AdviceFilter = filter;
            if (CurrentBatchId <= 0) return;
            if (filter == "All")
            {
                await LoadAdviceAsync();
            }
            else
            {
                var advices = await _aiService.GetAdviceByTypeAsync(CurrentBatchId, filter).ConfigureAwait(false);
                if (advices != null)
                {
                    ActiveAdvices = new ObservableCollection<AdviceDisplayItem>(
                        advices.Select(a => new AdviceDisplayItem
                        {
                            Id = a.Id, Title = a.Title, AdviceType = a.AdviceType,
                            Content = a.Content, Source = a.Source, Priority = a.Priority,
                            Status = a.Status, GeneratedTime = a.GeneratedTime
                        }));
                }
            }
        }

        private async Task GenerateAdviceAsync()
        {
            if (CurrentBatchId <= 0) return;
            var advices = await _aiService.GenerateAdviceAsync(CurrentBatchId).ConfigureAwait(false);
            if (advices != null)
            {
                ActiveAdvices = new ObservableCollection<AdviceDisplayItem>(
                    advices.Select(a => new AdviceDisplayItem
                    {
                        Id = a.Id, Title = a.Title, AdviceType = a.AdviceType,
                        Content = a.Content, Source = a.Source, Priority = a.Priority,
                        Status = a.Status, GeneratedTime = a.GeneratedTime
                    }));
            }
        }

        private async Task MarkExecutedAsync(object param)
        {
            if (param is not AdviceDisplayItem item) return;
            try
            {
                await _aiService.MarkAdviceExecutedAsync(item.Id, CurrentBatchId).ConfigureAwait(false);
                item.Status = "Executed";
                OnPropertyChanged(nameof(ActiveAdvices));
            }
            catch
            {
                ErrorMessage = "标记失败，请重试";
            }
        }

        private async Task CreateGroupAsync()
        {
            GroupNameInput = "";
            GroupDescriptionInput = "";
            IsGroupDialogOpen = true;
        }

        private async Task SubmitGroupAsync()
        {
            if (string.IsNullOrWhiteSpace(GroupNameInput)) return;
            IsGroupDialogOpen = false;
            await _iotService.CreateDeviceGroupAsync(new CreateDeviceGroupRequest
            {
                GroupName = GroupNameInput,
                Description = GroupDescriptionInput,
                GreenhouseId = CurrentGreenhouseId
            }).ConfigureAwait(false);
            await LoadDeviceGroupsAsync();
        }

        private async Task AddDeviceAsync()
        {
            DeviceNameInput = "";
            DeviceTypeInput = "Sensor";
            DeviceProtocolInput = "MQTT";
            DeviceGroupInput = null;
            DeviceLocationInput = "";
            DeviceManufacturerInput = "";
            DeviceModelInput = "";
            DeviceCapabilitiesInput = "";
            DeviceRemarkInput = "";
            var groups = await _iotService.GetDeviceGroupsAsync(CurrentGreenhouseId);
            if (groups != null)
                AvailableGroups = new ObservableCollection<GroupSelectItem>(groups.Select(g => new GroupSelectItem { Id = g.Id.ToString(), Name = g.GroupName }));
            IsDeviceDialogOpen = true;
        }

        private async Task SubmitDeviceAsync()
        {
            if (string.IsNullOrWhiteSpace(DeviceNameInput)) return;
            IsDeviceDialogOpen = false;
            var result = await _iotService.RegisterDeviceAsync(new RegisterDeviceRequest
            {
                DeviceName = DeviceNameInput,
                DeviceType = DeviceTypeInput,
                GreenhouseId = CurrentGreenhouseId,
                GroupId = DeviceGroupInput?.Id ?? "",
                Protocol = DeviceProtocolInput,
                Location = DeviceLocationInput,
                Manufacturer = DeviceManufacturerInput,
                Model = DeviceModelInput,
                SensorCapabilities = DeviceCapabilitiesInput,
                Remark = DeviceRemarkInput
            }).ConfigureAwait(false);
            if (result == null)
            {
                ErrorMessage = "设备注册失败，请重试";
            }
            await LoadDeviceGroupsAsync();
        }

        private async Task BindDeviceAsync()
        {
            BindDeviceCodeInput = "";
            BindGroupIdInput = "";
            IsBindDialogOpen = true;
        }

        private async Task SubmitBindDeviceAsync()
        {
            if (string.IsNullOrWhiteSpace(BindDeviceCodeInput)) return;
            IsBindDialogOpen = false;
            await _iotService.BindDeviceAsync(new BindDeviceRequestInfo
            {
                DeviceCode = BindDeviceCodeInput,
                GreenhouseId = CurrentGreenhouseId,
                GroupId = BindGroupIdInput
            }).ConfigureAwait(false);
            await LoadDeviceGroupsAsync();
        }

        private async Task UnbindDeviceAsync(object param)
        {
            if (param is not DeviceDisplayItem item) return;
            await _iotService.UnbindDeviceAsync(item.DeviceCode).ConfigureAwait(false);
            await LoadDeviceGroupsAsync();
        }

        private async Task ChangeGroupAsync(object param)
        {
            if (param is not DeviceDisplayItem item) return;
            _changeGroupDeviceCode = item.DeviceCode;
            ChangeGroupTargetGroupId = null;
            var groups = await _iotService.GetDeviceGroupsAsync(CurrentGreenhouseId).ConfigureAwait(false);
            if (groups != null && groups.Count > 0)
            {
                AvailableGroups = new ObservableCollection<GroupSelectItem>(groups.Select(g => new GroupSelectItem { Id = g.Id.ToString(), Name = g.GroupName }));
            }
            IsChangeGroupDialogOpen = true;
        }

        private async Task SubmitChangeGroupAsync()
        {
            if (ChangeGroupTargetGroupId == null || string.IsNullOrEmpty(_changeGroupDeviceCode)) return;
            IsChangeGroupDialogOpen = false;
            await _iotService.ChangeDeviceGroupAsync(_changeGroupDeviceCode, ChangeGroupTargetGroupId.Id).ConfigureAwait(false);
            await LoadDeviceGroupsAsync();
        }

        private async Task LoadComparisonAsync()
        {
            var selectedDeviceCodes = _comparisonDevices.Where(d => d.IsSelected).Select(d => d.DeviceCode).ToList();
            if (selectedDeviceCodes.Count < 2) return;

            IsComparisonLoading = true;
            try
            {
                var (start, end) = GetTimeRange("7d");
                var result = await _iotService.GetMultiDeviceComparisonAsync(selectedDeviceCodes, start, end).ConfigureAwait(false);
                if (result != null)
                {
                    var results = new ObservableCollection<DeviceComparisonResultItem>();
                    foreach (var device in result.Devices)
                    {
                        results.Add(new DeviceComparisonResultItem
                        {
                            DeviceId = device.DeviceId,
                            AvgTemperature = device.AvgTemperature,
                            AvgHumidity = device.AvgHumidity,
                            AvgLightIntensity = device.AvgLightIntensity,
                            AvgCo2Level = device.AvgCo2Level,
                            AvgSoilMoisture = device.AvgSoilMoisture
                        });
                    }
                    ComparisonResults = results;
                }
            }
            finally
            {
                IsComparisonLoading = false;
            }
        }

        private async Task LoadComparisonDevicesAsync()
        {
            var devices = await _iotService.GetIoTDevicesAsync(CurrentGreenhouseId).ConfigureAwait(false);
            if (devices != null && devices.Count > 0)
            {
                ComparisonDevices = new ObservableCollection<DeviceComparisonItem>(
                    devices.Select(d => new DeviceComparisonItem
                    {
                        DeviceCode = d.DeviceCode,
                        DeviceName = d.DeviceName,
                        IsSelected = false
                    }));
            }
        }

        private async Task LoadAnalysisDataAsync()
        {
            try
            {
                var (start, end) = GetTimeRange("7d");

                var healthIndex = await _iotService.GetHealthIndexAsync(CurrentGreenhouseId, start, end).ConfigureAwait(false);
                if (healthIndex != null)
                {
                    HealthIndexData = healthIndex;
                }

                var devices = await _iotService.GetIoTDevicesAsync(CurrentGreenhouseId).ConfigureAwait(false);
                if (devices != null && devices.Count > 0)
                {
                    var device = devices.FirstOrDefault(d => d.OnlineStatus == "Online") ?? devices[0];
                    var anomalies = await _iotService.GetAnomaliesAsync(device.DeviceCode, start, end).ConfigureAwait(false);
                    if (anomalies != null)
                    {
                        Anomalies = new ObservableCollection<AnomalyInfo>(anomalies);
                    }

                    var trend = await _iotService.GetTrendAnalysisAsync(device.DeviceCode, start, end, "day").ConfigureAwait(false);
                    if (trend?.SignificantChanges != null && trend.SignificantChanges.Count > 0)
                    {
                        SignificantChanges = new ObservableCollection<SignificantChangePointInfo>(trend.SignificantChanges);
                    }
                }
            }
            catch
            {
                ErrorMessage = "数据加载失败";
            }
        }

        private async Task SelectDeviceForControlAsync(object param)
        {
            if (param is DeviceDisplayItem item)
            {
                SelectedDeviceForControl = item;
                SelectedDeviceCode = item.DeviceCode;
                IsDeviceTwinLoaded = false;
                DeviceTwinDesiredProperties = new ObservableCollection<TwinPropertyDisplayItem>();
                DeviceTwinReportedProperties = new ObservableCollection<TwinPropertyDisplayItem>();
                DeviceTwinDifferences = new ObservableCollection<TwinDiffDisplayItem>();

                if (_mqttService.IsConnected)
                {
                    try
                    {
                        await _mqttService.SubscribeCommandResponseAsync(CurrentGreenhouseId, item.DeviceCode).ConfigureAwait(false);
                    }
                    catch { }
                }
            }
            await Task.CompletedTask;
        }

        private async Task SendIrrigationCommandAsync()
        {
            if (_selectedDeviceForControl == null) return;
            IsSendingIrrigation = true;
            try
            {
                await SendDeviceCommandAsync(CurrentGreenhouseId, _selectedDeviceForControl.DeviceCode, "irrigation", "{\"state\":\"on\"}").ConfigureAwait(false);
            }
            finally
            {
                IsSendingIrrigation = false;
            }
        }

        private async Task SendVentilationCommandAsync()
        {
            if (_selectedDeviceForControl == null) return;
            IsSendingVentilation = true;
            try
            {
                await SendDeviceCommandAsync(CurrentGreenhouseId, _selectedDeviceForControl.DeviceCode, "ventilation", "{\"state\":\"on\"}").ConfigureAwait(false);
            }
            finally
            {
                IsSendingVentilation = false;
            }
        }

        private async Task SendLightingCommandAsync()
        {
            if (_selectedDeviceForControl == null) return;
            IsSendingLighting = true;
            try
            {
                await SendDeviceCommandAsync(CurrentGreenhouseId, _selectedDeviceForControl.DeviceCode, "lighting", "{\"state\":\"on\"}").ConfigureAwait(false);
            }
            finally
            {
                IsSendingLighting = false;
            }
        }

        private async Task SendDeviceCommandAsync(string greenhouseId, string deviceCode, string action, string payload)
        {
            if (!_mqttService.IsConnected)
            {
                MqttConnectionStatus = "MQTT未连接，无法发送命令";
                return;
            }

            IsSendingCommand = true;
            try
            {
                await _mqttService.PublishCommandAsync(greenhouseId, deviceCode, action, payload).ConfigureAwait(false);
                MqttConnectionStatus = $"命令已发送: {action}";
            }
            catch (Exception ex)
            {
                MqttConnectionStatus = $"命令发送失败: {ex.Message}";
            }
            finally
            {
                IsSendingCommand = false;
            }
        }

        private async Task LoadDeviceTwinAsync()
        {
            if (_selectedDeviceForControl == null) return;
            try
            {
                var twin = await _iotService.GetDeviceTwinAsync(_selectedDeviceForControl.DeviceCode).ConfigureAwait(false);
                if (twin != null)
                {
                    CurrentDeviceTwin = twin;
                    DeviceTwinDesiredProperties = new ObservableCollection<TwinPropertyDisplayItem>(
                        twin.DesiredProperties.Select(kv => new TwinPropertyDisplayItem { Key = kv.Key, Value = kv.Value }));
                    DeviceTwinReportedProperties = new ObservableCollection<TwinPropertyDisplayItem>(
                        twin.ReportedProperties.Select(kv => new TwinPropertyDisplayItem { Key = kv.Key, Value = kv.Value }));
                    DeviceTwinDifferences = new ObservableCollection<TwinDiffDisplayItem>(
                        twin.Differences.Select(d => new TwinDiffDisplayItem
                        {
                            Key = d.Key,
                            DesiredValue = d.DesiredValue ?? "-",
                            ReportedValue = d.ReportedValue ?? "-",
                            IsDifferent = d.DesiredValue != d.ReportedValue
                        }));
                    IsDeviceTwinLoaded = true;
                    OnPropertyChanged(nameof(HasNoDesiredProperties));
                    OnPropertyChanged(nameof(HasNoReportedProperties));
                    OnPropertyChanged(nameof(HasNoTwinDifferences));
                }
            }
            catch
            {
                ErrorMessage = "数据加载失败";
            }
        }

        private async Task ConnectMqttAsync()
        {
            try
            {
                if (!_mqttService.IsConnected)
                {
                    var host = Environment.GetEnvironmentVariable("HUNDUN_MQTT_HOST") ?? "localhost";
                    var portStr = Environment.GetEnvironmentVariable("HUNDUN_MQTT_WS_PORT") ?? "8083";
                    var port = int.TryParse(portStr, out var p) ? p : 8083;
                    await _mqttService.ConnectAsync(host, port).ConfigureAwait(false);
                    await _mqttService.SubscribeSensorDataAsync(CurrentGreenhouseId).ConfigureAwait(false);
                }
                IsMqttConnected = _mqttService.IsConnected;
                MqttConnectionStatus = _mqttService.IsConnected ? "MQTT已连接" : "MQTT连接失败";
            }
            catch (Exception ex)
            {
                IsMqttConnected = false;
                MqttConnectionStatus = $"MQTT连接异常: {ex.Message}";
            }
        }

        private void InitializeMqtt()
        {
            _mqttService.SensorDataReceived += OnSensorDataReceived;
            _mqttService.ConnectionStateChanged += OnMqttConnectionStateChanged;
            _mqttService.CommandResponseReceived += OnCommandResponseReceived;
            IsMqttConnected = _mqttService.IsConnected;
            MqttConnectionStatus = _mqttService.IsConnected ? "MQTT已连接" : "MQTT未连接";
        }

        private void OnSensorDataReceived(object sender, SensorDataEventArgs e)
        {
            if (e.Reading == null) return;
            if (e.GreenhouseId != CurrentGreenhouseId) return;

            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                var reading = e.Reading;
                var isCurrentDevice = _selectedDeviceForControl != null && e.DeviceCode == _selectedDeviceForControl.DeviceCode;

                if (isCurrentDevice || string.IsNullOrEmpty(_selectedDeviceCode))
                {
                    SensorData = new SensorReadingData
                    {
                        Temperature = reading.Temperature,
                        Humidity = reading.Humidity,
                        LightIntensity = reading.LightIntensity,
                        Co2Concentration = reading.Co2Level,
                        SoilMoisture = reading.SoilMoisture,
                        TemperatureTrend = reading.Temperature > 25 ? "↑ 偏高" : reading.Temperature < 15 ? "↓ 偏低" : "→ 正常",
                        HumidityTrend = reading.Humidity > 80 ? "↑ 偏高" : reading.Humidity < 40 ? "↓ 偏低" : "→ 正常",
                        LightTrend = reading.LightIntensity > 20000 ? "↑ 强光" : reading.LightIntensity < 5000 ? "↓ 弱光" : "→ 适中",
                        Co2Trend = reading.Co2Level > 500 ? "↑ 偏高" : "→ 正常",
                        SoilTrend = reading.SoilMoisture < 40 ? "↓ 偏干" : "→ 正常",
                        Summary = "实时传感器数据推送更新。"
                    };

                    var alerts = new ObservableCollection<ThresholdAlertItem>();
                    if (reading.SoilMoisture < 40) alerts.Add(new() { Message = $"土壤湿度 {reading.SoilMoisture:F1}%，低于40%阈值，建议灌溉" });
                    if (reading.Co2Level > 500) alerts.Add(new() { Message = $"CO₂浓度 {reading.Co2Level:F0}ppm，高于500ppm阈值，建议通风" });
                    if (reading.Temperature > 35) alerts.Add(new() { Message = $"温度 {reading.Temperature:F1}°C，超过35°C高温阈值" });
                    if (alerts.Count > 0)
                    {
                        ThresholdAlerts = alerts;
                        OnPropertyChanged(nameof(HasNoThresholdAlerts));
                    }
                }
            });
        }

        private void OnMqttConnectionStateChanged(object sender, bool isConnected)
        {
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                IsMqttConnected = isConnected;
                MqttConnectionStatus = isConnected ? "MQTT已连接" : "MQTT已断开";
            });
        }

        private void OnCommandResponseReceived(object sender, CommandResponseEventArgs e)
        {
            if (e.Response == null) return;
            Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            {
                MqttConnectionStatus = e.Response.Success
                    ? $"设备 {e.DeviceCode} 命令 {e.Response.Action} 执行成功"
                    : $"设备 {e.DeviceCode} 命令 {e.Response.Action} 执行失败: {e.Response.Result}";
            });
        }

        private async Task OpenThresholdDialogAsync(object param)
        {
            if (param is not DeviceDisplayItem item) return;
            _thresholdDeviceCode = item.DeviceCode;
            var thresholds = await _iotService.GetThresholdsAsync(item.DeviceCode).ConfigureAwait(false);
            if (thresholds != null)
            {
                if (thresholds.TryGetValue("Temperature", out var temp)) ThresholdTemperature = temp;
                if (thresholds.TryGetValue("Humidity", out var hum)) ThresholdHumidity = hum;
                if (thresholds.TryGetValue("Co2Level", out var co2)) ThresholdCo2 = co2;
                if (thresholds.TryGetValue("LightIntensity", out var light)) ThresholdLight = light;
                if (thresholds.TryGetValue("SoilMoisture", out var soil)) ThresholdSoil = soil;
            }
            IsThresholdDialogOpen = true;
        }

        private async Task SaveThresholdsAsync()
        {
            if (string.IsNullOrEmpty(_thresholdDeviceCode)) return;
            IsThresholdSaving = true;
            try
            {
                var anyFailed = false;
                if (!await SetThresholdAsync(_thresholdDeviceCode, "Temperature", ThresholdTemperature).ConfigureAwait(false)) anyFailed = true;
                if (!await SetThresholdAsync(_thresholdDeviceCode, "Humidity", ThresholdHumidity).ConfigureAwait(false)) anyFailed = true;
                if (!await SetThresholdAsync(_thresholdDeviceCode, "Co2Level", ThresholdCo2).ConfigureAwait(false)) anyFailed = true;
                if (!await SetThresholdAsync(_thresholdDeviceCode, "LightIntensity", ThresholdLight).ConfigureAwait(false)) anyFailed = true;
                if (!await SetThresholdAsync(_thresholdDeviceCode, "SoilMoisture", ThresholdSoil).ConfigureAwait(false)) anyFailed = true;
                if (anyFailed)
                {
                    ErrorMessage = "阈值保存失败，请重试";
                }
                else
                {
                    IsThresholdDialogOpen = false;
                }
            }
            finally
            {
                IsThresholdSaving = false;
            }
        }

        private async Task OpenManualReportDialogAsync()
        {
            var devices = await _iotService.GetIoTDevicesAsync(CurrentGreenhouseId).ConfigureAwait(false);
            if (devices == null || devices.Count == 0)
            {
                ErrorMessage = "无可用设备，无法手动录入";
                return;
            }
            var boundDevice = devices.FirstOrDefault(d => d.BindingStatus == "Bound");
            if (boundDevice == null)
            {
                ErrorMessage = "无已绑定设备，请先完成设备绑定";
                return;
            }
            _manualReportDeviceCode = boundDevice.DeviceCode;
            ManualTemperature = 0;
            ManualHumidity = 0;
            ManualLight = 0;
            ManualCo2 = 0;
            ManualSoil = 0;
            IsManualReportDialogOpen = true;
        }

        private async Task SubmitManualReportAsync()
        {
            if (string.IsNullOrEmpty(_manualReportDeviceCode)) return;
            IsManualReportDialogOpen = false;
            var request = new ManualSensorReportRequest
            {
                DeviceId = _manualReportDeviceCode,
                GreenhouseId = CurrentGreenhouseId,
                Temperature = ManualTemperature,
                Humidity = ManualHumidity,
                LightIntensity = ManualLight,
                Co2Level = ManualCo2,
                SoilMoisture = ManualSoil
            };
            var success = await _iotService.ReportSensorDataAsync(request).ConfigureAwait(false);
            if (!success)
            {
                ErrorMessage = "手动录入失败，请检查设备状态";
            }
        }

        private async Task DeleteGroupAsync(object param)
        {
            if (param is DeviceGroupDisplayItem group && group.GroupName != "未分组设备")
            {
                var success = await _iotService.DeleteDeviceGroupAsync(group.GroupId).ConfigureAwait(false);
                if (!success) ErrorMessage = "删除分组失败，请重试";
                await LoadDeviceGroupsAsync();
            }
        }

        private async Task OpenRenameGroupDialogAsync(object param)
        {
            if (param is DeviceGroupDisplayItem group && group.GroupName != "未分组设备")
            {
                RenameGroupNewName = group.GroupName;
                _renameGroupTargetId = group.GroupId;
                IsRenameGroupDialogOpen = true;
            }
        }

        private async Task SubmitRenameGroupAsync()
        {
            if (string.IsNullOrWhiteSpace(RenameGroupNewName)) return;
            var success = await _iotService.RenameDeviceGroupAsync(_renameGroupTargetId, RenameGroupNewName).ConfigureAwait(false);
            if (!success)
            {
                ErrorMessage = "重命名分组失败，请重试";
                return;
            }
            IsRenameGroupDialogOpen = false;
            await LoadDeviceGroupsAsync();
        }

        public async Task<bool> SetThresholdAsync(string deviceCode, string metricName, double threshold)
        {
            try
            {
                var httpSuccess = await _iotService.SetThresholdAsync(deviceCode, metricName, threshold).ConfigureAwait(false);
                if (!httpSuccess) return false;

                if (_mqttService.IsConnected)
                {
                    try
                    {
                        var configPayload = JsonSerializer.Serialize(new { metric = metricName, threshold, updatedAt = DateTime.UtcNow });
                        await _mqttService.PublishCommandAsync(CurrentGreenhouseId, deviceCode, "config", configPayload).ConfigureAwait(false);
                    }
                    catch { }
                }

                MqttConnectionStatus = $"阈值 {metricName}={threshold} 已设置并推送";
                return true;
            }
            catch
            {
                MqttConnectionStatus = $"阈值设置失败";
                return false;
            }
        }

        private async Task CancelDialogAsync(object param)
        {
            var dialogName = param?.ToString();
            switch (dialogName)
            {
                case "Cost": IsCostDialogOpen = false; break;
                case "Yield": IsYieldDialogOpen = false; break;
                case "Device": IsDeviceDialogOpen = false; break;
                case "Group": IsGroupDialogOpen = false; break;
                case "Listing": IsListingDialogOpen = false; break;
                case "Bind": IsBindDialogOpen = false; break;
            }
            await Task.CompletedTask;
        }

        private async Task ListFromYieldAsync(object param)
        {
            if (param is not YieldDisplayItem item) return;

            var merchant = await _merchantService.GetMyMerchantAsync().ConfigureAwait(false);
            if (merchant == null) return;

            var result = await _iotService.CreateProductFromYieldAsync(item.Id, merchant.MerchantId).ConfigureAwait(false);
            if (result != null && result.Success)
            {
                ListingId = result.ListingId;
                ListingYieldRecordId = item.Id;
                ListingSpeciesName = item.SpeciesName;
                ListingGrade = item.Grade;
                ListingQuantity = item.Quantity;
                ListingSuggestedPrice = result.SuggestedPrice;
                ListingActualPrice = result.SuggestedPrice;
                IsListingDialogOpen = true;
            }
        }

        private async Task BatchListFromYieldAsync()
        {
            var selectedItems = _yieldRecords.Where(r => r.IsSelected).ToList();
            if (selectedItems.Count == 0) return;

            var merchant = await _merchantService.GetMyMerchantAsync().ConfigureAwait(false);
            if (merchant == null) return;

            var selectedIds = selectedItems.Select(r => r.Id).ToList();
            var result = await _iotService.BatchCreateProductsFromYieldAsync(selectedIds, merchant.MerchantId).ConfigureAwait(false);
            if (result != null && result.Success)
            {
                ListingId = result.ListingId;
                ListingYieldRecordId = 0;
                ListingSpeciesName = $"{selectedItems.Count}条记录";
                ListingGrade = "";
                ListingQuantity = selectedItems.Sum(r => r.Quantity);
                ListingSuggestedPrice = result.SuggestedPrice;
                ListingActualPrice = result.SuggestedPrice;
                IsListingDialogOpen = true;
            }
        }

        private async Task ConfirmListingAsync()
        {
            if (ListingId <= 0 || ListingActualPrice <= 0) return;
            IsListingDialogOpen = false;

            var success = await _iotService.ConfirmHarvestListingAsync(ListingId, ListingActualPrice).ConfigureAwait(false);
            if (success)
            {
                await LoadYieldDataAsync(CurrentBatchId);
            }
        }

        private static string MapDeviceType(string type) => type switch
        {
            "Sensor" => "传感器",
            "Gateway" => "网关设备",
            "Controller" => "控制器",
            _ => type
        };

        private static string MapCategoryIcon(string category) => category switch
        {
            "Seedling" => "🌱", "Fertilizer" => "🧪", "Pesticide" => "💊",
            "Labor" => "👷", "Utility" => "⚡", "Depreciation" => "🏗",
            _ => "📋"
        };

        private static string MapCategoryDisplay(string category) => category switch
        {
            "Seedling" => "种苗", "Fertilizer" => "肥料", "Pesticide" => "农药",
            "Labor" => "人工", "Utility" => "水电", "Depreciation" => "设备折旧",
            "Other" => "其他", _ => category
        };

        private static string MapGradeIcon(string grade) => grade switch
        {
            "A" => "🅰️", "B" => "🅱️", "C" => "🅲️", _ => "📋"
        };

        private async Task ShowBatchDetailAsync(object? parameter)
        {
            if (parameter is long batchId)
            {
                var lifecycle = await _iotService.GetBatchLifecycleAsync(batchId).ConfigureAwait(false);
                var profit = await _iotService.GetBatchProfitAnalysisAsync(batchId).ConfigureAwait(false);
                var presaleStatus = await _iotService.GetPresaleStatusAsync(batchId).ConfigureAwait(false);

                BatchLifecycle = lifecycle;
                BatchProfitAnalysis = profit;
                PresaleFulfillment = presaleStatus;

                if (presaleStatus != null && presaleStatus.PresaleOrders != null && presaleStatus.PresaleOrders.Count > 0)
                {
                    PresaleOrders = new ObservableCollection<PresaleOrderDisplayItem>(
                        presaleStatus.PresaleOrders.Select(o => new PresaleOrderDisplayItem
                        {
                            OrderId = o.OrderId,
                            OrderNo = o.OrderNo,
                            ProductName = o.ProductName,
                            Quantity = o.Quantity,
                            Subtotal = o.Subtotal,
                            IsNotified = o.IsPresaleReadyNotified
                        }));
                }
                else
                {
                    PresaleOrders = new ObservableCollection<PresaleOrderDisplayItem>();
                }
                OnPropertyChanged(nameof(HasPresaleOrders));
                OnPropertyChanged(nameof(PresaleProgressText));

                if (profit?.CostBreakdown != null && profit.CostBreakdown.Count > 0)
                {
                    var costColors = new[] { SKColor.Parse("#4CAF50"), SKColor.Parse("#2196F3"), SKColor.Parse("#FF9800"), SKColor.Parse("#9C27B0"), SKColor.Parse("#F44336"), SKColor.Parse("#00BCD4"), SKColor.Parse("#795548") };
                    ProfitCostPieSeries = profit.CostBreakdown.Select((c, i) => (ISeries)new PieSeries<decimal>
                    {
                        Values = new[] { c.TotalAmount },
                        Name = MapCategoryDisplay(c.Category ?? ""),
                        Fill = new SolidColorPaint(costColors[i % costColors.Length])
                    }).ToArray();
                }
                else
                {
                    ProfitCostPieSeries = Array.Empty<ISeries>();
                }

                if (profit?.RevenueBreakdown != null && profit.RevenueBreakdown.Count > 0)
                {
                    var revColors = new[] { SKColor.Parse("#66BB6A"), SKColor.Parse("#42A5F5"), SKColor.Parse("#FFA726"), SKColor.Parse("#AB47BC"), SKColor.Parse("#EF5350") };
                    ProfitRevenuePieSeries = profit.RevenueBreakdown.Select((r, i) => (ISeries)new PieSeries<decimal>
                    {
                        Values = new[] { r.Revenue },
                        Name = r.ProductName,
                        Fill = new SolidColorPaint(revColors[i % revColors.Length])
                    }).ToArray();
                }
                else
                {
                    ProfitRevenuePieSeries = Array.Empty<ISeries>();
                }

                IsBatchDetailDialogOpen = true;
            }
        }

        private async Task CloseBatchDetailAsync()
        {
            IsBatchDetailDialogOpen = false;
            await Task.CompletedTask;
        }
    }

    public class SensorReadingData : ViewModelBase
    {
        private double _temperature;
        private double _humidity;
        private double _lightIntensity;
        private double _co2Concentration;
        private double _soilMoisture;
        private string _summary = "";
        private string _temperatureTrend = "";
        private string _humidityTrend = "";
        private string _lightTrend = "";
        private string _co2Trend = "";
        private string _soilTrend = "";

        public double Temperature { get => _temperature; set => SetProperty(ref _temperature, value); }
        public double Humidity { get => _humidity; set => SetProperty(ref _humidity, value); }
        public double LightIntensity { get => _lightIntensity; set => SetProperty(ref _lightIntensity, value); }
        public double Co2Concentration { get => _co2Concentration; set => SetProperty(ref _co2Concentration, value); }
        public double SoilMoisture { get => _soilMoisture; set => SetProperty(ref _soilMoisture, value); }
        public string Summary { get => _summary; set => SetProperty(ref _summary, value); }
        public string TemperatureTrend { get => _temperatureTrend; set => SetProperty(ref _temperatureTrend, value); }
        public string HumidityTrend { get => _humidityTrend; set => SetProperty(ref _humidityTrend, value); }
        public string LightTrend { get => _lightTrend; set => SetProperty(ref _lightTrend, value); }
        public string Co2Trend { get => _co2Trend; set => SetProperty(ref _co2Trend, value); }
        public string SoilTrend { get => _soilTrend; set => SetProperty(ref _soilTrend, value); }
    }

    public class HarvestAdviceItem : ViewModelBase
    {
        private string _speciesName = "";
        private string _adviceText = "";
        private DateTime _suggestedHarvestDate;
        private decimal _priceForecast;
        public string SpeciesName { get => _speciesName; set => SetProperty(ref _speciesName, value); }
        public string AdviceText { get => _adviceText; set => SetProperty(ref _adviceText, value); }
        public DateTime SuggestedHarvestDate { get => _suggestedHarvestDate; set => SetProperty(ref _suggestedHarvestDate, value); }
        public decimal PriceForecast { get => _priceForecast; set => SetProperty(ref _priceForecast, value); }
    }

    public class PestAlertItem : ViewModelBase
    {
        private string _riskLevel = "low";
        private string _pestName = "";
        private string _description = "";
        private string _suggestedAction = "";
        public string RiskLevel { get => _riskLevel; set { if (SetProperty(ref _riskLevel, value)) { OnPropertyChanged(nameof(RiskLevelIcon)); OnPropertyChanged(nameof(RiskLevelDisplay)); OnPropertyChanged(nameof(RiskLevelColor)); } } }
        public string PestName { get => _pestName; set => SetProperty(ref _pestName, value); }
        public string Description { get => _description; set => SetProperty(ref _description, value); }
        public string SuggestedAction { get => _suggestedAction; set => SetProperty(ref _suggestedAction, value); }
        public string RiskLevelIcon => RiskLevel switch { "high" => "🔴", "medium" => "🟡", _ => "🟢" };
        public string RiskLevelDisplay => RiskLevel switch { "high" => "高风险", "medium" => "中风险", _ => "低风险" };
        public string RiskLevelColor => RiskLevel switch { "high" => "#EF5350", "medium" => "#FFA726", _ => "#66BB6A" };
    }

    public class ThresholdAlertItem : ViewModelBase
    {
        private string _message = "";
        public string Message { get => _message; set => SetProperty(ref _message, value); }
    }

    public class AdviceDisplayItem : ViewModelBase
    {
        private long _id;
        private string _title = "";
        private string _adviceType = "";
        private string _content = "";
        private string _source = "";
        private string _priority = "Normal";
        private string _status = "Pending";
        private DateTime _generatedTime;
        public long Id { get => _id; set => SetProperty(ref _id, value); }
        public string Title { get => _title; set => SetProperty(ref _title, value); }
        public string AdviceType { get => _adviceType; set => SetProperty(ref _adviceType, value); }
        public string Content { get => _content; set => SetProperty(ref _content, value); }
        public string Source { get => _source; set => SetProperty(ref _source, value); }
        public string Priority { get => _priority; set { if (SetProperty(ref _priority, value)) { OnPropertyChanged(nameof(PriorityBackground)); OnPropertyChanged(nameof(TypeIcon)); } } }
        public string Status { get => _status; set { if (SetProperty(ref _status, value)) { OnPropertyChanged(nameof(IsPending)); OnPropertyChanged(nameof(IsExecuted)); } } }
        public DateTime GeneratedTime { get => _generatedTime; set => SetProperty(ref _generatedTime, value); }
        public bool IsPending => Status == "Pending";
        public bool IsExecuted => Status == "Executed";
        public string TypeIcon => AdviceType switch { "Harvest" => "🌾", "Irrigation" => "💧", "Ventilation" => "🌬", "Pest" => "🐛", "Fertilizer" => "🧪", _ => "📋" };
        public string PriorityBackground => Priority switch { "High" => "#1AEF5350", "Normal" => "#1AFFA726", _ => "#1A66BB6A" };
        public string SourceBadgeBackground => Source switch { "传感器数据" => "#2196F3", "综合分析" => "#9C27B0", "预报数据" => "#FF9800", _ => "#607D8B" };
    }

    public class CostDisplayItem : ViewModelBase
    {
        private string _categoryIcon = "";
        private string _categoryDisplay = "";
        private decimal _amount;
        private DateTime _costDate;
        public string CategoryIcon { get => _categoryIcon; set => SetProperty(ref _categoryIcon, value); }
        public string CategoryDisplay { get => _categoryDisplay; set => SetProperty(ref _categoryDisplay, value); }
        public decimal Amount { get => _amount; set => SetProperty(ref _amount, value); }
        public DateTime CostDate { get => _costDate; set => SetProperty(ref _costDate, value); }
    }

    public class YieldDisplayItem : ViewModelBase
    {
        private long _id;
        private string _gradeIcon = "";
        private string _speciesName = "";
        private decimal _quantity;
        private string _grade = "A";
        private DateTime _harvestDate;
        private bool _isSelected;
        public long Id { get => _id; set => SetProperty(ref _id, value); }
        public string GradeIcon { get => _gradeIcon; set => SetProperty(ref _gradeIcon, value); }
        public string SpeciesName { get => _speciesName; set => SetProperty(ref _speciesName, value); }
        public decimal Quantity { get => _quantity; set => SetProperty(ref _quantity, value); }
        public string Grade { get => _grade; set => SetProperty(ref _grade, value); }
        public DateTime HarvestDate { get => _harvestDate; set => SetProperty(ref _harvestDate, value); }
        public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }
    }

    public class DeviceGroupDisplayItem : ViewModelBase
    {
        private string _groupId = "";
        public string GroupId { get => _groupId; set => SetProperty(ref _groupId, value); }
        private string _groupName = "";
        private ObservableCollection<DeviceDisplayItem> _devices = new();
        public string GroupName { get => _groupName; set => SetProperty(ref _groupName, value); }
        public ObservableCollection<DeviceDisplayItem> Devices { get => _devices; set => SetProperty(ref _devices, value); }
        public int DeviceCount => _devices?.Count ?? 0;
    }

    public class GroupSelectItem : ViewModelBase
    {
        private string _id = "";
        public string Id { get => _id; set => SetProperty(ref _id, value); }
        private string _name = "";
        public string Name { get => _name; set => SetProperty(ref _name, value); }
        public override string ToString() => Name;
    }

    public class DeviceDisplayItem : ViewModelBase
    {
        private string _deviceName = "";
        private string _deviceTypeDisplay = "";
        private Avalonia.Media.IBrush _statusBrush = Avalonia.Media.Brushes.Gray;
        private string _lastSeenText = "";
        private string _deviceCode = "";
        private string _bindingStatus = "Unbound";
        private string _selectedGroupId = "";
        private string _location = "";
        private string _manufacturer = "";
        private string _model = "";
        private string _protocol = "";
        private string _sensorCapabilities = "";
        private string _remark = "";
        public string DeviceName { get => _deviceName; set => SetProperty(ref _deviceName, value); }
        public string DeviceTypeDisplay { get => _deviceTypeDisplay; set => SetProperty(ref _deviceTypeDisplay, value); }
        public Avalonia.Media.IBrush StatusBrush { get => _statusBrush; set => SetProperty(ref _statusBrush, value); }
        public string LastSeenText { get => _lastSeenText; set => SetProperty(ref _lastSeenText, value); }
        public string DeviceCode { get => _deviceCode; set => SetProperty(ref _deviceCode, value); }
        public string BindingStatus { get => _bindingStatus; set { if (SetProperty(ref _bindingStatus, value)) { OnPropertyChanged(nameof(IsBound)); OnPropertyChanged(nameof(BindingStatusDisplay)); } } }
        public string SelectedGroupId { get => _selectedGroupId; set => SetProperty(ref _selectedGroupId, value); }
        public string Location { get => _location; set => SetProperty(ref _location, value); }
        public string Manufacturer { get => _manufacturer; set => SetProperty(ref _manufacturer, value); }
        public string Model { get => _model; set => SetProperty(ref _model, value); }
        public string Protocol { get => _protocol; set => SetProperty(ref _protocol, value); }
        public string SensorCapabilities { get => _sensorCapabilities; set => SetProperty(ref _sensorCapabilities, value); }
        public string Remark { get => _remark; set => SetProperty(ref _remark, value); }
        public bool IsBound => BindingStatus == "Bound";
        public string BindingStatusDisplay => BindingStatus switch { "Bound" => "已绑定", "Unbound" => "未绑定", "Disabled" => "已禁用", _ => BindingStatus };
    }

    public class ParameterAsyncCommand : ICommand
    {
        private readonly Func<object, Task> _execute;
        public ParameterAsyncCommand(Func<object, Task> execute) => _execute = execute;
        public bool CanExecute(object parameter) => true;
        public async void Execute(object parameter) => await _execute(parameter);
        public event EventHandler CanExecuteChanged;
    }

    public class PresaleOrderDisplayItem : ViewModelBase
    {
        private long _orderId;
        private string _orderNo = "";
        private string _productName = "";
        private int _quantity;
        private decimal _subtotal;
        private bool _isNotified;
        public long OrderId { get => _orderId; set => SetProperty(ref _orderId, value); }
        public string OrderNo { get => _orderNo; set => SetProperty(ref _orderNo, value); }
        public string ProductName { get => _productName; set => SetProperty(ref _productName, value); }
        public int Quantity { get => _quantity; set => SetProperty(ref _quantity, value); }
        public decimal Subtotal { get => _subtotal; set => SetProperty(ref _subtotal, value); }
        public bool IsNotified { get => _isNotified; set => SetProperty(ref _isNotified, value); }
        public string NotifiedText => IsNotified ? "已通知" : "待通知";
        public string NotifiedColor => IsNotified ? "#4CAF50" : "#FF9800";
    }

    public class TwinPropertyDisplayItem : ViewModelBase
    {
        private string _key = "";
        private string _value = "";
        public string Key { get => _key; set => SetProperty(ref _key, value); }
        public string Value { get => _value; set => SetProperty(ref _value, value); }
    }

    public class TwinDiffDisplayItem : ViewModelBase
    {
        private string _key = "";
        private string _desiredValue = "";
        private string _reportedValue = "";
        private bool _isDifferent;
        public string Key { get => _key; set => SetProperty(ref _key, value); }
        public string DesiredValue { get => _desiredValue; set => SetProperty(ref _desiredValue, value); }
        public string ReportedValue { get => _reportedValue; set => SetProperty(ref _reportedValue, value); }
        public bool IsDifferent { get => _isDifferent; set => SetProperty(ref _isDifferent, value); }
    }

    public class DeviceComparisonItem : ViewModelBase
    {
        private string _deviceCode = "";
        private string _deviceName = "";
        private bool _isSelected;
        public string DeviceCode { get => _deviceCode; set => SetProperty(ref _deviceCode, value); }
        public string DeviceName { get => _deviceName; set => SetProperty(ref _deviceName, value); }
        public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }
    }

    public class DeviceComparisonResultItem : ViewModelBase
    {
        private string _deviceId = "";
        private double _avgTemperature;
        private double _avgHumidity;
        private double _avgLightIntensity;
        private double _avgCo2Level;
        private double _avgSoilMoisture;
        public string DeviceId { get => _deviceId; set => SetProperty(ref _deviceId, value); }
        public double AvgTemperature { get => _avgTemperature; set => SetProperty(ref _avgTemperature, value); }
        public double AvgHumidity { get => _avgHumidity; set => SetProperty(ref _avgHumidity, value); }
        public double AvgLightIntensity { get => _avgLightIntensity; set => SetProperty(ref _avgLightIntensity, value); }
        public double AvgCo2Level { get => _avgCo2Level; set => SetProperty(ref _avgCo2Level, value); }
        public double AvgSoilMoisture { get => _avgSoilMoisture; set => SetProperty(ref _avgSoilMoisture, value); }
    }
}
