using System;
using System.Collections.Generic;
using System.IO;
using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.StyleSystem;

namespace HundunWorld.Game.UI.Ink.Pages.PhotoMode
{
    /// <summary>
    /// 拍照相册（dom-id: <c>nav-photo-album</c>）：列出 <c>Screenshots/Photo_*.png</c> 的最近成片。
    /// <para>
    /// 相册数据就是磁盘上的文件，不依赖服务端；缩略图用 <see cref="Texture.FromFile"/> 直接从 PNG 读，
    /// 所以刚拍完退出再进也能看到。此前「相册」按钮只做了返回上一页（2026-10-29 改）。
    /// </para>
    /// </summary>
    public class PhotoAlbumPage : ContainerControl, IInkPage, IInkNavigationSource
    {
        private const int MaxPhotos = 12;
        private const int Columns = 4;
        private const float PagePadding = 40f;
        private const float HeaderHeight = 96f;
        private const float Gap = 16f;
        private const float CaptionHeight = 22f;

        private readonly List<InkButton> _cells = new List<InkButton>();
        private readonly List<Image> _thumbs = new List<Image>();
        private readonly List<Texture> _textures = new List<Texture>();
        private readonly List<Label> _captions = new List<Label>();

        private Panel _previewOverlay;
        private Image _previewImage;
        private Label _previewCaption;
        private InkButton _previewCloseButton;

        private Label _titleLabel;
        private Label _countLabel;
        private InkButton _backButton;
        private Label _emptyLabel;

        /// <summary>页面请求跳转（参数为目标 dom-id）。</summary>
        public event Action<string> NavigationRequested;

        public PhotoAlbumPage()
        {
            AnchorPreset = AnchorPresets.StretchAll;
            BackgroundColor = InkWashTheme.BaseDefault;
            ClipChildren = true;
            AutoFocus = false;
            Size = new Float2(1920f, 1080f);

            try
            {
                BuildHeader();
                LoadPhotos();
                // 预览层必须最后建：控件按加入顺序绘制，早建就会被缩略图盖住
                BuildPreviewOverlay();
                RefreshLayout();

                // 自测：album-preview.on 存在时自动打开第一张的放大预览（运行时控件点不到，只能这样取证）
                if (HasSelfTestMarker("album-preview.on") && _thumbs.Count > 0)
                    OpenPreview(0);
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[PhotoAlbumPage] 初始化失败: {ex.Message}");
            }
        }

        private void BuildHeader()
        {
            _backButton = new InkButton
            {
                Text = "返回",
                Parent = this
            };
            _backButton.Clicked += () => NavigationRequested?.Invoke(InkPageDomIds.BackOneLevel);

            _titleLabel = new Label
            {
                Text = "相册",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 24f),
                TextColor = InkWashTheme.GoldBright,
                Parent = this
            };

            _countLabel = new Label
            {
                Text = string.Empty,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                TextColor = InkWashTheme.TextTertiary,
                Parent = this
            };

