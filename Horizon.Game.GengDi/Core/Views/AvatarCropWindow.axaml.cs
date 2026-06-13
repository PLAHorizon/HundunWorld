using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace Horizon.Game.GengDi.Core.Views
{
    /// <summary>
    /// 头像裁剪对话框：用户可在图片上拖动鼠标框选裁剪区域，
    /// 确认后使用 System.Drawing 完成实际裁剪并保存到本地。
    /// </summary>
    public partial class AvatarCropWindow : Window
    {
        private readonly string _sourcePath;
        private Bitmap _sourceBitmap;

        // 控件引用
        private Image _sourceImage;
        private Canvas _cropCanvas;
        private Button _confirmButton;
        private TextBlock _hintText;

        // 框选状态
        private Point _dragStart;
        private Point _dragEnd;
        private bool _isDragging;
        private bool _hasSelection;

        // 框选矩形覆盖层
        private Rectangle _selectionBorder;

        /// <summary>
        /// 裁剪失败时对用户显示的错误描述；为 null 表示没有错误。
        /// </summary>
        public string ErrorMessage { get; private set; }

        /// <summary>
        /// 裁剪完成后的图片本地路径；若用户取消或裁剪失败则为 null。
        /// </summary>
        public string ResultPath { get; private set; }

        public AvatarCropWindow(string sourcePath)
        {
            _sourcePath = sourcePath;
            AvaloniaXamlLoader.Load(this);

            _sourceImage = this.FindControl<Image>("SourceImage");
            _cropCanvas = this.FindControl<Canvas>("CropCanvas");
            _confirmButton = this.FindControl<Button>("ConfirmButton");
            _hintText = this.FindControl<TextBlock>("HintText");

            // 加载源图，捕获文件损坏、格式不支持或文件占用等异常
            if (File.Exists(_sourcePath))
            {
                try
                {
                    _sourceBitmap = new Bitmap(_sourcePath);
                    _sourceImage.Source = _sourceBitmap;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[AvatarCropWindow] 无法加载图片: {ex}");
                    ErrorMessage = "无法加载图片，文件可能已损坏或格式不支持";
                    // 禁用确认按钮，让用户只能取消
                    if (_confirmButton != null)
                        _confirmButton.IsEnabled = false;
                    if (_hintText != null)
                        _hintText.Text = ErrorMessage;
                }
            }

            // 框选矩形：虚线白色边框，内部半透明高亮
            _selectionBorder = new Rectangle
            {
                StrokeThickness = 2,
                Stroke = Brushes.White,
                StrokeDashArray = new Avalonia.Collections.AvaloniaList<double> { 6, 3 },
                Fill = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                IsHitTestVisible = false,
                IsVisible = false
            };
            _cropCanvas.Children.Add(_selectionBorder);

            // 注册鼠标事件
            _cropCanvas.PointerPressed += CropCanvas_PointerPressed;
            _cropCanvas.PointerMoved += CropCanvas_PointerMoved;
            _cropCanvas.PointerReleased += CropCanvas_PointerReleased;
        }

        // ────────────────────────────────────────────────────────────
        //  鼠标拖拽框选逻辑
        // ────────────────────────────────────────────────────────────

        private void CropCanvas_PointerPressed(object sender, PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(_cropCanvas).Properties.IsLeftButtonPressed)
            {
                return;
            }

            _dragStart = e.GetPosition(_cropCanvas);
            _dragEnd = _dragStart;
            _isDragging = true;
            _hasSelection = false;
            _confirmButton.IsEnabled = false;
            _selectionBorder.IsVisible = false;

            e.Pointer.Capture(_cropCanvas);
            e.Handled = true;
        }

        private void CropCanvas_PointerMoved(object sender, PointerEventArgs e)
        {
            if (!_isDragging)
            {
                return;
            }

            _dragEnd = e.GetPosition(_cropCanvas);
            UpdateSelectionVisual();
        }

        private void CropCanvas_PointerReleased(object sender, PointerReleasedEventArgs e)
        {
            if (!_isDragging)
            {
                return;
            }

            _dragEnd = e.GetPosition(_cropCanvas);
            _isDragging = false;
            e.Pointer.Capture(null);

            UpdateSelectionVisual();

            var rect = GetCanvasSelectionRect();
            if (rect.Width > 4 && rect.Height > 4)
            {
                _hasSelection = true;
                _confirmButton.IsEnabled = true;

                var imgRect = CanvasRectToImagePixelRect(rect);
                if (_hintText != null)
                {
                    _hintText.Text = $"已选择区域：{imgRect.Width} × {imgRect.Height} 像素";
                }
            }
            else
            {
                if (_hintText != null)
                {
                    _hintText.Text = "选区太小，请重新框选";
                }
            }
        }

        private void UpdateSelectionVisual()
        {
            var rect = GetCanvasSelectionRect();

            if (rect.Width < 1 || rect.Height < 1)
            {
                _selectionBorder.IsVisible = false;
                return;
            }

            Canvas.SetLeft(_selectionBorder, rect.X);
            Canvas.SetTop(_selectionBorder, rect.Y);
            _selectionBorder.Width = rect.Width;
            _selectionBorder.Height = rect.Height;
            _selectionBorder.IsVisible = true;
        }

        /// <summary>
        /// 返回画布坐标系下的框选矩形（自动修正起点/终点顺序）。
        /// </summary>
        private Rect GetCanvasSelectionRect()
        {
            var x = Math.Min(_dragStart.X, _dragEnd.X);
            var y = Math.Min(_dragStart.Y, _dragEnd.Y);
            var w = Math.Abs(_dragEnd.X - _dragStart.X);
            var h = Math.Abs(_dragEnd.Y - _dragStart.Y);
            return new Rect(x, y, w, h);
        }

        // ────────────────────────────────────────────────────────────
        //  按钮事件
        // ────────────────────────────────────────────────────────────

        private void CancelButton_Click(object sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            ResultPath = null;
            Close();
        }

        private void ConfirmButton_Click(object sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (!_hasSelection || _sourceBitmap == null)
            {
                Close();
                return;
            }

            try
            {
                var canvasRect = GetCanvasSelectionRect();
                var imageRect = CanvasRectToImagePixelRect(canvasRect);

                if (imageRect.Width < 1 || imageRect.Height < 1)
                {
                    Close();
                    return;
                }

                ResultPath = CropAndSave(imageRect);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AvatarCropWindow] 裁剪失败: {ex}");
                ErrorMessage = "图片裁剪失败，请重试";
                ResultPath = null;
            }

            Close();
        }

        // ────────────────────────────────────────────────────────────
        //  坐标转换与裁剪逻辑
        // ────────────────────────────────────────────────────────────

        /// <summary>
        /// 将画布（DIP）坐标中的框选矩形转换为原始图片的像素坐标矩形。
        /// 计算基于 Stretch="Uniform" 下图片在 Canvas 中的实际显示区域。
        /// </summary>
        private System.Drawing.Rectangle CanvasRectToImagePixelRect(Rect canvasRect)
        {
            if (_sourceBitmap == null || _cropCanvas == null)
            {
                return default;
            }

            ComputeDisplayBounds(out var scale, out var offsetX, out var offsetY);
            if (scale <= 0)
            {
                return default;
            }

            var imgPixelW = _sourceBitmap.PixelSize.Width;
            var imgPixelH = _sourceBitmap.PixelSize.Height;

            // 画布坐标 → 图片 DIP 坐标 → 图片像素坐标
            // scale = canvas_DIPs / image_pixels（Uniform fit 缩放比）
            var rawX = (canvasRect.X - offsetX) / scale;
            var rawY = (canvasRect.Y - offsetY) / scale;
            var rawW = canvasRect.Width / scale;
            var rawH = canvasRect.Height / scale;

            // 取与图片边界的交集，确保 x 负值时宽度也正确缩减
            var x1 = (int)Math.Max(0, Math.Round(rawX));
            var y1 = (int)Math.Max(0, Math.Round(rawY));
            var x2 = (int)Math.Min(imgPixelW, Math.Round(rawX + rawW));
            var y2 = (int)Math.Min(imgPixelH, Math.Round(rawY + rawH));
            var clampedW = x2 - x1;
            var clampedH = y2 - y1;

            if (clampedW <= 0 || clampedH <= 0)
            {
                return default;
            }

            return new System.Drawing.Rectangle(x1, y1, clampedW, clampedH);
        }

        /// <summary>
        /// 计算 Stretch="Uniform" 模式下图片在 Canvas 中的缩放比例和偏移量。
        /// scale   = 每像素对应的 Canvas DIP 数（即 canvas_size / pixel_size 的最小值）
        /// offsetX / offsetY = 图片显示区域左上角相对于 Canvas 左上角的 DIP 偏移。
        /// </summary>
        private void ComputeDisplayBounds(out double scale, out double offsetX, out double offsetY)
        {
            var canvasW = _cropCanvas.Bounds.Width;
            var canvasH = _cropCanvas.Bounds.Height;
            var imgW = (double)_sourceBitmap.PixelSize.Width;
            var imgH = (double)_sourceBitmap.PixelSize.Height;

            if (imgW <= 0 || imgH <= 0 || canvasW <= 0 || canvasH <= 0)
            {
                scale = 1;
                offsetX = 0;
                offsetY = 0;
                return;
            }

            var scaleX = canvasW / imgW;
            var scaleY = canvasH / imgH;
            scale = Math.Min(scaleX, scaleY);

            offsetX = (canvasW - imgW * scale) / 2.0;
            offsetY = (canvasH - imgH * scale) / 2.0;
        }

        /// <summary>
        /// 使用 System.Drawing 将源图片按给定像素矩形裁剪，保存为 PNG 并返回路径。
        /// </summary>
        private string CropAndSave(System.Drawing.Rectangle cropRect)
        {
            var avatarDir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "HundunWorld",
                "avatars");
            Directory.CreateDirectory(avatarDir);

            var destPath = System.IO.Path.Combine(avatarDir, $"{Guid.NewGuid():N}_crop.png");

            using var src = new System.Drawing.Bitmap(_sourcePath);
            using var cropped = src.Clone(cropRect, src.PixelFormat);
            cropped.Save(destPath, System.Drawing.Imaging.ImageFormat.Png);

            return destPath;
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            _sourceBitmap?.Dispose();
            _sourceBitmap = null;
        }
    }
}
