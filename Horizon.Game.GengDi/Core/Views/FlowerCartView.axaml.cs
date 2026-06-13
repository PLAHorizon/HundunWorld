using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Horizon.Game.GengDi.Core.ViewModels;

namespace Horizon.Game.GengDi.Core.Views
{
    public partial class FlowerCartView : UserControl
    {
        public FlowerCartView()
        {
            InitializeComponent();
            var userId = Guid.Empty;
            if (App.CurrentUser != null && Guid.TryParse(App.CurrentUser.PassportId, out var pid))
                userId = pid;
            DataContext = new FlowerCartViewModel(userId);
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}
