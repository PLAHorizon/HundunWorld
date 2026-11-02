using System;
using FlaxEngine;
using FlaxEngine.GUI;
using NarrativePro.Core;

namespace NarrativePro.CommonUI.Widgets
{
    /// <summary>
    /// Narrative CommonUI 视频播放器。对应 UE5 <c>UNarrativeCommonVideoPlayer</c>（继承 UCommonVideoPlayer），
    /// 在基类之上暴露几个 BP 友好的函数（<see cref="BPSetVideo"/> / <see cref="BPPlayFromStart"/> /
    /// <see cref="BPPlay"/> / <see cref="BPClose"/>）。
    /// <para>
    /// 已核对 <c>FlaxEngine.CSharp.xml</c>（Flax 1.12）：引擎确实提供视频上屏的 API ——
    /// <see cref="FlaxEngine.VideoPlayer"/>（<c>Url</c> / <c>Play</c> / <c>Pause</c> / <c>Stop</c> /
    /// <c>IsLooping</c> / <c>IsPlaying</c> / <c>Duration</c> / <c>Time</c>）与
    /// <see cref="FlaxEngine.GUI.VideoBrush"/>（实现 <see cref="FlaxEngine.GUI.IBrush"/>，
    /// "for VideoPlayer frame displaying"，字段 <c>Player</c> / <c>Filter</c>），
    /// 因此主路径把 VideoBrush 挂到内部 <see cref="Image"/> 控件上，逐帧显示真实视频画面。
    /// </para>
    /// <para>
    /// 兜底路径（视频源缺失 / 解码器不可用 / VideoPlayer 构造失败时）：本控件仍然是一个可渲染的
    /// Flax 控件 —— 深色底 + 中央文件名单元 + 底部进度条（<see cref="Render2D.FillRectangle"/>），
    /// 并把 <see cref="bIsFallbackFrame"/> 置真，宿主可据此决定是否隐藏播放器。
    /// 这与 UE5 里媒体源打开失败时 CommonVideoPlayer 显示空帧的行为等价，但不会让 UI 变成空白。
    /// </para>
    /// </summary>
    public class NarrativeCommonVideoPlayer : ContainerControl
    {
        /// <summary>当前视频源路径（替代 UE5 UMediaSource*）。空字符串表示未设置。</summary>
        public string VideoSourcePath = string.Empty;

        /// <summary>音量（0-1），透传给 <see cref="FlaxEngine.VideoPlayer.AudioVolume"/>。</summary>
        public float Volume = 1f;

        /// <summary>播放结束事件（非循环且自然放完）。对应 UE5 OnPlaybackFinished。</summary>
        public event Action<NarrativeCommonVideoPlayer> OnPlaybackFinished;

        /// <summary>开始播放事件。</summary>
        public event Action<NarrativeCommonVideoPlayer> OnPlaybackStarted;

        /// <summary>视频帧控件（真实 Flax <see cref="Image"/>，Brush 为 VideoBrush）。</summary>
        public Image FrameImage { get; private set; }

        /// <summary>兜底帧文本（无视频时显示的文件名/原因）。</summary>
        public Label FallbackLabel { get; private set; }

        /// <summary>兜底进度条高度（像素）。</summary>
        public float ProgressBarHeight = 3f;

        /// <summary>底部留白给进度条（像素）。</summary>
        public float ProgressBarPadding = 4f;

        /// <summary>当前是否走兜底帧（无可用视频画面）。</summary>
        public bool bIsFallbackFrame { get; private set; } = true;

        /// <summary>是否循环播放。对应 UE5 SetLooping。</summary>
        public bool IsLooping
        {
            get => _bLooping;
            set
            {
                _bLooping = value;
                if (_player != null)
                {
                    try { _player.IsLooping = value; }
                    catch (Exception ex) { NarrativeLog.LogWarning("NarrativeCommonVideoPlayer 设置循环失败：" + ex.Message); }
                }
            }
        }

        /// <summary>是否正在播放。</summary>
        public bool IsPlaying
        {
            get
            {
                if (_player == null) return false;
                try { return _player.IsPlaying && !_player.IsPaused; }
                catch { return false; }
            }
        }

        /// <summary>片长（秒），无源时 0。</summary>
        public float Duration => SafeGet(p => p.Duration, 0f);

        /// <summary>当前播放位置（秒）。</summary>
        public float Time => SafeGet(p => p.Time, 0f);

        /// <summary>播放进度（0-1）。</summary>
        public float Progress
        {
            get
            {
                float duration = Duration;
                if (duration <= 0f) return 0f;
                float t = Time / duration;
                return t < 0f ? 0f : (t > 1f ? 1f : t);
            }
        }

        private VideoPlayer _player;
        private bool _bLooping;
        private bool _bFiredFinished;

