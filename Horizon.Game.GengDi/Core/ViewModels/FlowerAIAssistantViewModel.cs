using System;
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
        private string _inputText = "";
        private bool _isGenerating;
        private int _dailyCallCount;
        private DateTime _dailyResetDate = DateTime.UtcNow.Date;
        private const int DailyCallLimit = 50;

        public ObservableCollection<ChatMessageItem> Messages
        {
            get => _messages;
            set => SetProperty(ref _messages, value);
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

        public bool IsEmpty => _messages.Count == 0;

        public bool CanSend => !string.IsNullOrWhiteSpace(_inputText) && !_isGenerating && _dailyCallCount < DailyCallLimit;

        public ICommand SendMessageCommand { get; }

        public FlowerAIAssistantViewModel()
        {
            _aiService = new FlowerAIService();
            SendMessageCommand = new AsyncCommand(SendMessageAsync);
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
                Role = "你",
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
                    Role = "AI助手",
                    Content = response ?? "抱歉，暂时无法回答您的问题，请稍后重试。",
                    IsUser = false,
                    Timestamp = DateTime.Now
                });
            }
            catch (Exception)
            {
                Messages.Add(new ChatMessageItem
                {
                    Role = "AI助手",
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
        public string Content { get; set; } = "";
        public bool IsUser { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
