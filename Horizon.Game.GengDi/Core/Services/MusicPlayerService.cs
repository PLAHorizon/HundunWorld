using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Horizon.Game.GengDi.Enums;
using Horizon.Game.GengDi.Models;

namespace Horizon.Game.GengDi.Core.Services
{
    public interface IAudioEngine : IDisposable
    {
        bool IsPlaying { get; }
        bool CanPlay { get; }
        bool IsOpen { get; }
        void Open(string source);
        Task<bool> OpenAsync(string source);
        void Play();
        void Pause();
        void Stop();
        Task StopAsync();
        void SetVolume(double volume);
        void SeekTo(TimeSpan position);
        TimeSpan CurrentPosition { get; }
        TimeSpan Duration { get; }
        event EventHandler PlaybackEnded;
    }

    public class SimulatedAudioEngine : IAudioEngine
    {
        private Timer _timer;
        private DateTime _lastTick;
        private TimeSpan _position;
        private TimeSpan _duration;
        private bool _isPlaying;
        private double _volume = 1.0;

        public bool IsPlaying => _isPlaying;
        public bool CanPlay => true;
        public bool IsOpen => true;
        public TimeSpan CurrentPosition => _position;
        public TimeSpan Duration => _duration;

        public event EventHandler PlaybackEnded;

        public SimulatedAudioEngine()
        {
            _timer = new Timer(OnTick, null, Timeout.Infinite, 16);
        }

        public void Open(string source)
        {
            Stop();
        }

        public Task<bool> OpenAsync(string source)
        {
            Stop();
            return Task.FromResult(true);
        }

        public void SetDuration(TimeSpan duration)
        {
            _duration = duration;
        }

        public void Play()
        {
            _isPlaying = true;
            _lastTick = DateTime.UtcNow;
            _timer.Change(0, 16);
        }

        public void Pause()
        {
            _isPlaying = false;
            _timer.Change(Timeout.Infinite, 16);
        }

        public void Stop()
        {
            _isPlaying = false;
            _timer.Change(Timeout.Infinite, 16);
            _position = TimeSpan.Zero;
        }

        public Task StopAsync()
        {
            Stop();
            return Task.CompletedTask;
        }

        public void SetVolume(double volume)
        {
            _volume = Math.Clamp(volume, 0, 1);
        }

        public void SeekTo(TimeSpan position)
        {
            _position = position;
        }

        public void Dispose()
        {
            _timer?.Dispose();
        }

        private void OnTick(object state)
        {
            if (!_isPlaying) return;
            var now = DateTime.UtcNow;
            var elapsed = now - _lastTick;
            _lastTick = now;
            _position += elapsed;
            if (_position >= _duration)
            {
                _position = _duration;
                _isPlaying = false;
                _timer.Change(Timeout.Infinite, 16);
                PlaybackEnded?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public class MusicPlayerService : INotifyPropertyChanged
    {
        private static MusicPlayerService _instance;
        private static readonly object _lock = new object();

        public static MusicPlayerService Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                        {
                            _instance = new MusicPlayerService();
                        }
                    }
                }
                return _instance;
            }
        }

        private readonly PlayQueue _playQueue;
        private IAudioEngine _audioEngine;
        private PlaybackState _playbackState = PlaybackState.Stopped;
        private double _volume = 1.0;
        private TimeSpan _currentPosition = TimeSpan.Zero;
        private TimeSpan _totalDuration = TimeSpan.Zero;
        private Lyrics _currentLyrics;
        private Timer _positionTimer;
        private DateTime _lastTickTime;
        private bool _useRealAudio;
        private volatile int _playbackGeneration;
        private string _statusMessage = string.Empty;

