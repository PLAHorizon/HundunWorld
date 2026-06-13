using System;
using Avalonia;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Horizon.Game.GengDi.Core.Controls
{
    /// <summary>
    /// 左滑显示操作按钮的面板控件。
    /// 子内容默认显示，向左滑动超过阈值后露出右侧隐藏的操作区域（如删除按钮）。
    /// </summary>
    public class SwipeRevealPanel : ContentControl
    {
        /// <summary>
        /// 右侧操作区宽度（隐藏区域的宽度），默认 70。
        /// </summary>
        public static readonly StyledProperty<double> ActionWidthProperty =
            AvaloniaProperty.Register<SwipeRevealPanel, double>(nameof(ActionWidth), 70d);

        /// <summary>
        /// 滑动触发阈值（超过此值松手后自动展开），默认 35。
        /// </summary>
        public static readonly StyledProperty<double> SwipeThresholdProperty =
            AvaloniaProperty.Register<SwipeRevealPanel, double>(nameof(SwipeThreshold), 35d);

        /// <summary>
        /// 动画时长（毫秒），默认 250。
        /// </summary>
        public static readonly StyledProperty<int> AnimationDurationMsProperty =
            AvaloniaProperty.Register<SwipeRevealPanel, int>(nameof(AnimationDurationMs), 250);

        /// <summary>
        /// 放置在右侧隐藏区域的操作内容（如删除按钮）。
        /// </summary>
        public static readonly StyledProperty<object> ActionContentProperty =
            AvaloniaProperty.Register<SwipeRevealPanel, object>(nameof(ActionContent));

        /// <summary>
        /// 当前是否处于展开（已露出操作区）状态。
        /// </summary>
        public static readonly StyledProperty<bool> IsRevealedProperty =
            AvaloniaProperty.Register<SwipeRevealPanel, bool>(nameof(IsRevealed));

        private Border _contentWrapper;
        private ContentPresenter _actionPresenter;
        private TranslateTransform _contentTranslate;
        private TranslateTransform _actionTranslate;
        private bool _isPointerDown;
        private Point _pointerStart;
        private double _offsetAtStart;
        private double _currentOffset;
        private bool _isAnimating;
        private bool _isSwiping;
        private const double SwipeDetectionThreshold = 8;

        public double ActionWidth
        {
            get => GetValue(ActionWidthProperty);
            set => SetValue(ActionWidthProperty, value);
        }

        public double SwipeThreshold
        {
            get => GetValue(SwipeThresholdProperty);
            set => SetValue(SwipeThresholdProperty, value);
        }

        public int AnimationDurationMs
        {
            get => GetValue(AnimationDurationMsProperty);
            set => SetValue(AnimationDurationMsProperty, value);
        }

        public object ActionContent
        {
            get => GetValue(ActionContentProperty);
            set => SetValue(ActionContentProperty, value);
        }

        public bool IsRevealed
        {
            get => GetValue(IsRevealedProperty);
            set => SetValue(IsRevealedProperty, value);
        }

        public SwipeRevealPanel()
        {
            ClipToBounds = true;
        }

        protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
        {
            base.OnApplyTemplate(e);
            BuildVisualTree();
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            BuildVisualTree();
        }

        private void BuildVisualTree()
        {
            if (_contentWrapper != null)
                return;

            // 保存原始 Content
            var originalContent = Content;
            var actionContent = ActionContent;
            Content = null;

            _contentTranslate = new TranslateTransform(0, 0);
            _actionTranslate = new TranslateTransform(ActionWidth, 0);

            _contentWrapper = new Border
            {
                Background = Brushes.Transparent,
                Child = originalContent as Control,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                RenderTransform = _contentTranslate
            };

            _actionPresenter = new ContentPresenter
            {
                Content = actionContent,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Stretch,
                Width = ActionWidth,
                RenderTransform = _actionTranslate,
                Opacity = 0
            };

            var grid = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            grid.Children.Add(_contentWrapper);
            grid.Children.Add(_actionPresenter);

            Content = grid;

            PointerPressed += OnPointerPressed;
            PointerMoved += OnPointerMoved;
            PointerReleased += OnPointerReleased;
            PointerCaptureLost += OnPointerCaptureLost;
        }

        private void OnPointerPressed(object sender, PointerPressedEventArgs e)
        {
            if (_isAnimating)
                return;

            var point = e.GetCurrentPoint(this);
            if (!point.Properties.IsLeftButtonPressed)
                return;

            _isPointerDown = true;
            _isSwiping = false;
            _pointerStart = point.Position;
            _offsetAtStart = _currentOffset;
        }

        private void OnPointerMoved(object sender, PointerEventArgs e)
        {
            if (!_isPointerDown)
                return;

            var currentPos = e.GetCurrentPoint(this).Position;
            var deltaX = currentPos.X - _pointerStart.X;

            if (!_isSwiping)
            {
                if (Math.Abs(deltaX) < SwipeDetectionThreshold)
                    return;
                var deltaY = currentPos.Y - _pointerStart.Y;
                if (Math.Abs(deltaY) > Math.Abs(deltaX))
                    return;
                _isSwiping = true;
                e.Pointer.Capture(this);
            }

            var newOffset = _offsetAtStart + deltaX;
            newOffset = Math.Clamp(newOffset, -ActionWidth, 0);
            ApplyOffset(newOffset);
        }

        private void OnPointerReleased(object sender, PointerReleasedEventArgs e)
        {
            if (!_isPointerDown)
                return;

            _isPointerDown = false;
            if (_isSwiping)
            {
                e.Pointer.Capture(null);
                SnapToFinalPosition();
            }
            _isSwiping = false;
        }

        private void OnPointerCaptureLost(object sender, PointerCaptureLostEventArgs e)
        {
            if (!_isPointerDown)
                return;

            _isPointerDown = false;
            if (_isSwiping)
                SnapToFinalPosition();
            _isSwiping = false;
        }

        /// <summary>操作区域淡入速度系数（大于1使内容在滑动完成前完全可见）。</summary>
        private const double FadeInMultiplier = 1.5;

        private void ApplyOffset(double offset)
        {
            _currentOffset = offset;
            _contentTranslate.X = offset;
            // Action slides in from the right edge
            _actionTranslate.X = ActionWidth + offset;

            // Fade in/out action content based on reveal progress
            var progress = Math.Abs(offset) / ActionWidth;
            if (_actionPresenter != null)
            {
                _actionPresenter.Opacity = Math.Clamp(progress * FadeInMultiplier, 0, 1);
            }
        }

        private void SnapToFinalPosition()
        {
            var targetOffset = Math.Abs(_currentOffset) > SwipeThreshold
                ? -ActionWidth
                : 0d;

            AnimateTo(targetOffset);
        }

        /// <summary>
        /// 关闭已展开的操作区（恢复到初始状态）。
        /// </summary>
        public void CloseReveal()
        {
            if (_currentOffset < 0)
            {
                AnimateTo(0);
            }
        }

        private void AnimateTo(double target)
        {
            _isAnimating = true;
            var startOffset = _currentOffset;
            var distance = target - startOffset;

            if (Math.Abs(distance) < 0.5)
            {
                ApplyOffset(target);
                IsRevealed = Math.Abs(target) > 0.5;
                _isAnimating = false;
                return;
            }

            var durationMs = AnimationDurationMs;
            var startTime = DateTime.UtcNow;

            DispatcherTimer.Run(() =>
            {
                var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
                var t = Math.Clamp(elapsed / durationMs, 0, 1);

                // Ease-out cubic: 1 - (1-t)^3
                var eased = 1.0 - Math.Pow(1.0 - t, 3);
                var current = startOffset + distance * eased;
                ApplyOffset(current);

                if (t >= 1.0)
                {
                    ApplyOffset(target);
                    IsRevealed = Math.Abs(target) > 0.5;
                    _isAnimating = false;
                    return false; // stop timer
                }

                return true; // continue
            }, TimeSpan.FromMilliseconds(16));
        }
    }
}
