using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Horizon.Game.GengDi.Models;

namespace Horizon.Game.GengDi.Core.ViewModels
{
    public class FriendGroupItem : INotifyPropertyChanged
    {
        private bool _isExpanded = true;
        private string _groupName = string.Empty;

        public event PropertyChangedEventHandler PropertyChanged;

        public bool IsDefault => string.IsNullOrEmpty(_groupName);

        public string GroupName
        {
            get => _groupName;
            set
            {
                if (string.Equals(_groupName, value, StringComparison.Ordinal)) return;
                _groupName = value ?? string.Empty;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DisplayName));
                OnPropertyChanged(nameof(IsDefault));
            }
        }

        public string DisplayName => string.IsNullOrEmpty(_groupName) ? "默认分组" : _groupName;

        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded == value) return;
                _isExpanded = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ToggleSymbol));
            }
        }

        public string ToggleSymbol => _isExpanded ? "▼" : "▶";

        public int SortOrder { get; set; }

        public ObservableCollection<User> Friends { get; } = new();

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