        public event PropertyChangedEventHandler PropertyChanged;
        public event EventHandler<Song> SongChanged;
        public event EventHandler<PlaybackState> PlaybackStateChanged;
        public event EventHandler<TimeSpan> PositionChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private MusicPlayerService()
        {
            _playQueue = new PlayQueue();
            _playQueue.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(PlayQueue.CurrentSong))
                {
                    OnPropertyChanged(nameof(CurrentSong));
                    OnPropertyChanged(nameof(HasCurrentSong));
                    SongChanged?.Invoke(this, CurrentSong);
                }
            };

            MusicSourceRegistry.Instance.InitializeDefaultProviders();

            _useRealAudio = TryCreateRealAudioEngine(out var realEngine);
            _audioEngine = _useRealAudio ? realEngine : new SimulatedAudioEngine();
            _audioEngine.PlaybackEnded += OnAudioEnginePlaybackEnded;

            _positionTimer = new Timer(OnPositionTimerTick, null, Timeout.Infinite, 16);
        }

        private static bool TryCreateRealAudioEngine(out IAudioEngine engine)
        {
            engine = null;
            try
            {
                var real = new RealAudioEngine();
                if (real.CanPlay)
                {
                    engine = real;
                    return true;
                }
                real.Dispose();
                return false;
            }
            catch
            {
                return false;
            }
        }

        public PlayQueue Queue => _playQueue;
        public Song CurrentSong => _playQueue.CurrentSong;
        public bool HasCurrentSong => _playQueue.HasCurrentSong;
        public bool UseRealAudio => _useRealAudio;

        public PlaybackState PlaybackState
        {
            get => _playbackState;
            private set
            {
                if (_playbackState != value)
                {
                    _playbackState = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsPlaying));
                    OnPropertyChanged(nameof(IsPaused));
                    OnPropertyChanged(nameof(IsStopped));
                    OnPropertyChanged(nameof(IsLoading));
                    OnPropertyChanged(nameof(IsError));
                    PlaybackStateChanged?.Invoke(this, _playbackState);
                }
            }
        }

        public bool IsPlaying => _playbackState == PlaybackState.Playing;
        public bool IsPaused => _playbackState == PlaybackState.Paused;
        public bool IsStopped => _playbackState == PlaybackState.Stopped;
        public bool IsLoading => _playbackState == PlaybackState.Loading;
        public bool IsError => _playbackState == PlaybackState.Error;

        public string StatusMessage
        {
            get => _statusMessage;
            private set
            {
                if (_statusMessage != value)
                {
                    _statusMessage = value;
                    OnPropertyChanged();
                }
            }
        }

        public double Volume
        {
            get => _volume;
            set
            {
                if (Math.Abs(_volume - value) > 0.001)
                {
                    _volume = Math.Clamp(value, 0, 1);
                    _audioEngine.SetVolume(_volume);
                    OnPropertyChanged();
                }
            }
        }

        public TimeSpan CurrentPosition
        {
            get => _currentPosition;
            private set
            {
                if (_currentPosition != value)
                {
                    _currentPosition = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(CurrentPositionText));
                    OnPropertyChanged(nameof(Progress));
                    OnPropertyChanged(nameof(CurrentLyricLineIndex));
                    PositionChanged?.Invoke(this, value);
                }
            }
        }

        public TimeSpan TotalDuration
        {
            get => _totalDuration;
            private set
            {
                if (_totalDuration != value)
                {
                    _totalDuration = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(TotalDurationText));
                    OnPropertyChanged(nameof(Progress));
                }
            }
        }

        public Lyrics CurrentLyrics
        {
            get => _currentLyrics;
            private set
            {
                if (_currentLyrics != value)
                {
                    _currentLyrics = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HasLyrics));
                    OnPropertyChanged(nameof(CurrentLyricLineIndex));
                }
            }
        }

        public bool HasLyrics => _currentLyrics != null && _currentLyrics.Lines.Count > 0;
        public int CurrentLyricLineIndex => _currentLyrics?.FindCurrentLineIndex(_currentPosition) ?? -1;
        public double Progress => _totalDuration.TotalSeconds > 0
            ? _currentPosition.TotalSeconds / _totalDuration.TotalSeconds
            : 0;

        public string CurrentPositionText => $"{(int)_currentPosition.TotalMinutes}:{_currentPosition.Seconds:D2}";
        public string TotalDurationText => $"{(int)_totalDuration.TotalMinutes}:{_totalDuration.Seconds:D2}";

        public void Play(Song song)
        {
            if (song == null) return;
            _playQueue.Clear();
            _playQueue.AddSong(song);
            _playQueue.CurrentIndex = 0;
            StartPlayback(song);
        }

        public void PlayAll(System.Collections.Generic.List<Song> songs, int startIndex = 0)
        {
            if (songs == null || songs.Count == 0) return;
            _playQueue.Clear();
            _playQueue.AddSongs(songs);
            _playQueue.CurrentIndex = startIndex;
            StartPlayback(_playQueue.CurrentSong);
        }

        public void Play()
        {
            if (CurrentSong == null) return;
            if (_playbackState == PlaybackState.Error)
            {
                StartPlayback(CurrentSong);
                return;
            }
            PlaybackState = PlaybackState.Playing;
            _audioEngine.Play();
            StartPositionTimer();
        }

        public void Pause()
        {
            if (PlaybackState != PlaybackState.Playing) return;
            PlaybackState = PlaybackState.Paused;
            _audioEngine.Pause();
            StopPositionTimer();
        }

        public void TogglePlayPause()
        {
            if (IsPlaying) Pause();
            else Play();
        }

        public void Next()
        {
            var nextIndex = _playQueue.GetNextIndex();
            if (nextIndex < 0) { Stop(); return; }
            _playQueue.CurrentIndex = nextIndex;
            StartPlayback(CurrentSong);
        }

        public void Previous()
        {
            if (_currentPosition.TotalSeconds > 3)
            {
                SeekTo(TimeSpan.Zero);
                return;
            }
            var prevIndex = _playQueue.GetPreviousIndex();
            if (prevIndex < 0) { Stop(); return; }
            _playQueue.CurrentIndex = prevIndex;
            StartPlayback(CurrentSong);
        }

        public void Stop()
        {
            PlaybackState = PlaybackState.Stopped;
            StatusMessage = string.Empty;
            _audioEngine.Stop();
            StopPositionTimer();
            CurrentPosition = TimeSpan.Zero;
        }

        public void SeekTo(TimeSpan position)
        {
            CurrentPosition = position;
            _audioEngine.SeekTo(position);
            OnPropertyChanged(nameof(CurrentLyricLineIndex));
        }

        public void SeekToProgress(double progress)
        {
            SeekTo(TimeSpan.FromSeconds(_totalDuration.TotalSeconds * Math.Clamp(progress, 0, 1)));
        }

        public void SetVolume(double volume)
        {
            Volume = volume;
        }

        public void TogglePlayMode()
        {
            var modes = (PlayMode[])Enum.GetValues(typeof(PlayMode));
            var currentIdx = Array.IndexOf(modes, _playQueue.PlayMode);
            _playQueue.PlayMode = modes[(currentIdx + 1) % modes.Length];
        }

        public void RetryPlayback()
        {
            if (CurrentSong != null && _playbackState == PlaybackState.Error)
            {
                StartPlayback(CurrentSong);
            }
        }

        private async void StartPlayback(Song song)
        {
            if (song == null) return;

            var generation = ++_playbackGeneration;

            StopPositionTimer();
            CurrentPosition = TimeSpan.Zero;
            TotalDuration = song.Duration;
            StatusMessage = string.Empty;

            _audioEngine.SetVolume(_volume);

            if (_useRealAudio)
            {
                PlaybackState = PlaybackState.Loading;
                StatusMessage = "正在获取音频源...";

                try
                {
                    await _audioEngine.StopAsync();
                }
                catch { }

                if (_playbackGeneration != generation) return;

                StartRealAudioPlaybackAsync(song, generation);
            }
            else
            {
                _audioEngine.Stop();
                if (_audioEngine is SimulatedAudioEngine sim)
                    sim.SetDuration(song.Duration);
                PlaybackState = PlaybackState.Playing;
                _audioEngine.Play();
                StartPositionTimer();
                RecordPlayHistory(song);
            }

            ParseLyrics(song);
        }

        private async void StartRealAudioPlaybackAsync(Song song, int generation)
        {
            await StartRealAudioPlaybackAsyncImpl(song, generation);
        }

        private async Task StartRealAudioPlaybackAsyncImpl(Song song, int generation)
        {
            if (song.IsLocal)
            {
                if (string.IsNullOrEmpty(song.LocalFilePath) || !System.IO.File.Exists(song.LocalFilePath))
                {
                    SetPlaybackError("本地文件不存在，将自动播放下一首");
                    return;
                }

                try
                {
                    await _audioEngine.StopAsync();
                }
                catch { }

                if (_playbackGeneration != generation) return;

                StatusMessage = "正在加载本地音频...";
                var opened = await _audioEngine.OpenAsync(song.LocalFilePath);

                if (_playbackGeneration != generation) return;

                if (opened && _audioEngine.IsOpen)
                {
                    _audioEngine.Play();
                    if (_audioEngine.Duration > TimeSpan.Zero)
                    {
                        TotalDuration = _audioEngine.Duration;
                    }
                    if (_playbackGeneration == generation)
                    {
                        PlaybackState = PlaybackState.Playing;
                        StatusMessage = string.Empty;
                        StartPositionTimer();
                        RecordPlayHistory(song);
                    }
                }
                else
                {
                    SetPlaybackError("无法加载本地音频文件");
                }
                return;
            }

            string audioUrl = null;

            try
            {
                StatusMessage = "正在获取播放链接...";
                var selector = new MusicSourceSelector();
                audioUrl = await selector.GetSongUrlWithFallback(song.Id, song, maxRetries: 3);
                if (_playbackGeneration != generation)
                {
                    return;
                }

                if (!string.IsNullOrWhiteSpace(audioUrl))
                {
                    song.AudioUrl = audioUrl;
                    SourceHealthTracker.Instance.RecordSuccess(song.Source ?? "unknown", 0);
                }
                else
                {
                    if (_playbackGeneration != generation) return;
                    StatusMessage = "暂无可用音源，将自动播放下一首";
                    SourceHealthTracker.Instance.RecordFailure(song.Source ?? "unknown", 0);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GetSongUrlAsync failed: {ex.Message}");
                if (_playbackGeneration != generation) return;
                StatusMessage = $"获取播放链接失败: {ex.Message}";
                SourceHealthTracker.Instance.RecordFailure(song.Source ?? "unknown", 0);
            }

            if (string.IsNullOrWhiteSpace(audioUrl) && !string.IsNullOrWhiteSpace(song.AudioUrl))
            {
                audioUrl = song.AudioUrl;
            }

            if (_playbackGeneration != generation)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(audioUrl))
            {
                StatusMessage = "正在加载音频...";
                var opened = await _audioEngine.OpenAsync(audioUrl);

                if (_playbackGeneration != generation)
                {
                    return;
                }

                if (opened && _audioEngine.IsOpen)
                {
                    _audioEngine.Play();

                    if (_audioEngine.Duration > TimeSpan.Zero)
                    {
                        TotalDuration = _audioEngine.Duration;
                    }

                    if (_playbackGeneration == generation)
                    {
                        PlaybackState = PlaybackState.Playing;
                        StatusMessage = string.Empty;
                        StartPositionTimer();
                        RecordPlayHistory(song);
                    }
                }
                else
                {
                    StatusMessage = "音频加载失败，正在重试...";
                    try
                    {
                        var selector = new MusicSourceSelector();
                        var freshUrl = await selector.GetSongUrlWithFallback(song.Id, song, maxRetries: 3);
                        if (_playbackGeneration != generation) return;

                        if (!string.IsNullOrWhiteSpace(freshUrl) && freshUrl != audioUrl)
                        {
                            song.AudioUrl = freshUrl;
                            SourceHealthTracker.Instance.RecordSuccess(song.Source ?? "unknown", 0);
                            var reopened = await _audioEngine.OpenAsync(freshUrl);

                            if (_playbackGeneration != generation) return;

                            if (reopened && _audioEngine.IsOpen)
                            {
                                _audioEngine.Play();
                                if (_audioEngine.Duration > TimeSpan.Zero)
                                {
                                    TotalDuration = _audioEngine.Duration;
                                }
                                if (_playbackGeneration == generation)
                                {
                                    PlaybackState = PlaybackState.Playing;
                                    StatusMessage = string.Empty;
                                    StartPositionTimer();
                                    RecordPlayHistory(song);
                                }
                            }
                            else
                            {
                                SourceHealthTracker.Instance.RecordFailure(song.Source ?? "unknown", 0);
                                SetPlaybackError("无法加载此歌曲的音频，可能暂无音源");
                            }
                        }
                        else
                        {
                            SourceHealthTracker.Instance.RecordFailure(song.Source ?? "unknown", 0);
                            SetPlaybackError("无法获取此歌曲的播放链接");
                        }
                    }
                    catch (Exception ex)
                    {
                        if (_playbackGeneration != generation) return;
                        SourceHealthTracker.Instance.RecordFailure(song.Source ?? "unknown", 0);
                        SetPlaybackError($"获取播放链接失败: {ex.InnerException?.Message ?? ex.Message}");
                    }
                }
            }
            else
            {
                SetPlaybackError("此歌曲暂无可用音源");
            }
        }

        private void SetPlaybackError(string message)
        {
            System.Diagnostics.Debug.WriteLine($"PlaybackError: {message}");
            StatusMessage = message;
            PlaybackState = PlaybackState.Error;
        }

        private async void ParseLyrics(Song song)
        {
            CurrentLyrics = null;

            if (!string.IsNullOrWhiteSpace(song?.LyricsJson))
            {
                try
                {
                    CurrentLyrics = Newtonsoft.Json.JsonConvert.DeserializeObject<Lyrics>(song.LyricsJson);
                    if (CurrentLyrics != null && CurrentLyrics.Lines.Count > 0) return;
                }
                catch { }
            }

            if (song?.Source == "netease")
            {
                try
                {
                    var (lrc, tlyric) = await NeteaseMusicApiService.Instance.GetLyricsAsync(song.Id);
                    if (!string.IsNullOrWhiteSpace(lrc))
                    {
                        CurrentLyrics = LrcParser.Parse(lrc, tlyric);
                        song.LyricsJson = LrcParser.ToJson(lrc, tlyric);
                    }
                }
                catch { }
            }
        }

        private void RecordPlayHistory(Song song)
        {
            try
            {
                var library = MusicLibraryService.Instance;
                library.EnsureSongInLibrary(song);
                library.AddSongToRecentPlaylist(song.Id);
                library.IncrementPlayCount(song.Id);
            }
            catch { }
        }

        private void StartPositionTimer()
        {
            _lastTickTime = DateTime.UtcNow;
            _positionTimer.Change(0, 16);
        }

        private void StopPositionTimer()
        {
            _positionTimer.Change(Timeout.Infinite, 16);
        }

        private void OnPositionTimerTick(object state)
        {
            try
            {
                if (_playbackState != PlaybackState.Playing) return;

                if (_useRealAudio && _audioEngine is RealAudioEngine)
                {
                    var pos = _audioEngine.CurrentPosition;
                    var dur = _audioEngine.Duration;
                    if (dur > TimeSpan.Zero && dur != _totalDuration)
                    {
                        TotalDuration = dur;
                    }
                    CurrentPosition = pos;
                    return;
                }

                var now = DateTime.UtcNow;
                var elapsed = now - _lastTickTime;
                _lastTickTime = now;

                var newPosition = _currentPosition + elapsed;
                if (newPosition >= _totalDuration)
                {
                    CurrentPosition = _totalDuration;
                    OnSongFinished();
                }
                else
                {
                    CurrentPosition = newPosition;
                }
            }
            catch { }
        }

        private void OnAudioEnginePlaybackEnded(object sender, EventArgs e)
        {
            System.Diagnostics.Debug.WriteLine("OnAudioEnginePlaybackEnded triggered");
            OnSongFinished();
        }

        private void OnSongFinished()
        {
            StopPositionTimer();

            var nextIndex = _playQueue.GetNextIndex();
            if (nextIndex < 0)
            {
                PlaybackState = PlaybackState.Stopped;
                StatusMessage = string.Empty;
                CurrentPosition = TimeSpan.Zero;
                return;
            }

            _playQueue.CurrentIndex = nextIndex;
            var nextSong = _playQueue.CurrentSong;
            System.Diagnostics.Debug.WriteLine($"OnSongFinished: switching to song '{nextSong?.Title}' at index {nextIndex}");
            StartPlayback(nextSong);
        }

        public void UpdatePosition(TimeSpan position)
        {
            CurrentPosition = position;
            OnPropertyChanged(nameof(CurrentLyricLineIndex));
        }
    }
}
