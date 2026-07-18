using Avalonia.Controls;
using Avalonia.Interactivity;
using Horizon.Game.GengDi.Core.ViewModels;

namespace Horizon.Game.GengDi.Core.Views
{
    public partial class LoginView : UserControl
    {
        private readonly LoginDashboardViewModel _dashboardViewModel;

        public LoginView()
        {
            InitializeComponent();
            _dashboardViewModel = new LoginDashboardViewModel();
            DataContext = _dashboardViewModel;
            Loaded += LoginView_Loaded;
        }

        private async void LoginView_Loaded(object sender, RoutedEventArgs e)
        {
            Loaded -= LoginView_Loaded;
            await _dashboardViewModel.LoginVm.InitializeAsync();
            await _dashboardViewModel.LoadDashboardData();
        }
    }
}
