using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Horizon.Game.GengDi.Core.Services;
using Horizon.Game.Message.Network;   // FlowerPriceSnapshot（行情快照 DTO，与 FlowerMarketService 同源）

namespace Horizon.Game.GengDi.Core.ViewModels
{
    public class FlowerAIAssistantViewModel : ViewModelBase
    {
        private readonly FlowerAIService _aiService;
        private readonly FlowerAIAnswerEngine _answerEngine = new();
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
            // 快捷问题分组（2026-09-18 精准化）：
            // 每组的问题都**能命中本地意图引擎**，且尽量点明品种/问法，
            // 避免"行情咨询"这类泛化表述带来泛化回答。
            QuickActionGroups = new ObservableCollection<QuickActionGroup>
            {
                new QuickActionGroup
                {
                    IconEmoji = "📈",
                    GroupTitle = "花价行情",
                    Items = new List<QuickActionItem>
                    {
                        new QuickActionItem { Text = "红玫瑰今天多少钱？" },
                        new QuickActionItem { Text = "百合最近一周走势" },
                        new QuickActionItem { Text = "本周涨幅最大的品种" },
                        new QuickActionItem { Text = "下周价格预判" },
                    }
                },
                new QuickActionGroup
                {
                    IconEmoji = "🌱",
                    GroupTitle = "种植养护",
                    Items = new List<QuickActionItem>
                    {
                        new QuickActionItem { Text = "土壤 pH 偏高怎么调？" },
                        new QuickActionItem { Text = "春季种什么品种收益高？" },
                        new QuickActionItem { Text = "水肥怎么管理？" },
                        new QuickActionItem { Text = "花期怎么保鲜？" },
                    }
                },
                new QuickActionGroup
                {
                    IconEmoji = "🐛",
                    GroupTitle = "病虫害防治",
                    Items = new List<QuickActionItem>
                    {
                        new QuickActionItem { Text = "白粉病怎么防治？" },
                        new QuickActionItem { Text = "红蜘蛛怎么处理？" },
                    }
                },
                new QuickActionGroup
                {
                    IconEmoji = "🛍️",
                    GroupTitle = "订单售后",
                    Items = new List<QuickActionItem>
                    {
                        new QuickActionItem { Text = "如何申请退款？" },
                        new QuickActionItem { Text = "发货周期是多久？" },
                        new QuickActionItem { Text = "怎么查物流？" },
                    }
                },
                new QuickActionGroup
                {
                    IconEmoji = "👤",
                    GroupTitle = "账户与会员",
                    Items = new List<QuickActionItem>
                    {
                        new QuickActionItem { Text = "如何升级会员？" },
                        new QuickActionItem { Text = "修改绑定手机" },
                    }
                },
            };

