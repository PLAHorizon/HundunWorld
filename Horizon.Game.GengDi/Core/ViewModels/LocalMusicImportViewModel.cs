using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Controls;
using Horizon.Game.GengDi.Core.Services;
using Horizon.Game.GengDi.Models;

namespace Horizon.Game.GengDi.Core.ViewModels
{
    public class LocalMusicImportViewModel : ViewModelBase
    {
        private readonly MusicLibraryService _libraryService;
        private readonly LocalMusicScannerService _scannerService;
        private bool _isScanning;
        private bool _isImporting;
        private int _scannedCount;
        private int _totalCount;
        private string _currentScanningFile;
        private List<Song> _scannedSongs;
        private List<Song> _selectedSongs;
        private bool _copyToLibrary = true;
        private string _selectedDirectory;
        private bool _isDialogOpen;
        private string _statusMessage;
        private string _targetPlaylistId;
        private Window _ownerWindow;

        public LocalMusicImportViewModel()
        {
            _libraryService = MusicLibraryService.Instance;
            _scannerService = new LocalMusicScannerService();
            _scannedSongs = new List<Song>();
            _selectedSongs = new List<Song>();

            SelectDirectoryCommand = new AsyncRelayCommand(SelectDirectoryAsync);
            SelectFilesCommand = new AsyncRelayCommand(SelectFilesAsync);
            ImportCommand = new AsyncRelayCommand(ImportAsync);
            CloseDialogCommand = new RelayCommand(() => IsDialogOpen = false);
            ToggleSelectAllCommand = new RelayCommand(ToggleSelectAll);
            ToggleSongSelectionCommand = new RelayCommand<Song>(ToggleSongSelection);
        }

        public bool IsScanning
        {
            get => _isScanning;
            set => SetProperty(ref _isScanning, value);
        }

        public bool IsImporting
        {
            get => _isImporting;
            set => SetProperty(ref _isImporting, value);
        }

        public int ScannedCount
        {
            get => _scannedCount;
            set => SetProperty(ref _scannedCount, value);
        }

        public int TotalCount
        {
            get => _totalCount;
            set => SetProperty(ref _totalCount, value);
        }

        public string CurrentScanningFile
        {
            get => _currentScanningFile;
            set => SetProperty(ref _currentScanningFile, value);
        }

        public List<Song> ScannedSongs
        {
            get => _scannedSongs;
            set => SetProperty(ref _scannedSongs, value);
        }

        public List<Song> SelectedSongs
        {
            get => _selectedSongs;
            set => SetProperty(ref _selectedSongs, value);
        }

        public bool CopyToLibrary
        {
            get => _copyToLibrary;
            set => SetProperty(ref _copyToLibrary, value);
        }

        public string SelectedDirectory
        {
            get => _selectedDirectory;
            set => SetProperty(ref _selectedDirectory, value);
        }

