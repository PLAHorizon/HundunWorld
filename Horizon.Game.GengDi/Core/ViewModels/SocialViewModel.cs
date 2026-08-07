using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;
using Horizon.Game.GengDi.Core.Services;
using Horizon.Game.GengDi.Enums;
using Horizon.Game.GengDi.Models;
using Horizon.IM.Message.Enums;
using Horizon.IM.Message.Network;

namespace Horizon.Game.GengDi.Core.ViewModels
{
    public class SocialViewModel : ViewModelBase
    {
        private readonly SocialService _socialService;
        private readonly ObservableCollection<Horizon.Game.GengDi.Models.IMMessage> _messages;
        private readonly ObservableCollection<Horizon.Game.GengDi.Models.IMMessage> _groupMessages;
        private readonly ObservableCollection<ChatMessageItemViewModel> _messageItems;
        private readonly ObservableCollection<ChatMessageItemViewModel> _groupMessageItems;
        private readonly Dictionary<string, User> _knownUsersById;
        private readonly AsyncRelayCommand _sendMessageCommand;
        private readonly AsyncRelayCommand _sendGroupMessageCommand;
        private readonly AsyncRelayCommand _sendFriendRequestCommand;
        private readonly AsyncRelayCommand<string> _removeFriendCommand;
        private readonly AsyncRelayCommand _createGroupCommand;
        private readonly AsyncRelayCommand<string> _addSuggestedFriendCommand;
        private readonly AsyncRelayCommand<string> _rejectFriendRequestCommand;
        private readonly AsyncRelayCommand _clearChatCommand;
        private readonly AsyncRelayCommand<GroupInviteItem> _acceptGroupInviteCommand;
        private readonly AsyncRelayCommand<GroupInviteItem> _rejectGroupInviteCommand;
        private readonly AsyncRelayCommand _inviteToGroupCommand;
        private readonly AsyncRelayCommand<string> _removeGroupCommand;
        private readonly AsyncRelayCommand<GroupInviteApprovalItem> _approveInviteApprovalCommand;
        private readonly AsyncRelayCommand<GroupInviteApprovalItem> _rejectInviteApprovalCommand;
        private readonly AsyncRelayCommand _confirmFriendGroupOverlayCommand;

        private string _currentUserId;
        private ObservableCollection<User> _friends;
        private ObservableCollection<User> _suggestedFriends;
        private ObservableCollection<FriendGroupItem> _friendGroups = new();
        private Dictionary<string, int> _contactGroupDefinitions = new();
        private ObservableCollection<Group> _groups;
        private ObservableCollection<GroupInviteItem> _pendingGroupInvites;
        private ObservableCollection<GroupInviteApprovalItem> _pendingInviteApprovals;
        private string _newMessageContent;
        private string _selectedFriendId;
        private string _selectedGroupId;
        private string _friendRequestUsername;
        private string _newGroupName;
        private string _actionStatusMessage;
        private string _inviteStatusMessage;
        private ConversationViewState _activeConversationState;
        private readonly ObservableCollection<PendingSocialAttachment> _pendingAttachments = new();
        private bool _isSending;
        private bool _isInitializing;
        private bool _isRosterLoading;
        private bool _isConversationLoading;
        private bool _isInitialized;
        private int _conversationLoadVersion;
        private User _selectedFriend;
        private Group _selectedGroup;
        // 邀请好友入群弹窗状态
        private bool _isInviteFriendsOverlayOpen;
        // 好友分组新增/编辑弹窗状态
        private bool _isFriendGroupOverlayOpen;
        private string _friendGroupOverlayTitle = string.Empty;
        private string _friendGroupOverlaySubtitle = string.Empty;
        private string _friendGroupInputText = string.Empty;
        private string _friendGroupOriginalName = string.Empty;
        private bool _isFriendGroupEditMode;
        private string _friendGroupStatusMessage = string.Empty;
        private string _pendingAssignFriendId;
        // 删除分组确认弹窗状态
        private bool _isDeleteGroupOverlayOpen;
        private string _deleteGroupName = string.Empty;
        private string _deleteGroupDisplayName = string.Empty;
        private string _deleteGroupStatusMessage = string.Empty;
        // 初始化重试取消令牌
        private CancellationTokenSource _initRetryCts;
        private bool _notificationSubscribed;
        // 后台初始化任务引用，供调用方按需 await
        private Task _initializationTask = Task.CompletedTask;
        // 离线未读消息数量（服务端未读 - 本地已缓存）
        private int _totalOfflineUnreadCount;
        // 上次合并服务端会话时每个会话的增量，用于精确扣减离线未读计数。
        private readonly Dictionary<string, int> _offlineUnreadDeltas = new(StringComparer.Ordinal);
        // 添加好友/创建群组相关
        private string _searchQuery = string.Empty;
        private bool _isAddingFriend;
        private bool _isCreatingGroup;
        private string _newFriendUserId = string.Empty;
        private string _conversationHeaderTitle = string.Empty;
        private ObservableCollection<GroupMemberItem> _groupMembers = new();
        private bool _isGroupMembersPanelOpen;
        private bool _isGroupMembersLoading;
        private RelayCommand _toggleGroupMembersPanelCommand;

        public event Action<string, bool>? ChatAnimationRequested;

        public SocialViewModel() : this(ImIdentity.ResolvePassportId(App.CurrentUser) ?? string.Empty)
        {
        }

        public SocialViewModel(string userId)
        {
            _currentUserId = userId;
            _socialService = new SocialService();
            _messages = new ObservableCollection<Horizon.Game.GengDi.Models.IMMessage>();
            _groupMessages = new ObservableCollection<Horizon.Game.GengDi.Models.IMMessage>();
            _messageItems = new ObservableCollection<ChatMessageItemViewModel>();
            _groupMessageItems = new ObservableCollection<ChatMessageItemViewModel>();
            _knownUsersById = new Dictionary<string, User>(StringComparer.Ordinal);
            _friends = new ObservableCollection<User>();
            _suggestedFriends = new ObservableCollection<User>();
            _groups = new ObservableCollection<Group>();
            _pendingGroupInvites = new ObservableCollection<GroupInviteItem>();
            _pendingInviteApprovals = new ObservableCollection<GroupInviteApprovalItem>();
            _actionStatusMessage = string.IsNullOrWhiteSpace(userId)
                ? "登录后可使用社交功能。"
                : string.Empty;
            _inviteStatusMessage = string.Empty;
            _activeConversationState = ConversationViewState.CreateEmpty();

            _sendMessageCommand = new AsyncRelayCommand(SendMessageAsync, CanSendMessage);
            _sendGroupMessageCommand = new AsyncRelayCommand(SendMessageAsync, CanSendMessage);
            _sendFriendRequestCommand = new AsyncRelayCommand(SendFriendRequestAsync, CanSendFriendRequest);
            _removeFriendCommand = new AsyncRelayCommand<string>(RemoveFriendAsync, CanEditRoster);
            _createGroupCommand = new AsyncRelayCommand(CreateGroupAsync, CanCreateGroup);
            _addSuggestedFriendCommand = new AsyncRelayCommand<string>(AddSuggestedFriendAsync, CanEditRoster);
            _rejectFriendRequestCommand = new AsyncRelayCommand<string>(RejectFriendRequestAsync, CanEditRoster);
            _clearChatCommand = new AsyncRelayCommand(ClearChatAsync, CanClearChat);
            _acceptGroupInviteCommand = new AsyncRelayCommand<GroupInviteItem>(AcceptGroupInviteAsync, CanEditRoster);
            _rejectGroupInviteCommand = new AsyncRelayCommand<GroupInviteItem>(RejectGroupInviteAsync, CanEditRoster);
            _inviteToGroupCommand = new AsyncRelayCommand(OpenInviteFriendsOverlayAsync, CanInviteToGroup);
            _removeGroupCommand = new AsyncRelayCommand<string>(RemoveGroupAsync, CanEditRoster);
            _approveInviteApprovalCommand = new AsyncRelayCommand<GroupInviteApprovalItem>(ApproveInviteApprovalAsync, CanEditRoster);
            _rejectInviteApprovalCommand = new AsyncRelayCommand<GroupInviteApprovalItem>(RejectInviteApprovalAsync, CanEditRoster);
            _toggleGroupMembersPanelCommand = new RelayCommand(ToggleGroupMembersPanel);

            SendMessageCommand = _sendMessageCommand;
            SendGroupMessageCommand = _sendGroupMessageCommand;
            SendFriendRequestCommand = _sendFriendRequestCommand;
            RemoveFriendCommand = _removeFriendCommand;
            CreateGroupCommand = _createGroupCommand;
            AddSuggestedFriendCommand = _addSuggestedFriendCommand;
            RejectFriendRequestCommand = _rejectFriendRequestCommand;
            ClearAttachmentCommand = new RelayCommand(ClearPendingAttachment);
            ClearAllAttachmentsCommand = new RelayCommand(ClearPendingAttachment);
            ClearChatCommand = _clearChatCommand;
            AcceptGroupInviteCommand = _acceptGroupInviteCommand;
            RejectGroupInviteCommand = _rejectGroupInviteCommand;
            InviteToGroupCommand = _inviteToGroupCommand;
            RemoveGroupCommand = _removeGroupCommand;
            ApproveInviteApprovalCommand = _approveInviteApprovalCommand;
            RejectInviteApprovalCommand = _rejectInviteApprovalCommand;
            CloseInviteFriendsOverlayCommand = new RelayCommand(CloseInviteFriendsOverlay);
            CloseFriendGroupOverlayCommand = new RelayCommand(CloseFriendGroupOverlay);
            _confirmFriendGroupOverlayCommand = new AsyncRelayCommand(ConfirmFriendGroupOverlayAsync, CanConfirmFriendGroupOverlay);
            ConfirmFriendGroupOverlayCommand = _confirmFriendGroupOverlayCommand;
            CloseDeleteGroupOverlayCommand = new RelayCommand(CloseDeleteGroupOverlay);
            ConfirmDeleteGroupOverlayCommand = new AsyncRelayCommand(ConfirmDeleteGroupOverlayAsync, CanConfirmDeleteGroupOverlay);

            // ===== 语音/视频通话（增量接入）=====
            // 将通话服务绑定到当前用户的 IM 长连接，并挂载通话窗口宿主；
            // 不影响现有文本聊天/会话/通知链路。
            LastCreatedInstance = this;
            if (ImIdentity.TryResolveUserId(userId, out var localCallUserId))
            {
                Services.Call.CallService.Instance.Initialize(_socialService.GatewayClient, localCallUserId);
                Core.Views.CallWindowHost.EnsureAttached();
            }
        }

        /// <summary>最近创建的社交视图模型实例（供通话服务解析对端昵称等展示信息，可为 null）。</summary>
        internal static SocialViewModel LastCreatedInstance { get; private set; }

        /// <summary>尽力解析已知用户信息（本地缓存/网关缓存），失败返回 null，不抛异常。</summary>
        internal User ResolveKnownUser(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId))
            {
                return null;
            }

