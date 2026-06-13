using System.Threading.Tasks;
using System.Windows.Input;
using Horizon.Game.GengDi.Core.Services.Database;
using Horizon.Game.GengDi.Core.Services;

namespace Horizon.Game.GengDi.Core.ViewModels
{
    public class LoginViewModel : ViewModelBase
    {
        private readonly AccountService _accountService;
        private readonly AsyncRelayCommand _loginCommand;
        private string _username;
        private string _password;
        private string _errorMessage;
        private bool _isLoading;
        private bool _rememberLogin;
        private bool _isInitialized;

        public string Username
        {
            get => _username;
            set
            {
                if (SetProperty(ref _username, value))
                {
                    _loginCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public string Password
        {
            get => _password;
            set
            {
                if (SetProperty(ref _password, value))
                {
                    _loginCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public string ErrorMessage
        {
            get => _errorMessage;
            set => SetProperty(ref _errorMessage, value);
        }

        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                if (SetProperty(ref _isLoading, value))
                {
                    _loginCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public bool RememberLogin
        {
            get => _rememberLogin;
            set => SetProperty(ref _rememberLogin, value);
        }

        public ICommand LoginCommand { get; }
        public ICommand NavigateToRegisterCommand { get; }

        public LoginViewModel()
        {
            _accountService = new AccountService();
            _loginCommand = new AsyncRelayCommand(LoginAsync, CanLogin);
            LoginCommand = _loginCommand;
            NavigateToRegisterCommand = new RelayCommand(NavigateToRegister);
        }

        public async Task InitializeAsync()
        {
            if (_isInitialized)
            {
                return;
            }

            _isInitialized = true;
            await LoadRememberedLoginAsync();
        }

        private async Task LoginAsync()
        {
            if (string.IsNullOrEmpty(Username) || string.IsNullOrEmpty(Password))
            {
                ErrorMessage = "请输入用户名和密码";
                return;
            }

            IsLoading = true;
            ErrorMessage = string.Empty;

            try
            {
                var user = await _accountService.LoginAsync(Username, Password);
                if (user != null)
                {
                    // 登录成功，保存用户信息并导航到主页面
                    App.CurrentUser = user;
                    await LocalPassportStore.SavePassportAsync(Username, Password, RememberLogin);
                    NavigateToMain();
                }
                else
                {
                    ErrorMessage = string.IsNullOrWhiteSpace(_accountService.LastErrorMessage)
                        ? "用户名或密码错误"
                        : _accountService.LastErrorMessage;
                }
            }
            catch (System.Exception ex)
            {
                ErrorMessage = "登录失败：" + ex.Message;
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void NavigateToRegister()
        {
            NavigationService.Instance.NavigateToRegister();
        }

        private void NavigateToMain()
        {
            NavigationService.Instance.NavigateToMain();
        }

        private async Task LoadRememberedLoginAsync()
        {
            var result = await LocalPassportStore.TryLoadPassportAsync();
            if (result.Success)
            {
                Username = result.PassportId;
                Password = result.Password;
                RememberLogin = true;
            }
        }

        private bool CanLogin()
        {
            return !IsLoading && !string.IsNullOrWhiteSpace(Username) && !string.IsNullOrWhiteSpace(Password);
        }
    }
}
