using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Horizon.Game.GengDi.Core.ViewModels;

namespace Horizon.Game.GengDi.Core.Views
{
    public partial class FlowerAlertCenterView : UserControl
    {
        public FlowerAlertCenterView()
        {
            InitializeComponent();
            DataContext = new FlowerAlertCenterViewModel();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void OnSpeciesFilterClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tag && int.TryParse(tag, out var speciesId))
            {
                if (DataContext is FlowerAlertCenterViewModel vm)
                    vm.SelectedSpeciesFilter = speciesId;

                UpdateFilterButtonStyle(btn);
            }
        }

        private static void UpdateFilterButtonStyle(Button selectedBtn)
        {
            if (selectedBtn.Parent is StackPanel panel)
            {
                foreach (var child in panel.Children)
                {
                    if (child is Button childBtn)
                    {
                        childBtn.Classes.Remove("PrimaryAction");
                        if (!childBtn.Classes.Contains("QuietAction"))
                            childBtn.Classes.Add("QuietAction");
                    }
                }
                selectedBtn.Classes.Remove("QuietAction");
                if (!selectedBtn.Classes.Contains("PrimaryAction"))
                    selectedBtn.Classes.Add("PrimaryAction");
            }
        }
    }
}
