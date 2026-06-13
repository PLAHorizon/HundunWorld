using System.Threading.Tasks;
using System.Windows.Input;
using Horizon.Game.GengDi.Core.Services;

namespace Horizon.Game.GengDi.Core.ViewModels
{
    public class SecurityViewModel : ViewModelBase
    {
        private readonly AccountService _accountService;
        private readonly AsyncRelayCommand _changePasswordCommand;
        private string _oldPassword;
        private string _newPassword;
        private string _confirmPassword;
        private string _message;
        private bool _isLoading;

        public string OldPassword
        {
            get => _oldPassword;
            set
            {
                if (SetProperty(ref _oldPassword, value))
                {
                    _changePasswordCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public string NewPassword
        {
            get => _newPassword;
            set
            {
                if (SetProperty(ref _newPassword, value))
                {
                    _changePasswordCommand.RaiseCanExecuteChanged();
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
                    _changePasswordCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public string Message
        {
            get => _message;
            set => SetProperty(ref _message, value);
        }

        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                if (SetProperty(ref _isLoading, value))
                {
                    _changePasswordCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public ICommand ChangePasswordCommand { get; }
        public ICommand NavigateToProfileCommand { get; }

        public SecurityViewModel()
        {
            _accountService = new AccountService();
            _changePasswordCommand = new AsyncRelayCommand(ChangePasswordAsync, CanChangePassword);
            ChangePasswordCommand = _changePasswordCommand;
            NavigateToProfileCommand = new RelayCommand(NavigateToProfile);
        }

        private async Task ChangePasswordAsync()
        {
            if (App.CurrentUser == null)
            {
                Message = "请先登录";
                return;
            }

            if (string.IsNullOrEmpty(OldPassword) || string.IsNullOrEmpty(NewPassword))
            {
                Message = "请填写所有必填字段";
                return;
            }

            if (NewPassword != ConfirmPassword)
            {
                Message = "两次输入的新密码不一致";
                return;
            }

            if (NewPassword.Length < 6)
            {
                Message = "新密码长度至少为6位";
                return;
            }

            IsLoading = true;
            Message = string.Empty;

            try
            {
                var success = await _accountService.ChangePasswordAsync(App.CurrentUser.Id, OldPassword, NewPassword);
                if (success)
                {
                    Message = "密码修改成功";
                    // 清空输入框
                    OldPassword = string.Empty;
                    NewPassword = string.Empty;
                    ConfirmPassword = string.Empty;
                }
                else
                {
                    Message = "原密码错误，修改失败";
                }
            }
            catch (System.Exception ex)
            {
                Message = "修改失败：" + ex.Message;
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void NavigateToProfile()
        {
            NavigationService.Instance.NavigateToProfile();
        }

        private bool CanChangePassword()
        {
            return !IsLoading
                && App.CurrentUser != null
                && !string.IsNullOrWhiteSpace(OldPassword)
                && !string.IsNullOrWhiteSpace(NewPassword)
                && !string.IsNullOrWhiteSpace(ConfirmPassword);
        }
    }
}
