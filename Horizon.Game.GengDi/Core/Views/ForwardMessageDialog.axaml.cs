using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Horizon.Game.GengDi.Models;

namespace Horizon.Game.GengDi.Core.Views
{
    public class ForwardTarget
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string SubTitle { get; set; }
        public string Initial => string.IsNullOrWhiteSpace(Name) ? "?" : Name[..1];
        public bool IsGroup { get; set; }
    }

    public partial class ForwardMessageDialog : Window
    {
        private readonly List<ForwardTarget> _allTargets;

        public ForwardMessageDialog(IEnumerable<User> friends, IEnumerable<Group> groups)
        {
            InitializeComponent();

            _allTargets = friends
                .Select(f => new ForwardTarget
                {
                    Id = f.PassportId ?? f.Id,
                    Name = f.Username ?? f.Id,
                    SubTitle = f.IsAvailable ? "在线" : "离线",
                    IsGroup = false
                })
                .Concat(groups.Select(g => new ForwardTarget
                {
                    Id = g.Id,
                    Name = g.Name ?? g.Id,
                    SubTitle = "群组",
                    IsGroup = true
                }))
                .ToList();

            ContactList.ItemsSource = _allTargets;
            SearchBox.TextChanged += SearchBox_TextChanged;
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var query = SearchBox.Text?.Trim() ?? string.Empty;
            ContactList.ItemsSource = string.IsNullOrEmpty(query)
                ? _allTargets
                : _allTargets.Where(t => t.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        private void ForwardConfirmButton_Click(object sender, RoutedEventArgs e)
        {
            Close(ContactList.SelectedItem as ForwardTarget);
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            Close(null);
        }
    }
}