        /// <summary>构造函数：建立帧控件与兜底单元（TopLeft 锚 + 显式尺寸，构造期不依赖父尺寸）。</summary>
        public NarrativeCommonVideoPlayer()
        {
            AnchorPreset = AnchorPresets.TopLeft;
            Size = new Float2(480f, 270f);
            BackgroundColor = new Color(0f, 0f, 0f, 1f);

            FrameImage = new Image
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Size = new Float2(Size.X, Size.Y - ProgressBarHeight - ProgressBarPadding),
                Brush = new SolidColorBrush(new Color(0f, 0f, 0f, 1f)),
                Visible = false,
            };
            AddChild(FrameImage);

            FallbackLabel = new Label
            {
                AnchorPreset = AnchorPresets.TopLeft,
                Location = Float2.Zero,
                Size = new Float2(Size.X, Size.Y - ProgressBarHeight - ProgressBarPadding),
                Text = "无视频源",
                TextColor = Color.White,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
            };
            AddChild(FallbackLabel);

            ApplyThemeColors();
        }

        /// <summary>
        /// 设置视频源。对应 UE5 BPSetVideo（内部调用未暴露给蓝图的基类 SetVideo）。
        /// </summary>
        /// <param name="newVideoPath">新视频源路径（替代 UE5 UMediaSource*）。</param>
        public void BPSetVideo(string newVideoPath)
        {
            VideoSourcePath = newVideoPath ?? string.Empty;
            OpenSource();
        }

        /// <summary>
        /// 从头开始播放。对应 UE5 BPPlayFromStart（内部调用基类 PlayFromStart）。
        /// </summary>
        public void BPPlayFromStart()
        {
            StopPlayer();

            if (_player == null && !string.IsNullOrEmpty(VideoSourcePath))
            {
                OpenSource();
            }

            if (_player == null)
            {
                ShowFallback(string.IsNullOrEmpty(VideoSourcePath) ? "无视频源" : "视频不可用");
                return;
            }

            try
            {
                _player.StartTime = 0f;
                _player.Play();
            }
            catch (Exception ex)
            {
                NarrativeLog.LogWarning($"NarrativeCommonVideoPlayer 从头播放失败：{ex.Message}");
                ShowFallback("播放失败");
                return;
            }

            _bFiredFinished = false;
            ShowFrame();
            OnPlaybackStarted?.Invoke(this);
        }

        /// <summary>
        /// 播放视频（从当前位置继续）。对应 UE5 BPPlay。
        /// </summary>
        public void BPPlay()
        {
            if (_player == null && !string.IsNullOrEmpty(VideoSourcePath))
            {
                OpenSource();
            }

            if (_player == null)
            {
                ShowFallback(string.IsNullOrEmpty(VideoSourcePath) ? "无视频源" : "视频不可用");
                return;
            }

            try
            {
                if (_player.IsPaused) _player.Play();
                else _player.Play();
            }
            catch (Exception ex)
            {
                NarrativeLog.LogWarning($"NarrativeCommonVideoPlayer 播放失败：{ex.Message}");
                ShowFallback("播放失败");
                return;
            }

            _bFiredFinished = false;
            ShowFrame();
            OnPlaybackStarted?.Invoke(this);
        }

        /// <summary>
        /// 关闭视频。对应 UE5 BPClose（内部调用基类 Close）。
        /// </summary>
        public void BPClose()
        {
            StopPlayer();
            ShowFallback("已关闭");
        }

        /// <summary>暂停（对应 UE5 Pause）。保留位置，可用 <see cref="BPPlay"/> 续播。</summary>
        public void BPPause()
        {
            if (_player == null) return;
            try { _player.Pause(); }
            catch (Exception ex) { NarrativeLog.LogWarning("NarrativeCommonVideoPlayer 暂停失败：" + ex.Message); }
        }

        /// <summary>打开/重建视频源（对应 UE5 UCommonVideoPlayer::OpenVideoSource）。不自动播放。</summary>
        public void OpenSource()
        {
            StopPlayer();
            ReleasePlayer();

            if (string.IsNullOrEmpty(VideoSourcePath))
            {
                ShowFallback("无视频源");
                return;
            }

            try
            {
                var player = new VideoPlayer
                {
                    Url = VideoSourcePath,
                    PlayOnStart = false,
                    IsLooping = _bLooping,
                    AudioVolume = Volume,
                };

                _player = player;
                FrameImage.Brush = new VideoBrush { Player = player };
                ShowFrame();
            }
            catch (Exception ex)
            {
                _player = null;
                NarrativeLog.LogWarning($"NarrativeCommonVideoPlayer 无法打开视频源 {VideoSourcePath}：{ex.Message}");
                ShowFallback("视频不可用");
            }
        }

