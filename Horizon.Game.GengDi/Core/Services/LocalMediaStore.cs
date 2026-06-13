using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Horizon.Game.GengDi.Core.Services.Database;
using Horizon.Game.GengDi.Enums;

namespace Horizon.Game.GengDi.Core.Services
{
    internal sealed class StoredMediaAsset
    {
        public string MediaPath { get; init; } = string.Empty;

        public string PreviewPath { get; init; } = string.Empty;
    }

    internal static class LocalMediaStore
    {
        private const int ImagePreviewMaxWidth = 960;
        private const int ImagePreviewMaxHeight = 640;

        public static StoredMediaAsset PersistAttachment(string sourcePath, MediaAttachmentType attachmentType)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                return new StoredMediaAsset
                {
                    MediaPath = sourcePath ?? string.Empty,
                    PreviewPath = sourcePath ?? string.Empty
                };
            }

            EnsureDirectories(attachmentType, out var originalsDirectory, out var previewsDirectory);

            var extension = Path.GetExtension(sourcePath);
            var assetKey = $"{DateTime.UtcNow:yyyyMMddHHmmssfff}_{Guid.NewGuid():N}";
            var persistedMediaPath = Path.Combine(originalsDirectory, $"{assetKey}{extension}");
            File.Copy(sourcePath, persistedMediaPath, overwrite: true);

            var previewPath = attachmentType == MediaAttachmentType.Image
                ? persistedMediaPath
                : string.Empty;

            if (OperatingSystem.IsWindows())
            {
                previewPath = attachmentType switch
                {
                    MediaAttachmentType.Image => TryCreateImagePreview(persistedMediaPath, previewsDirectory, assetKey) ?? persistedMediaPath,
                    MediaAttachmentType.Video => TryCreateVideoPoster(persistedMediaPath, previewsDirectory, assetKey) ?? string.Empty,
                    _ => persistedMediaPath
                };
            }

