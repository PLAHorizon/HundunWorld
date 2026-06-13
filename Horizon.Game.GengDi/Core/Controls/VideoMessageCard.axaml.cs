using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Horizon.Game.GengDi.Core.ViewModels;
using WebViewControl;

namespace Horizon.Game.GengDi.Core.Controls
{
    public partial class VideoMessageCard : UserControl
    {
        private static bool _webViewUnavailable;

        private readonly DispatcherTimer _hoverOpenTimer;
        private readonly DispatcherTimer _hoverCloseTimer;
        private readonly ContentControl _playbackHost;
        private bool _isPointerInside;
        private bool _hoverTriggeredSinceExit;

        public VideoMessageCard()
        {
            _hoverOpenTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
            _hoverCloseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(160) };
            _hoverOpenTimer.Tick += HoverOpenTimer_Tick;
            _hoverCloseTimer.Tick += HoverCloseTimer_Tick;
            InitializeComponent();
            _playbackHost = this.FindControl<ContentControl>("PlaybackHost");
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void InteractiveRoot_PointerEntered(object sender, PointerEventArgs e)
        {
            if (DataContext is not ChatMessageItemViewModel messageItem || !messageItem.CanInlinePlay)
            {
                return;
            }

            _isPointerInside = true;
            _hoverCloseTimer.Stop();

            if (_hoverTriggeredSinceExit || messageItem.IsInlinePlayerOpen)
            {
                return;
            }

            if (!messageItem.IsInlinePlayerOpen)
            {
                _hoverOpenTimer.Stop();
                _hoverOpenTimer.Start();
            }
        }

        private void InteractiveRoot_PointerExited(object sender, PointerEventArgs e)
        {
            _isPointerInside = false;
            _hoverOpenTimer.Stop();

            if (IsPointerOver)
            {
                return;
            }

            _hoverTriggeredSinceExit = false;
            _hoverCloseTimer.Stop();
            _hoverCloseTimer.Start();
        }

        private void HoverOpenTimer_Tick(object sender, EventArgs e)
        {
            _hoverOpenTimer.Stop();

            if (!_isPointerInside && !IsPointerOver)
            {
                return;
            }

            if (DataContext is ChatMessageItemViewModel messageItem)
            {
                _hoverTriggeredSinceExit = true;

                if (!EnsureWebView(messageItem.PlaybackAddress))
                {
                    messageItem.StopInlinePlayback();
                    return;
                }

                messageItem.StartInlinePlayback();
            }
        }

        private void HoverCloseTimer_Tick(object sender, EventArgs e)
        {
            _hoverCloseTimer.Stop();

            if (_isPointerInside || IsPointerOver)
            {
                return;
            }

            StopPlayback();
        }

        private void StopPlayback(bool resetHoverState = false)
        {
            _hoverOpenTimer.Stop();
            _hoverCloseTimer.Stop();

            if (resetHoverState)
            {
                _isPointerInside = false;
                _hoverTriggeredSinceExit = false;
            }

            if (DataContext is ChatMessageItemViewModel messageItem)
            {
                messageItem.StopInlinePlayback();
            }

            if (_playbackHost != null)
            {
                _playbackHost.Content = null;
            }
        }

        protected override void OnDetachedFromVisualTree(Avalonia.VisualTreeAttachmentEventArgs e)
        {
            StopPlayback(resetHoverState: true);
            base.OnDetachedFromVisualTree(e);
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            StopPlayback(resetHoverState: true);
            base.OnDataContextChanged(e);
        }

        private bool EnsureWebView(string address)
        {
            if (_webViewUnavailable || _playbackHost == null || string.IsNullOrWhiteSpace(address))
            {
                return false;
            }

            if (_playbackHost.Content is WebView existingWebView)
            {
                existingWebView.Address = address;
                return true;
            }

            try
            {
                _playbackHost.Content = new WebView
                {
                    Address = address,
                    Focusable = false
                };

                return true;
            }
            catch (InvalidOperationException)
            {
                _webViewUnavailable = true;
                _playbackHost.Content = null;
                return false;
            }
        }
    }
}