        public bool IsDialogOpen
        {
            get => _isDialogOpen;
            set => SetProperty(ref _isDialogOpen, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public bool HasScannedSongs => _scannedSongs?.Count > 0;
        public string ScanProgressText => TotalCount > 0 ? $"{ScannedCount}/{TotalCount}" : string.Empty;

        public ICommand SelectDirectoryCommand { get; }
        public ICommand SelectFilesCommand { get; }
        public ICommand ImportCommand { get; }
        public ICommand CloseDialogCommand { get; }
        public ICommand ToggleSelectAllCommand { get; }
        public ICommand ToggleSongSelectionCommand { get; }

        public event Action<List<Song>> ImportCompleted;

        public void OpenDialog(string targetPlaylistId = null)
        {
            _targetPlaylistId = targetPlaylistId;
            ScannedSongs = new List<Song>();
            SelectedSongs = new List<Song>();
            SelectedDirectory = string.Empty;
            StatusMessage = string.Empty;
            ScannedCount = 0;
            TotalCount = 0;
            CurrentScanningFile = string.Empty;
            IsDialogOpen = true;
        }

        private Window GetOwnerWindow()
        {
            if (_ownerWindow != null) return _ownerWindow;
            return Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
                ? desktop.MainWindow
                : null;
        }

        private async Task SelectDirectoryAsync()
        {
            try
            {
                var owner = GetOwnerWindow();
                var dialog = new OpenFolderDialog();
                var result = await dialog.ShowAsync(owner);
                if (string.IsNullOrEmpty(result)) return;

                SelectedDirectory = result;
                await ScanDirectoryAsync(result);
            }
            catch { }
        }

        private async Task SelectFilesAsync()
        {
            try
            {
                var owner = GetOwnerWindow();
                var dialog = new OpenFileDialog();
                dialog.Title = "选择音乐文件";
                dialog.AllowMultiple = true;
                dialog.Filters.Add(new FileDialogFilter { Name = "音乐文件", Extensions = new System.Collections.Generic.List<string> { "mp3", "flac", "wav", "aac", "wma", "ogg", "m4a" } });

                var result = await dialog.ShowAsync(owner);
                if (result == null || result.Length == 0) return;

                await ScanFilesAsync(result);
            }
            catch { }
        }

        private async Task ScanDirectoryAsync(string path)
        {
            IsScanning = true;
            StatusMessage = "正在扫描...";
            try
            {
                var progress = new Progress<ScanProgress>(p =>
                {
                    ScannedCount = p.ScannedCount;
                    TotalCount = p.TotalCount;
                    CurrentScanningFile = p.CurrentFile;
                    OnPropertyChanged(nameof(ScanProgressText));
                });

                var songs = await _scannerService.ScanDirectoryAsync(path, progress, CopyToLibrary);
                ScannedSongs = songs;
                SelectedSongs = new List<Song>(songs);
                StatusMessage = $"扫描完成，共发现 {songs.Count} 首歌曲";
            }
            catch (Exception ex)
            {
                StatusMessage = $"扫描失败: {ex.Message}";
            }
            finally
            {
                IsScanning = false;
                OnPropertyChanged(nameof(HasScannedSongs));
            }
        }

        private async Task ScanFilesAsync(string[] files)
        {
            IsScanning = true;
            StatusMessage = "正在扫描...";
            try
            {
                var progress = new Progress<ScanProgress>(p =>
                {
                    ScannedCount = p.ScannedCount;
                    TotalCount = p.TotalCount;
                    CurrentScanningFile = p.CurrentFile;
                    OnPropertyChanged(nameof(ScanProgressText));
                });

                var songs = await _scannerService.ScanFilesAsync(files, progress, CopyToLibrary);
                ScannedSongs = songs;
                SelectedSongs = new List<Song>(songs);
                StatusMessage = $"扫描完成，共发现 {songs.Count} 首歌曲";
            }
            catch (Exception ex)
            {
                StatusMessage = $"扫描失败: {ex.Message}";
            }
            finally
            {
                IsScanning = false;
                OnPropertyChanged(nameof(HasScannedSongs));
            }
        }

        private async Task ImportAsync()
        {
            if (SelectedSongs == null || SelectedSongs.Count == 0)
            {
                StatusMessage = "请先选择要导入的歌曲";
                return;
            }

            IsImporting = true;
            StatusMessage = "正在导入...";
            try
            {
                await _libraryService.ImportLocalSongsAsync(SelectedSongs);

                if (!string.IsNullOrEmpty(_targetPlaylistId))
                {
                    foreach (var song in SelectedSongs)
                    {
                        _libraryService.AddSongToPlaylist(_targetPlaylistId, song.Id);
                    }
                }

                StatusMessage = $"成功导入 {SelectedSongs.Count} 首本地歌曲";
                IsDialogOpen = false;
                ImportCompleted?.Invoke(SelectedSongs);
            }
            catch (Exception ex)
            {
                StatusMessage = $"导入失败: {ex.Message}";
            }
            finally
            {
                IsImporting = false;
            }
        }

        private void ToggleSelectAll()
        {
            if (SelectedSongs?.Count == ScannedSongs?.Count)
            {
                SelectedSongs = new List<Song>();
            }
            else
            {
                SelectedSongs = new List<Song>(ScannedSongs ?? new List<Song>());
            }
        }

        private void ToggleSongSelection(Song song)
        {
            if (song == null) return;
            var list = new List<Song>(SelectedSongs ?? new List<Song>());
            if (list.Any(s => s.Id == song.Id))
            {
                list.RemoveAll(s => s.Id == song.Id);
            }
            else
            {
                list.Add(song);
            }
            SelectedSongs = list;
        }
    }
}