            return new StoredMediaAsset
            {
                MediaPath = persistedMediaPath,
                PreviewPath = previewPath
            };
        }

        internal static string GetMediaRootDirectory()
        {
            var appDataDirectory = !string.IsNullOrWhiteSpace(LocalPassportStore.DbDirectoryOverride)
                ? LocalPassportStore.DbDirectoryOverride
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HundunWorld");

            return Path.Combine(appDataDirectory, "MediaStore");
        }

        private static void EnsureDirectories(MediaAttachmentType attachmentType, out string originalsDirectory, out string previewsDirectory)
        {
            var mediaRootDirectory = GetMediaRootDirectory();
            var categoryName = attachmentType == MediaAttachmentType.Video ? "videos" : "images";

            originalsDirectory = Path.Combine(mediaRootDirectory, categoryName, "originals");
            previewsDirectory = Path.Combine(mediaRootDirectory, categoryName, "previews");

            Directory.CreateDirectory(originalsDirectory);
            Directory.CreateDirectory(previewsDirectory);
        }

        [SupportedOSPlatform("windows")]
        private static string TryCreateImagePreview(string sourcePath, string previewsDirectory, string assetKey)
        {
            try
            {
                using var image = Image.FromFile(sourcePath);
                var previewSize = CalculateFitSize(image.Width, image.Height, ImagePreviewMaxWidth, ImagePreviewMaxHeight);
                using var previewBitmap = new Bitmap(previewSize.Width, previewSize.Height);
                using var graphics = Graphics.FromImage(previewBitmap);
                graphics.Clear(Color.FromArgb(8, 18, 28));
                graphics.CompositingQuality = CompositingQuality.HighQuality;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.SmoothingMode = SmoothingMode.HighQuality;
                graphics.DrawImage(image, new Rectangle(0, 0, previewSize.Width, previewSize.Height));

                var previewPath = Path.Combine(previewsDirectory, $"{assetKey}.png");
                previewBitmap.Save(previewPath, ImageFormat.Png);
                return previewPath;
            }
            catch
            {
                return null;
            }
        }

        [SupportedOSPlatform("windows")]
        private static string TryCreateVideoPoster(string sourcePath, string previewsDirectory, string assetKey)
        {
            var shellThumbnailPath = TryCreateShellVideoThumbnail(sourcePath, previewsDirectory, assetKey);
            if (!string.IsNullOrWhiteSpace(shellThumbnailPath))
            {
                return shellThumbnailPath;
            }

            try
            {
                const int width = 960;
                const int height = 540;

                using var posterBitmap = new Bitmap(width, height);
                using var graphics = Graphics.FromImage(posterBitmap);
                using var backgroundBrush = new LinearGradientBrush(
                    new Rectangle(0, 0, width, height),
                    Color.FromArgb(5, 12, 20),
                    Color.FromArgb(13, 53, 92),
                    35f);

                graphics.FillRectangle(backgroundBrush, 0, 0, width, height);
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

                using var accentPen = new Pen(Color.FromArgb(0, 174, 255), 4);
                graphics.DrawRectangle(accentPen, 28, 28, width - 56, height - 56);

                var playTriangle = new[]
                {
                    new Point(width / 2 - 40, height / 2 - 65),
                    new Point(width / 2 - 40, height / 2 + 65),
                    new Point(width / 2 + 78, height / 2)
                };

                using var playBrush = new SolidBrush(Color.FromArgb(230, 255, 255, 255));
                graphics.FillPolygon(playBrush, playTriangle);

                var title = Path.GetFileNameWithoutExtension(sourcePath);
                using var titleFont = new Font("Segoe UI", 24, FontStyle.Bold, GraphicsUnit.Pixel);
                using var subtitleFont = new Font("Segoe UI", 14, FontStyle.Regular, GraphicsUnit.Pixel);
                using var titleBrush = new SolidBrush(Color.White);
                using var subtitleBrush = new SolidBrush(Color.FromArgb(210, 207, 229, 245));

                var textBounds = new RectangleF(48, height - 136, width - 96, 50);
                graphics.DrawString(title, titleFont, titleBrush, textBounds);
                graphics.DrawString("本地视频预览", subtitleFont, subtitleBrush, new RectangleF(48, height - 80, 280, 24));

                var previewPath = Path.Combine(previewsDirectory, $"{assetKey}.png");
                posterBitmap.Save(previewPath, ImageFormat.Png);
                return previewPath;
            }
            catch
            {
                return null;
            }
        }

        [SupportedOSPlatform("windows")]
        private static string TryCreateShellVideoThumbnail(string sourcePath, string previewsDirectory, string assetKey)
        {
            IntPtr thumbnailHandle = IntPtr.Zero;
            IShellItemImageFactory shellImageFactory = null;

            try
            {
                var interfaceId = typeof(IShellItemImageFactory).GUID;
                SHCreateItemFromParsingName(sourcePath, IntPtr.Zero, ref interfaceId, out shellImageFactory);

                var result = shellImageFactory.GetImage(
                    new NativeSize(960, 540),
                    ShellImageOptions.ThumbnailOnly | ShellImageOptions.BiggerSizeOk,
                    out thumbnailHandle);

                if (result != 0 || thumbnailHandle == IntPtr.Zero)
                {
                    return null;
                }

                using var thumbnailBitmap = Image.FromHbitmap(thumbnailHandle);
                if (thumbnailBitmap == null || thumbnailBitmap.Width <= 0 || thumbnailBitmap.Height <= 0)
                {
                    return null;
                }

                var previewPath = Path.Combine(previewsDirectory, $"{assetKey}.png");
                thumbnailBitmap.Save(previewPath, ImageFormat.Png);
                return previewPath;
            }
            catch
            {
                return null;
            }
            finally
            {
                if (thumbnailHandle != IntPtr.Zero)
                {
                    DeleteObject(thumbnailHandle);
                }

                if (shellImageFactory != null && Marshal.IsComObject(shellImageFactory))
                {
                    Marshal.FinalReleaseComObject(shellImageFactory);
                }
            }
        }

        private static Size CalculateFitSize(int originalWidth, int originalHeight, int maxWidth, int maxHeight)
        {
            if (originalWidth <= 0 || originalHeight <= 0)
            {
                return new Size(maxWidth, maxHeight);
            }

            var widthRatio = (double)maxWidth / originalWidth;
            var heightRatio = (double)maxHeight / originalHeight;
            var scale = Math.Min(1d, Math.Min(widthRatio, heightRatio));

            return new Size(
                Math.Max(1, (int)Math.Round(originalWidth * scale)),
                Math.Max(1, (int)Math.Round(originalHeight * scale)));
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        private static extern void SHCreateItemFromParsingName(
            [MarshalAs(UnmanagedType.LPWStr)] string path,
            IntPtr pbc,
            ref Guid riid,
            [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory shellItemImageFactory);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteObject(IntPtr hObject);

        [ComImport]
        [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellItemImageFactory
        {
            [PreserveSig]
            int GetImage(NativeSize size, ShellImageOptions flags, out IntPtr bitmapHandle);
        }

        [StructLayout(LayoutKind.Sequential)]
        private readonly struct NativeSize
        {
            public NativeSize(int width, int height)
            {
                Width = width;
                Height = height;
            }

            public int Width { get; }

            public int Height { get; }
        }

        [Flags]
        private enum ShellImageOptions
        {
            BiggerSizeOk = 0x1,
            ThumbnailOnly = 0x8
        }
    }
}