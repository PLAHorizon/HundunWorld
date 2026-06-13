using System.Threading.Tasks;
using System.Windows.Input;
using Horizon.Game.GengDi.Core.Services;

namespace Horizon.Game.GengDi.Core.ViewModels
{
    public class RegisterViewModel : ViewModelBase
    {
        private readonly AccountService _accountService;
        private readonly AsyncRelayCommand _registerCommand;
        private string _username;
        private string _email;
        private string _password;
        private string _confirmPassword;
        private string _errorMessage;
        private bool _isLoading;

        public string Username
        {
            get => _username;
            set
            {
                if (SetProperty(ref _username, value))
                {
                    _registerCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public string Email
        {
            get => _email;
            set
            {
                if (SetProperty(ref _email, value))
                {
                    _registerCommand.RaiseCanExecuteChanged();
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
                    _registerCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public string ConfirmPassword
        {
            get => _confirmPassword;
            set
            {
                if (SetProperty(ref _confirmPassword, value))
                {
                    _registerCommand.RaiseCanExecuteChanged();
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
                    _registerCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public ICommand RegisterCommand { get; }
        public ICommand NavigateToLoginCommand { get; }

        public RegisterViewModel()
        {
            _accountService = new AccountService();
            _registerCommand = new AsyncRelayCommand(RegisterAsync, CanRegister);
            RegisterCommand = _registerCommand;
            NavigateToLoginCommand = new RelayCommand(NavigateToLogin);
        }

        private async Task RegisterAsync()
        {
            if (string.IsNullOrEmpty(Username) || string.IsNullOrEmpty(Email) || string.IsNullOrEmpty(Password))
            {
                ErrorMessage = "请填写所有必填字段";
                return;
            }

            if (Password != ConfirmPassword)
            {
                ErrorMessage = "两次输入的密码不一致";
                return;
            }

            if (Password.Length < 6)
            {
                ErrorMessage = "密码长度至少为6位";
                return;
            }

            IsLoading = true;
            ErrorMessage = string.Empty;

            try
            {
                var user = await _accountService.RegisterAsync(Username, Email, Password);
                if (user != null)
                {
                    // 注册成功，自动登录并导航到主页面
                    App.CurrentUser = user;
                    NavigateToMain();
                }
                else
                {
                    ErrorMessage = "用户名或邮箱已存在";
                }
            }
            catch (System.Exception ex)
            {
                ErrorMessage = "注册失败：" + ex.Message;
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void NavigateToLogin()
        {
            NavigationService.Instance.NavigateToLogin();
        }

        private void NavigateToMain()
        {
            NavigationService.Instance.NavigateToMain();
        }

        private bool CanRegister()
        {
            return !IsLoading
                && !string.IsNullOrWhiteSpace(Username)
                && !string.IsNullOrWhiteSpace(Email)
                && !string.IsNullOrWhiteSpace(Password)
                && !string.IsNullOrWhiteSpace(ConfirmPassword);
        }
    }
}