            try
            {
                return _socialService.GetUserById(userId);
            }
            catch
            {
                return null;
            }
        }

        internal ImGatewayContactClient GatewayClient => _socialService.GatewayClient;

        public string CurrentUserId
        {
            get => _currentUserId;
            set
            {
                if (SetProperty(ref _currentUserId, value))
                {
                    OnPropertyChanged(nameof(UsesGatewayContacts));
                    OnPropertyChanged(nameof(ShouldAutoRefreshRoster));
                    OnPropertyChanged(nameof(SuggestedFriendsHeader));
                    OnPropertyChanged(nameof(SuggestedFriendsDescription));
                    OnPropertyChanged(nameof(SuggestedFriendsActionLabel));
                    OnPropertyChanged(nameof(SidebarLoadingMessage));
                }
            }
        }

        public ObservableCollection<User> Friends
        {
            get => _friends;
            set => SetProperty(ref _friends, value);
        }

        public ObservableCollection<FriendGroupItem> FriendGroups
        {
            get => _friendGroups;
            set => SetProperty(ref _friendGroups, value);
        }

        /// <summary>私聊消息列表（供 View 绑定）。</summary>
        public ObservableCollection<ChatMessageItemViewModel> Messages => _messageItems;

        /// <summary>群聊消息列表（供 View 绑定）。</summary>
        public ObservableCollection<ChatMessageItemViewModel> GroupMessages => _groupMessageItems;

        public ObservableCollection<User> SuggestedFriends
        {
            get => _suggestedFriends;
            set => SetProperty(ref _suggestedFriends, value);
        }

        public ObservableCollection<Group> Groups
        {
            get => _groups;
            set => SetProperty(ref _groups, value);
        }

        public ObservableCollection<GroupInviteItem> PendingGroupInvites
        {
            get => _pendingGroupInvites;
            set => SetProperty(ref _pendingGroupInvites, value);
        }

        public bool HasPendingGroupInvites => PendingGroupInvites.Count > 0;

        public int PendingGroupInviteCount => PendingGroupInvites.Count;

        /// <summary>群主待审批的入群邀请（由非群主成员发起）。</summary>
        public ObservableCollection<GroupInviteApprovalItem> PendingInviteApprovals
        {
            get => _pendingInviteApprovals;
            set => SetProperty(ref _pendingInviteApprovals, value);
        }

        public bool HasPendingInviteApprovals => PendingInviteApprovals.Count > 0;

        public int PendingInviteApprovalCount => PendingInviteApprovals.Count;

        /// <summary>当前是否在群组会话中（用于显示"邀请好友"按钮）。</summary>
        public bool IsGroupConversationActive => !string.IsNullOrWhiteSpace(SelectedGroupId);

        public ObservableCollection<GroupMemberItem> GroupMembers => _groupMembers;

        public bool IsGroupMembersPanelOpen
        {
            get => _isGroupMembersPanelOpen;
            set => SetProperty(ref _isGroupMembersPanelOpen, value);
        }

        public bool IsGroupMembersLoading
        {
            get => _isGroupMembersLoading;
            set => SetProperty(ref _isGroupMembersLoading, value);
        }

        public string GroupMemberCountText
        {
            get
            {
                if (_selectedGroup == null)
                    return string.Empty;
                var count = _selectedGroup.ServerMemberCount > 0
                    ? _selectedGroup.ServerMemberCount
                    : _selectedGroup.Members.Count;
                return count > 0 ? $"{count} 人" : string.Empty;
            }
        }

        public ICommand ToggleGroupMembersPanelCommand => _toggleGroupMembersPanelCommand;

        /// <summary>邀请好友弹窗是否打开。</summary>
        public bool IsInviteFriendsOverlayOpen
        {
            get => _isInviteFriendsOverlayOpen;
            set => SetProperty(ref _isInviteFriendsOverlayOpen, value);
        }

        /// <summary>邀请操作状态消息。</summary>
        public string InviteStatusMessage
        {
            get => _inviteStatusMessage;
            set => SetProperty(ref _inviteStatusMessage, value);
        }

        /// <summary>好友分组弹窗是否打开。</summary>
        public bool IsFriendGroupOverlayOpen
        {
            get => _isFriendGroupOverlayOpen;
            set => SetProperty(ref _isFriendGroupOverlayOpen, value);
        }

        /// <summary>好友分组弹窗标题。</summary>
        public string FriendGroupOverlayTitle
        {
            get => _friendGroupOverlayTitle;
            set => SetProperty(ref _friendGroupOverlayTitle, value);
        }

        /// <summary>好友分组弹窗副标题。</summary>
        public string FriendGroupOverlaySubtitle
        {
            get => _friendGroupOverlaySubtitle;
            set => SetProperty(ref _friendGroupOverlaySubtitle, value);
        }

        /// <summary>好友分组弹窗输入文本。</summary>
        public string FriendGroupInputText
        {
            get => _friendGroupInputText;
            set
            {
                if (SetProperty(ref _friendGroupInputText, value))
                {
                    _confirmFriendGroupOverlayCommand.RaiseCanExecuteChanged();
                }
            }
        }

        /// <summary>好友分组弹窗是否为编辑模式。</summary>
        public bool IsFriendGroupEditMode
        {
            get => _isFriendGroupEditMode;
            set => SetProperty(ref _isFriendGroupEditMode, value);
        }

        /// <summary>好友分组弹窗状态消息。</summary>
        public string FriendGroupStatusMessage
        {
            get => _friendGroupStatusMessage;
            set => SetProperty(ref _friendGroupStatusMessage, value);
        }

        /// <summary>删除分组确认弹窗是否打开。</summary>
        public bool IsDeleteGroupOverlayOpen
        {
            get => _isDeleteGroupOverlayOpen;
            set => SetProperty(ref _isDeleteGroupOverlayOpen, value);
        }

        /// <summary>待删除的分组显示名称。</summary>
        public string DeleteGroupDisplayName
        {
            get => _deleteGroupDisplayName;
            set => SetProperty(ref _deleteGroupDisplayName, value);
        }

        /// <summary>删除分组操作状态消息。</summary>
        public string DeleteGroupStatusMessage
        {
            get => _deleteGroupStatusMessage;
            set => SetProperty(ref _deleteGroupStatusMessage, value);
        }

        public string NewMessageContent
        {
            get => _newMessageContent;
            set
            {
                if (SetProperty(ref _newMessageContent, value))
                {
                    RaiseSendCommandStateChanged();
                }
            }
        }

        public string SelectedFriendId
        {
            get => _selectedFriendId;
            set
            {
                if (SetProperty(ref _selectedFriendId, value))
                {
                    RaiseSendCommandStateChanged();
                }
            }
        }

        public string SelectedGroupId
        {
            get => _selectedGroupId;
            set
            {
                if (SetProperty(ref _selectedGroupId, value))
                {
                    OnPropertyChanged(nameof(IsGroupConversationActive));
                    _inviteToGroupCommand.RaiseCanExecuteChanged();
                    RaiseSendCommandStateChanged();
                }
            }
        }

        public User SelectedFriend
        {
            get => _selectedFriend;
            set => _ = SelectFriendAsync(value);
        }

        public Group SelectedGroup
        {
            get => _selectedGroup;
            set => _ = SelectGroupAsync(value);
        }

        public ConversationViewState ActiveConversationState
        {
            get => _activeConversationState;
            private set => SetProperty(ref _activeConversationState, value);
        }

        public string FriendRequestUsername
        {
            get => _friendRequestUsername;
            set
            {
                if (SetProperty(ref _friendRequestUsername, value))
                {
                    _sendFriendRequestCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public string NewGroupName
        {
            get => _newGroupName;
            set
            {
                if (SetProperty(ref _newGroupName, value))
                {
                    _createGroupCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public string ActionStatusMessage
        {
            get => _actionStatusMessage;
            set => SetProperty(ref _actionStatusMessage, value);
        }

        public int OnlineFriendsCount => Friends.Count(friend => friend.IsAvailable);

        public int SuggestedFriendsCount => SuggestedFriends.Count;

        public bool HasSuggestedFriends => SuggestedFriends.Count > 0;

        public bool UsesGatewayContacts => ImIdentity.TryResolveUserId(_currentUserId, out _);

        public bool ShouldAutoRefreshRoster => UsesGatewayContacts;

        public string SuggestedFriendsHeader => UsesGatewayContacts ? "待处理申请" : "推荐战友";

        public string SuggestedFriendsDescription => UsesGatewayContacts
            ? "这里显示通过通行证号发来的好友申请，点击接受后会加入通讯录。"
            : "把推荐联系人整理成更接近微信的轻量条目。";

        public string SuggestedFriendsActionLabel => UsesGatewayContacts ? "接受" : "添加";

        public bool HasPendingAttachment => _pendingAttachments.Count > 0;
        public bool HasPendingAttachments => _pendingAttachments.Count > 0;
        public int PendingAttachmentCount => _pendingAttachments.Count;
        public ObservableCollection<PendingSocialAttachment> PendingAttachments => _pendingAttachments;

        /// <summary>
        /// 服务端侧离线未读消息总数（当前会话期间接收到服务端数据时更新）。
        /// </summary>
        public int TotalOfflineUnreadCount
        {
            get => _totalOfflineUnreadCount;
            private set
            {
                if (SetProperty(ref _totalOfflineUnreadCount, value))
                {
                    OnPropertyChanged(nameof(HasOfflineUnreadMessages));
                    OnPropertyChanged(nameof(OfflineUnreadBannerText));
                }
            }
        }

        /// <summary>
        /// 是否存在离线未读消息（服务端有未读但本地尚未接收到的消息）。
        /// </summary>
        public bool HasOfflineUnreadMessages => _totalOfflineUnreadCount > 0;

        /// <summary>
        /// 离线未读消息提示文字。
        /// </summary>
        public string OfflineUnreadBannerText =>
            _totalOfflineUnreadCount == 1
                ? "您有 1 条离线期间的未读消息"
                : $"您有 {_totalOfflineUnreadCount} 条离线期间的未读消息";

        public string PendingAttachmentDisplayName => _pendingAttachments.Count > 0 ? _pendingAttachments[0].DisplayName : string.Empty;

        public string PendingAttachmentSummary => _pendingAttachments.Count > 0
            ? $"{_pendingAttachments.Count} 个附件待发送"
            : string.Empty;

        public string PendingAttachmentKindLabel => _pendingAttachments.Count > 0 ? _pendingAttachments[0].KindLabel : string.Empty;

        public string SendButtonLabel => _isSending ? "发送中..." : "发送";

        public bool IsSending => _isSending;

        public bool IsSidebarBusy => _isInitializing || _isRosterLoading;

        public bool IsConversationBusy => _isInitializing || _isConversationLoading;

        /// <summary>
        /// 搜索查询（用于添加好友时的搜索）。
        /// </summary>
        public string SearchQuery
        {
            get => _searchQuery;
            set => SetProperty(ref _searchQuery, value);
        }

        /// <summary>
        /// 是否显示添加好友面板。
        /// </summary>
        public bool IsAddingFriend
        {
            get => _isAddingFriend;
            set => SetProperty(ref _isAddingFriend, value);
        }

        /// <summary>
        /// 是否显示创建群组面板。
        /// </summary>
        public bool IsCreatingGroup
        {
            get => _isCreatingGroup;
            set => SetProperty(ref _isCreatingGroup, value);
        }

        /// <summary>
        /// 新好友的用户 ID（用于添加好友）。
        /// </summary>
        public string NewFriendUserId
        {
            get => _newFriendUserId;
            set => SetProperty(ref _newFriendUserId, value);
        }

        /// <summary>
        /// 创建群组时选中的好友 ID 列表。
        /// </summary>
        public ObservableCollection<string> SelectedFriendIdsForGroup { get; } = new();

        /// <summary>
        /// 会话头部标题（用于显示当前聊天对象名称）。
        /// </summary>
        public string ConversationHeaderTitle
        {
            get => _conversationHeaderTitle;
            set => SetProperty(ref _conversationHeaderTitle, value);
        }

        public string SidebarLoadingMessage => _isInitializing
            ? "正在同步社交数据..."
            : UsesGatewayContacts ? "正在刷新好友、待处理申请与群组..." : "正在刷新好友、推荐与群组...";

        public string ConversationLoadingMessage => _isInitializing ? "正在准备社交面板..." : "正在异步加载会话消息...";

        public ICommand SendMessageCommand { get; }

        public ICommand SendGroupMessageCommand { get; }

        public ICommand SendFriendRequestCommand { get; }

        public ICommand RemoveFriendCommand { get; }

        public ICommand CreateGroupCommand { get; }

        public ICommand AddSuggestedFriendCommand { get; }

        public ICommand RejectFriendRequestCommand { get; }

        public ICommand ClearAttachmentCommand { get; }

        public ICommand ClearAllAttachmentsCommand { get; }

        public ICommand ClearChatCommand { get; }

        /// <summary>接受入群邀请。</summary>
        public ICommand AcceptGroupInviteCommand { get; }

        /// <summary>拒绝入群邀请。</summary>
        public ICommand RejectGroupInviteCommand { get; }

        /// <summary>邀请好友入群（打开选择弹窗）。</summary>
        public ICommand InviteToGroupCommand { get; }

        /// <summary>左滑群组项触发：群主=解散群组，非群主=退出群组。</summary>
        public ICommand RemoveGroupCommand { get; }

        /// <summary>群主批准非群主成员发起的入群邀请。</summary>
        public ICommand ApproveInviteApprovalCommand { get; }

        /// <summary>群主拒绝非群主成员发起的入群邀请。</summary>
        public ICommand RejectInviteApprovalCommand { get; }

        /// <summary>关闭邀请好友弹窗。</summary>
        public ICommand CloseInviteFriendsOverlayCommand { get; }

        /// <summary>关闭好友分组弹窗。</summary>
        public ICommand CloseFriendGroupOverlayCommand { get; }

        /// <summary>确认好友分组弹窗操作。</summary>
        public ICommand ConfirmFriendGroupOverlayCommand { get; }

        /// <summary>关闭删除分组确认弹窗。</summary>
        public ICommand CloseDeleteGroupOverlayCommand { get; }

        /// <summary>确认删除分组。</summary>
        public ICommand ConfirmDeleteGroupOverlayCommand { get; }

        /// <summary>
        /// 后台初始化任务，成功完成时表示首次通讯录加载已结束。
        /// 调用方可按需 await，也可忽略（UI 通过 IsSidebarBusy 反映骨架屏状态）。
        /// </summary>
        public Task InitializationTask => _initializationTask;

        public async Task InitializeAsync()
        {
            if (_isInitialized || _isInitializing)
            {
                return;
            }

            SetInitializing(true);
            ActionStatusMessage = "正在同步社交数据...";

            // 将任务引用保存到 _initializationTask，供调用方通过 InitializationTask 属性按需等待。
            // 内部已捕获所有异常，不会产生未观察异常。
            _initializationTask = RunInitializationWithRetryAsync();
        }

        /// <summary>
        /// 后台静默重试初始化，直到成功为止。
        /// 采用指数退避策略：2 s → 4 s → 8 s → … 上限 30 s。
        /// </summary>
        private async Task RunInitializationWithRetryAsync()
        {
            // 取消前一轮重试（若存在）并创建新的取消令牌。
            // 注意：仅调用 Cancel() 而不立即 Dispose()，避免前一轮循环在还未观察到取消信号时
            // 访问已释放的 CancellationTokenSource 引发 ObjectDisposedException。
            var cts = new CancellationTokenSource();
            var previousCts = Interlocked.Exchange(ref _initRetryCts, cts);
            previousCts?.Cancel();
            // previousCts 在所有持有其 Token 的等待完成后由 GC 回收，此处不提前 Dispose。

            var token = cts.Token;
            var delay = TimeSpan.FromSeconds(2);
            var maxDelay = TimeSpan.FromSeconds(30);

            try
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        if (!string.IsNullOrWhiteSpace(_currentUserId))
                        {
                            await _socialService.EnsureDemoSocialGraphAsync(_currentUserId);
                        }

                        await ReloadRosterAsync();

                        if (token.IsCancellationRequested)
                        {
                            return;
                        }

                        _isInitialized = true;
                        SetInitializing(false);
                        StartNotificationSubscriptions();
                        ActionStatusMessage = string.IsNullOrWhiteSpace(_currentUserId)
                            ? "登录后可使用社交功能。"
                            : string.Empty;
                        return;
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception)
                    {
                        // 静默忽略，保持骨架屏（IsSidebarBusy = true）并稍后重试
                        if (token.IsCancellationRequested)
                        {
                            return;
                        }
                    }

                    try
                    {
                        await Task.Delay(delay, token);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }

                    delay = TimeSpan.FromMilliseconds(
                        Math.Min(delay.TotalMilliseconds * 2, maxDelay.TotalMilliseconds));
                }
            }
            finally
            {
                // 无条件释放本轮 CTS；并将字段清空（仅在仍指向本轮时），表示无活跃重试。
                Interlocked.CompareExchange(ref _initRetryCts, null, cts);
                cts.Dispose();
            }
        }

        /// <summary>
        /// 取消后台初始化重试。由外部（如退出登录）调用以停止后台循环。
        /// </summary>
        public void Shutdown()
        {
            UnsubscribeFromNotifications();
            var cts = Interlocked.Exchange(ref _initRetryCts, null);
            cts?.Cancel();
            cts?.Dispose();
            _isInitialized = false;
            _currentUserId = string.Empty;
        }

        public async Task SelectFriendAsync(User friend)
        {
            ApplySelectedFriend(friend);
            await RefreshSelectedConversationAsync();
        }

        public async Task SelectGroupAsync(Group group)
        {
            ApplySelectedGroup(group);
            await RefreshSelectedConversationAsync();
        }

        public async Task SendMessageAsync()
        {
            if (_isSending)
            {
                return;
            }

            var isGroupConversation = !string.IsNullOrWhiteSpace(SelectedGroupId);
            var conversationId = isGroupConversation ? SelectedGroupId : SelectedFriendId;

            if (string.IsNullOrWhiteSpace(conversationId))
            {
                ActionStatusMessage = "请先在左侧选择好友或群组。";
                return;
            }

            var draft = NewMessageContent?.Trim() ?? string.Empty;
            if (!HasPendingAttachment && string.IsNullOrWhiteSpace(draft))
            {
                return;
            }

            const int MaxDraftLength = 30000;
            if (draft.Length > MaxDraftLength)
            {
                ActionStatusMessage = $"消息内容过长（{draft.Length} 字符），请缩短至 {MaxDraftLength} 字符以内后重试。";
                return;
            }

            try
            {
                _isSending = true;
                OnPropertyChanged(nameof(SendButtonLabel));
                RaiseSendCommandStateChanged();
                ActionStatusMessage = "正在整理消息内容...";

                var attachments = _pendingAttachments.ToList();
                var sentMessage = await _socialService.SendComposedMessageAsync(_currentUserId, conversationId, draft, attachments, isGroupConversation);

                if (isGroupConversation && string.Equals(SelectedGroupId, conversationId, StringComparison.Ordinal))
                {
                    if (_groupMessages.All(message => !string.Equals(message.Id, sentMessage.Id, StringComparison.Ordinal)))
                    {
                        _groupMessages.Add(sentMessage);
                        _groupMessageItems.Add(CreateMessageItem(sentMessage));
                    }

                    await ApplyGroupConversationStatesAsync(new[] { conversationId });
                }
                else if (!isGroupConversation && string.Equals(SelectedFriendId, conversationId, StringComparison.Ordinal))
                {
                    if (_messages.All(message => !string.Equals(message.Id, sentMessage.Id, StringComparison.Ordinal)))
                    {
                        _messages.Add(sentMessage);
                        _messageItems.Add(CreateMessageItem(sentMessage));
                    }

                    await ApplyDirectConversationStatesAsync(new[] { conversationId });
                    
                }

                NewMessageContent = string.Empty;
                ClearPendingAttachmentInternal();
                ActionStatusMessage = sentMessage.Type == MessageType.LinkCard || sentMessage.Type == MessageType.Video
                    ? "富媒体卡片已发送，悬停卡片即可在消息流中预览播放。"
                    : "消息已发送。";
                RefreshConversationState();
            }
            catch (Exception ex)
            {
                ActionStatusMessage = $"发送失败：{ex.Message}";
            }
            finally
            {
                _isSending = false;
                OnPropertyChanged(nameof(SendButtonLabel));
                RaiseSendCommandStateChanged();
            }
        }

        public async Task SendFriendRequestAsync()
        {
            var username = FriendRequestUsername?.Trim();
            if (string.IsNullOrWhiteSpace(username))
            {
                return;
            }

            try
            {
                ActionStatusMessage = "正在添加好友...";
                var success = await _socialService.SendFriendRequestAsync(_currentUserId, username);
                await ReloadRosterAsync();

                ActionStatusMessage = success
                    ? $"好友申请已发送：{username}。"
                    : $"未找到通行证：{username}。";

                if (success)
                {
                    FriendRequestUsername = string.Empty;
                }

                RefreshConversationState();
            }
            catch (Exception ex)
            {
                ActionStatusMessage = $"添加好友失败：{ex.Message}";
            }
        }

        public async Task AddSuggestedFriendAsync(string friendId)
        {
            if (string.IsNullOrWhiteSpace(friendId))
            {
                return;
            }

            try
            {
                ActionStatusMessage = UsesGatewayContacts ? "正在处理好友申请..." : "正在添加推荐战友...";
                var success = await _socialService.AcceptFriendRequestAsync(_currentUserId, friendId);
                await ReloadRosterAsync();

                if (success)
                {
                    ActionStatusMessage = UsesGatewayContacts ? "已接受好友申请。" : "好友申请已发送。";
                    return;
                }

                ActionStatusMessage = "添加好友失败，请稍后重试。";
            }
            catch (Exception ex)
            {
                ActionStatusMessage = $"添加好友失败：{ex.Message}";
            }
        }

        public async Task RejectFriendRequestAsync(string requesterId)
        {
            if (string.IsNullOrWhiteSpace(requesterId))
            {
                return;
            }

            try
            {
                ActionStatusMessage = UsesGatewayContacts ? "正在处理好友申请..." : "正在拒绝好友申请...";
                var success = await _socialService.RejectFriendRequestAsync(_currentUserId, requesterId);
                await ReloadRosterAsync();

                if (success)
                {
                    ActionStatusMessage = UsesGatewayContacts ? "已拒绝好友申请。" : "已拒绝好友申请。";
                    return;
                }

                ActionStatusMessage = "拒绝好友申请失败，请稍后重试。";
            }
            catch (Exception ex)
            {
                ActionStatusMessage = $"拒绝好友申请失败：{ex.Message}";
            }
        }

        public async Task RemoveFriendAsync(string friendId)
        {
            if (string.IsNullOrWhiteSpace(friendId))
            {
                return;
            }

            var removedActiveConversation = string.Equals(SelectedFriendId, friendId, StringComparison.Ordinal);

            try
            {
                ActionStatusMessage = "正在移除联系人...";
                await _socialService.RemoveFriendAsync(_currentUserId, friendId);

                if (removedActiveConversation)
                {
                    await SelectFriendAsync(null);
                }

                await ReloadRosterAsync();
                ActionStatusMessage = "已从好友列表移除该联系人。";
                RefreshConversationState();
            }
            catch (Exception ex)
            {
                ActionStatusMessage = $"移除好友失败：{ex.Message}";
            }
        }

        public async Task CreateGroupAsync()
        {
            var groupName = NewGroupName?.Trim();
            if (string.IsNullOrWhiteSpace(groupName))
            {
                return;
            }

            try
            {
                ActionStatusMessage = "正在创建群组会话...";
                var group = await _socialService.CreateGroupAsync(_currentUserId, groupName);
                await ReloadRosterAsync();
                NewGroupName = string.Empty;
                await SelectGroupAsync(Groups.FirstOrDefault(item => string.Equals(item.Id, group.Id, StringComparison.Ordinal)) ?? group);
                ActionStatusMessage = "已创建新的群组会话。";
                RefreshConversationState();
            }
            catch (Exception ex)
            {
                ActionStatusMessage = $"创建群组失败：{ex.Message}";
            }
        }

        public async Task AddMemberToGroupAsync(string groupId, string userId)
        {
            if (string.IsNullOrWhiteSpace(groupId) || string.IsNullOrWhiteSpace(userId))
            {
                return;
            }

            await _socialService.AddMemberToGroupAsync(groupId, userId);
            await ReloadRosterAsync();
            RefreshConversationState();
        }

        public async Task RemoveMemberFromGroupAsync(string groupId, string userId)
        {
            if (string.IsNullOrWhiteSpace(groupId) || string.IsNullOrWhiteSpace(userId))
            {
                return;
            }

            await _socialService.RemoveMemberFromGroupAsync(groupId, userId);
            await ReloadRosterAsync();
            RefreshConversationState();
        }

        #region Group Invite & Accept/Reject

        /// <summary>处理收到的群组邀请通知（由 View 调用）。</summary>
        public async void HandleGroupInviteNotify(IMGroupInviteNotify notify)
        {
            if (notify == null) return;

            var inviterDisplayName = string.IsNullOrWhiteSpace(notify.InviterName)
                ? notify.InviterId.ToString()
                : notify.InviterName;
            var groupDisplayName = string.IsNullOrWhiteSpace(notify.GroupName)
                ? notify.GroupId.ToString()
                : notify.GroupName;

            try
            {
                if (!notify.RequiresConsent)
                {
                    // 已被直接拉入群聊：无需用户确认，先序列化写入本地数据库再刷新群组列表。
                    // 不向 PendingGroupInvites 写入，避免徽标出现无法清除的计数。
                    await _socialService.EnsureGroupInLocalDatabaseAsync(
                        _currentUserId, notify.GroupId.ToString(), notify.GroupName);
                    await ReloadRosterAsync();
                    ActionStatusMessage = $"{inviterDisplayName} 已将你加入群组「{groupDisplayName}」。";
                    return;
                }

                // 客户端记录收到时间作为本地过期基准（后端通知不携带时间戳字段）
                var item = new GroupInviteItem
                {
                    GroupId = notify.GroupId,
                    GroupName = groupDisplayName,
                    InviterId = notify.InviterId,
                    InviterName = inviterDisplayName,
                    RequiresConsent = true,
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };

                var existingItem = PendingGroupInvites.FirstOrDefault(invite => invite.GroupId == notify.GroupId);
                if (existingItem != null)
                {
                    PendingGroupInvites.Remove(existingItem);
                }

                PendingGroupInvites.Insert(0, item);
                RaisePendingGroupInviteChanged();
                ActionStatusMessage = $"{inviterDisplayName} 邀请你加入群组「{groupDisplayName}」，请在左侧入群邀请中处理。";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SocialViewModel] 处理入群邀请通知失败：{ex.Message}");
            }
        }

        /// <summary>处理收到的群组加入申请通知（由 View 调用，仅群主/管理员会收到）。</summary>
        public void HandleGroupJoinApplyNotify(IMGroupJoinApplyNotify notify)
        {
            if (notify == null) return;

            var groupDisplayName = string.IsNullOrWhiteSpace(notify.GroupName)
                ? notify.GroupId.ToString()
                : notify.GroupName;
            var applicantDisplayName = string.IsNullOrWhiteSpace(notify.ApplicantName)
                ? notify.ApplicantId.ToString()
                : notify.ApplicantName;

            ActionStatusMessage = string.IsNullOrWhiteSpace(notify.Reason)
                ? $"{applicantDisplayName} 申请加入群组「{groupDisplayName}」。"
                : $"{applicantDisplayName} 申请加入群组「{groupDisplayName}」：{notify.Reason}";

            // 刷新群组列表以便群主/管理员看到最新申请状态
            _ = RefreshRosterAsync(silent: true);
        }

        public async Task AcceptGroupInviteAsync(GroupInviteItem invite)
        {
            if (invite == null) return;

            try
            {
                ActionStatusMessage = "正在接受入群邀请...";
                await _socialService.RespondToGroupInviteAsync(_currentUserId, invite.GroupId, true);
                PendingGroupInvites.Remove(invite);
                RaisePendingGroupInviteChanged();
                await _socialService.EnsureGroupInLocalDatabaseAsync(
                    _currentUserId, invite.GroupId.ToString(), invite.GroupName);
                await ReloadRosterAsync();
                ActionStatusMessage = $"已成功加入群组「{invite.GroupName}」。";
                RefreshConversationState();
            }
            catch (Exception ex)
            {
                ActionStatusMessage = $"接受入群邀请失败：{ex.Message}";
            }
        }

        public async Task RejectGroupInviteAsync(GroupInviteItem invite)
        {
            if (invite == null) return;

            try
            {
                ActionStatusMessage = "正在拒绝入群邀请...";
                await _socialService.RespondToGroupInviteAsync(_currentUserId, invite.GroupId, false);
                PendingGroupInvites.Remove(invite);
                RaisePendingGroupInviteChanged();
                ActionStatusMessage = "已拒绝入群邀请。";
            }
            catch (Exception ex)
            {
                ActionStatusMessage = $"拒绝入群邀请失败：{ex.Message}";
            }
        }

        private void ToggleGroupMembersPanel()
        {
            if (!IsGroupConversationActive)
            {
                IsGroupMembersPanelOpen = false;
                return;
            }

            var willOpen = !_isGroupMembersPanelOpen;
            IsGroupMembersPanelOpen = willOpen;

            if (willOpen && _groupMembers.Count == 0)
            {
                _ = LoadGroupMembersAsync();
            }
        }

        private async Task LoadGroupMembersAsync()
        {
            if (string.IsNullOrWhiteSpace(SelectedGroupId) || _isGroupMembersLoading)
                return;

            IsGroupMembersLoading = true;
            try
            {
                var members = await _socialService.GetGroupMembersAsync(_currentUserId, SelectedGroupId);

                var items = members
                    .OrderByDescending(m => m.Role)
                    .ThenBy(m => m.JoinTime)
                    .Select(m => new GroupMemberItem(m))
                    .ToList();

                foreach (var old in _groupMembers)
                    old.Dispose();

                ReplaceCollection(_groupMembers, items);
                OnPropertyChanged(nameof(GroupMemberCountText));
            }
            finally
            {
                IsGroupMembersLoading = false;
            }
        }

        public Task OpenInviteFriendsOverlayAsync()
        {
            if (string.IsNullOrWhiteSpace(SelectedGroupId))
            {
                ActionStatusMessage = "请先选择一个群组会话。";
                return Task.CompletedTask;
            }

            InviteStatusMessage = string.Empty;
            IsInviteFriendsOverlayOpen = true;
            return Task.CompletedTask;
        }

        public void CloseInviteFriendsOverlay()
        {
            IsInviteFriendsOverlayOpen = false;
            InviteStatusMessage = string.Empty;
        }

        public void OpenCreateFriendGroupOverlay(string assignFriendId = null)
        {
            FriendGroupOverlayTitle = "新建分组";
            FriendGroupOverlaySubtitle = "请输入分组名称";
            FriendGroupInputText = string.Empty;
            _friendGroupOriginalName = string.Empty;
            IsFriendGroupEditMode = false;
            FriendGroupStatusMessage = string.Empty;
            _pendingAssignFriendId = assignFriendId;
            IsFriendGroupOverlayOpen = true;
        }

        public void OpenEditFriendGroupOverlay(string currentName)
        {
            FriendGroupOverlayTitle = "重命名分组";
            FriendGroupOverlaySubtitle = "请输入新的分组名称";
            FriendGroupInputText = currentName;
            _friendGroupOriginalName = currentName;
            IsFriendGroupEditMode = true;
            FriendGroupStatusMessage = string.Empty;
            IsFriendGroupOverlayOpen = true;
        }

        public void CloseFriendGroupOverlay()
        {
            IsFriendGroupOverlayOpen = false;
            FriendGroupStatusMessage = string.Empty;
        }

        private bool CanConfirmFriendGroupOverlay()
        {
            return !string.IsNullOrWhiteSpace(FriendGroupInputText);
        }

        private async Task ConfirmFriendGroupOverlayAsync()
        {
            var inputName = FriendGroupInputText?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(inputName))
            {
                FriendGroupStatusMessage = "分组名称不能为空。";
                return;
            }

            try
            {
                FriendGroupStatusMessage = IsFriendGroupEditMode ? "正在重命名..." : "正在创建...";

                if (IsFriendGroupEditMode)
                {
                    if (inputName == _friendGroupOriginalName)
                    {
                        CloseFriendGroupOverlay();
                        return;
                    }
                    await RenameFriendGroupAsync(_friendGroupOriginalName, inputName);
                }
                else
                {
                    await CreateFriendGroupAsync(inputName);

                    if (!string.IsNullOrWhiteSpace(_pendingAssignFriendId))
                    {
                        await AssignFriendToGroupAsync(_pendingAssignFriendId, inputName);
                        _pendingAssignFriendId = null;
                    }
                }

                CloseFriendGroupOverlay();
            }
            catch (Exception ex)
            {
                FriendGroupStatusMessage = IsFriendGroupEditMode
                    ? $"重命名失败：{ex.Message}"
                    : $"创建失败：{ex.Message}";
            }
        }

        public void OpenDeleteGroupOverlay(string groupName, string displayName)
        {
            _deleteGroupName = groupName;
            DeleteGroupDisplayName = displayName;
            DeleteGroupStatusMessage = string.Empty;
            IsDeleteGroupOverlayOpen = true;
        }

        public void CloseDeleteGroupOverlay()
        {
            IsDeleteGroupOverlayOpen = false;
            DeleteGroupStatusMessage = string.Empty;
        }

        private bool CanConfirmDeleteGroupOverlay()
        {
            return !string.IsNullOrWhiteSpace(_deleteGroupName);
        }

        private async Task ConfirmDeleteGroupOverlayAsync()
        {
            try
            {
                DeleteGroupStatusMessage = "正在删除...";
                await DeleteFriendGroupAsync(_deleteGroupName);
                CloseDeleteGroupOverlay();
            }
            catch (Exception ex)
            {
                DeleteGroupStatusMessage = $"删除失败：{ex.Message}";
            }
        }

        public async Task SendGroupInvitesAsync(IList<User> selectedFriends)
        {
            if (selectedFriends == null || selectedFriends.Count == 0)
            {
                InviteStatusMessage = "请至少选择一位好友。";
                return;
            }

            if (string.IsNullOrWhiteSpace(SelectedGroupId))
            {
                InviteStatusMessage = "未选择群组会话。";
                return;
            }

            try
            {
                InviteStatusMessage = "正在发送邀请...";
                var friendIds = selectedFriends.Select(f => f.Id).ToList();
                var response = await _socialService.InviteToGroupAsync(_currentUserId, SelectedGroupId, friendIds);
                InviteStatusMessage = response.Message ?? $"已向 {friendIds.Count} 位好友发送入群邀请。";
                IsInviteFriendsOverlayOpen = false;
                ActionStatusMessage = InviteStatusMessage;

                if (response.Success)
                {
                    await ReloadRosterAsync();

                    if (IsGroupMembersPanelOpen)
                    {
                        _groupMembers.Clear();
                        await LoadGroupMembersAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                InviteStatusMessage = $"邀请失败：{ex.Message}";
            }
        }

        private bool CanInviteToGroup() => !_isInitializing && !string.IsNullOrWhiteSpace(SelectedGroupId);

        private void RaisePendingGroupInviteChanged()
        {
            OnPropertyChanged(nameof(HasPendingGroupInvites));
            OnPropertyChanged(nameof(PendingGroupInviteCount));
        }

        private void RaisePendingInviteApprovalChanged()
        {
            OnPropertyChanged(nameof(HasPendingInviteApprovals));
            OnPropertyChanged(nameof(PendingInviteApprovalCount));
        }

        /// <summary>处理群主收到的入群邀请审批通知（由 View 调用）。</summary>
        public void HandleGroupInviteApprovalNotify(IMGroupInviteApprovalNotify notify)
        {
            if (notify == null) return;

            var existing = PendingInviteApprovals.FirstOrDefault(
                x => x.GroupId == notify.GroupId && x.InviteeId == notify.InviteeId);
            if (existing != null)
            {
                PendingInviteApprovals.Remove(existing);
            }

            var item = new GroupInviteApprovalItem
            {
                GroupId = notify.GroupId,
                GroupName = string.IsNullOrWhiteSpace(notify.GroupName) ? notify.GroupId.ToString() : notify.GroupName,
                InviterId = notify.InviterId,
                InviterName = string.IsNullOrWhiteSpace(notify.InviterName) ? notify.InviterId.ToString() : notify.InviterName,
                InviteeId = notify.InviteeId,
                Timestamp = notify.Timestamp
            };
            PendingInviteApprovals.Insert(0, item);
            RaisePendingInviteApprovalChanged();
            ActionStatusMessage = $"{item.InviterName} 申请邀请用户 {item.InviteeId} 加入群组「{item.GroupName}」，请审批。";
        }

        /// <summary>处理收到的群组解散通知（由 View 调用）。</summary>
        public void HandleGroupDisbandNotify(IMGroupDisbandNotify notify)
        {
            if (notify == null) return;

            var groupIdStr = notify.GroupId.ToString();
            // 本地标记群组为已解散，但保留成员与缓存消息
            _ = _socialService.MarkGroupDisbandedLocalAsync(groupIdStr);

            var target = Groups.FirstOrDefault(g => string.Equals(g.Id, groupIdStr, StringComparison.Ordinal));
            if (target != null)
            {
                target.IsDisbanded = true;
            }

            var name = string.IsNullOrWhiteSpace(notify.GroupName) ? groupIdStr : notify.GroupName;
            ActionStatusMessage = $"群组「{name}」已被群主解散，你仍可查看本地缓存的历史消息。";
        }

        /// <summary>处理群主对自己发起的邀请的审批结果通知（由 View 调用）。</summary>
        public void HandleGroupInviteResultNotify(IMGroupInviteResultNotify notify)
        {
            if (notify == null) return;

            var groupName = string.IsNullOrWhiteSpace(notify.GroupName)
                ? notify.GroupId.ToString()
                : notify.GroupName;

            // 通知消息中不携带被邀请者昵称；尝试从已知用户缓存中查找，否则回退到 ID 字符串。
            var inviteeName = _knownUsersById.TryGetValue(notify.InviteeId.ToString(), out var knownUser) && !string.IsNullOrWhiteSpace(knownUser?.Username)
                ? knownUser.Username
                : notify.InviteeId.ToString();

            ActionStatusMessage = notify.Approved
                ? $"你邀请 {inviteeName} 加入群组「{groupName}」的申请已被群主批准。"
                : $"你邀请 {inviteeName} 加入群组「{groupName}」的申请已被群主拒绝。";
        }

        public async Task ApproveInviteApprovalAsync(GroupInviteApprovalItem item)
        {
            if (item == null) return;

            try
            {
                ActionStatusMessage = "正在批准入群邀请...";
                await _socialService.ReviewGroupInviteApprovalAsync(_currentUserId, item.GroupId, item.InviteeId, true);
                PendingInviteApprovals.Remove(item);
                RaisePendingInviteApprovalChanged();
                ActionStatusMessage = $"已批准 {item.InviterName} 发起的邀请。";
            }
            catch (Exception ex)
            {
                ActionStatusMessage = $"批准入群邀请失败：{ex.Message}";
            }
        }

        public async Task RejectInviteApprovalAsync(GroupInviteApprovalItem item)
        {
            if (item == null) return;

            try
            {
                ActionStatusMessage = "正在拒绝入群邀请...";
                await _socialService.ReviewGroupInviteApprovalAsync(_currentUserId, item.GroupId, item.InviteeId, false);
                PendingInviteApprovals.Remove(item);
                RaisePendingInviteApprovalChanged();
                ActionStatusMessage = $"已拒绝 {item.InviterName} 发起的邀请。";
            }
            catch (Exception ex)
            {
                ActionStatusMessage = $"拒绝入群邀请失败：{ex.Message}";
            }
        }

        /// <summary>
        /// 左滑群组项触发：若群组已解散则执行删除（彻底移除本地记录并释放群组名称）；
        /// 若为活跃群且当前用户是群主则解散群组；否则退出群组。
        /// </summary>
        public async Task RemoveGroupAsync(string groupId)
        {
            if (string.IsNullOrWhiteSpace(groupId))
            {
                return;
            }

            var group = Groups.FirstOrDefault(g => string.Equals(g.Id, groupId, StringComparison.Ordinal));
            if (group == null)
            {
                return;
            }

            var removedActiveConversation = string.Equals(SelectedGroupId, groupId, StringComparison.Ordinal);

            try
            {
                if (group.IsDisbanded)
                {
                    ActionStatusMessage = "正在删除群组...";
                    await _socialService.DeleteGroupAsync(groupId);
                    Groups.Remove(group);
                    ActionStatusMessage = $"已删除群组「{group.Name}」，群组名称已释放且不可恢复。";
                }
                else
                {
                    var isOwner = string.Equals(group.CreatorId, _currentUserId, StringComparison.Ordinal);
                    if (isOwner)
                    {
                        ActionStatusMessage = "正在解散群组...";
                        await _socialService.DisbandGroupAsync(_currentUserId, groupId);
                        group.IsDisbanded = true;
                        ActionStatusMessage = $"已解散群组「{group.Name}」，仍保留本地历史消息。";
                    }
                    else
                    {
                        ActionStatusMessage = "正在退出群组...";
                        await _socialService.LeaveGroupAsync(_currentUserId, groupId);
                        Groups.Remove(group);
                        ActionStatusMessage = $"已退出群组「{group.Name}」。";
                    }
                }

                if (removedActiveConversation)
                {
                    await SelectGroupAsync(null);
                }

                await ReloadRosterAsync();
                RefreshConversationState();
            }
            catch (Exception ex)
            {
                var actionDesc = group.IsDisbanded ? "删除" : (string.Equals(group.CreatorId, _currentUserId, StringComparison.Ordinal) ? "解散" : "退出");
                ActionStatusMessage = $"{actionDesc}群组失败：{ex.Message}";
            }
        }

        #endregion

        public async Task UpdateStatusAsync(UserStatus status)
        {
            await _socialService.UpdateUserStatusAsync(_currentUserId, status);
            await ReloadRosterAsync();
        }

        public async Task ClearChatAsync()
        {
            // 在 await 前捕获当前会话快照，避免用户切换会话后操作到错误对话
            var capturedIsGroup = !string.IsNullOrWhiteSpace(SelectedGroupId);
            var capturedConversationId = capturedIsGroup ? SelectedGroupId : SelectedFriendId;

            if (string.IsNullOrWhiteSpace(capturedConversationId))
            {
                ActionStatusMessage = "请先选择一个会话。";
                return;
            }

            try
            {
                ActionStatusMessage = "正在清理聊天记录...";
                await _socialService.ClearConversationWithArchiveAsync(_currentUserId, capturedConversationId, capturedIsGroup);

                // await 后验证用户未切换到其他会话
                if (capturedIsGroup && !string.Equals(SelectedGroupId, capturedConversationId, StringComparison.Ordinal))
                {
                    return;
                }

                if (!capturedIsGroup && !string.Equals(SelectedFriendId, capturedConversationId, StringComparison.Ordinal))
                {
                    return;
                }

                if (capturedIsGroup)
                {
                    _groupMessages.Clear();
                    _groupMessageItems.Clear();
                    await ApplyGroupConversationStatesAsync(new[] { capturedConversationId });
                    ResortGroupsForDisplay();
                }
                else
                {
                    _messages.Clear();
                    _messageItems.Clear();
                    await ApplyDirectConversationStatesAsync(new[] { capturedConversationId });
                    ResortFriendsForDisplay();
                }

                RefreshConversationState();
                ActionStatusMessage = "聊天记录已清理。服务端已标记为已存档，客户端本地数据（含缩略图）已删除。";
            }
            catch (Exception ex)
            {
                ActionStatusMessage = $"清理聊天记录失败：{ex.Message}";
            }
        }

        public async Task RefreshRosterAsync(bool silent = false)
        {
            try
            {
                await ReloadRosterAsync();
            }
            catch (Exception) when (silent)
            {
            }
            catch (Exception ex)
            {
                ActionStatusMessage = $"刷新社交数据失败：{ex.Message}";
            }
        }

        public async Task<bool> HandleIncomingPrivateMessageAsync(IMPrivateChatNotifyMessage notification)
        {
            if (notification == null || string.IsNullOrWhiteSpace(_currentUserId))
            {
                return false;
            }

            var senderId = notification.SenderId.ToString();
            if (string.IsNullOrWhiteSpace(senderId) || string.Equals(senderId, _currentUserId, StringComparison.Ordinal))
            {
                return false;
            }

            UpsertKnownUser(CreateIncomingUserProjection(notification));

            var isActiveConversation = string.Equals(_selectedFriendId, senderId, StringComparison.Ordinal)
                && string.IsNullOrWhiteSpace(_selectedGroupId);
            var savedMessage = await _socialService
                .SaveIncomingGatewayPrivateMessageAsync(_currentUserId, notification, markAsRead: isActiveConversation);

            if (isActiveConversation && _messages.All(message => !string.Equals(message.Id, savedMessage.Id, StringComparison.Ordinal)))
            {
                _messages.Add(savedMessage);
                _messageItems.Add(CreateMessageItem(savedMessage));
                await _socialService.MarkConversationAsReadAsync(_currentUserId, senderId);
            }

            await ApplyDirectConversationStatesAsync(new[] { senderId });
            ResortFriendsForDisplay();

            var senderDisplayName = ResolveUserDisplayName(senderId);
            ActionStatusMessage = isActiveConversation
                ? $"收到来自 {senderDisplayName} 的新消息，已同步到当前会话。"
                : $"收到来自 {senderDisplayName} 的新消息。";

            if (isActiveConversation)
            {
                RefreshConversationState();
                return false;
            }

            return true;
        }

        public async Task<bool> HandleIncomingGroupMessageAsync(IMGroupChatNotifyMessage notification)
        {
            if (notification == null || string.IsNullOrWhiteSpace(_currentUserId))
            {
                return false;
            }

            var senderId = notification.SenderId.ToString();
            var groupId = notification.GroupId.ToString();
            if (string.IsNullOrWhiteSpace(groupId))
            {
                return false;
            }

            var isOwnMessage = string.Equals(senderId, _currentUserId, StringComparison.Ordinal);
            UpsertKnownUser(CreateIncomingUserProjection(notification));

            var hadGroup = Groups.Any(group => string.Equals(group.Id, groupId, StringComparison.Ordinal));
            var isActiveConversation = string.Equals(_selectedGroupId, groupId, StringComparison.Ordinal)
                && string.IsNullOrWhiteSpace(_selectedFriendId);

            var savedMessage = await _socialService
                .SaveIncomingGatewayGroupMessageAsync(_currentUserId, notification, markAsRead: isActiveConversation || isOwnMessage);

            if (!hadGroup)
            {
                await RefreshRosterAsync(silent: true);
            }

            if (isActiveConversation && _groupMessages.All(message => !string.Equals(message.Id, savedMessage.Id, StringComparison.Ordinal)))
            {
                _groupMessages.Add(savedMessage);
                _groupMessageItems.Add(CreateMessageItem(savedMessage));
                await _socialService.MarkGroupConversationAsReadAsync(_currentUserId, groupId);
            }

            await ApplyGroupConversationStatesAsync(new[] { groupId });
            ResortGroupsForDisplay();

            var groupDisplayName = ResolveGroupDisplayName(groupId);
            ActionStatusMessage = isActiveConversation || isOwnMessage
                ? $"群 {groupDisplayName} 的新消息已同步。"
                : $"群 {groupDisplayName} 收到新消息。";

            if (isActiveConversation || isOwnMessage)
            {
                RefreshConversationState();
                return false;
            }

            return true;
        }

        public Task<UserStatus> GetStatusAsync(string userId)
        {
            return _socialService.GetUserStatusAsync(userId);
        }

        public void HandleContactOnlineStatus(IMContactOnlineStatusMessage statusMessage)
        {
            if (statusMessage == null || string.IsNullOrWhiteSpace(_currentUserId))
            {
                return;
            }

            var contactId = statusMessage.UserId.ToString();
            var newStatus = statusMessage.OnlineStatus switch
            {
                IMOnlineStatus.Online => UserStatus.Online,
                IMOnlineStatus.Away => UserStatus.Away,
                IMOnlineStatus.Busy => UserStatus.Busy,
                IMOnlineStatus.Invisible => UserStatus.Invisible,
                _ => UserStatus.Offline
            };

            var friend = Friends.FirstOrDefault(f => string.Equals(f.Id, contactId, StringComparison.Ordinal));
            if (friend != null)
            {
                friend.Status = newStatus;
                OnPropertyChanged(nameof(OnlineFriendsCount));
            }

            if (_knownUsersById.TryGetValue(contactId, out var knownUser))
            {
                knownUser.Status = newStatus;
            }

            RefreshConversationState();
        }

        /// <summary>
        /// 处理来自服务器的好友资料变更推送，静默更新本地好友列表中的昵称、头像和简介。
        /// 此方法应在 UI 线程上调用。
        /// </summary>
        public void HandleContactProfileUpdate(IMContactProfileUpdateMessage updateMessage)
        {
            if (updateMessage == null || string.IsNullOrWhiteSpace(_currentUserId))
            {
                return;
            }

            var contactId = updateMessage.UserId.ToString();

            var friend = Friends.FirstOrDefault(f => string.Equals(f.Id, contactId, StringComparison.Ordinal));
            if (friend != null)
            {
                if (updateMessage.Nickname != null)
                    friend.Nickname = updateMessage.Nickname;
                if (updateMessage.Avatar != null)
                    friend.Avatar = updateMessage.Avatar;
                friend.Bio = updateMessage.Bio;
            }

            if (_knownUsersById.TryGetValue(contactId, out var knownUser))
            {
                if (updateMessage.Nickname != null)
                    knownUser.Nickname = updateMessage.Nickname;
                if (updateMessage.Avatar != null)
                    knownUser.Avatar = updateMessage.Avatar;
                knownUser.Bio = updateMessage.Bio;
            }

            // 若当前会话对方正是该用户，刷新会话视图以呈现最新显示名
            RefreshConversationState();
        }

        private void StartNotificationSubscriptions()
        {
            if (_notificationSubscribed)
            {
                return;
            }

            if (!ImIdentity.TryResolveUserId(_currentUserId, out var numericUserId))
            {
                return;
            }

            var gatewayClient = _socialService.GatewayClient;
            gatewayClient.SystemNotificationReceived += OnSystemNotificationReceived;
            gatewayClient.PrivateChatReceived += OnPrivateChatReceived;
            gatewayClient.GroupChatReceived += OnGroupChatReceived;
            gatewayClient.ContactOnlineStatusReceived += OnContactOnlineStatusReceived;
            gatewayClient.ContactProfileUpdateReceived += OnContactProfileUpdateReceived;
            gatewayClient.GroupInviteReceived += OnGroupInviteReceived;
            gatewayClient.GroupJoinApplyReceived += OnGroupJoinApplyReceived;
            gatewayClient.GroupInviteApprovalReceived += OnGroupInviteApprovalReceived;
            gatewayClient.GroupInviteResultReceived += OnGroupInviteResultReceived;
            gatewayClient.GroupDisbandReceived += OnGroupDisbandReceived;

            _ = gatewayClient.StartRealtimeNotificationsAsync(numericUserId);
            _notificationSubscribed = true;
        }

        private void UnsubscribeFromNotifications()
        {
            if (!_notificationSubscribed)
            {
                return;
            }

            var gatewayClient = _socialService.GatewayClient;
            gatewayClient.SystemNotificationReceived -= OnSystemNotificationReceived;
            gatewayClient.PrivateChatReceived -= OnPrivateChatReceived;
            gatewayClient.GroupChatReceived -= OnGroupChatReceived;
            gatewayClient.ContactOnlineStatusReceived -= OnContactOnlineStatusReceived;
            gatewayClient.ContactProfileUpdateReceived -= OnContactProfileUpdateReceived;
            gatewayClient.GroupInviteReceived -= OnGroupInviteReceived;
            gatewayClient.GroupJoinApplyReceived -= OnGroupJoinApplyReceived;
            gatewayClient.GroupInviteApprovalReceived -= OnGroupInviteApprovalReceived;
            gatewayClient.GroupInviteResultReceived -= OnGroupInviteResultReceived;
            gatewayClient.GroupDisbandReceived -= OnGroupDisbandReceived;

            _ = gatewayClient.StopRealtimeNotificationsAsync();
            _notificationSubscribed = false;
        }

        private async void OnSystemNotificationReceived(object? sender, IMSystemNotificationMessage notification)
        {
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                if (notification == null || string.IsNullOrWhiteSpace(_currentUserId))
                {
                    return;
                }

                if (notification.TargetUserId != 0
                    && (!ImIdentity.TryResolveUserId(_currentUserId, out var uid) || notification.TargetUserId != uid))
                {
                    return;
                }

                await RefreshRosterAsync(silent: true);
            });
        }

        private async void OnPrivateChatReceived(object? sender, IMPrivateChatNotifyMessage message)
        {
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var shouldAnimate = await HandleIncomingPrivateMessageAsync(message);
                if (shouldAnimate)
                {
                    ChatAnimationRequested?.Invoke(message.SenderId.ToString(), false);
                }
            });
        }

        private async void OnGroupChatReceived(object? sender, IMGroupChatNotifyMessage message)
        {
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var shouldAnimate = await HandleIncomingGroupMessageAsync(message);
                if (shouldAnimate)
                {
                    ChatAnimationRequested?.Invoke(message.GroupId.ToString(), true);
                }
            });
        }

        private async void OnContactOnlineStatusReceived(object? sender, IMContactOnlineStatusMessage message)
        {
            // 关键修复：原实现使用 Dispatcher.UIThread.Invoke（同步阻塞）。
            // 当 UI 线程被其它页面（如 FlowerMerchant）的 DataContext 绑定初始化占用时，
            // 同步 Invoke 会死锁。改为 InvokeAsync 后，回调仅排队等待，不阻塞后台 IM 线程。
            await Dispatcher.UIThread.InvokeAsync(() => HandleContactOnlineStatus(message));
        }

        private async void OnContactProfileUpdateReceived(object? sender, IMContactProfileUpdateMessage message)
        {
            await Dispatcher.UIThread.InvokeAsync(() => HandleContactProfileUpdate(message));
        }

        private async void OnGroupInviteReceived(object? sender, IMGroupInviteNotify notify)
        {
            await Dispatcher.UIThread.InvokeAsync(() => HandleGroupInviteNotify(notify));
        }

        private async void OnGroupJoinApplyReceived(object? sender, IMGroupJoinApplyNotify notify)
        {
            await Dispatcher.UIThread.InvokeAsync(() => HandleGroupJoinApplyNotify(notify));
        }

        private async void OnGroupInviteApprovalReceived(object? sender, IMGroupInviteApprovalNotify notify)
        {
            await Dispatcher.UIThread.InvokeAsync(() => HandleGroupInviteApprovalNotify(notify));
        }

        private async void OnGroupInviteResultReceived(object? sender, IMGroupInviteResultNotify notify)
        {
            await Dispatcher.UIThread.InvokeAsync(() => HandleGroupInviteResultNotify(notify));
        }

        private async void OnGroupDisbandReceived(object? sender, IMGroupDisbandNotify notify)
        {
            await Dispatcher.UIThread.InvokeAsync(() => HandleGroupDisbandNotify(notify));
        }

        private const int MaxImageAttachmentCount = 9;

        public void SetPendingAttachment(string path, MediaAttachmentType type)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                ClearPendingAttachmentInternal();
                return;
            }

            AddPendingAttachment(path, type);
        }

        public void AddPendingAttachment(string path, MediaAttachmentType type)
        {
            if (string.IsNullOrWhiteSpace(path)) return;

            if (type == MediaAttachmentType.Image && _pendingAttachments.Count(a => a.Type == MediaAttachmentType.Image) >= MaxImageAttachmentCount)
            {
                ActionStatusMessage = $"单条消息最多支持 {MaxImageAttachmentCount} 张图片。";
                return;
            }

            if (type == MediaAttachmentType.Video || type == MediaAttachmentType.File)
            {
                _pendingAttachments.Clear();
            }

            _pendingAttachments.Add(new PendingSocialAttachment(path, type));
            RaisePendingAttachmentChanged();
        }

        public void RemovePendingAttachment(int index)
        {
            if (index >= 0 && index < _pendingAttachments.Count)
            {
                _pendingAttachments.RemoveAt(index);
                RaisePendingAttachmentChanged();
            }
        }

        /// <summary>
        /// 附加图片文件。
        /// </summary>
        public void AttachImage(string path) => AddPendingAttachment(path, MediaAttachmentType.Image);

        /// <summary>
        /// 附加视频文件。
        /// </summary>
        public void AttachVideo(string path) => AddPendingAttachment(path, MediaAttachmentType.Video);

        /// <summary>
        /// 附加文件。
        /// </summary>
        public void AttachFile(string path) => AddPendingAttachment(path, MediaAttachmentType.File);

        /// <summary>
        /// 打开私聊会话。
        /// </summary>
        public async Task OpenConversationAsync(string friendId)
        {
            if (string.IsNullOrWhiteSpace(friendId))
            {
                return;
            }

            SelectedFriendId = friendId;
            SelectedGroupId = null;
            await RefreshSelectedConversationAsync();
        }

        /// <summary>
        /// 打开群聊会话。
        /// </summary>
        public async Task OpenGroupConversationAsync(string groupId)
        {
            if (string.IsNullOrWhiteSpace(groupId))
            {
                return;
            }

            SelectedGroupId = groupId;
            SelectedFriendId = null;
            await RefreshSelectedConversationAsync();
        }

        /// <summary>
        /// 退出群组。
        /// </summary>
        public async Task LeaveGroupAsync(string groupId)
        {
            if (string.IsNullOrWhiteSpace(groupId) || string.IsNullOrWhiteSpace(_currentUserId))
            {
                return;
            }

            try
            {
                await _socialService.LeaveGroupAsync(_currentUserId, groupId);
                ActionStatusMessage = "已退出群组。";

                var group = Groups.FirstOrDefault(g => string.Equals(g.Id, groupId, StringComparison.Ordinal));
                if (group != null)
                {
                    Groups.Remove(group);
                }

                if (string.Equals(SelectedGroupId, groupId, StringComparison.Ordinal))
                {
                    SelectedGroupId = null;
                    _groupMessages.Clear();
                    _groupMessageItems.Clear();
                }
            }
            catch (Exception ex)
            {
                ActionStatusMessage = $"退出群组失败：{ex.Message}";
            }
        }

        /// <summary>
        /// 添加好友。
        /// </summary>
        public async Task AddFriendAsync(string userId)
        {
            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(_currentUserId))
            {
                ActionStatusMessage = "请输入有效的用户 ID。";
                return;
            }

            try
            {
                var users = await _socialService.GetUsersByIdsAsync(new List<string> { userId });
                var user = users.FirstOrDefault();
                if (user == null)
                {
                    ActionStatusMessage = $"未找到用户 {userId}。";
                    return;
                }

                var success = await _socialService.SendFriendRequestAsync(_currentUserId, user.Username);
                ActionStatusMessage = success
                    ? $"已向 {user.Username} 发送好友请求。"
                    : $"发送好友请求失败。";

                await ReloadRosterAsync();
            }
            catch (Exception ex)
            {
                ActionStatusMessage = $"添加好友失败：{ex.Message}";
            }
        }

        public void ClearPendingAttachment()
        {
            ClearPendingAttachmentInternal();
            ActionStatusMessage = "已清除待发送附件。";
        }

        private bool _isEmojiPickerOpen;

        public bool IsEmojiPickerOpen
        {
            get => _isEmojiPickerOpen;
            set
            {
                _isEmojiPickerOpen = value;
                OnPropertyChanged();
            }
        }

        public void InsertEmoji(string emoji)
        {
            if (string.IsNullOrEmpty(emoji)) return;
            NewMessageContent = (NewMessageContent ?? string.Empty) + emoji;
        }

        public async Task<bool> ForwardMessageAsync(string targetId, bool isGroup, string serializedContent)
        {
            if (string.IsNullOrWhiteSpace(serializedContent) || string.IsNullOrWhiteSpace(_currentUserId))
            {
                ActionStatusMessage = "转发失败：消息内容或用户信息缺失。";
                return false;
            }

            try
            {
                // 反序列化以获取原始消息类型，确保转发时保留原始卡片格式（图片/视频等）
                var content = RichMessageContentSerializer.Deserialize(MessageType.Text, serializedContent);
                await _socialService.SendForwardedMessageAsync(_currentUserId, targetId, serializedContent, content.Type, isGroup).ConfigureAwait(false);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SocialViewModel] 转发消息失败: {ex.Message}");
                ActionStatusMessage = "转发失败，请稍后重试。";
                return false;
            }
        }

        private async Task ReloadRosterAsync()
        {
            SetRosterLoading(true);

            try
            {
                var friendsTask = _socialService.GetFriendsAsync(_currentUserId);
                var suggestionsTask = _socialService.GetSuggestedFriendsAsync(_currentUserId);
                var groupsTask = _socialService.GetUserGroupsAsync(_currentUserId);

                await Task.WhenAll(friendsTask, suggestionsTask, groupsTask);

                var friends = friendsTask.Result;
                var suggestions = suggestionsTask.Result;
                var groups = groupsTask.Result;

                ReplaceCollection(Friends, friends);
                ReplaceCollection(SuggestedFriends, suggestions);
                ReplaceCollection(Groups, groups);

                foreach (var g in Groups)
                {
                    g.RefreshRemoveGroupActionText(_currentUserId);
                }

                UpdateKnownUsers(friends);
                UpdateKnownUsers(suggestions);

                var currentPassportId = ImIdentity.ResolvePassportId(App.CurrentUser);
                if (App.CurrentUser != null && !string.IsNullOrWhiteSpace(currentPassportId))
                {
                    _knownUsersById[currentPassportId] = CreateCurrentUserProjection(App.CurrentUser, currentPassportId);
                }

                var convStatesTask = Task.WhenAll(
                    ApplyDirectConversationStatesAsync(friends.Select(friend => friend.Id).ToList()),
                    ApplyGroupConversationStatesAsync(groups.Select(group => group.Id).ToList())
                );

                await convStatesTask;

                await MergeServerConversationStatesAsync();

                var hasDirectAdds = await MergePendingGroupInvitesAsync();
                await LoadContactGroupDefinitionsAsync();

                List<Group> groupsToSync = groups;
                if (hasDirectAdds)
                {
                    groupsToSync = await _socialService.GetUserGroupsAsync(_currentUserId);
                    ReplaceCollection(Groups, groupsToSync);
                }

                var memberCountTask = _socialService.SyncGroupMemberCountsAsync(_currentUserId, groupsToSync);
                var inviteApprovalsTask = MergePendingInviteApprovalsAsync(groupsToSync);

                await Task.WhenAll(memberCountTask, inviteApprovalsTask);

                ResortFriendsForDisplay();
                ResortGroupsForDisplay();

                RestoreSelectionReferences();
                OnPropertyChanged(nameof(OnlineFriendsCount));
                OnPropertyChanged(nameof(SuggestedFriendsCount));
                OnPropertyChanged(nameof(HasSuggestedFriends));
                RefreshConversationState();
            }
            finally
            {
                SetRosterLoading(false);
            }
        }

        /// <summary>
        /// 从服务端拉取待处理入群邀请，将离线期间收到的邀请合并到本地 <see cref="PendingGroupInvites"/> 列表。
        /// 已存在于列表中的邀请（用户当前会话已收到通知推送）不会重复添加。
        /// </summary>
        private async Task LoadContactGroupDefinitionsAsync()
        {
            try
            {
                var response = await _socialService.UpdateContactGroupAsync(_currentUserId, "list", string.Empty, null, null);
                if (response.Success && response.ContactGroups != null)
                {
                    _contactGroupDefinitions = response.ContactGroups;
                }
            }
            catch
            {
            }
        }

        private async Task<bool> MergePendingGroupInvitesAsync()
        {
            try
            {
                var serverInvites = await _socialService.GetPendingGroupInvitesAsync(_currentUserId)
                    .ConfigureAwait(false);

                if (serverInvites == null || serverInvites.Count == 0)
                {
                    return false;
                }

                var existingGroupIds = new HashSet<ulong>(
                    PendingGroupInvites.Select(x => x.GroupId));

                bool addedConsentInvites = false;
                bool hasDirectAdds = false;

                foreach (var entry in serverInvites)
                {
                    if (!entry.RequiresConsent)
                    {
                        await _socialService.EnsureGroupInLocalDatabaseAsync(
                            _currentUserId, entry.GroupId.ToString(), entry.GroupName);
                        hasDirectAdds = true;
                        continue;
                    }

                    if (existingGroupIds.Contains(entry.GroupId))
                    {
                        continue;
                    }

                    PendingGroupInvites.Add(new GroupInviteItem
                    {
                        GroupId = entry.GroupId,
                        GroupName = entry.GroupName,
                        InviterId = entry.InviterId,
                        InviterName = entry.InviterName,
                        RequiresConsent = true,
                        Timestamp = entry.Timestamp
                    });
                    addedConsentInvites = true;
                }

                if (addedConsentInvites)
                {
                    RaisePendingGroupInviteChanged();
                    ActionStatusMessage = PendingGroupInvites.Count == 1
                        ? "你有 1 条待处理入群邀请，请在左侧入群邀请中处理。"
                        : $"你有 {PendingGroupInvites.Count} 条待处理入群邀请，请在左侧入群邀请中处理。";
                }

                return hasDirectAdds;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SocialViewModel] 拉取待处理入群邀请失败：{ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 从服务端拉取当前用户所拥有的群组的待审批邀请列表，
        /// 将离线期间积压的审批通知合并到 <see cref="PendingInviteApprovals"/> 集合，
        /// 避免因重连期间推送漏接而导致审批列表空白。
        /// </summary>
        private async Task MergePendingInviteApprovalsAsync(IEnumerable<Group> ownedGroups)
        {
            if (ownedGroups == null) return;
            try
            {
                foreach (var group in ownedGroups)
                {
                    if (group.IsDisbanded) continue;
                    if (!string.Equals(group.CreatorId, _currentUserId, StringComparison.Ordinal)) continue;
                    if (!ulong.TryParse(group.Id, out var groupIdValue)) continue;

                    var serverApprovals = await _socialService
                        .GetPendingInviteApprovalsAsync(_currentUserId, groupIdValue)
                        .ConfigureAwait(false);

                    if (serverApprovals == null || serverApprovals.Count == 0) continue;

                    var existingKeys = new HashSet<(ulong, ulong)>(
                        PendingInviteApprovals.Select(x => (x.GroupId, x.InviteeId)));

                    foreach (var entry in serverApprovals)
                    {
                        if (existingKeys.Contains((entry.GroupId, entry.InviteeId))) continue;

                        PendingInviteApprovals.Insert(0, new GroupInviteApprovalItem
                        {
                            GroupId = entry.GroupId,
                            GroupName = string.IsNullOrWhiteSpace(entry.GroupName)
                                ? entry.GroupId.ToString()
                                : entry.GroupName,
                            InviterId = entry.InviterId,
                            InviterName = string.IsNullOrWhiteSpace(entry.InviterName)
                                ? entry.InviterId.ToString()
                                : entry.InviterName,
                            InviteeId = entry.InviteeId,
                            Timestamp = entry.Timestamp
                        });
                    }
                }

                if (PendingInviteApprovals.Count > 0)
                {
                    RaisePendingInviteApprovalChanged();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SocialViewModel] 拉取待审批邀请失败：{ex.Message}");
            }
        }

        /// <summary>
        /// 从服务端拉取会话列表，将离线期间产生的未读计数合并到本地好友/群组列表中，
        /// 并计算 <see cref="TotalOfflineUnreadCount"/>。
        /// </summary>
        private async Task MergeServerConversationStatesAsync()
        {
            if (!UsesGatewayContacts)
            {
                TotalOfflineUnreadCount = 0;
                return;
            }

            var serverConversations = await _socialService
                .GetServerConversationListAsync(_currentUserId);

            if (serverConversations.Count == 0)
            {
                TotalOfflineUnreadCount = 0;
                return;
            }

            // 构建服务端会话 PeerId → UnreadCount 索引，按会话类型分开，
            // 方便后续 O(1) 查找而无需遍历整个列表。
            var serverPrivate = new Dictionary<string, int>(StringComparer.Ordinal);
            var serverGroup = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (var conv in serverConversations)
            {
                if (conv.UnreadCount <= 0)
                {
                    continue;
                }

                var key = conv.PeerId.ToString();
                if (conv.ChatRelationType == IMChatRelationType.Group)
                {
                    serverGroup[key] = conv.UnreadCount;
                }
                else
                {
                    serverPrivate[key] = conv.UnreadCount;
                }
            }

            var totalOffline = 0;
            _offlineUnreadDeltas.Clear();

            // 合并私聊好友的未读计数：取服务端与本地较大值，差值即为离线未读消息数。
            foreach (var friend in Friends)
            {
                if (string.IsNullOrWhiteSpace(friend.PassportId))
                {
                    continue;
                }

                if (serverPrivate.TryGetValue(friend.PassportId, out var serverCount)
                    && serverCount > friend.UnreadCount)
                {
                    var delta = serverCount - friend.UnreadCount;
                    totalOffline += delta;
                    _offlineUnreadDeltas[$"f:{friend.PassportId}"] = delta;
                    friend.UnreadCount = serverCount;
                }
            }

            // 合并群聊的未读计数。
            foreach (var group in Groups)
            {
                if (string.IsNullOrWhiteSpace(group.Id))
                {
                    continue;
                }

                if (serverGroup.TryGetValue(group.Id, out var serverCount)
                    && serverCount > group.UnreadCount)
                {
                    var delta = serverCount - group.UnreadCount;
                    totalOffline += delta;
                    _offlineUnreadDeltas[$"g:{group.Id}"] = delta;
                    group.UnreadCount = serverCount;
                }
            }

            TotalOfflineUnreadCount = totalOffline;
        }

        /// <summary>
        /// 用户打开某好友会话后，从离线未读总数中减去该会话对应的未读数。
        /// </summary>
        private void DecrementOfflineUnreadForFriend(string friendId)
        {
            if (_totalOfflineUnreadCount <= 0 || string.IsNullOrWhiteSpace(friendId))
            {
                return;
            }

            var friend = Friends.FirstOrDefault(f =>
                string.Equals(f.Id, friendId, StringComparison.Ordinal)
                || string.Equals(f.PassportId, friendId, StringComparison.Ordinal));

            if (friend == null)
            {
                return;
            }

            var key = $"f:{friend.PassportId}";
            if (_offlineUnreadDeltas.TryGetValue(key, out var delta) && delta > 0)
            {
                TotalOfflineUnreadCount = Math.Max(0, _totalOfflineUnreadCount - delta);
                _offlineUnreadDeltas.Remove(key);
            }
        }

        /// <summary>
        /// 用户打开某群聊后，从离线未读总数中减去该群对应的未读数。
        /// </summary>
        private void DecrementOfflineUnreadForGroup(string groupId)
        {
            if (_totalOfflineUnreadCount <= 0 || string.IsNullOrWhiteSpace(groupId))
            {
                return;
            }

            var key = $"g:{groupId}";
            if (_offlineUnreadDeltas.TryGetValue(key, out var delta) && delta > 0)
            {
                TotalOfflineUnreadCount = Math.Max(0, _totalOfflineUnreadCount - delta);
                _offlineUnreadDeltas.Remove(key);
            }
        }

        private async Task RefreshSelectedConversationAsync()
        {
            if (!string.IsNullOrWhiteSpace(_selectedFriendId) && string.IsNullOrWhiteSpace(_selectedGroupId))
            {
                await LoadMessagesAsync(_selectedFriendId);
                return;
            }

            if (!string.IsNullOrWhiteSpace(_selectedGroupId) && string.IsNullOrWhiteSpace(_selectedFriendId))
            {
                await LoadGroupMessagesAsync(_selectedGroupId);
                return;
            }

            Interlocked.Increment(ref _conversationLoadVersion);
            ClearConversationCollections();
            SetConversationLoading(false);
            RefreshConversationState();
        }

        private async Task LoadMessagesAsync(string friendId)
        {
            var requestVersion = Interlocked.Increment(ref _conversationLoadVersion);

            _messages.Clear();
            _messageItems.Clear();
            RefreshConversationState();

            if (string.IsNullOrWhiteSpace(friendId))
            {
                SetConversationLoading(false);
                return;
            }

            SetConversationLoading(true);

            try
            {
                // When the friend has a pending unread count (which includes offline messages stored
                // server-side) and we are connected via the IM gateway, pull the server chat history
                // first so that messages received while this client was offline are persisted locally
                // before we read from the local database.
                var friend = Friends.FirstOrDefault(f =>
                    string.Equals(f.PassportId, friendId, StringComparison.Ordinal)
                    || string.Equals(f.Id, friendId, StringComparison.Ordinal));

                List<Horizon.Game.GengDi.Models.IMMessage> messages;
                if (UsesGatewayContacts && friend?.UnreadCount > 0)
                {
                    messages = await _socialService.FetchAndPersistOfflineMessagesAsync(_currentUserId, friendId);
                }
                else
                {
                    messages = await _socialService.GetMessagesAsync(_currentUserId, friendId);
                }

                var senderIds = messages
                    .Select(message => message.SenderId)
                    .Where(senderId => !string.IsNullOrWhiteSpace(senderId) && !string.Equals(senderId, _currentUserId, StringComparison.Ordinal))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

                if (senderIds.Count > 0)
                {
                    var users = await _socialService.GetUsersByIdsAsync(senderIds);
                    if (!IsCurrentFriendRequest(requestVersion, friendId))
                    {
                        return;
                    }

                    UpdateKnownUsers(users);
                }

                if (!IsCurrentFriendRequest(requestVersion, friendId))
                {
                    return;
                }

                ApplyConversationMessages(_messages, _messageItems, messages);
                await _socialService.MarkConversationAsReadAsync(_currentUserId, friendId);
                // 向服务端发送已读回执，重置服务端侧未读计数，避免下次登录仍显示旧的离线未读数。
                _ = _socialService.SendReadReceiptToServerAsync(_currentUserId, friendId);
                // 若该好友在离线未读列表中，打开会话后减去其对应的离线未读数。
                DecrementOfflineUnreadForFriend(friendId);
                await ApplyDirectConversationStatesAsync(new[] { friendId });
                ResortFriendsForDisplay();
                RefreshConversationState();
            }
            catch (Exception ex)
            {
                if (IsCurrentFriendRequest(requestVersion, friendId))
                {
                    ActionStatusMessage = $"加载会话失败：{ex.Message}";
                }
            }
            finally
            {
                SetConversationLoading(false);
            }
        }

        private async Task LoadGroupMessagesAsync(string groupId)
        {
            var requestVersion = Interlocked.Increment(ref _conversationLoadVersion);

            _groupMessages.Clear();
            _groupMessageItems.Clear();
            RefreshConversationState();

            if (string.IsNullOrWhiteSpace(groupId))
            {
                SetConversationLoading(false);
                return;
            }

            SetConversationLoading(true);

            try
            {
                var messages = await _socialService.GetGroupMessagesAsync(groupId);
                var senderIds = messages
                    .Select(message => message.SenderId)
                    .Where(senderId => !string.IsNullOrWhiteSpace(senderId))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();

                if (senderIds.Count > 0)
                {
                    var users = await _socialService.GetUsersByIdsAsync(senderIds);
                    if (!IsCurrentGroupRequest(requestVersion, groupId))
                    {
                        return;
                    }

                    UpdateKnownUsers(users);
                }

                if (!IsCurrentGroupRequest(requestVersion, groupId))
                {
                    return;
                }

                ApplyConversationMessages(_groupMessages, _groupMessageItems, messages);
                await _socialService.MarkGroupConversationAsReadAsync(_currentUserId, groupId);
                // 打开群聊后减去该群的离线未读计数。
                DecrementOfflineUnreadForGroup(groupId);
                await ApplyGroupConversationStatesAsync(new[] { groupId });
                ResortGroupsForDisplay();
                RefreshConversationState();
            }
            catch (Exception ex)
            {
                if (IsCurrentGroupRequest(requestVersion, groupId))
                {
                    ActionStatusMessage = $"加载群消息失败：{ex.Message}";
                }
            }
            finally
            {
                SetConversationLoading(false);
            }
        }

        private void ApplyConversationMessages(
            ObservableCollection<Horizon.Game.GengDi.Models.IMMessage> targetMessages,
            ObservableCollection<ChatMessageItemViewModel> targetItems,
            IEnumerable<Horizon.Game.GengDi.Models.IMMessage> messages)
        {
            targetMessages.Clear();
            targetItems.Clear();

            foreach (var message in messages)
            {
                targetMessages.Add(message);
                targetItems.Add(CreateMessageItem(message));
            }
        }

        private void ReplaceCollection<T>(ObservableCollection<T> target, IEnumerable<T> items)
        {
            target.Clear();

            foreach (var item in items)
            {
                target.Add(item);
            }
        }

        private void RestoreSelectionReferences()
        {
            var matchedFriend = string.IsNullOrWhiteSpace(_selectedFriendId)
                ? null
                : Friends.FirstOrDefault(friend => string.Equals(friend.Id, _selectedFriendId, StringComparison.Ordinal));
            var matchedGroup = string.IsNullOrWhiteSpace(_selectedGroupId)
                ? null
                : Groups.FirstOrDefault(group => string.Equals(group.Id, _selectedGroupId, StringComparison.Ordinal));

            if (!ReferenceEquals(_selectedFriend, matchedFriend))
            {
                if (_selectedFriend != null)
                    _selectedFriend.IsSelected = false;
                _selectedFriend = matchedFriend;
                if (_selectedFriend != null)
                    _selectedFriend.IsSelected = true;
                OnPropertyChanged(nameof(SelectedFriend));
            }

            if (!ReferenceEquals(_selectedGroup, matchedGroup))
            {
                _selectedGroup = matchedGroup;
                OnPropertyChanged(nameof(SelectedGroup));
            }

            if (_selectedFriend == null && !string.IsNullOrWhiteSpace(_selectedFriendId))
            {
                _selectedFriendId = null;
                OnPropertyChanged(nameof(SelectedFriendId));
            }

            if (_selectedGroup == null && !string.IsNullOrWhiteSpace(_selectedGroupId))
            {
                _selectedGroupId = null;
                OnPropertyChanged(nameof(SelectedGroupId));
            }
        }

        private void UpdateKnownUsers(IEnumerable<User> users)
        {
            foreach (var user in users.Where(user => user != null && !string.IsNullOrWhiteSpace(user.Id)))
            {
                _knownUsersById[user.Id] = user;
            }
        }

        private void UpsertKnownUser(User user)
        {
            if (user == null || string.IsNullOrWhiteSpace(user.Id))
            {
                return;
            }

            _knownUsersById[user.Id] = user;
        }

        private async Task ApplyDirectConversationStatesAsync(IEnumerable<string> friendIds = null)
        {
            await ApplyConversationStatesAsync(
                friendIds,
                friend => friend.Id,
                Friends,
                _selectedFriend,
                ids => _socialService.GetDirectConversationStatesAsync(_currentUserId, ids),
                ApplyDirectConversationState);
        }

        private void ApplyDirectConversationState(User friend, SocialService.DirectConversationState state)
        {
            if (friend == null)
            {
                return;
            }

            friend.UnreadCount = state?.UnreadCount ?? 0;
            friend.RecentMessagePreview = BuildMessagePreview(state?.LatestMessage);
            friend.LastMessageAt = state?.LatestMessage?.Timestamp;
        }

        private async Task ApplyGroupConversationStatesAsync(IEnumerable<string> groupIds = null)
        {
            await ApplyConversationStatesAsync(
                groupIds,
                group => group.Id,
                Groups,
                _selectedGroup,
                ids => _socialService.GetGroupConversationStatesAsync(ids, _currentUserId),
                ApplyGroupConversationState);
        }

        private void ApplyGroupConversationState(Group group, SocialService.GroupConversationState state)
        {
            if (group == null)
            {
                return;
            }

            group.UnreadCount = state?.UnreadCount ?? 0;
            group.RecentMessagePreview = BuildMessagePreview(state?.LatestMessage);
            group.LastMessageAt = state?.LatestMessage?.Timestamp;
        }

        /// <summary>
        /// 通用会话状态应用逻辑。从服务层拉取状态字典后，仅更新 <paramref name="targetIds"/> 范围内的条目。
        /// </summary>
        private async Task ApplyConversationStatesAsync<TItem, TState>(
            IEnumerable<string> targetIds,
            Func<TItem, string> getId,
            ObservableCollection<TItem> collection,
            TItem selectedItem,
            Func<List<string>, Task<Dictionary<string, TState>>> fetchStates,
            Action<TItem, TState> applyState)
            where TItem : class
        {
            var resolvedIds = (targetIds ?? collection.Select(getId))
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (resolvedIds.Count == 0)
            {
                return;
            }

            var states = await fetchStates(resolvedIds);

            // 仅更新被查询条目的状态，避免覆盖未查询条目的未读计数
            var targetSet = new HashSet<string>(resolvedIds, StringComparer.Ordinal);
            foreach (var item in collection)
            {
                var id = getId(item);
                if (states.TryGetValue(id, out var state))
                {
                    applyState(item, state);
                }
                else if (targetSet.Contains(id))
                {
                    applyState(item, default);
                }
            }

            if (selectedItem != null && targetSet.Contains(getId(selectedItem)))
            {
                applyState(selectedItem,
                    states.TryGetValue(getId(selectedItem), out var selectedState) ? selectedState : default);
            }
        }

        private void ResortFriendsForDisplay()
        {
            ReorderCollection(Friends, OrderFriendsForDisplay(Friends));
            RebuildFriendGroups();
            OnPropertyChanged(nameof(OnlineFriendsCount));
        }

        private void ResortGroupsForDisplay()
        {
            ReorderCollection(Groups, OrderGroupsForDisplay(Groups));
        }

        private static List<User> OrderFriendsForDisplay(IEnumerable<User> friends)
        {
            return friends?
                .Where(friend => friend != null)
                .OrderByDescending(friend => friend.UnreadCount > 0)
                .ThenByDescending(friend => friend.LastMessageAt ?? DateTime.MinValue)
                .ThenByDescending(friend => GetFriendStatusPriority(friend.Status))
                .ThenBy(friend => friend.Username ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToList()
                ?? new List<User>();
        }

        private void RebuildFriendGroups()
        {
            var grouped = new Dictionary<string, List<User>>();
            if (Friends != null && Friends.Count > 0)
            {
                var ordered = OrderFriendsForDisplay(Friends);
                foreach (var friend in ordered)
                {
                    var key = friend.GroupName ?? string.Empty;
                    if (!grouped.ContainsKey(key))
                        grouped[key] = new List<User>();
                    grouped[key].Add(friend);
                }
            }

            var existingMap = new Dictionary<string, FriendGroupItem>();
            foreach (var g in _friendGroups)
                existingMap[g.GroupName] = g;

            var result = new List<FriendGroupItem>();

            var sortedGroupNames = _contactGroupDefinitions
                .OrderBy(kv => kv.Value)
                .Select(kv => kv.Key)
                .ToList();

            if (!sortedGroupNames.Contains(string.Empty))
                sortedGroupNames.Insert(0, string.Empty);

            foreach (var name in sortedGroupNames.Concat(grouped.Keys.Where(k => !sortedGroupNames.Contains(k))))
            {
                var friends = grouped.ContainsKey(name) ? grouped[name] : new List<User>();

                if (existingMap.TryGetValue(name, out var existingGroup))
                {
                    existingGroup.Friends.Clear();
                    foreach (var f in friends)
                        existingGroup.Friends.Add(f);
                    result.Add(existingGroup);
                }
                else
                {
                    var newGroup = new FriendGroupItem
                    {
                        GroupName = name,
                        SortOrder = _contactGroupDefinitions.TryGetValue(name, out var order) ? order : int.MaxValue
                    };
                    foreach (var f in friends)
                        newGroup.Friends.Add(f);
                    result.Add(newGroup);
                }
            }

            FriendGroups.Clear();
            foreach (var g in result)
                FriendGroups.Add(g);
        }

        public void ToggleFriendGroup(FriendGroupItem group)
        {
            if (group == null) return;
            group.IsExpanded = !group.IsExpanded;
        }

        public async Task CreateFriendGroupAsync(string groupName)
        {
            if (string.IsNullOrWhiteSpace(groupName)) return;
            try
            {
                var response = await _socialService.UpdateContactGroupAsync(_currentUserId, "create", groupName, null, null);
                if (response.Success)
                {
                    _contactGroupDefinitions = response.ContactGroups ?? new Dictionary<string, int>();
                    RebuildFriendGroups();
                    ActionStatusMessage = $"分组「{groupName}」已创建。";
                }
                else
                {
                    ActionStatusMessage = response.Message ?? "创建分组失败。";
                }
            }
            catch (Exception ex)
            {
                ActionStatusMessage = $"创建分组失败：{ex.Message}";
            }
        }

        public async Task RenameFriendGroupAsync(string oldName, string newName)
        {
            if (string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName)) return;
            if (oldName == newName) return;
            try
            {
                var response = await _socialService.UpdateContactGroupAsync(_currentUserId, "rename", oldName, newName, null);
                if (response.Success)
                {
                    _contactGroupDefinitions = response.ContactGroups ?? new Dictionary<string, int>();
                    foreach (var friend in Friends.Where(f => f.GroupName == oldName))
                        friend.GroupName = newName;
                    RebuildFriendGroups();
                    ActionStatusMessage = $"分组「{oldName}」已重命名为「{newName}」。";
                }
                else
                {
                    ActionStatusMessage = response.Message ?? "重命名分组失败。";
                }
            }
            catch (Exception ex)
            {
                ActionStatusMessage = $"重命名分组失败：{ex.Message}";
            }
        }

        public async Task DeleteFriendGroupAsync(string groupName)
        {
            if (string.IsNullOrWhiteSpace(groupName)) return;
            try
            {
                var response = await _socialService.UpdateContactGroupAsync(_currentUserId, "delete", groupName, null, null);
                if (response.Success)
                {
                    _contactGroupDefinitions = response.ContactGroups ?? new Dictionary<string, int>();
                    foreach (var friend in Friends.Where(f => f.GroupName == groupName))
                        friend.GroupName = string.Empty;
                    RebuildFriendGroups();
                    ActionStatusMessage = $"分组「{groupName}」已删除。";
                }
                else
                {
                    ActionStatusMessage = response.Message ?? "删除分组失败。";
                }
            }
            catch (Exception ex)
            {
                ActionStatusMessage = $"删除分组失败：{ex.Message}";
            }
        }

        public async Task AssignFriendToGroupAsync(string friendId, string groupName)
        {
            if (string.IsNullOrWhiteSpace(friendId)) return;
            var currentFriend = Friends.FirstOrDefault(f => f.Id == friendId);
            if (currentFriend != null && string.Equals(currentFriend.GroupName ?? string.Empty, groupName ?? string.Empty, StringComparison.Ordinal))
                return;
            try
            {
                if (!ImIdentity.TryResolveUserId(friendId, out var userId)) return;
                var response = await _socialService.UpdateContactGroupAsync(_currentUserId, "assign", groupName ?? string.Empty, null, new List<ulong> { userId });
                if (response.Success)
                {
                    var friend = Friends.FirstOrDefault(f => f.Id == friendId);
                    if (friend != null)
                        friend.GroupName = groupName ?? string.Empty;
                    RebuildFriendGroups();
                }
                else
                {
                    ActionStatusMessage = response.Message ?? "移动好友失败。";
                }
            }
            catch (Exception ex)
            {
                ActionStatusMessage = $"移动好友失败：{ex.Message}";
            }
        }

        private static List<Group> OrderGroupsForDisplay(IEnumerable<Group> groups)
        {
            return groups?
                .Where(group => group != null)
                .OrderByDescending(group => group.UnreadCount > 0)
                .ThenByDescending(group => group.LastMessageAt ?? DateTime.MinValue)
                .ThenBy(group => group.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToList()
                ?? new List<Group>();
        }

        private static void ReorderCollection<T>(ObservableCollection<T> collection, IReadOnlyList<T> orderedItems)
        {
            if (collection == null || orderedItems == null || collection.Count != orderedItems.Count)
            {
                return;
            }

            for (var targetIndex = 0; targetIndex < orderedItems.Count; targetIndex++)
            {
                var currentIndex = collection.IndexOf(orderedItems[targetIndex]);
                if (currentIndex >= 0 && currentIndex != targetIndex)
                {
                    collection.Move(currentIndex, targetIndex);
                }
            }
        }

        private static int GetFriendStatusPriority(UserStatus status)
        {
            return status switch
            {
                UserStatus.Online => 4,
                UserStatus.Away => 3,
                UserStatus.Busy => 2,
                UserStatus.Invisible => 1,
                _ => 0
            };
        }


        private void ClearConversationCollections()
        {
            _messages.Clear();
            _messageItems.Clear();
            _groupMessages.Clear();
            _groupMessageItems.Clear();
        }

        private void ApplySelectedFriend(User value)
        {
            var groupConversationChanged = false;

            if (!ReferenceEquals(_selectedFriend, value))
            {
                if (_selectedFriend != null)
                    _selectedFriend.IsSelected = false;
                _selectedFriend = value;
                if (_selectedFriend != null)
                    _selectedFriend.IsSelected = true;
                OnPropertyChanged(nameof(SelectedFriend));
            }

            var nextFriendId = value?.Id;
            if (!string.Equals(_selectedFriendId, nextFriendId, StringComparison.Ordinal))
            {
                _selectedFriendId = nextFriendId;
                OnPropertyChanged(nameof(SelectedFriendId));
            }

            if (value != null)
            {
                if (_selectedGroup != null)
                {
                    _selectedGroup = null;
                    OnPropertyChanged(nameof(SelectedGroup));
                }

                if (!string.IsNullOrWhiteSpace(_selectedGroupId))
                {
                    _selectedGroupId = null;
                    OnPropertyChanged(nameof(SelectedGroupId));
                    groupConversationChanged = true;
                }
            }

            if (groupConversationChanged)
            {
                OnPropertyChanged(nameof(IsGroupConversationActive));
                OnPropertyChanged(nameof(GroupMemberCountText));
                _inviteToGroupCommand.RaiseCanExecuteChanged();

                IsGroupMembersPanelOpen = false;
                foreach (var old in _groupMembers)
                    old.Dispose();
                _groupMembers.Clear();
            }

            RefreshConversationState();
            RaiseSendCommandStateChanged();
        }

        private void ApplySelectedGroup(Group value)
        {
            var groupConversationChanged = false;

            if (!ReferenceEquals(_selectedGroup, value))
            {
                _selectedGroup = value;
                OnPropertyChanged(nameof(SelectedGroup));
            }

            var nextGroupId = value?.Id;
            if (!string.Equals(_selectedGroupId, nextGroupId, StringComparison.Ordinal))
            {
                _selectedGroupId = nextGroupId;
                OnPropertyChanged(nameof(SelectedGroupId));
                groupConversationChanged = true;
            }

            if (value != null)
            {
                if (_selectedFriend != null)
                {
                    _selectedFriend.IsSelected = false;
                    _selectedFriend = null;
                    OnPropertyChanged(nameof(SelectedFriend));
                }

                if (!string.IsNullOrWhiteSpace(_selectedFriendId))
                {
                    _selectedFriendId = null;
                    OnPropertyChanged(nameof(SelectedFriendId));
                }
            }

            if (groupConversationChanged)
            {
                OnPropertyChanged(nameof(IsGroupConversationActive));
                _inviteToGroupCommand.RaiseCanExecuteChanged();
            }

            RefreshConversationState();
            RaiseSendCommandStateChanged();
        }
        private void ClearPendingAttachmentInternal()
        {
            _pendingAttachments.Clear();
            RaisePendingAttachmentChanged();
        }

        private void RaisePendingAttachmentChanged()
        {
            OnPropertyChanged(nameof(HasPendingAttachment));
            OnPropertyChanged(nameof(HasPendingAttachments));
            OnPropertyChanged(nameof(PendingAttachmentCount));
            OnPropertyChanged(nameof(PendingAttachments));
            OnPropertyChanged(nameof(PendingAttachmentDisplayName));
            OnPropertyChanged(nameof(PendingAttachmentSummary));
            OnPropertyChanged(nameof(PendingAttachmentKindLabel));
            RaiseSendCommandStateChanged();
        }

        private ChatMessageItemViewModel CreateMessageItem(Horizon.Game.GengDi.Models.IMMessage message)
        {
            return new ChatMessageItemViewModel(message, _currentUserId, ResolveUserDisplayName(message.SenderId));
        }

        private static bool IsGroupMessage(Horizon.Game.GengDi.Models.IMMessage message)
        {
            return message?.IsGroupConversation == true;
        }

        private string ResolveUserDisplayName(string userId)
        {
            if (string.Equals(userId, _currentUserId, StringComparison.Ordinal))
            {
                return App.CurrentUser?.Username ?? "我";
            }

            return _knownUsersById.TryGetValue(userId, out var user)
                ? user.DisplayName
                : userId;
        }

        private string ResolveGroupDisplayName(string groupId)
        {
            return Groups.FirstOrDefault(group => string.Equals(group.Id, groupId, StringComparison.Ordinal))?.Name
                ?? groupId;
        }

        private static string BuildMessagePreview(Horizon.Game.GengDi.Models.IMMessage message)
        {
            if (message == null)
            {
                return string.Empty;
            }

            var content = RichMessageContentSerializer.Deserialize(message);
            var preview = !string.IsNullOrWhiteSpace(content.Text)
                ? content.Text.Trim()
                : content.Type switch
                {
                    MessageType.Image => "[图片]",
                    MessageType.Video => "[视频]",
                    MessageType.LinkCard => string.IsNullOrWhiteSpace(content.Title) ? "[链接卡片]" : $"[链接] {content.Title}",
                    MessageType.File => "[文件]",
                    MessageType.System => "[系统消息]",
                    MessageType.Emoji => "[表情]",
                    _ => string.Empty
                };

            if (string.IsNullOrWhiteSpace(preview))
            {
                return string.Empty;
            }

            const int maxPreviewLength = 28;
            return preview.Length <= maxPreviewLength
                ? preview
                : $"{preview[..maxPreviewLength]}...";
        }

        private static User CreateIncomingUserProjection(IMPrivateChatNotifyMessage notification)
        {
            var senderId = notification.SenderId.ToString();
            return new User
            {
                Id = senderId,
                PassportId = senderId,
                Username = senderId,
                Nickname = string.IsNullOrWhiteSpace(notification.SenderName) ? string.Empty : notification.SenderName,
                Avatar = notification.SenderAvatar ?? string.Empty,
                Bio = "已通过 IM 网关同步",
                Status = UserStatus.Online
            };
        }

        private static User CreateIncomingUserProjection(IMGroupChatNotifyMessage notification)
        {
            var senderId = notification.SenderId.ToString();
            return new User
            {
                Id = senderId,
                PassportId = senderId,
                Username = senderId,
                Nickname = string.IsNullOrWhiteSpace(notification.SenderName) ? string.Empty : notification.SenderName,
                Avatar = notification.SenderAvatar ?? string.Empty,
                Bio = "已通过 IM 网关同步",
                Status = UserStatus.Online
            };
        }

        private static User CreateCurrentUserProjection(User currentUser, string passportId)
        {
            return new User
            {
                Id = passportId,
                PassportId = string.IsNullOrWhiteSpace(currentUser.PassportId) ? passportId : currentUser.PassportId,
                Username = currentUser.Username,
                Email = currentUser.Email,
                Avatar = currentUser.Avatar,
                Bio = currentUser.Bio,
                Status = currentUser.Status
            };
        }

        private void RefreshConversationState()
        {
            if (_selectedGroup != null && !string.IsNullOrWhiteSpace(SelectedGroupId))
            {
                var title = _selectedGroup.Name ?? string.Empty;
                var description = string.IsNullOrWhiteSpace(_selectedGroup.Description)
                    ? _selectedGroup.MemberSummary
                    : _selectedGroup.Description;

                // 如果当前已是同一群组会话，直接更新属性，不替换整个对象（避免触发过渡动画）
                if (_activeConversationState != null
                    && ReferenceEquals(_activeConversationState.Messages, _groupMessageItems))
                {
                    _activeConversationState.Title = title;
                    _activeConversationState.Description = description;
                    return;
                }

                ActiveConversationState = new ConversationViewState(
                    title,
                    "群组会话",
                    description,
                    _groupMessageItems,
                    "输入群消息，或直接粘贴视频链接自动解析",
                    string.IsNullOrWhiteSpace(title) ? "" : title[..1],
                    false);
                return;
            }

            if (_selectedFriend != null && !string.IsNullOrWhiteSpace(SelectedFriendId))
            {
                var title = _selectedFriend.DisplayName ?? string.Empty;
                var description = string.IsNullOrWhiteSpace(_selectedFriend.Bio)
                    ? _selectedFriend.DisplayStatus
                    : _selectedFriend.Bio;

                // 如果当前已是同一好友会话，直接更新属性，不替换整个对象（避免触发过渡动画）
                if (_activeConversationState != null
                    && ReferenceEquals(_activeConversationState.Messages, _messageItems))
                {
                    _activeConversationState.Title = title;
                    _activeConversationState.Description = description;
                    _activeConversationState.IsOnline = _selectedFriend.IsAvailable;
                    return;
                }

                ActiveConversationState = new ConversationViewState(
                    title,
                    "好友会话",
                    description,
                    _messageItems,
                    "输入消息，或发送图片、视频与链接卡片",
                    _selectedFriend.AvatarInitial,
                    _selectedFriend.IsAvailable);
                return;
            }

            // 无选中会话：若当前已是空状态（其 Messages 集合既不是好友也不是群组消息集合），则不重新创建对象
            var isAlreadyEmptyState = _activeConversationState != null
                && !ReferenceEquals(_activeConversationState.Messages, _messageItems)
                && !ReferenceEquals(_activeConversationState.Messages, _groupMessageItems);

            if (isAlreadyEmptyState)
            {
                return;
            }

            ActiveConversationState = ConversationViewState.CreateEmpty();
        }

        private bool CanSendMessage()
        {
            var isSending = _isSending;
            var isConversationBusy = IsConversationBusy;
            var hasConversation = !string.IsNullOrWhiteSpace(SelectedFriendId) || !string.IsNullOrWhiteSpace(SelectedGroupId);
            var hasDraft = !string.IsNullOrWhiteSpace(NewMessageContent?.Trim()) || HasPendingAttachment;

            System.Diagnostics.Debug.WriteLine($"[CanSendMessage] _isSending={isSending}, IsConversationBusy={isConversationBusy}, _isInitializing={_isInitializing}, _isConversationLoading={_isConversationLoading}");
            System.Diagnostics.Debug.WriteLine($"[CanSendMessage] SelectedFriendId='{SelectedFriendId}', SelectedGroupId='{SelectedGroupId}', hasConversation={hasConversation}");
            System.Diagnostics.Debug.WriteLine($"[CanSendMessage] NewMessageContent='{NewMessageContent}', HasPendingAttachment={HasPendingAttachment}, hasDraft={hasDraft}");

            if (isSending || isConversationBusy)
            {
                return false;
            }

            return hasConversation && hasDraft;
        }

        private bool CanEditRoster()
        {
            return !_isInitializing && !_isRosterLoading;
        }

        private bool CanSendFriendRequest()
        {
            return CanEditRoster() && !string.IsNullOrWhiteSpace(FriendRequestUsername?.Trim());
        }

        private bool CanCreateGroup()
        {
            return CanEditRoster() && !string.IsNullOrWhiteSpace(NewGroupName?.Trim());
        }

        private bool CanClearChat()
        {
            return !_isInitializing && !_isConversationLoading
                && (!string.IsNullOrWhiteSpace(SelectedFriendId) || !string.IsNullOrWhiteSpace(SelectedGroupId));
        }

        private bool IsCurrentFriendRequest(int requestVersion, string friendId)
        {
            return requestVersion == Volatile.Read(ref _conversationLoadVersion)
                && string.Equals(_selectedFriendId, friendId, StringComparison.Ordinal)
                && string.IsNullOrWhiteSpace(_selectedGroupId);
        }

        private bool IsCurrentGroupRequest(int requestVersion, string groupId)
        {
            return requestVersion == Volatile.Read(ref _conversationLoadVersion)
                && string.Equals(_selectedGroupId, groupId, StringComparison.Ordinal)
                && string.IsNullOrWhiteSpace(_selectedFriendId);
        }

        private void SetInitializing(bool value)
        {
            if (_isInitializing == value)
            {
                return;
            }

            _isInitializing = value;
            RaiseLoadingStateChanged();
        }

        private void SetRosterLoading(bool value)
        {
            if (_isRosterLoading == value)
            {
                return;
            }

            _isRosterLoading = value;
            RaiseLoadingStateChanged();
        }

        private void SetConversationLoading(bool value)
        {
            System.Diagnostics.Debug.WriteLine($"[SetConversationLoading] from={_isConversationLoading} to={value}");
            if (_isConversationLoading == value)
            {
                return;
            }

            _isConversationLoading = value;
            RaiseLoadingStateChanged();
            RaiseSendCommandStateChanged();
        }

        private void RaiseLoadingStateChanged()
        {
            OnPropertyChanged(nameof(IsSidebarBusy));
            OnPropertyChanged(nameof(IsConversationBusy));
            OnPropertyChanged(nameof(SidebarLoadingMessage));
            OnPropertyChanged(nameof(ConversationLoadingMessage));

            _sendFriendRequestCommand.RaiseCanExecuteChanged();
            _removeFriendCommand.RaiseCanExecuteChanged();
            _createGroupCommand.RaiseCanExecuteChanged();
            _addSuggestedFriendCommand.RaiseCanExecuteChanged();
            _rejectFriendRequestCommand.RaiseCanExecuteChanged();
            _acceptGroupInviteCommand.RaiseCanExecuteChanged();
            _rejectGroupInviteCommand.RaiseCanExecuteChanged();
            _inviteToGroupCommand.RaiseCanExecuteChanged();
            _removeGroupCommand.RaiseCanExecuteChanged();
            _approveInviteApprovalCommand.RaiseCanExecuteChanged();
            _rejectInviteApprovalCommand.RaiseCanExecuteChanged();
        }

        private void RaiseSendCommandStateChanged()
        {
            _sendMessageCommand.RaiseCanExecuteChanged();
            _sendGroupMessageCommand.RaiseCanExecuteChanged();
            _clearChatCommand.RaiseCanExecuteChanged();
        }
    }

    public sealed class ConversationViewState : INotifyPropertyChanged
    {
        private string _title;
        private string _description;
        private bool _isOnline;

        public ConversationViewState(
            string title,
            string category,
            string description,
            ObservableCollection<ChatMessageItemViewModel> messages,
            string inputWatermark,
            string avatarInitial = "",
            bool isOnline = false)
        {
            _title = title ?? string.Empty;
            Category = category;
            _description = description ?? string.Empty;
            Messages = messages;
            InputWatermark = inputWatermark;
            AvatarInitial = avatarInitial;
            _isOnline = isOnline;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public string Title
        {
            get => _title;
            set
            {
                var normalized = value ?? string.Empty;
                if (string.Equals(_title, normalized, StringComparison.Ordinal))
                {
                    return;
                }

                _title = normalized;
                OnPropertyChanged(nameof(Title));
            }
        }

        public string Category { get; }

        public string Description
        {
            get => _description;
            set
            {
                var normalized = value ?? string.Empty;
                if (string.Equals(_description, normalized, StringComparison.Ordinal))
                {
                    return;
                }

                _description = normalized;
                OnPropertyChanged(nameof(Description));
            }
        }

        public ObservableCollection<ChatMessageItemViewModel> Messages { get; }

        public string InputWatermark { get; }

        /// <summary>头像首字（用于聊天头部32x32头像显示）</summary>
        public string AvatarInitial { get; }

        /// <summary>是否在线（用于聊天头部在线状态文字显示）</summary>
        public bool IsOnline
        {
            get => _isOnline;
            set
            {
                if (_isOnline != value)
                {
                    _isOnline = value;
                    OnPropertyChanged(nameof(IsOnline));
                    OnPropertyChanged(nameof(OnlineStatusText));
                }
            }
        }

        /// <summary>在线状态文字："在线"/"离线"</summary>
        public string OnlineStatusText => IsOnline ? "在线" : "离线";

        private void OnPropertyChanged(string propertyName)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        public static ConversationViewState CreateEmpty()
        {
            return new ConversationViewState(
                "选择好友或群组开始聊天",
                "当前没有活动会话",
                "这里支持文本、图片、本地视频以及抖音、哔哩哔哩等主流视频网站链接解析。",
                new ObservableCollection<ChatMessageItemViewModel>(),
                "选择左侧会话后输入消息或粘贴链接");
        }
    }

    public sealed class PendingSocialAttachment
    {
        public PendingSocialAttachment(string sourcePath, MediaAttachmentType type)
        {
            SourcePath = sourcePath;
            Type = type;
        }

        public string SourcePath { get; }

        public MediaAttachmentType Type { get; }

        public string DisplayName => System.IO.Path.GetFileName(SourcePath);

        public string Summary => Type switch
        {
            MediaAttachmentType.Image => "图片会作为消息卡片发送。",
            MediaAttachmentType.Video => "视频会作为消息卡片发送，并支持悬停内联播放。",
            MediaAttachmentType.File => $"文件将作为附件发送（{FormatFileSize()}）。",
            _ => string.Empty
        };

        public string KindLabel => Type switch
        {
            MediaAttachmentType.Image => "图片",
            MediaAttachmentType.Video => "视频",
            MediaAttachmentType.File => "文件",
            _ => "附件"
        };

        private string FormatFileSize()
        {
            try
            {
                var info = new System.IO.FileInfo(SourcePath);
                if (!info.Exists) return string.Empty;
                var bytes = info.Length;
                if (bytes < 1024) return $"{bytes} B";
                if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
                return $"{bytes / (1024.0 * 1024):F1} MB";
            }
            catch { return string.Empty; }
        }
    }
}