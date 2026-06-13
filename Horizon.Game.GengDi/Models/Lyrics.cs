using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Horizon.Game.GengDi.Models
{
    public class LyricLine
    {
        public TimeSpan Timestamp { get; set; }
        public string Text { get; set; }

        public string TimestampText => $"{(int)Timestamp.TotalMinutes}:{Timestamp.Seconds:D2}.{Timestamp.Milliseconds / 10:D2}";
    }

    public class DisplayLyricLine : INotifyPropertyChanged
    {
        public TimeSpan Timestamp { get; set; }
        public string Text { get; set; }

        private bool _isCurrent;
        public bool IsCurrent
        {
            get => _isCurrent;
            set { if (_isCurrent != value) { _isCurrent = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsNotCurrent)); } }
        }

        public bool IsNotCurrent => !_isCurrent;

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class Lyrics
    {
        public List<LyricLine> Lines { get; set; } = new List<LyricLine>();

        public int FindCurrentLineIndex(TimeSpan currentPosition)
        {
            if (Lines == null || Lines.Count == 0) return -1;

            for (int i = Lines.Count - 1; i >= 0; i--)
            {
                if (Lines[i].Timestamp <= currentPosition)
                    return i;
            }
            return -1;
        }

        public List<DisplayLyricLine> ToDisplayLines()
        {
            var result = new List<DisplayLyricLine>();
            if (Lines == null) return result;
            foreach (var line in Lines)
            {
                result.Add(new DisplayLyricLine
                {
                    Timestamp = line.Timestamp,
                    Text = line.Text
                });
            }
            return result;
        }
    }
}
