using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Horizon.Game.GengDi.Core.Services;
using Horizon.Game.GengDi.Core.ViewModels;

namespace Horizon.Game.GengDi.Core.Controls
{
    /// <summary>
    /// 语音消息卡片：点击经 <see cref="VoiceMessagePlayer"/>（全局单实例）切换播放；
    /// 播放中声波三柱循环跳动，停止/切换/结束自动复位（含其他消息卡片的互斥复位）。
    /// </summary>
    public partial class VoiceMessageCard : UserControl
    {
        // 声波动画帧：三柱高度（像素）循环
        private static readonly double[][] WaveFrames =
        {
            new[] { 8.0, 14.0, 6.0 },
            new[] { 12.0, 8.0, 14.0 },
            new[] { 6.0, 16.0, 10.0 },
            new[] { 10.0, 12.0, 8.0 },
        };

        private readonly DispatcherTimer _waveTimer;
        private int _waveFrame;
        private Rectangle _wave1;
        private Rectangle _wave2;
        private Rectangle _wave3;

        public VoiceMessageCard()
        {
            InitializeComponent();
            _wave1 = this.FindControl<Rectangle>(nameof(Wave1));
            _wave2 = this.FindControl<Rectangle>(nameof(Wave2));
            _wave3 = this.FindControl<Rectangle>(nameof(Wave3));
            _waveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(160) };
            _waveTimer.Tick += (_, _) =>
            {
                _waveFrame = (_waveFrame + 1) % WaveFrames.Length;
                ApplyWaveFrame();
            };
            DataContextChanged += (_, _) => SyncWaveAnimation();
            DetachedFromVisualTree += (_, _) => _waveTimer.Stop();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            SyncWaveAnimation();
        }

        protected override void OnDataContextChanged(EventArgs e)
        {
            base.OnDataContextChanged(e);
            if (DataContext is ChatMessageItemViewModel previous)
            {
                previous.PropertyChanged -= OnViewModelPropertyChanged;
            }

            if (DataContext is ChatMessageItemViewModel current)
            {
                current.PropertyChanged += OnViewModelPropertyChanged;
            }

            SyncWaveAnimation();
        }

        private void OnViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ChatMessageItemViewModel.IsVoicePlaying))
            {
                SyncWaveAnimation();
            }
        }

        private void SyncWaveAnimation()
        {
            var playing = DataContext is ChatMessageItemViewModel vm && vm.IsVoicePlaying;
            if (playing)
            {
                _waveTimer.Start();
            }
            else
            {
                _waveTimer.Stop();
                _waveFrame = 0;
                ApplyWaveFrame();
            }
        }

        private void ApplyWaveFrame()
        {
            var frame = WaveFrames[_waveFrame];
            if (_wave1 != null)
            {
                _wave1.Height = frame[0];
            }

            if (_wave2 != null)
            {
                _wave2.Height = frame[1];
            }

            if (_wave3 != null)
            {
                _wave3.Height = frame[2];
            }
        }

        private async void Card_PointerPressed(object sender, PointerPressedEventArgs e)
        {
            if (DataContext is not ChatMessageItemViewModel item || !item.IsVoiceMessage)
            {
                return;
            }

            e.Handled = true;
            var player = VoiceMessagePlayer.Instance;
            var messageKey = item.Message?.Id ?? item.VoicePlaybackUrl;

            if (player.IsPlaying(messageKey))
            {
                item.IsVoicePlaying = false;
                player.Stop();
                return;
            }

            // 先置播放态（下载中即有声波反馈），失败经回调复位
            item.IsVoicePlaying = true;
            var started = await player.PlayAsync(
                item.VoicePlaybackUrl,
                messageKey,
                () => Dispatcher.UIThread.Post(() => item.IsVoicePlaying = false)).ConfigureAwait(true);
            if (!started)
            {
                item.IsVoicePlaying = false;
            }
        }
    }
}
