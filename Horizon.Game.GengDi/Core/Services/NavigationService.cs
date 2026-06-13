using System;
using Avalonia.Controls;
using Horizon.Game.GengDi.Core.Views;

namespace Horizon.Game.GengDi.Core.Services
{
    public class NavigationService
    {
        private static NavigationService _instance;

        public NavigationService() { }

        public static NavigationService Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = new NavigationService();
                }
                return _instance;
            }
        }

        public void NavigateTo<TView>(Window window) where TView : UserControl
        {
            var view = Activator.CreateInstance<TView>();
            window.Content = view;
        }

        public void NavigateTo<TView, TViewModel>(Window window, TViewModel viewModel) where TView : UserControl
        {
            var view = Activator.CreateInstance<TView>();
            view.DataContext = viewModel;
            window.Content = view;
        }

        public void NavigateToLogin()
        {
            var loginView = new LoginView();
            App.MainWindow.Content = loginView;
            App.MainWindow.Title = "登录";
        }

        public void NavigateToRegister()
        {
            var registerView = new RegisterView();
            App.MainWindow.Content = registerView;
            App.MainWindow.Title = "注册";
        }

        public void NavigateToMain()
        {
            App.MainWindow.Content = App.CreateMainShell();
            App.MainWindow.Title = "Horizon Game GengDi";
        }

        public void NavigateToProfile()
        {
            if (!TryNavigateInShell("Profile"))
            {
                App.MainWindow.Content = App.CreateMainShell("Profile");
            }
            App.MainWindow.Title = "个人设置";
        }

        public void NavigateToSecurity()
        {
            if (!TryNavigateInShell("Security"))
            {
                App.MainWindow.Content = App.CreateMainShell("Security");
            }
            App.MainWindow.Title = "安全设置";
        }

        private static bool TryNavigateInShell(string tag)
        {
            if (App.MainWindow?.Content is MainView mainView && mainView.DataContext is Core.ViewModels.MainViewModel viewModel)
            {
                return viewModel.NavigateTo(tag);
            }

            return false;
        }
    }
}
