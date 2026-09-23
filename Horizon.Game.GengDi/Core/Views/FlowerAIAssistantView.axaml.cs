using System;
using System.Collections.Specialized;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Horizon.Game.GengDi.Core.ViewModels;

namespace Horizon.Game.GengDi.Core.Views
{
    public partial class FlowerAIAssistantView : UserControl
    {
        /// <summary>距底部超过该像素时显示「回到底部」按钮。</summary>
        private const double ScrollToBottomThreshold = 40d;

        private readonly FlowerAIAssistantViewModel _viewModel;
        private ScrollViewer? _messageScrollViewer;
        private Button? _scrollToBottomButton;

        public FlowerAIAssistantView()
        {
            InitializeComponent();

            _viewModel = new FlowerAIAssistantViewModel();
            DataContext = _viewModel;

            // 新消息到达 → 自动滚到底部（否则用户看不到刚追加的回答）
            _viewModel.Messages.CollectionChanged += OnMessagesChanged;

            _messageScrollViewer = this.FindControl<ScrollViewer>("MessageScrollViewer");
            if (_messageScrollViewer != null)
            {
                _messageScrollViewer.ScrollChanged += OnMessageScrollChanged;
            }

            _scrollToBottomButton = this.FindControl<Button>("ScrollToBottomButton");
            if (_scrollToBottomButton != null)
            {
                _scrollToBottomButton.Click += OnScrollToBottomClick;
            }

            DetachedFromVisualTree += OnDetachedFromVisualTree;
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        /// <summary>视图卸载时解绑，避免页面切换后仍然持有引用（项目其它页面同样处理）。</summary>
        private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
        {
            _viewModel.Messages.CollectionChanged -= OnMessagesChanged;

            if (_messageScrollViewer != null)
            {
                _messageScrollViewer.ScrollChanged -= OnMessageScrollChanged;
                _messageScrollViewer = null;
            }

            if (_scrollToBottomButton != null)
            {
                _scrollToBottomButton.Click -= OnScrollToBottomClick;
                _scrollToBottomButton = null;
            }

            DetachedFromVisualTree -= OnDetachedFromVisualTree;
        }

        private void OnMessagesChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            // 等布局完成后再滚，确保新气泡已参与测量
            Dispatcher.UIThread.Post(
                () => _messageScrollViewer?.ScrollToEnd(),
                DispatcherPriority.Background);
        }

        private void OnMessageScrollChanged(object? sender, ScrollChangedEventArgs e)
        {
            if (_scrollToBottomButton == null || _messageScrollViewer == null)
            {
                return;
            }

            var remaining = _messageScrollViewer.Extent.Height
                            - _messageScrollViewer.Offset.Y
                            - _messageScrollViewer.Viewport.Height;

            _scrollToBottomButton.IsVisible = remaining > ScrollToBottomThreshold;
        }

        private void OnScrollToBottomClick(object? sender, RoutedEventArgs e)
        {
            _messageScrollViewer?.ScrollToEnd();
        }

        private void OnInputKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && DataContext is FlowerAIAssistantViewModel vm && vm.CanSend)
            {
                _ = vm.SendMessageAsync();
                e.Handled = true;
            }
        }

        private void OnSuggestionTapped(object? sender, TappedEventArgs e)
        {
            if (sender is Border border && border.Child is TextBlock textBlock && DataContext is FlowerAIAssistantViewModel vm)
            {
                vm.InputText = textBlock.Text;
                _ = vm.SendMessageAsync();
            }
        }

        /// <summary>快捷问题分组标题点击：折叠 / 展开该分组。</summary>
        private void OnGroupHeaderTapped(object? sender, TappedEventArgs e)
        {
            if (sender is Border { DataContext: QuickActionGroup group })
            {
                group.IsExpanded = !group.IsExpanded;
                e.Handled = true; // 避免冒泡到外层容器造成二次切换
            }
        }

        /// <summary>复制 AI 回答到剪贴板，并在按钮上给出短暂的「已复制」反馈。</summary>
        private async void OnCopyMessageClick(object? sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.DataContext is not ChatMessageItem message)
            {
                return;
            }

            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard == null || string.IsNullOrEmpty(message.Content))
            {
                return;
            }

            try
            {
                await clipboard.SetTextAsync(message.Content);

                button.Content = "已复制";
                button.IsEnabled = false;
                await Task.Delay(1200);
                button.Content = "复制";
                button.IsEnabled = true;
            }
            catch (Exception)
            {
                // 剪贴板被系统占用等异常：静默忽略，不打断对话
            }
        }
    }
}