            // 初始对话只放**欢迎语**：不再预置写死行情数字的示例回答（¥3.20 之类），
            // 避免"看起来在答非所问 / 数据不实"。行情问题一律走真实行情接口后再回答。
            Messages = new ObservableCollection<ChatMessageItem>
            {
                new ChatMessageItem
                {
                    Role = "花卉AI助手",
                    SenderName = "花卉AI助手",
                    Content = "您好！我是花卉 AI 助手，可以为您查行情、讲种植、诊病虫害，也能处理订单与账户问题。\n" +
                              "试着问我：「今天红玫瑰均价多少？」「土壤 pH 偏高怎么办？」「白粉病怎么治？」「如何申请退款？」",
                    IsUser = false,
                    Timestamp = DateTime.Now,
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
                var answer = await ResolveAnswerAsync(userMessage).ConfigureAwait(false);

                var aiMessage = new ChatMessageItem
                {
                    Role = "花卉AI助手",
                    SenderName = "花卉AI助手",
                    Content = answer.Text,
                    IsUser = false,
                    Timestamp = DateTime.Now
                };

                // 行情类回答附带行情卡片（数据由应答引擎随回答一并产出，不重复请求）
                if (answer.MarketCard is { Rows.Count: > 0 } card)
                {
                    aiMessage.HasMarketData = true;
                    aiMessage.MarketDataHeader = card.Header;
                    aiMessage.MarketDataItems = card.Rows.Select(r => new MarketDataRow
                    {
                        Variety = r.Variety,
                        Price = r.Price,
                        Change = r.Change,
                        IsUp = r.IsUp,
                        Suggestion = r.Suggestion,
                    }).ToList();
                    aiMessage.MarketSummary = card.Summary;
                }

                Messages.Add(aiMessage);
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

        /// <summary>
        /// 应答解析顺序（2026-09-18 调整，修复"答非所问"）：
        /// ① 本地意图引擎 —— 行情类用真实行情接口，种植/病虫害/订单/账户用内置知识库，
        ///    命中即返回与问题强相关的答案；
        /// ② 远程 AI（flower-ai/chat）—— 仅用于本地引擎识别不出意图的开放性问题；
        /// ③ 都没有 —— 给出"没听懂 + 建议问法"的引导，而不是丢一句泛化寒暄。
        /// </summary>
        private async Task<AIAnswerResult> ResolveAnswerAsync(string question)
        {
            try
            {
                var local = await _answerEngine.TryAnswerAsync(question).ConfigureAwait(false);
                if (local != null && !string.IsNullOrWhiteSpace(local.Text))
                {
                    return local;
                }
            }
            catch (Exception ex)
            {
                LogDiagnostic($"本地意图引擎异常：{ex.Message}");
            }

            try
            {
                var remote = await _aiService.ChatWithAIAsync(question).ConfigureAwait(false);
                if (IsRemoteAnswerUsable(question, remote))
                {
                    return new AIAnswerResult { Text = remote!.Trim(), Intent = AIAnswerIntent.Unknown };
                }
            }
            catch (Exception ex)
            {
                LogDiagnostic($"远程 AI 调用异常：{ex.Message}");
            }

            // ③ 本地分诊兜底：保证"随意/自定义问题"也有体面、可执行的回应
            //    （而不是一句"抱歉，这个问题我还没完全理解"）
            var triage = _answerEngine.BuildFallbackAnswer(question);
            if (triage != null)
            {
                return triage;
            }

            return new AIAnswerResult
            {
                Intent = AIAnswerIntent.Unknown,
                Text = "请直接说说您想了解的问题，例如花价、种植、病虫害或订单售后。",
            };
        }

        /// <summary>
        /// 远程回答质量闸门（2026-09-18）：后端 flower-ai/chat 在无法回答时会返回
        /// 与提问无关的自我介绍类寒暄（如"我是花卉AI助手，可以为您提供行情分析…"），
        /// 直接展示就是用户看到的"答非所问"。这里把这类回复识别出来并丢弃，
        /// 交给本地分诊兜底，保证输出始终与提问相关。
        /// </summary>
        private static bool IsRemoteAnswerUsable(string question, string? remote)
        {
            if (string.IsNullOrWhiteSpace(remote)) return false;

            var text = remote.Trim();
            if (text.Length < 8) return false;

            var askedForIntro = question.Contains("你好", StringComparison.Ordinal)
                                || question.Contains("您好", StringComparison.Ordinal)
                                || question.Contains("你是谁", StringComparison.Ordinal)
                                || question.Contains("能做什么", StringComparison.Ordinal);

            // 短小的自我介绍式寒暄，且用户问的并不是"你是谁" → 视为无效回答
            string[] cannedMarkers =
            {
                "我是花卉AI助手", "我是花卉 AI 助手", "可以为您提供行情分析",
                "请问有什么可以帮您", "随时为您服务",
            };

            if (!askedForIntro && text.Length <= 200 &&
                cannedMarkers.Any(marker => text.Contains(marker, StringComparison.Ordinal)))
            {
                return false;
            }

            return true;
        }

        /// <summary>引擎/远程调用失败的诊断输出（走 Debug 输出，不引入额外日志依赖）。</summary>
        private static void LogDiagnostic(string message)
        {
            System.Diagnostics.Debug.WriteLine($"[FlowerAIAssistant] {message}");
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

    /// <summary>快捷问题分组（含图标 emoji、组标题、问题条目，可折叠）。</summary>
    public class QuickActionGroup : ViewModelBase
    {
        private bool _isExpanded = true;

        public string IconEmoji { get; set; } = "";
        public string GroupTitle { get; set; } = "";
        public List<QuickActionItem> Items { get; set; } = new();

        /// <summary>分组是否展开（点击分组标题切换）。</summary>
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (SetProperty(ref _isExpanded, value))
                {
                    OnPropertyChanged(nameof(ExpandGlyph));
                }
            }
        }

        /// <summary>折叠指示符（XAML 直接用字形，项目图标集无 chevron）。</summary>
        public string ExpandGlyph => _isExpanded ? "▾" : "▸";

        /// <summary>条目数（分组标题右侧显示）。</summary>
        public string ItemCountText => Items.Count.ToString();
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
