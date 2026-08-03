using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Horizon.Game.GengDi.Core.Services;

namespace Horizon.Game.GengDi.Core.ViewModels
{
    public class FlowerAIAssistantViewModel : ViewModelBase
    {
        private readonly FlowerAIService _aiService;
        private ObservableCollection<ChatMessageItem> _messages = new();
        private ObservableCollection<QuickActionGroup> _quickActionGroups = new();
        private string _inputText = "";
        private bool _isGenerating;
        private bool _isAIOnline = true;
        private int _dailyCallCount;
        private DateTime _dailyResetDate = DateTime.UtcNow.Date;
        private const int DailyCallLimit = 50;

        public ObservableCollection<ChatMessageItem> Messages
        {
            get => _messages;
            set => SetProperty(ref _messages, value);
        }

        /// <summary>
        /// 左侧快捷问题分组集合（行情咨询 / 种植建议 / 订单帮助 / 账户问题）。
        /// </summary>
        public ObservableCollection<QuickActionGroup> QuickActionGroups
        {
            get => _quickActionGroups;
            set => SetProperty(ref _quickActionGroups, value);
        }

        public string InputText
        {
            get => _inputText;
            set => SetProperty(ref _inputText, value);
        }

        public bool IsGenerating
        {
            get => _isGenerating;
            set => SetProperty(ref _isGenerating, value);
        }

        /// <summary>
        /// AI 在线状态（顶部标识区展示）。
        /// </summary>
        public bool IsAIOnline
        {
            get => _isAIOnline;
            set => SetProperty(ref _isAIOnline, value);
        }

        public bool IsEmpty => _messages.Count == 0;

        public bool CanSend => !string.IsNullOrWhiteSpace(_inputText) && !_isGenerating && _dailyCallCount < DailyCallLimit;

        public ICommand SendMessageCommand { get; }

        /// <summary>
        /// 快捷问题点击命令：将问题文本填入输入框并发送。
        /// </summary>
        public ICommand QuickActionCommand { get; }

        public FlowerAIAssistantViewModel()
        {
            _aiService = new FlowerAIService();
            SendMessageCommand = new AsyncCommand(SendMessageAsync);
            QuickActionCommand = new RelayCommand<string>(ExecuteQuickAction);
            InitializeMockData();
        }

        private void ExecuteQuickAction(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            InputText = text;
            OnPropertyChanged(nameof(CanSend));
            _ = SendMessageAsync();
        }

        /// <summary>
        /// 初始化模拟数据：快捷问题分组 + 原型聊天记录，100% 匹配设计原型。
        /// </summary>
        private void InitializeMockData()
        {
            // 快捷问题分组（匹配设计原型左侧面板）
            QuickActionGroups = new ObservableCollection<QuickActionGroup>
            {
                new QuickActionGroup
                {
                    IconEmoji = "📈",
                    GroupTitle = "行情咨询",
                    Items = new List<QuickActionItem>
                    {
                        new QuickActionItem { Text = "今天红玫瑰均价多少？" },
                        new QuickActionItem { Text = "下周花卉价格走势" },
                        new QuickActionItem { Text = "哪种花本周涨幅最大？" },
                    }
                },
                new QuickActionGroup
                {
                    IconEmoji = "🌱",
                    GroupTitle = "种植建议",
                    Items = new List<QuickActionItem>
                    {
                        new QuickActionItem { Text = "推荐春季种植品种" },
                        new QuickActionItem { Text = "白粉病防治方法" },
                        new QuickActionItem { Text = "土壤 PH 偏高怎么办" },
                    }
                },
                new QuickActionGroup
                {
                    IconEmoji = "🛍️",
                    GroupTitle = "订单帮助",
                    Items = new List<QuickActionItem>
                    {
                        new QuickActionItem { Text = "如何申请退款？" },
                        new QuickActionItem { Text = "发货周期是多久" },
                    }
                },
                new QuickActionGroup
                {
                    IconEmoji = "👤",
                    GroupTitle = "账户问题",
                    Items = new List<QuickActionItem>
                    {
                        new QuickActionItem { Text = "如何升级会员？" },
                        new QuickActionItem { Text = "修改绑定手机" },
                    }
                },
            };

            // 聊天记录（匹配设计原型右侧对话区）
            var baseTime = DateTime.Today.AddHours(9);
            Messages = new ObservableCollection<ChatMessageItem>
            {
                new ChatMessageItem
                {
                    Role = "花卉AI助手",
                    SenderName = "花卉AI助手",
                    Content = "您好！我是花卉AI助手，可以为您提供行情分析、种植建议与市场洞察。请问有什么可以帮您？",
                    IsUser = false,
                    Timestamp = baseTime.AddMinutes(12),
                },
                new ChatMessageItem
                {
                    Role = "我",
                    SenderName = "我",
                    Content = "今天红玫瑰的均价是多少？下周走势如何？",
                    IsUser = true,
                    Timestamp = baseTime.AddMinutes(13),
                },
                new ChatMessageItem
                {
                    Role = "花卉AI助手",
                    SenderName = "花卉AI助手",
                    Content = "今日红玫瑰均价 ¥3.20/支，较昨日 +2.4%。预计下周延续上行，建议关注以下品种：",
                    IsUser = false,
                    Timestamp = baseTime.AddMinutes(13),
                    HasMarketData = true,
                    MarketDataHeader = "行情分析",
                    MarketDataItems = new List<MarketDataRow>
                    {
                        new MarketDataRow { Variety = "红玫瑰", Price = "¥3.20", Change = "+2.4%", IsUp = true, Suggestion = "增持" },
                        new MarketDataRow { Variety = "百合", Price = "¥2.45", Change = "-1.1%", IsUp = false, Suggestion = "观望" },
                        new MarketDataRow { Variety = "康乃馨", Price = "¥1.80", Change = "+0.8%", IsUp = true, Suggestion = "持有" },
                    },
                    MarketSummary = "下周一红玫瑰预计冲高至 ¥3.35，建议本周完成备货。",
                },
                new ChatMessageItem
                {
                    Role = "我",
                    SenderName = "我",
                    Content = "那推荐一个适合春季种植的品种",
                    IsUser = true,
                    Timestamp = baseTime.AddMinutes(15),
                },
                new ChatMessageItem
                {
                    Role = "花卉AI助手",
                    SenderName = "花卉AI助手",
                    Content = "综合收益与难度，推荐 高原红玫瑰：预计收益 ¥2,580/亩，建议评分 5.0，适宜春季种植，抗病性较强。",
                    IsUser = false,
                    Timestamp = baseTime.AddMinutes(15),
                },
            };

            OnPropertyChanged(nameof(IsEmpty));
        }

