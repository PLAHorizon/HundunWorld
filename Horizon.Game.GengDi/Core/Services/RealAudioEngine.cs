using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using NAudio.Wave;

namespace Horizon.Game.GengDi.Core.Services
{
    public class RealAudioEngine : IAudioEngine
    {
        private IWavePlayer _wavePlayer;
        private WaveStream _waveStream;
        private VolumeWaveProvider16 _volumeProvider;
        private volatile bool _isPlaying;
        private bool _disposed;
        private double _volume = 1.0;
        private TimeSpan _duration;
        private string _currentSource;
        private volatile bool _suppressPlaybackEnded;
        private readonly object _syncLock = new object();

        public bool IsPlaying => _isPlaying;
        public bool CanPlay => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        public bool IsOpen
        {
            get
            {
                lock (_syncLock) { return _waveStream != null && !_disposed; }
            }
        }

        public TimeSpan CurrentPosition
        {
            get
            {
                try { return _waveStream?.CurrentTime ?? TimeSpan.Zero; }
                catch { return TimeSpan.Zero; }
            }
        }
        public TimeSpan Duration
        {
            get
            {
                try { return _waveStream?.TotalTime ?? _duration; }
                catch { return _duration; }
            }
        }

        public event EventHandler PlaybackEnded;

        public RealAudioEngine()
        {
            if (!CanPlay) return;
        }

        private IWavePlayer CreateWavePlayer()
        {
            var player = new WaveOutEvent();
            player.PlaybackStopped += OnPlaybackStopped;
            return player;
        }

        public void Open(string source)
        {
            if (!CanPlay || _disposed) return;
            OpenSync(source);
        }

        public async Task<bool> OpenAsync(string source)
        {
            if (!CanPlay || _disposed) return false;
            return await Task.Run(() => OpenSync(source));
        }

        private bool OpenSync(string source)
        {
            lock (_syncLock)
            {
                _suppressPlaybackEnded = true;
                _isPlaying = false;

                if (_wavePlayer != null)
                {
                    try { _wavePlayer.Stop(); } catch { }
                    try { _wavePlayer.Dispose(); } catch { }
                    _wavePlayer = null;
                }

                _currentSource = source;

                try
                {
                    _volumeProvider = null;
                    _waveStream?.Dispose();
                    _waveStream = null;

                    if (!string.IsNullOrWhiteSpace(source))
                    {
                        _waveStream = new MediaFoundationReader(source);
                        _duration = _waveStream.TotalTime;
                        _volumeProvider = new VolumeWaveProvider16(_waveStream) { Volume = (float)_volume };
                        _wavePlayer = CreateWavePlayer();
                        _wavePlayer.Init(_volumeProvider);
                        System.Diagnostics.Debug.WriteLine($"RealAudioEngine Open OK: {source.Substring(0, Math.Min(80, source.Length))}...");
                        return true;
                    }
                    return false;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"RealAudioEngine Open failed: {ex.Message}");
                    _volumeProvider = null;
                    _waveStream?.Dispose();
                    _waveStream = null;
                    try { _wavePlayer?.Dispose(); } catch { }
                    _wavePlayer = null;
                    return false;
                }
            }
        }

        public void Play()
        {
            if (!CanPlay || _disposed) return;

            lock (_syncLock)
            {
                if (_wavePlayer == null || _waveStream == null) return;

                try
                {
                    _suppressPlaybackEnded = false;
                    _wavePlayer.Play();
                    _isPlaying = true;
                    System.Diagnostics.Debug.WriteLine("RealAudioEngine Play OK");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"RealAudioEngine Play failed: {ex.Message}");
                    try { _wavePlayer.Dispose(); } catch { }
                    _wavePlayer = null;

                    try
                    {
                        _wavePlayer = CreateWavePlayer();
                        _volumeProvider = new VolumeWaveProvider16(_waveStream) { Volume = (float)_volume };
                        _wavePlayer.Init(_volumeProvider);
                        _suppressPlaybackEnded = false;
                        _wavePlayer.Play();
                        _isPlaying = true;
                        System.Diagnostics.Debug.WriteLine("RealAudioEngine Play recovery OK");
                    }
                    catch (Exception ex2)
                    {
                        System.Diagnostics.Debug.WriteLine($"RealAudioEngine Play recovery failed: {ex2.Message}");
                    }
                }
            }
        }

        public void Pause()
        {
            if (!CanPlay || _disposed) return;

            lock (_syncLock)
            {
                if (_wavePlayer == null) return;
                try
                {
                    _wavePlayer.Pause();
                    _isPlaying = false;
                }
                catch { }
            }
        }

        public void Stop()
        {
            if (!CanPlay || _disposed) return;

            lock (_syncLock)
            {
                _suppressPlaybackEnded = true;
                _isPlaying = false;
                if (_wavePlayer != null)
                {
                    try { _wavePlayer.Stop(); } catch { }
                }
                if (_waveStream != null)
                {
                    try { _waveStream.Position = 0; } catch { }
                }
            }
        }

        public async Task StopAsync()
        {
            if (!CanPlay || _disposed) return;
            await Task.Run(() =>
            {
                lock (_syncLock)
                {
                    _suppressPlaybackEnded = true;
                    _isPlaying = false;
                    if (_wavePlayer != null)
                    {
                        try { _wavePlayer.Stop(); } catch { }
                    }
                    if (_waveStream != null)
                    {
                        try { _waveStream.Position = 0; } catch { }
                    }
                }
            });
        }

        public void SetVolume(double volume)
        {
            _volume = Math.Clamp((float)volume, 0f, 1f);
            if (_volumeProvider != null)
            {
                _volumeProvider.Volume = (float)_volume;
            }
        }

        public void SeekTo(TimeSpan position)
        {
            if (_waveStream == null) return;
            try
            {
                _waveStream.CurrentTime = position;
            }
            catch { }
        }

        private void OnPlaybackStopped(object sender, StoppedEventArgs e)
        {
            _isPlaying = false;
            if (_suppressPlaybackEnded) return;

            if (e.Exception != null)
            {
                System.Diagnostics.Debug.WriteLine($"RealAudioEngine PlaybackStopped with error: {e.Exception.Message}");
                return;
            }

            lock (_syncLock)
            {
                if (_waveStream != null)
                {
                    try
                    {
                        var pos = _waveStream.Position;
                        var len = _waveStream.Length;
                        if (len > 0 && pos >= len - 2000)
                        {
                            System.Diagnostics.Debug.WriteLine("RealAudioEngine: Song naturally ended");
                            PlaybackEnded?.Invoke(this, EventArgs.Empty);
                        }
                    }
                    catch { }
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _isPlaying = false;
            _suppressPlaybackEnded = true;

            lock (_syncLock)
            {
                try
                {
                    _wavePlayer?.Stop();
                    _wavePlayer?.Dispose();
                    _waveStream?.Dispose();
                }
                catch { }
                _wavePlayer = null;
                _waveStream = null;
                _volumeProvider = null;
            }
        }
    }
}
