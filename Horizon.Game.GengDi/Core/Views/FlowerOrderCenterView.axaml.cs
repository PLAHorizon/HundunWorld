using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Horizon.Game.GengDi.Core.Controls;
using Horizon.Game.GengDi.Core.ViewModels;

namespace Horizon.Game.GengDi.Core.Views
{
    public partial class FlowerOrderCenterView : UserControl
    {
        private ReviewDialog _reviewDialog;

        public FlowerOrderCenterView()
        {
            InitializeComponent();
            DataContext = new FlowerOrderCenterViewModel();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void OnStatusFilterClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string tag && int.TryParse(tag, out var status))
            {
                if (DataContext is FlowerOrderCenterViewModel vm)
                    vm.SelectedStatusFilter = status;

                if (btn.Parent is StackPanel panel)
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
                    btn.Classes.Remove("QuietAction");
                    if (!btn.Classes.Contains("PrimaryAction"))
                        btn.Classes.Add("PrimaryAction");
                }
            }
        }

        private async void OnReviewClick(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is long orderId)
            {
                if (DataContext is FlowerOrderCenterViewModel vm)
                {
                    var reviewVm = new ReviewOrderViewModel();
                    reviewVm.Initialize(orderId, vm.UserId, 0);
                    _reviewDialog = new ReviewDialog(reviewVm);
                    var result = await _reviewDialog.ShowDialog();
                    if (result)
                        await vm.LoadOrdersAsync();
                }
            }
        }
    }
}