        public async Task SendMessageAsync()
        {
            if (string.IsNullOrWhiteSpace(InputText) || IsGenerating) return;

            ResetDailyCountIfNeeded();

            if (_dailyCallCount >= DailyCallLimit)
                return;

            var userMessage = InputText.Trim();
            InputText = "";

            Messages.Add(new ChatMessageItem
            {
                Role = "我",
                SenderName = "我",
                Content = userMessage,
                IsUser = true,
                Timestamp = DateTime.Now
            });

            IsGenerating = true;
            _dailyCallCount++;
            OnPropertyChanged(nameof(CanSend));

            try
            {
                var response = await CallAIAssistantAsync(userMessage).ConfigureAwait(false);

                Messages.Add(new ChatMessageItem
                {
                    Role = "花卉AI助手",
                    SenderName = "花卉AI助手",
                    Content = response ?? "抱歉，暂时无法回答您的问题，请稍后重试。",
                    IsUser = false,
                    Timestamp = DateTime.Now
                });
            }
            catch (Exception)
            {
                Messages.Add(new ChatMessageItem
                {
                    Role = "花卉AI助手",
                    SenderName = "花卉AI助手",
                    Content = "网络连接异常，请检查网络后重试。",
                    IsUser = false,
                    Timestamp = DateTime.Now
                });
            }
            finally
            {
                IsGenerating = false;
                OnPropertyChanged(nameof(CanSend));
                OnPropertyChanged(nameof(IsEmpty));
            }
        }

        private async Task<string?> CallAIAssistantAsync(string question)
        {
            return await _aiService.ChatWithAIAsync(question).ConfigureAwait(false);
        }

        private void ResetDailyCountIfNeeded()
        {
            if (DateTime.UtcNow.Date != _dailyResetDate)
            {
                _dailyCallCount = 0;
                _dailyResetDate = DateTime.UtcNow.Date;
            }
        }

    }

    public class ChatMessageItem
    {
        public string Role { get; set; } = "";
        /// <summary>显示用发送者名称（花卉AI助手 / 我）。</summary>
        public string SenderName { get; set; } = "";
        public string Content { get; set; } = "";
        public bool IsUser { get; set; }
        /// <summary>是否为 AI 发送（= !IsUser）。</summary>
        public bool IsFromAI => !IsUser;
        /// <summary>头像 emoji（AI 用 🧠）。</summary>
        public string AvatarEmoji { get; set; } = "🧠";
        public DateTime Timestamp { get; set; }
        /// <summary>是否包含行情数据卡片。</summary>
        public bool HasMarketData { get; set; }
        public string MarketDataHeader { get; set; } = "";
        public List<MarketDataRow> MarketDataItems { get; set; } = new();
        public string MarketSummary { get; set; } = "";
    }

    /// <summary>快捷问题分组（含图标 emoji、组标题、问题条目）。</summary>
    public class QuickActionGroup
    {
        public string IconEmoji { get; set; } = "";
        public string GroupTitle { get; set; } = "";
        public List<QuickActionItem> Items { get; set; } = new();
    }

    /// <summary>快捷问题条目（满足 IconEmoji/Title/Description 契约，Text 为展示文本）。</summary>
    public class QuickActionItem
    {
        public string IconEmoji { get; set; } = "";
        public string Title { get; set; } = "";
        public string Description { get; set; } = "";
        public string Text { get; set; } = "";
    }

    /// <summary>行情数据卡片表格行。</summary>
    public class MarketDataRow
    {
        public string Variety { get; set; } = "";
        public string Price { get; set; } = "";
        public string Change { get; set; } = "";
        public bool IsUp { get; set; }
        public string Suggestion { get; set; } = "";
    }
}