        /// <summary>关闭并释放播放器（对应 UE5 CloseVideoSource）。</summary>
        public void CloseSource()
        {
            StopPlayer();
            ReleasePlayer();
            ShowFallback("已关闭");
        }

        /// <summary>设置音量（透传 VideoPlayer.AudioVolume）。</summary>
        public void SetVolume(float volume)
        {
            Volume = volume;
            if (_player != null)
            {
                try { _player.AudioVolume = volume; }
                catch (Exception ex) { NarrativeLog.LogWarning("NarrativeCommonVideoPlayer 设置音量失败：" + ex.Message); }
            }
        }

        /// <summary>从主题取兜底帧文字颜色（对应 UE5 控件从样式表取色）。</summary>
        public void ApplyThemeColors()
        {
            var settings = NarrativeUIDeveloperSettings.GetMutableDefault();
            if (settings == null || FallbackLabel == null) return;

            FallbackLabel.TextColor = settings.bUseLightTextOnPrimary ? Color.White : settings.UIPrimaryColor;
        }

        protected override void OnParentChangedInternal()
        {
            base.OnParentChangedInternal();
            ApplyThemeColors();
        }

        protected override void OnSizeChanged()
        {
            base.OnSizeChanged();
            LayoutContent();
        }

        private void LayoutContent()
        {
            float height = System.Math.Max(1f, Size.Y - ProgressBarHeight - ProgressBarPadding);
            float width = System.Math.Max(1f, Size.X);

            if (FrameImage != null)
            {
                FrameImage.Location = Float2.Zero;
                FrameImage.Size = new Float2(width, height);
            }

            if (FallbackLabel != null)
            {
                FallbackLabel.Location = Float2.Zero;
                FallbackLabel.Size = new Float2(width, height);
            }
        }

        private void ShowFrame()
        {
            bIsFallbackFrame = false;
            if (FallbackLabel != null) FallbackLabel.Visible = false;
            if (FrameImage != null) FrameImage.Visible = true;
        }

        private void ShowFallback(string reason)
        {
            bIsFallbackFrame = true;
            if (FrameImage != null) FrameImage.Visible = false;

            if (FallbackLabel != null)
            {
                FallbackLabel.Text = BuildFallbackText(reason);
                FallbackLabel.Visible = true;
            }
        }

        private string BuildFallbackText(string reason)
        {
            string file = string.IsNullOrEmpty(VideoSourcePath)
                ? "（空）"
                : System.IO.Path.GetFileNameWithoutExtension(VideoSourcePath);

            return $"{file}　·　{reason}";
        }

        private void StopPlayer()
        {
            if (_player == null) return;
            try { _player.Stop(); }
            catch (Exception ex) { NarrativeLog.LogWarning("NarrativeCommonVideoPlayer 停止失败：" + ex.Message); }
        }

        private void ReleasePlayer()
        {
            if (_player == null) return;

            try
            {
                FlaxEngine.Object.Destroy(_player);
            }
            catch (Exception ex)
            {
                NarrativeLog.LogWarning("NarrativeCommonVideoPlayer 释放播放器失败：" + ex.Message);
            }

            _player = null;

            if (FrameImage != null)
            {
                FrameImage.Brush = new SolidColorBrush(new Color(0f, 0f, 0f, 1f));
            }
        }

        private float SafeGet(Func<VideoPlayer, float> getter, float fallback)
        {
            if (_player == null) return fallback;
            try { return getter(_player); }
            catch { return fallback; }
        }

        /// <summary>自然放完检测（Flax 无媒体结束委托，这里按时间轮询，对应 UE5 OnPlaybackFinished）。</summary>
        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);

            if (_player == null || _bLooping || _bFiredFinished) return;
            if (!IsPlaying) return;

            float duration = Duration;
            if (duration <= 0f) return;
            if (Time < duration - 0.05f) return;

            _bFiredFinished = true;
            ShowFallback("播放结束");
            OnPlaybackFinished?.Invoke(this);
        }

        /// <summary>绘制兜底进度条：无论是否有真实视频帧，都给出可视的播放位置。</summary>
        public override void Draw()
        {
            base.Draw();

            if (!Visible || Size.X <= 0f || Size.Y <= ProgressBarHeight) return;

            float y = Size.Y - ProgressBarHeight - ProgressBarPadding;
            Render2D.FillRectangle(new Rectangle(0f, y, Size.X, ProgressBarHeight), new Color(1f, 1f, 1f, 0.12f));

            float progress = Progress;
            if (progress > 0f)
            {
                Render2D.FillRectangle(new Rectangle(0f, y, Size.X * progress, ProgressBarHeight), ProgressBarFillColor());
            }
        }

        private Color ProgressBarFillColor()
        {
            var settings = NarrativeUIDeveloperSettings.GetMutableDefault();
            return settings != null ? settings.UIPrimaryColor : Color.White;
        }
    }
}