            _emptyLabel = new Label
            {
                Text = "还没有照片 · 回到取景按快门拍一张",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 14f),
                TextColor = InkWashTheme.TextTertiary,
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = TextAlignment.Center,
                Visible = false,
                Parent = this
            };
        }

        /// <summary>扫描 Screenshots 目录，按修改时间倒序取最近 <see cref="MaxPhotos"/> 张。</summary>
        private void LoadPhotos()
        {
            string dir = Path.Combine(Globals.ProjectFolder, "Screenshots");
            string[] files;
            try
            {
                files = Directory.Exists(dir) ? Directory.GetFiles(dir, "Photo_*.png") : Array.Empty<string>();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogWarning($"[PhotoAlbumPage] 读取相册目录失败: {ex.Message}");
                files = Array.Empty<string>();
            }

            Array.Sort(files, (a, b) =>
                File.GetLastWriteTimeUtc(b).CompareTo(File.GetLastWriteTimeUtc(a)));

            int count = Mathf.Min(files.Length, MaxPhotos);
            for (int i = 0; i < count; i++)
            {
                Texture texture = null;
                try
                {
                    texture = Texture.FromFile(files[i], false);
                }
                catch (Exception ex)
                {
                    FlaxEngine.Debug.LogWarning($"[PhotoAlbumPage] 缩略图加载失败 {files[i]}: {ex.Message}");
                }

                var cell = new InkButton
                {
                    Text = string.Empty,
                    Variant = InkButtonVariant.Secondary,
                    Parent = this
                };
                int photoIndex = i;
                cell.Clicked += () => OpenPreview(photoIndex);
                _cells.Add(cell);

                var thumb = new Image
                {
                    KeepAspectRatio = true,
                    BackgroundColor = InkWashTheme.BaseSecondary,
                    AnchorPreset = AnchorPresets.StretchAll,
                    Offsets = Margin.Zero,
                    Parent = cell
                };
                if (texture != null)
                    thumb.Brush = new TextureBrush(texture);
                _thumbs.Add(thumb);
                _textures.Add(texture);

                var caption = new Label
                {
                    Text = Path.GetFileNameWithoutExtension(files[i]),
                    Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 10f),
                    TextColor = InkWashTheme.TextTertiary,
                    HorizontalAlignment = TextAlignment.Center,
                    Parent = this
                };
                _captions.Add(caption);
            }

            _emptyLabel.Visible = files.Length == 0;
            _countLabel.Text = files.Length == 0
                ? "还没有照片"
                : $"共 {files.Length} 张，显示最近 {count} 张";
        }

        /// <inheritdoc />
        public void RefreshLayout()
        {
            float w = Width;
            float h = Height;
            if (w <= 0f || h <= 0f)
                return;

            if (_backButton != null)
                _backButton.Bounds = new Rectangle(PagePadding, 28f, 96f, 36f);
            if (_titleLabel != null)
                _titleLabel.Bounds = new Rectangle(PagePadding + 116f, 30f, 200f, 32f);
            if (_countLabel != null)
                _countLabel.Bounds = new Rectangle(PagePadding + 320f, 38f, 420f, 20f);
            if (_emptyLabel != null)
                _emptyLabel.Bounds = new Rectangle(PagePadding, HeaderHeight, w - PagePadding * 2f, h - HeaderHeight - PagePadding);

            float contentW = w - PagePadding * 2f;
            float cellW = (contentW - Gap * (Columns - 1)) / Columns;
            float cellH = cellW * 9f / 16f;

            for (int i = 0; i < _thumbs.Count; i++)
            {
                int col = i % Columns;
                int row = i / Columns;
                float x = PagePadding + col * (cellW + Gap);
                float y = HeaderHeight + row * (cellH + CaptionHeight + Gap);

                _cells[i].Bounds = new Rectangle(x, y, cellW, cellH);
                _captions[i].Bounds = new Rectangle(x, y + cellH + 2f, cellW, CaptionHeight - 4f);
            }

            if (_previewOverlay != null && _previewOverlay.Visible)
                LayoutPreview();
        }

        private void BuildPreviewOverlay()
        {
            _previewOverlay = new Panel
            {
                BackgroundColor = new Color(0f, 0f, 0f, 0.86f),
                Visible = false,
                Parent = this
            };

            _previewImage = new Image
            {
                KeepAspectRatio = true,
                BackgroundColor = InkWashTheme.BaseSecondary,
                Parent = _previewOverlay
            };

            _previewCaption = new Label
            {
                Text = string.Empty,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Body, 12f),
                TextColor = InkWashTheme.PaperAged,
                HorizontalAlignment = TextAlignment.Center,
                Parent = _previewOverlay
            };

            _previewCloseButton = new InkButton
            {
                Text = "关闭",
                Parent = _previewOverlay
            };
            _previewCloseButton.Clicked += ClosePreview;
        }

        /// <summary>点缩略图放大查看：复用同一张纹理，叠一层暗底 + 居中大图 + 文件名。</summary>
        private void OpenPreview(int index)
        {
            if (_previewOverlay == null || index < 0 || index >= _thumbs.Count)
                return;

            // 同一个 TextureBrush 只归一个控件用：预览层单独建一个 brush，否则大图不画。
            Texture texture = index < _textures.Count ? _textures[index] : null;
            _previewImage.Brush = texture != null ? new TextureBrush(texture) : null;
            _previewCaption.Text = _captions[index].Text;
            _previewOverlay.Visible = true;
            LayoutPreview();

            if (HasSelfTestMarker("album-preview.on"))
                AppendSelfTestReceipt($"album preview index={index} caption={_captions[index].Text}");
        }

        private void ClosePreview()
        {
            if (_previewOverlay != null)
                _previewOverlay.Visible = false;
        }

        private void LayoutPreview()
        {
            if (_previewOverlay == null)
                return;

            float w = Width;
            float h = Height;
            _previewOverlay.Bounds = new Rectangle(0f, 0f, w, h);
            _previewCloseButton.Bounds = new Rectangle(w - PagePadding - 96f, 28f, 96f, 36f);

            float imgW = w * 0.8f;
            float imgH = imgW * 9f / 16f;
            float maxH = h * 0.78f;
            if (imgH > maxH)
            {
                imgH = maxH;
                imgW = imgH * 16f / 9f;
            }

            float imgX = (w - imgW) * 0.5f;
            float imgY = (h - imgH) * 0.5f - 10f;
            _previewImage.Bounds = new Rectangle(imgX, imgY, imgW, imgH);
            _previewCaption.Bounds = new Rectangle(imgX, imgY + imgH + 8f, imgW, 22f);
        }

        private static bool HasSelfTestMarker(string fileName)
        {
            try
            {
                string path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "HundunWorld", fileName);
                return File.Exists(path);
            }
            catch
            {
                return false;
            }
        }

        private static void AppendSelfTestReceipt(string line)
        {
            try
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "HundunWorld");
                File.AppendAllText(Path.Combine(dir, "ui-selftest.out"), line + Environment.NewLine);
            }
            catch
            {
                // 回执写不进去不影响功能
            }
        }

        /// <inheritdoc />
        protected override void OnSizeChanged()
        {
            base.OnSizeChanged();
            RefreshLayout();
        }
    }
}
