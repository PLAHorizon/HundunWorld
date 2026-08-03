using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Media;
using Horizon.Game.GengDi.Core.Services;

namespace Horizon.Game.GengDi.Core.ViewModels
{
    public class ProfileViewModel : ViewModelBase
    {
        private readonly AccountService _accountService;
        private readonly AsyncRelayCommand _updateBasicInfoCommand;
        private readonly AsyncRelayCommand _updateTitleCommand;
        private readonly AsyncRelayCommand _updateContactInfoCommand;
        private readonly AsyncRelayCommand _updateRealNameCommand;

        // 基本信息
        private string _nickName;
        private string _bio;
        private string _avatar;
        private string _passportId;
        private string _gender;
        private DateTime? _birthday;
        private string _province;
        private string _city;

        // 头衔
        private string _title;

        // 账号绑定
        private string _phone;
        private string _email;

        // 实名认证
        private string _realName;
        private string _idCard;

        // 状态消息（每个区域独立）
        private string _basicInfoMessage;
        private string _titleMessage;
        private string _contactInfoMessage;
        private string _realNameMessage;

        // 状态消息颜色（成功为绿色，失败为红色）
        private static readonly IBrush SuccessBrush = Brushes.SeaGreen;
        private static readonly IBrush ErrorBrush = Brushes.OrangeRed;

        // 重置用原始值快照
        private string _origNickName;
        private string _origBio;
        private string _origAvatar;
        private string _origGender;
        private DateTime? _origBirthday;
        private string _origProvince;
        private string _origCity;
        private string _origEmail;
        private string _origPhone;

        private IBrush _basicInfoMessageBrush = SuccessBrush;
        private IBrush _titleMessageBrush = SuccessBrush;
        private IBrush _contactInfoMessageBrush = SuccessBrush;
        private IBrush _realNameMessageBrush = SuccessBrush;

        // 头像加载完成标志（用于控制 XAML 中图片控件的可见性）
        private bool _avatarIsLoaded;

        /// <summary>
        /// 头像 Bitmap 已就绪时为 true，用于 AXAML IsVisible 绑定。
        /// </summary>
        public bool AvatarIsLoaded
        {
            get => _avatarIsLoaded;
            set => SetProperty(ref _avatarIsLoaded, value);
        }

        private bool _isLoading;
        private bool _isInitialized;

        #region 通行证信息（只读展示）

        /// <summary>
        /// 通行证号（只读）
        /// </summary>
        public string PassportId
        {
            get => _passportId;
            private set => SetProperty(ref _passportId, value);
        }

        #endregion

        #region 基本信息

        public string NickName
        {
            get => _nickName;
            set
            {
                if (SetProperty(ref _nickName, value))
                {
                    _updateBasicInfoCommand.RaiseCanExecuteChanged();
                    OnPropertyChanged(nameof(NickNameInitial));
                }
            }
        }

