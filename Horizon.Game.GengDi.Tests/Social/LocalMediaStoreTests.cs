using System;
using System.IO;
using Horizon.Game.GengDi.Core.Services;
using Horizon.Game.GengDi.Core.Services.Database;
using Horizon.Game.GengDi.Enums;

namespace Horizon.Game.GengDi.Tests.Social;

public sealed class LocalMediaStoreTests
{
    [Fact]
    public void PersistAttachment_CopiesImageAndBuildsPreview()
    {
        using var scope = new MediaStoreScope();
        var sourcePath = CreatePngFile(scope.SourceDirectory, "image-source.png", SamplePngBytes);

        var storedAsset = LocalMediaStore.PersistAttachment(sourcePath, MediaAttachmentType.Image);

        Assert.NotNull(storedAsset);
        Assert.True(File.Exists(storedAsset.MediaPath));
        Assert.True(File.Exists(storedAsset.PreviewPath));
        Assert.StartsWith(LocalMediaStore.GetMediaRootDirectory(), storedAsset.MediaPath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PersistAttachment_CopiesVideoAndBuildsPosterPreview()
    {
        using var scope = new MediaStoreScope();
        var sourcePath = Path.Combine(scope.SourceDirectory, "demo-video.mp4");
        File.WriteAllBytes(sourcePath, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });

        var storedAsset = LocalMediaStore.PersistAttachment(sourcePath, MediaAttachmentType.Video);

        Assert.NotNull(storedAsset);
        Assert.True(File.Exists(storedAsset.MediaPath));
        Assert.True(File.Exists(storedAsset.PreviewPath));
        Assert.EndsWith(".png", storedAsset.PreviewPath, StringComparison.OrdinalIgnoreCase);
    }

    private static string CreatePngFile(string directory, string fileName, byte[] bytes)
    {
        var filePath = Path.Combine(directory, fileName);
        File.WriteAllBytes(filePath, bytes);
        return filePath;
    }

    private sealed class MediaStoreScope : IDisposable
    {
        private readonly string? _originalOverride;

        public MediaStoreScope()
        {
            RootDirectory = Path.Combine(Path.GetTempPath(), "Horizon.Game.GengDi.MediaTests", Guid.NewGuid().ToString("N"));
            SourceDirectory = Path.Combine(RootDirectory, "sources");
            Directory.CreateDirectory(SourceDirectory);
            _originalOverride = LocalPassportStore.DbDirectoryOverride;
            LocalPassportStore.DbDirectoryOverride = RootDirectory;
        }

        public string RootDirectory { get; }

        public string SourceDirectory { get; }

        public void Dispose()
        {
            LocalPassportStore.DbDirectoryOverride = _originalOverride;

            if (Directory.Exists(RootDirectory))
            {
                try
                {
                    Directory.Delete(RootDirectory, true);
                }
                catch
                {
                }
            }
        }
    }

    private static readonly byte[] SamplePngBytes = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aA1EAAAAASUVORK5CYII=");
}