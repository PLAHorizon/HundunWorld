using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Styling;
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

            var anim = new Animation
            {
                Duration = TimeSpan.FromMilliseconds(600),
                Delay = TimeSpan.FromMilliseconds(300),
                FillMode = FillMode.Forward,
                Children =
                {
                    new KeyFrame
                    {
                        Cue = new Cue(0d),
                        Setters = { new Setter(OpacityProperty, 0d) }
                    },
                    new KeyFrame
                    {
                        Cue = new Cue(1d),
                        Setters = { new Setter(OpacityProperty, 1d) }
                    }
                }
            };
            anim.RunAsync(LoginSubtitleText);
        }
    }
}
