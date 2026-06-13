using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Horizon.Game.GengDi.Core.ViewModels;

namespace Horizon.Game.GengDi.Core.Views
{
    public partial class MiniPlayerView : UserControl
    {
        public MiniPlayerView()
        {
            InitializeComponent();
            DataContext = new MiniPlayerViewModel();
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                if (DataContext is MiniPlayerViewModel vm)
                {
                    vm.RequestExpand();
                }
            }
        }

        private void OnControlButtonPressed(object sender, PointerPressedEventArgs e)
        {
            e.Handled = true;
        }
    }
}
