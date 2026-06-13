using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NAudio.Wave;
using Horizon.Game.GengDi.Core.Services;
using Horizon.Game.GengDi.Models;

namespace Horizon.Game.GengDi.Core.Services
{
    public class ScanProgress
    {
        public int ScannedCount { get; set; }
        public int TotalCount { get; set; }
        public string CurrentFile { get; set; }
    }

    public class LocalMusicScannerService
    {
        private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp3", ".flac", ".wav", ".aac", ".wma", ".ogg", ".m4a"
        };

        public async Task<List<Song>> ScanDirectoryAsync(string directoryPath, IProgress<ScanProgress> progress = null, bool copyToLibrary = false)
        {
            if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
            {
                return new List<Song>();
            }

            string[] files;
            try
            {
                files = Directory.EnumerateFiles(directoryPath, "*.*", SearchOption.AllDirectories)
                    .Where(f => SupportedExtensions.Contains(Path.GetExtension(f)))
                    .ToArray();
            }
            catch (UnauthorizedAccessException)
            {
                return new List<Song>();
            }
            catch (DirectoryNotFoundException)
            {
                return new List<Song>();
            }

            return await ScanFilesAsync(files, progress, copyToLibrary);
        }

        public async Task<List<Song>> ScanFilesAsync(string[] filePaths, IProgress<ScanProgress> progress = null, bool copyToLibrary = false)
        {
            if (filePaths == null || filePaths.Length == 0)
            {
                return new List<Song>();
            }

            var filteredFiles = filePaths.Where(f => SupportedExtensions.Contains(Path.GetExtension(f))).ToArray();
            var songs = new List<Song>();
            var totalCount = filteredFiles.Length;

            for (var i = 0; i < filteredFiles.Length; i++)
            {
                var filePath = filteredFiles[i];

                progress?.Report(new ScanProgress
                {
                    ScannedCount = i + 1,
                    TotalCount = totalCount,
                    CurrentFile = Path.GetFileName(filePath)
                });

                try
                {
                    var song = await CreateSongFromFileAsync(filePath, copyToLibrary);
                    if (song != null)
                    {
                        songs.Add(song);
                    }
                }
                catch (UnauthorizedAccessException)
                {
                }
                catch (IOException)
                {
                }
            }

            return songs;
        }

        private async Task<Song> CreateSongFromFileAsync(string filePath, bool copyToLibrary)
        {
            var targetPath = filePath;

            if (copyToLibrary)
            {
                try
                {
                    var musicDirectory = Path.Combine(LocalMediaStore.GetMediaRootDirectory(), "music");
                    Directory.CreateDirectory(musicDirectory);

                    var uniqueName = $"{DateTime.UtcNow:yyyyMMddHHmmssfff}_{Guid.NewGuid():N}{Path.GetExtension(filePath)}";
                    targetPath = Path.Combine(musicDirectory, uniqueName);
                    File.Copy(filePath, targetPath, overwrite: false);
                }
                catch (UnauthorizedAccessException)
                {
                    targetPath = filePath;
                }
                catch (IOException)
                {
                    targetPath = filePath;
                }
            }

            var song = new Song();
            var id = "local_" + Math.Abs(filePath.GetHashCode()).ToString("x8") + "_" + Path.GetFileNameWithoutExtension(filePath);

            song.Id = id;
            song.Title = Path.GetFileNameWithoutExtension(filePath);
            song.ArtistName = "未知艺术家";
            song.AlbumName = "未知专辑";
            song.Source = "local";
            song.FileFormat = Path.GetExtension(filePath).TrimStart('.');
            song.LocalFilePath = targetPath;
            song.AddedDate = DateTime.UtcNow;

            try
            {
                var fileInfo = new FileInfo(filePath);
                song.FileSize = fileInfo.Length;
            }
            catch
            {
                song.FileSize = 0;
            }

            try
            {
                using var reader = new MediaFoundationReader(targetPath);
                song.Duration = reader.TotalTime;
            }
            catch
            {
                song.Duration = TimeSpan.Zero;
            }

            return await Task.FromResult(song);
        }
    }
}