        public string Bio
        {
            get => _bio;
            set
            {
                if (SetProperty(ref _bio, value))
                {
                    _updateBasicInfoCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public string Avatar
        {
            get => _avatar;
            set
            {
                if (SetProperty(ref _avatar, value))
                {
                    _updateBasicInfoCommand.RaiseCanExecuteChanged();
                }
            }
        }

        /// <summary>
        /// 昵称首字母，用于头像未设置时的占位显示。
        /// </summary>
        public string NickNameInitial
        {
            get
            {
                var trimmed = NickName?.Trim();
                return string.IsNullOrEmpty(trimmed) ? "?" : trimmed[0].ToString().ToUpperInvariant();
            }
        }

        public string BasicInfoMessage
        {
            get => _basicInfoMessage;
            set => SetProperty(ref _basicInfoMessage, value);
        }

        public IBrush BasicInfoMessageBrush
        {
            get => _basicInfoMessageBrush;
            private set => SetProperty(ref _basicInfoMessageBrush, value);
        }

        /// <summary>性别（"男"/"女"/"保密"）</summary>
        public string Gender
        {
            get => _gender;
            set
            {
                if (SetProperty(ref _gender, value))
                {
                    OnPropertyChanged(nameof(IsMale));
                    OnPropertyChanged(nameof(IsFemale));
                    OnPropertyChanged(nameof(IsSecret));
                    _updateBasicInfoCommand.RaiseCanExecuteChanged();
                }
            }
        }

        /// <summary>生日</summary>
        public DateTime? Birthday
        {
            get => _birthday;
            set
            {
                if (SetProperty(ref _birthday, value))
                    _updateBasicInfoCommand.RaiseCanExecuteChanged();
            }
        }

        /// <summary>所在省份</summary>
        public string Province
        {
            get => _province;
            set
            {
                if (SetProperty(ref _province, value))
                {
                    OnPropertyChanged(nameof(CityOptions));
                    // 省份变化后，若当前城市不在新选项中则清空
                    if (CityOptions != null && Array.IndexOf(CityOptions, City) < 0)
                        City = null;
                    _updateBasicInfoCommand.RaiseCanExecuteChanged();
                }
            }
        }

        /// <summary>所在城市</summary>
        public string City
        {
            get => _city;
            set
            {
                if (SetProperty(ref _city, value))
                    _updateBasicInfoCommand.RaiseCanExecuteChanged();
            }
        }

        /// <summary>性别"男"是否选中（支持双向绑定）</summary>
        public bool IsMale
        {
            get => Gender == "男";
            set { if (value) Gender = "男"; }
        }
        /// <summary>性别"女"是否选中（支持双向绑定）</summary>
        public bool IsFemale
        {
            get => Gender == "女";
            set { if (value) Gender = "女"; }
        }
        /// <summary>性别"保密"是否选中（支持双向绑定）</summary>
        public bool IsSecret
        {
            get => Gender == "保密";
            set { if (value) Gender = "保密"; }
        }

        /// <summary>省份选项列表</summary>
        public string[] ProvinceOptions { get; } = { "北京市", "上海市", "广东省", "浙江省", "江苏省", "河南省", "四川省", "湖北省", "湖南省", "福建省" };

        /// <summary>城市选项列表（根据省份动态变化）</summary>
        public string[] CityOptions => Province switch
        {
            "北京市" => new[] { "东城区", "西城区", "海淀区", "朝阳区", "丰台区" },
            "上海市" => new[] { "黄浦区", "徐汇区", "浦东新区", "静安区", "长宁区" },
            "广东省" => new[] { "深圳市", "广州市", "珠海市", "佛山市", "东莞市" },
            "浙江省" => new[] { "杭州市", "宁波市", "温州市", "嘉兴市", "绍兴市" },
            "江苏省" => new[] { "南京市", "苏州市", "无锡市", "常州市", "南通市" },
            "河南省" => new[] { "郑州市", "洛阳市", "开封市", "新乡市", "南阳市" },
            "四川省" => new[] { "成都市", "绵阳市", "德阳市", "宜宾市", "南充市" },
            "湖北省" => new[] { "武汉市", "宜昌市", "襄阳市", "荆州市", "黄冈市" },
            "湖南省" => new[] { "长沙市", "株洲市", "湘潭市", "衡阳市", "岳阳市" },
            "福建省" => new[] { "福州市", "厦门市", "泉州市", "漳州市", "莆田市" },
            _ => new[] { "—" }
        };

        #endregion

        #region 头衔

        public string Title
        {
            get => _title;
            set
            {
                if (SetProperty(ref _title, value))
                {
                    _updateTitleCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public string TitleMessage
        {
            get => _titleMessage;
            set => SetProperty(ref _titleMessage, value);
        }

        public IBrush TitleMessageBrush
        {
            get => _titleMessageBrush;
            private set => SetProperty(ref _titleMessageBrush, value);
        }

        #endregion

        #region 账号绑定

        public string Phone
        {
            get => _phone;
            set
            {
                if (SetProperty(ref _phone, value))
                {
                    _updateContactInfoCommand.RaiseCanExecuteChanged();
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
                    _updateContactInfoCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public string ContactInfoMessage
        {
            get => _contactInfoMessage;
            set => SetProperty(ref _contactInfoMessage, value);
        }

        public IBrush ContactInfoMessageBrush
        {
            get => _contactInfoMessageBrush;
            private set => SetProperty(ref _contactInfoMessageBrush, value);
        }

        #endregion

        #region 实名认证

        public string RealName
        {
            get => _realName;
            set
            {
                if (SetProperty(ref _realName, value))
                {
                    _updateRealNameCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public string IdCard
        {
            get => _idCard;
            set
            {
                if (SetProperty(ref _idCard, value))
                {
                    _updateRealNameCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public string RealNameMessage
        {
            get => _realNameMessage;
            set => SetProperty(ref _realNameMessage, value);
        }

        public IBrush RealNameMessageBrush
        {
            get => _realNameMessageBrush;
            private set => SetProperty(ref _realNameMessageBrush, value);
        }

        #endregion

        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                if (SetProperty(ref _isLoading, value))
                {
                    _updateBasicInfoCommand.RaiseCanExecuteChanged();
                    _updateTitleCommand.RaiseCanExecuteChanged();
                    _updateContactInfoCommand.RaiseCanExecuteChanged();
                    _updateRealNameCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public ICommand UpdateBasicInfoCommand { get; }
        public ICommand UpdateTitleCommand { get; }
        public ICommand UpdateContactInfoCommand { get; }
        public ICommand UpdateRealNameCommand { get; }
        public ICommand NavigateToSecurityCommand { get; }
        public ICommand LogoutCommand { get; }
        public ICommand RemoveAvatarCommand { get; }
        public ICommand ResetCommand { get; }

        private ICommand _pickAvatarCommand;

        /// <summary>
        /// 触发头像文件选择流程（由 View 层提供实际的文件选择回调）。
        /// 绑定命令，View 层通过设置 <see cref="PickAvatarCommand"/> 来实现平台文件选择器。
        /// </summary>
        public ICommand PickAvatarCommand
        {
            get => _pickAvatarCommand;
            set => SetProperty(ref _pickAvatarCommand, value);
        }

        public ProfileViewModel()
        {
            _accountService = new AccountService();
            _updateBasicInfoCommand = new AsyncRelayCommand(UpdateBasicInfoAsync, CanUpdate);
            _updateTitleCommand = new AsyncRelayCommand(UpdateTitleAsync, CanUpdate);
            _updateContactInfoCommand = new AsyncRelayCommand(UpdateContactInfoAsync, CanUpdate);
            _updateRealNameCommand = new AsyncRelayCommand(UpdateRealNameAsync, CanUpdate);

            UpdateBasicInfoCommand = _updateBasicInfoCommand;
            UpdateTitleCommand = _updateTitleCommand;
            UpdateContactInfoCommand = _updateContactInfoCommand;
            UpdateRealNameCommand = _updateRealNameCommand;
            NavigateToSecurityCommand = new RelayCommand(NavigateToSecurity);
            LogoutCommand = new RelayCommand(Logout);
            RemoveAvatarCommand = new RelayCommand(RemoveAvatar);
            ResetCommand = new RelayCommand(Reset);
        }

        public async Task InitializeAsync()
        {
            if (_isInitialized)
            {
                return;
            }

            _isInitialized = true;
            await LoadUserProfileAsync();
        }

        private async Task LoadUserProfileAsync()
        {
            if (App.CurrentUser == null)
            {
                return;
            }

            IsLoading = true;
            try
            {
                var user = await _accountService.GetUserByIdAsync(App.CurrentUser.Id);
                if (user != null)
                {
                    PassportId = user.PassportId ?? string.Empty;
                    NickName = user.Username ?? string.Empty;
                    Bio = user.Bio ?? string.Empty;
                    Avatar = user.Avatar ?? string.Empty;
                    Gender = user.Gender ?? string.Empty;
                    Birthday = user.Birthday;
                    Province = user.Province ?? string.Empty;
                    City = user.City ?? string.Empty;
                    Title = user.Title ?? string.Empty;
                    Phone = user.Phone ?? string.Empty;
                    Email = user.Email ?? string.Empty;
                    RealName = user.RealName ?? string.Empty;
                    IdCard = user.IdCard ?? string.Empty;

                    // 保存原始快照用于重置
                    SnapshotOriginalValues();
                }
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task UpdateBasicInfoAsync()
        {
            if (App.CurrentUser == null)
            {
                BasicInfoMessage = "请先登录";
                BasicInfoMessageBrush = ErrorBrush;
                return;
            }

            if (string.IsNullOrWhiteSpace(NickName))
            {
                BasicInfoMessage = "昵称不能为空";
                BasicInfoMessageBrush = ErrorBrush;
                return;
            }

            if (NickName.Trim().Length > 32)
            {
                BasicInfoMessage = "昵称长度不能超过32个字符";
                BasicInfoMessageBrush = ErrorBrush;
                return;
            }

            IsLoading = true;
            BasicInfoMessage = string.Empty;

            try
            {
                var success = await _accountService.UpdateProfileAsync(
                    App.CurrentUser.Id, NickName, Bio, Avatar,
                    Gender, Birthday, Province, City, Email, Phone);
                if (success)
                {
                    App.CurrentUser = await _accountService.GetUserByIdAsync(App.CurrentUser.Id);
                    BasicInfoMessage = "资料保存成功";
                    BasicInfoMessageBrush = SuccessBrush;

                    // 保存最新快照（保存成功后重置基准更新为当前值）
                    SnapshotOriginalValues();

                    // 延迟 3 秒后向所有在线好友广播资料变更，使好友客户端静默更新（用户无感知）
                    ScheduleProfileBroadcastAsync(NickName?.Trim(), Avatar?.Trim(), Bio?.Trim());
                }
                else
                {
                    BasicInfoMessage = "更新失败，请重试";
                    BasicInfoMessageBrush = ErrorBrush;
                }
            }
            catch (System.Exception ex)
            {
                BasicInfoMessage = "更新失败：" + ex.Message;
                BasicInfoMessageBrush = ErrorBrush;
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// 在后台延迟 3 秒后通过 IM 网关向所有在线好友广播当前资料变更。
        /// 此方法不阻塞调用方，在 Task 内部静默处理异常。
        /// </summary>
        private void ScheduleProfileBroadcastAsync(string nickname, string avatar, string bio)
        {
            if (App.CurrentUser == null)
            {
                return;
            }

            if (!ulong.TryParse(App.CurrentUser.Id, out var userId) || userId == 0)
            {
                return;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(3000).ConfigureAwait(false);
                    await using var client = new ImGatewayContactClient();
                    await client.SendProfileUpdateBroadcastAsync(
                        userId,
                        nickname ?? string.Empty,
                        avatar ?? string.Empty,
                        bio ?? string.Empty).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // 广播失败不影响主流程，静默记录
                    System.Diagnostics.Debug.WriteLine($"[ProfileViewModel] 延迟资料广播失败: {ex}");
                }
            });
        }

        private async Task UpdateTitleAsync()
        {
            if (App.CurrentUser == null)
            {
                TitleMessage = "请先登录";
                TitleMessageBrush = ErrorBrush;
                return;
            }

            IsLoading = true;
            TitleMessage = string.Empty;

            try
            {
                var success = await _accountService.UpdateTitleAsync(App.CurrentUser.Id, Title);
                if (success)
                {
                    App.CurrentUser = await _accountService.GetUserByIdAsync(App.CurrentUser.Id);
                    TitleMessage = "头衔更新成功";
                    TitleMessageBrush = SuccessBrush;
                }
                else
                {
                    TitleMessage = "更新失败，请重试";
                    TitleMessageBrush = ErrorBrush;
                }
            }
            catch (System.Exception ex)
            {
                TitleMessage = "更新失败：" + ex.Message;
                TitleMessageBrush = ErrorBrush;
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task UpdateContactInfoAsync()
        {
            if (App.CurrentUser == null)
            {
                ContactInfoMessage = "请先登录";
                ContactInfoMessageBrush = ErrorBrush;
                return;
            }

            if (!string.IsNullOrWhiteSpace(Phone) && !IsValidPhone(Phone.Trim()))
            {
                ContactInfoMessage = "手机号格式不正确，请输入有效的手机号";
                ContactInfoMessageBrush = ErrorBrush;
                return;
            }

            if (!string.IsNullOrWhiteSpace(Email) && !IsValidEmail(Email.Trim()))
            {
                ContactInfoMessage = "邮箱格式不正确，请输入有效的邮箱地址";
                ContactInfoMessageBrush = ErrorBrush;
                return;
            }

            IsLoading = true;
            ContactInfoMessage = string.Empty;

            try
            {
                var success = await _accountService.UpdateContactInfoAsync(App.CurrentUser.Id, Phone, Email);
                if (success)
                {
                    App.CurrentUser = await _accountService.GetUserByIdAsync(App.CurrentUser.Id);
                    ContactInfoMessage = "账号绑定信息更新成功";
                    ContactInfoMessageBrush = SuccessBrush;
                }
                else
                {
                    ContactInfoMessage = "更新失败，请重试";
                    ContactInfoMessageBrush = ErrorBrush;
                }
            }
            catch (System.Exception ex)
            {
                ContactInfoMessage = "更新失败：" + ex.Message;
                ContactInfoMessageBrush = ErrorBrush;
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task UpdateRealNameAsync()
        {
            if (App.CurrentUser == null)
            {
                RealNameMessage = "请先登录";
                RealNameMessageBrush = ErrorBrush;
                return;
            }

            // 姓名与身份证号须同时填写或同时为空
            var hasName = !string.IsNullOrWhiteSpace(RealName);
            var hasIdCard = !string.IsNullOrWhiteSpace(IdCard);
            if (hasName != hasIdCard)
            {
                RealNameMessage = "真实姓名和身份证号须同时填写或同时留空";
                RealNameMessageBrush = ErrorBrush;
                return;
            }

            if (!string.IsNullOrWhiteSpace(IdCard) && !IsValidIdCard(IdCard.Trim()))
            {
                RealNameMessage = "身份证号格式不正确，请输入18位有效身份证号";
                RealNameMessageBrush = ErrorBrush;
                return;
            }

            IsLoading = true;
            RealNameMessage = string.Empty;

            try
            {
                var success = await _accountService.UpdateRealNameAsync(App.CurrentUser.Id, RealName, IdCard);
                if (success)
                {
                    App.CurrentUser = await _accountService.GetUserByIdAsync(App.CurrentUser.Id);
                    RealNameMessage = "实名信息已保存（待后续真实验证）";
                    RealNameMessageBrush = SuccessBrush;
                }
                else
                {
                    RealNameMessage = "保存失败，请重试";
                    RealNameMessageBrush = ErrorBrush;
                }
            }
            catch (System.Exception ex)
            {
                RealNameMessage = "保存失败：" + ex.Message;
                RealNameMessageBrush = ErrorBrush;
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void NavigateToSecurity()
        {
            NavigationService.Instance.NavigateToSecurity();
        }

        /// <summary>
        /// 移除头像（清空 Avatar 字段）。
        /// </summary>
        private void RemoveAvatar()
        {
            Avatar = string.Empty;
        }

        /// <summary>
        /// 重置表单为上次加载/保存时的原始值。
        /// </summary>
        private void Reset()
        {
            NickName = _origNickName;
            Bio = _origBio;
            Avatar = _origAvatar;
            Gender = _origGender;
            Birthday = _origBirthday;
            Province = _origProvince;
            City = _origCity;
            Email = _origEmail;
            Phone = _origPhone;
            BasicInfoMessage = string.Empty;
        }

        /// <summary>
        /// 将当前所有可编辑字段保存为原始快照，供 Reset 命令恢复。
        /// </summary>
        private void SnapshotOriginalValues()
        {
            _origNickName = NickName;
            _origBio = Bio;
            _origAvatar = Avatar;
            _origGender = Gender;
            _origBirthday = Birthday;
            _origProvince = Province;
            _origCity = City;
            _origEmail = Email;
            _origPhone = Phone;
        }

        private void Logout()
        {
            App.CurrentUser = null;
            NavigationService.Instance.NavigateToLogin();
        }

        private bool CanUpdate()
        {
            return !IsLoading && App.CurrentUser != null;
        }

        private static bool IsValidPhone(string phone)
        {
            if (string.IsNullOrWhiteSpace(phone))
            {
                return true;
            }

            // 仅允许数字、加号、连字符和空格，长度在7到20位之间
            return System.Text.RegularExpressions.Regex.IsMatch(phone, @"^\+?[\d\s\-]{7,20}$");
        }

        private static bool IsValidEmail(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
            {
                return true;
            }

            return System.Text.RegularExpressions.Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$");
        }

        private static bool IsValidIdCard(string idCard)
        {
            if (string.IsNullOrWhiteSpace(idCard))
            {
                return true;
            }

            // 中国居民身份证：18位，前17位为数字，最后一位为数字或X
            return System.Text.RegularExpressions.Regex.IsMatch(idCard, @"^\d{17}[\dXx]$");
        }
    }
}
