using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Horizon.Game.GengDi.Core.Animations;
using Horizon.Game.GengDi.Core.Services;
using Horizon.Game.GengDi.Core.ViewModels;
using Horizon.Game.GengDi.Enums;
using Horizon.Game.GengDi.Models;

namespace Horizon.Game.GengDi.Core.Views
{
    public partial class SocialView : UserControl
    {
        private const string ConversationScrollViewerName = "ConversationMessagesScrollViewer";
        private const string ScrollToBottomButtonName = "ScrollToBottomButton";
        private const string FriendAvatarContainerName = "FriendAvatarContainer";
        private const string GroupAvatarContainerName = "GroupAvatarContainer";
        private const double BottomThreshold = 72d;

        private static readonly string DragDataFriendId = "FriendId";

        private TransitioningContentControl _conversationTransitionHost;
        private ScrollViewer _conversationScrollViewer;
        private Button _scrollToBottomButton;
        private ListBox _friendsListBox;
        private ListBox _groupsListBox;
        private SocialViewModel _viewModel;
        private NotifyCollectionChangedEventHandler _messagesCollectionChangedHandler;
        private ObservableCollection<ChatMessageItemViewModel> _trackedMessages;
        private readonly HashSet<string> _avatarAnimationsInFlight = new(StringComparer.Ordinal);
        private bool _stickToBottom = true;
        private bool _isAdjustingScroll;
        private bool _isViewInitialized;
        private bool _isViewLoading;
        private ContextMenu _activeContextMenu;

        public SocialView(string userId)
        {
            InitializeComponent();
            var viewModel = new SocialViewModel(userId);
            DataContext = viewModel;

            Loaded += SocialView_Loaded;
            Unloaded += SocialView_Unloaded;
        }

        public SocialView()
            : this(ImIdentity.ResolvePassportId(App.CurrentUser))
        {
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void SocialView_Loaded(object sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            if (_isViewLoading)
            {
                return;
            }

            _ = LoadSocialViewAsync().ContinueWith(t =>
            {
                if (t.IsFaulted) Debug.WriteLine($"SocialView init failed: {t.Exception}");
            }, TaskScheduler.Default);
        }

        private async Task LoadSocialViewAsync()
        {
            _isViewLoading = true;

            try
            {
                AttachViewModel();

                if (!_isViewInitialized)
                {
                    _conversationTransitionHost = this.FindControl<TransitioningContentControl>("ConversationTransitionHost");
                    _friendsListBox = this.FindControl<ListBox>("FriendsListBox");
                    _groupsListBox = this.FindControl<ListBox>("GroupsListBox");
                    ImplicitContentAnimationHelper.AttachSlideAndScale(_conversationTransitionHost);
                    RefreshConversationControls();
                    RequestScrollToBottom(forceStickToBottom: true);
                    AttachMessageInputHandler();

                    AddHandler(Avalonia.Input.DragDrop.DropEvent, OnDragDrop);
                    AddHandler(Avalonia.Input.DragDrop.DragOverEvent, OnDragOver);

                    if (_viewModel != null)
                    {
                        await _viewModel.InitializeAsync();
                        await SynchronizeConversationSelectionAsync();
                        RefreshConversationControls();
                    }

                    _isViewInitialized = true;
                }
                else
                {
                    await SynchronizeConversationSelectionAsync();
                    RefreshConversationControls();
                    AttachMessageInputHandler();
                }
            }
            finally
            {
                _isViewLoading = false;
            }
        }

        private bool _globalMessageInputHandlerAttached;

        private void AttachMessageInputHandler()
        {
            if (_globalMessageInputHandlerAttached)
            {
                return;
            }

            _globalMessageInputHandlerAttached = true;
            System.Diagnostics.Debug.WriteLine("[SocialView] AttachMessageInputHandler called");
            AddHandler(Avalonia.Input.InputElement.KeyDownEvent, MessageInputBox_KeyDown, Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
        }

        private void MessageInputBox_KeyDown(object sender, Avalonia.Input.KeyEventArgs e)
        {
            // Check if event originated from the MessageInputBox TextBox
            if (e.Source is not TextBox textBox || textBox.Name != "MessageInputBox")
            {
                return;
            }

            // Verify this is the MessageInputBox by checking if it's inside the conversation area
            var isInConversationArea = false;
            var parent = textBox.GetVisualParent();
            while (parent != null)
            {
                if (parent == _conversationTransitionHost)
                {
                    isInConversationArea = true;
                    break;
                }
                parent = parent.GetVisualParent();
            }

            if (!isInConversationArea)
            {
                return;
            }

            if (e.Key != Avalonia.Input.Key.Enter)
            {
                return;
            }

            if (e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Alt))
            {
                var caretIndex = textBox.CaretIndex;
                var currentText = textBox.Text ?? string.Empty;
                textBox.Text = currentText.Insert(caretIndex, "\n");
                textBox.CaretIndex = caretIndex + 1;
                e.Handled = true;
                return;
            }

            // Send message on Enter (when Alt is not pressed)
            var vm = _viewModel;
            if (vm?.SendMessageCommand.CanExecute(null) == true)
            {
                vm.SendMessageCommand.Execute(null);
            }

            e.Handled = true;
        }

        private void SocialView_Unloaded(object sender, Avalonia.Interactivity.RoutedEventArgs e)
        {
            // SocialView 在主壳内会被复用；切走标签页时不要销毁 IM 长连接，
            // 否则离开社交页期间将收不到群邀请/聊天推送。
        }

        /// <summary>
        /// 退出登录时由外部调用，停止 IM 订阅并取消后台初始化重试。
        /// </summary>
        public async Task ShutdownAsync()
        {
            try
            {
                (_viewModel as SocialViewModel)?.Shutdown();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SocialView] Shutdown 异常：{ex.Message}");
            }
        }

        /// <summary>
        /// 在主壳创建后即提前启动社交数据初始化与 IM 推送订阅，
        /// 避免用户首次进入社交页之前错过入群邀请徽标和提示。
        /// </summary>
        public async Task EnsureBackgroundSessionAsync()
        {
            AttachViewModel();

            if (_viewModel == null)
            {
                return;
            }

            await _viewModel.InitializeAsync();
        }

        private async Task SynchronizeConversationSelectionAsync()
        {
            if (_viewModel?.SelectedFriend != null)
            {
                await _viewModel.SelectFriendAsync(_viewModel.SelectedFriend);
                return;
            }

            if (_viewModel?.SelectedGroup != null)
            {
                await _viewModel.SelectGroupAsync(_viewModel.SelectedGroup);
            }
        }

        private void FriendGroupHeader_PointerPressed(object sender, Avalonia.Input.PointerPressedEventArgs e)
        {
            var props = e.GetCurrentPoint(this).Properties;

            if (props.IsRightButtonPressed)
            {
                if (sender is Border border && border.DataContext is FriendGroupItem group)
                {
                    e.Handled = true;
                    ShowGroupContextMenu(border, group);
                }
                return;
            }

            if (!props.IsLeftButtonPressed)
                return;

            if (sender is Border border2 && border2.DataContext is FriendGroupItem group2)
            {
                _viewModel?.ToggleFriendGroup(group2);
                e.Handled = true;
            }
        }

        private void FriendGroupHeader_PointerReleased(object sender, Avalonia.Input.PointerReleasedEventArgs e)
        {
        }

        private bool _isDraggingFriend;
        private bool _dragStarted;
        private Avalonia.Point _dragStartPosition;
        private const double DragThreshold = 5;

        private async void FriendItem_PointerMoved(object sender, Avalonia.Input.PointerEventArgs e)
        {
            if (sender is not Border border || border.DataContext is not User friend) return;
            if (!_isDraggingFriend || _dragStarted) return;

            var currentPos = e.GetPosition(this);
            var delta = currentPos - _dragStartPosition;
            if (Math.Abs(delta.X) < DragThreshold && Math.Abs(delta.Y) < DragThreshold)
                return;

            _dragStarted = true;

            var dataObject = new Avalonia.Input.DataObject();
            dataObject.Set("text/plain", friend.Id);
            dataObject.Set("application/x-friend-id", friend.Id);

            try
            {
                var topLevel = TopLevel.GetTopLevel(this);
                if (topLevel != null)
                {
                    await Avalonia.Input.DragDrop.DoDragDrop(e, dataObject, Avalonia.Input.DragDropEffects.Move);
                }
            }
            catch
            {
            }
        }

        private async void FriendItem_PointerPressed(object sender, Avalonia.Input.PointerPressedEventArgs e)
        {
            _dragStartPosition = e.GetPosition(this);
            _isDraggingFriend = false;
            _dragStarted = false;

            if (_viewModel == null || _isViewLoading)
            {
                return;
            }

            var props = e.GetCurrentPoint(this).Properties;

            if (props.IsRightButtonPressed)
            {
                if (sender is Border border && border.DataContext is User friend)
                {
                    e.Handled = true;
                    await ShowFriendContextMenu(border, friend);
                }
                return;
            }

            if (!props.IsLeftButtonPressed)
                return;

            _isDraggingFriend = true;

            var current = sender as Control;
            while (current != null && current.DataContext is not User friend2)
            {
                current = current.Parent as Control;
            }

            if (current?.DataContext is User selectedFriend)
            {
                await _viewModel.SelectFriendAsync(selectedFriend);
            }
        }

        private async void OnEditGroupClick(object sender, RoutedEventArgs e)
        {
            if (_viewModel == null) return;
            if (sender is not Button button || button.Tag is not FriendGroupItem group) return;
            if (group.IsDefault) return;

            _viewModel.OpenEditFriendGroupOverlay(group.GroupName);
            await FocusFriendGroupInputBoxAsync();
        }

        private async void OnDeleteGroupClick(object sender, RoutedEventArgs e)
        {
            if (_viewModel == null) return;
            if (sender is not Button button || button.Tag is not FriendGroupItem group) return;

            _viewModel.OpenDeleteGroupOverlay(group.GroupName, group.DisplayName);
        }

        private async System.Threading.Tasks.Task FocusFriendGroupInputBoxAsync()
        {
            await Task.Delay(50);
            var inputBox = this.FindControl<TextBox>("FriendGroupInputBox");
            if (inputBox != null)
            {
                inputBox.Focus();
                inputBox.SelectAll();
            }
        }

        private void FriendGroupInputBox_KeyDown(object sender, Avalonia.Input.KeyEventArgs e)
        {
            if (e.Key == Avalonia.Input.Key.Enter && _viewModel?.ConfirmFriendGroupOverlayCommand?.CanExecute(null) == true)
            {
                _viewModel.ConfirmFriendGroupOverlayCommand.Execute(null);
            }
        }

        private void FriendItem_PointerReleased(object sender, Avalonia.Input.PointerReleasedEventArgs e)
        {
        }

        private void CloseActiveContextMenu()
        {
            if (_activeContextMenu == null) return;
            try
            {
                _activeContextMenu.Close();
            }
            catch
            {
            }
            _activeContextMenu = null;
        }

        private void OnDragOver(object sender, Avalonia.Input.DragEventArgs e)
        {
            if (e.Source is Visual v)
            {
                var groupItem = FindAncestorDataContext<FriendGroupItem>(v);
                if (groupItem != null && (e.Data.Contains("application/x-friend-id") || e.Data.Contains("Text")))
                {
                    e.DragEffects = Avalonia.Input.DragDropEffects.Move;
                    e.Handled = true;
                    return;
                }
            }
            e.DragEffects = Avalonia.Input.DragDropEffects.None;
        }

        private async void OnDragDrop(object sender, Avalonia.Input.DragEventArgs e)
        {
            if (_viewModel == null) return;

            FriendGroupItem targetGroup = null;
            if (e.Source is Visual v)
                targetGroup = FindAncestorDataContext<FriendGroupItem>(v);

            if (targetGroup == null) return;

            string friendId = null;
            if (e.Data.Contains("application/x-friend-id"))
                friendId = e.Data.Get("application/x-friend-id") as string;
            else if (e.Data.Contains("Text"))
                friendId = e.Data.Get("Text") as string;

            if (string.IsNullOrWhiteSpace(friendId)) return;

            e.DragEffects = Avalonia.Input.DragDropEffects.Move;
            e.Handled = true;

            try
            {
                await _viewModel.AssignFriendToGroupAsync(friendId, targetGroup.GroupName);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SocialView] 拖放好友到分组失败：{ex.Message}");
            }
        }

        private static T FindAncestorDataContext<T>(Visual visual) where T : class
        {
            var current = visual;
            while (current != null)
            {
                if (current is StyledElement se && se.DataContext is T t)
                    return t;
                current = current.GetVisualParent();
            }
            return null;
        }

        private void ShowGroupContextMenu(Border anchor, FriendGroupItem group)
        {
            CloseActiveContextMenu();

            if (_viewModel == null || group == null) return;

            var menu = new ContextMenu();
            var groupName = group.GroupName;
            var isDefault = group.IsDefault;

            if (!isDefault)
            {
                var renameItem = new MenuItem { Header = "重命名分组" };
                renameItem.Click += (s, e) => OnRenameGroupClick(groupName);
                menu.Items.Add(renameItem);

                var deleteItem = new MenuItem { Header = "删除分组" };
                deleteItem.Click += (s, e) => _viewModel.OpenDeleteGroupOverlay(groupName, group.DisplayName);
                menu.Items.Add(deleteItem);
            }

            menu.Closed += (_, _) =>
            {
                if (ReferenceEquals(_activeContextMenu, menu))
                    _activeContextMenu = null;
            };

            _activeContextMenu = menu;

            try
            {
                menu.PlacementTarget = anchor;
                menu.Placement = PlacementMode.Pointer;
                menu.Open();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SocialView] 打开分组上下文菜单失败：{ex.Message}");
                _activeContextMenu = null;
            }
        }

        private async void OnRenameGroupClick(string groupName)
        {
            if (_viewModel == null || string.IsNullOrEmpty(groupName)) return;
            _viewModel.OpenEditFriendGroupOverlay(groupName);
            await FocusFriendGroupInputBoxAsync();
        }

        private async void OnCreateGroupClick(object sender, RoutedEventArgs e)
        {
            if (_viewModel == null) return;
            _viewModel.OpenCreateFriendGroupOverlay();
            await FocusFriendGroupInputBoxAsync();
        }

        private async System.Threading.Tasks.Task ShowFriendContextMenu(Border anchor, User friend)
        {
            CloseActiveContextMenu();

            if (_viewModel == null || friend == null) return;

            var menu = new ContextMenu();
            var friendId = friend.Id;
            var friendGroupName = friend.GroupName ?? string.Empty;

            var moveToMenu = new MenuItem { Header = "移动到分组" };

            var defaultItem = new MenuItem { Header = "默认分组" };
            if (string.IsNullOrEmpty(friendGroupName))
                defaultItem.Header = "✓ 默认分组";
            defaultItem.Click += async (s, e) =>
            {
                try
                {
                    await _viewModel.AssignFriendToGroupAsync(friendId, string.Empty);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[SocialView] 移动好友到分组失败：{ex.Message}");
                }
            };
            moveToMenu.Items.Add(defaultItem);

            foreach (var group in _viewModel.FriendGroups)
            {
                if (group.IsDefault) continue;

                var targetGroupName = group.GroupName;
                var displayName = group.DisplayName;
                var item = new MenuItem
                {
                    Header = string.Equals(friendGroupName, targetGroupName, StringComparison.Ordinal)
                        ? $"✓ {displayName}"
                        : displayName
                };
                item.Click += async (s, e) =>
                {
                    try
                    {
                        await _viewModel.AssignFriendToGroupAsync(friendId, targetGroupName);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[SocialView] 移动好友到分组失败：{ex.Message}");
                    }
                };
                moveToMenu.Items.Add(item);
            }

            moveToMenu.Items.Add(new Separator());

            var createGroupItem = new MenuItem { Header = "新建分组…" };
            createGroupItem.Click += async (s, e) =>
            {
                _viewModel.OpenCreateFriendGroupOverlay(friendId);
                await FocusFriendGroupInputBoxAsync();
            };
            moveToMenu.Items.Add(createGroupItem);

            menu.Items.Add(moveToMenu);

            menu.Closed += (_, _) =>
            {
                if (ReferenceEquals(_activeContextMenu, menu))
                    _activeContextMenu = null;
            };

            _activeContextMenu = menu;

            try
            {
                menu.PlacementTarget = anchor;
                menu.Placement = PlacementMode.Pointer;
                menu.Open();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SocialView] 打开好友上下文菜单失败：{ex.Message}");
                _activeContextMenu = null;
            }
        }

        private async void FriendsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_viewModel == null || _isViewLoading)
            {
                return;
            }

            await _viewModel.SelectFriendAsync((sender as ListBox)?.SelectedItem as User);
        }

        private async void GroupsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_viewModel == null || _isViewLoading)
            {
                return;
            }

            await _viewModel.SelectGroupAsync((sender as ListBox)?.SelectedItem as Group);
        }

        private async Task AnimateFriendAvatarAsync(string friendId)
        {
            if (string.IsNullOrWhiteSpace(friendId) || !_avatarAnimationsInFlight.Add(friendId))
            {
                return;
            }

            try
            {
                var avatarContainer = FindFriendAvatarContainer(friendId);
                if (avatarContainer == null)
                {
                    return;
                }

                avatarContainer.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
                var transform = new ScaleTransform(1d, 1d);
                avatarContainer.RenderTransform = transform;

                var frames = new[] { 1.0d, 1.15d, 0.96d, 1.08d, 1.0d };
                foreach (var scale in frames)
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        transform.ScaleX = scale;
                        transform.ScaleY = scale;
                    });
                    await Task.Delay(70);
                }
            }
            finally
            {
                _avatarAnimationsInFlight.Remove(friendId);
            }
        }

        private Border FindFriendAvatarContainer(string friendId)
        {
            _friendsListBox ??= this.FindControl<ListBox>("FriendsListBox");

            return _friendsListBox?
                .GetVisualDescendants()
                .OfType<Border>()
                .FirstOrDefault(control => string.Equals(control.Name, FriendAvatarContainerName, StringComparison.Ordinal)
                    && control.DataContext is User user
                    && string.Equals(user.Id, friendId, StringComparison.Ordinal));
        }

        private async Task AnimateGroupAvatarAsync(string groupId)
        {
            if (string.IsNullOrWhiteSpace(groupId) || !_avatarAnimationsInFlight.Add($"group:{groupId}"))
            {
                return;
            }

            try
            {
                var avatarContainer = FindGroupAvatarContainer(groupId);
                if (avatarContainer == null)
                {
                    return;
                }

                avatarContainer.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
                var transform = new ScaleTransform(1d, 1d);
                avatarContainer.RenderTransform = transform;

                var frames = new[] { 1.0d, 1.15d, 0.96d, 1.08d, 1.0d };
                foreach (var scale in frames)
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        transform.ScaleX = scale;
                        transform.ScaleY = scale;
                    });
                    await Task.Delay(70);
                }
            }
            finally
            {
                _avatarAnimationsInFlight.Remove($"group:{groupId}");
            }
        }

        private Border FindGroupAvatarContainer(string groupId)
        {
            _groupsListBox ??= this.FindControl<ListBox>("GroupsListBox");

            return _groupsListBox?
                .GetVisualDescendants()
                .OfType<Border>()
                .FirstOrDefault(control => string.Equals(control.Name, GroupAvatarContainerName, StringComparison.Ordinal)
                    && control.DataContext is Group group
                    && string.Equals(group.Id, groupId, StringComparison.Ordinal));
        }

        private void AttachViewModel()
        {
            if (ReferenceEquals(_viewModel, DataContext))
            {
                return;
            }

            if (_viewModel != null)
            {
                _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
                _viewModel.ChatAnimationRequested -= OnChatAnimationRequested;
            }

            _viewModel = DataContext as SocialViewModel;
            if (_viewModel == null)
            {
                TrackMessages(null);
                return;
            }

            _viewModel.PropertyChanged += ViewModel_PropertyChanged;
            _viewModel.ChatAnimationRequested += OnChatAnimationRequested;
            TrackMessages(_viewModel.ActiveConversationState?.Messages);
        }

        private async void OnChatAnimationRequested(string id, bool isGroup)
        {
            if (isGroup)
            {
                await AnimateGroupAvatarAsync(id);
            }
            else
            {
                await AnimateFriendAvatarAsync(id);
            }
        }

        private void ViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (!string.Equals(e.PropertyName, nameof(SocialViewModel.ActiveConversationState), StringComparison.Ordinal))
            {
                return;
            }

            TrackMessages(_viewModel?.ActiveConversationState?.Messages);
            _stickToBottom = true;
            // 后台标签页跳过滚动刷新，等视图重新可见时在 Loaded 中刷新
            if (!IsVisible)
            {
                return;
            }
            RefreshConversationControls();
            RequestScrollToBottom(forceStickToBottom: true);
        }

        private void TrackMessages(ObservableCollection<ChatMessageItemViewModel> messages)
        {
            if (ReferenceEquals(_trackedMessages, messages))
            {
                return;
            }

            if (_trackedMessages != null && _messagesCollectionChangedHandler != null)
            {
                _trackedMessages.CollectionChanged -= _messagesCollectionChangedHandler;
            }

            _trackedMessages = messages;

            if (_trackedMessages == null)
            {
                return;
            }

            _messagesCollectionChangedHandler ??= Messages_CollectionChanged;
            _trackedMessages.CollectionChanged += _messagesCollectionChangedHandler;
        }

        private void Messages_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            // 后台标签页不可见时无需触发 UI 线程滚动操作
            if (!IsVisible)
            {
                return;
            }

            var shouldForceStick = e.NewItems != null
                && e.NewItems.OfType<ChatMessageItemViewModel>().Any(message => message.IsOutgoing);

            if (_stickToBottom || shouldForceStick)
            {
                RequestScrollToBottom(forceStickToBottom: shouldForceStick);
                return;
            }

            Dispatcher.UIThread.Post(UpdateScrollAffordance);
        }

        private void RefreshConversationControls()
        {
            Dispatcher.UIThread.Post(() =>
            {
                SetConversationScrollViewer(FindConversationScrollViewer());
                _scrollToBottomButton = FindScrollToBottomButton();
                UpdateScrollAffordance();
            });
        }

        private ScrollViewer FindConversationScrollViewer()
        {
            var activeConversation = _viewModel?.ActiveConversationState;

            return _conversationTransitionHost?
                .GetVisualDescendants()
                .OfType<ScrollViewer>()
                .FirstOrDefault(control => string.Equals(control.Name, ConversationScrollViewerName, StringComparison.Ordinal)
                    && ReferenceEquals(control.DataContext, activeConversation));
        }

        private Button FindScrollToBottomButton()
        {
            var activeConversation = _viewModel?.ActiveConversationState;

            return _conversationTransitionHost?
                .GetVisualDescendants()
                .OfType<Button>()
                .FirstOrDefault(control => string.Equals(control.Name, ScrollToBottomButtonName, StringComparison.Ordinal)
                    && ReferenceEquals(control.DataContext, activeConversation));
        }

        private void SetConversationScrollViewer(ScrollViewer scrollViewer)
        {
            if (ReferenceEquals(_conversationScrollViewer, scrollViewer))
            {
                return;
            }

            if (_conversationScrollViewer != null)
            {
                _conversationScrollViewer.PropertyChanged -= ConversationScrollViewer_PropertyChanged;
            }

            _conversationScrollViewer = scrollViewer;

            if (_conversationScrollViewer != null)
            {
                _conversationScrollViewer.PropertyChanged += ConversationScrollViewer_PropertyChanged;
            }
        }

        private void ConversationScrollViewer_PropertyChanged(object sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (_conversationScrollViewer == null)
            {
                return;
            }

            if (e.Property == ScrollViewer.OffsetProperty)
            {
                UpdateScrollAffordance();
                return;
            }

            if (e.Property == ScrollViewer.ExtentProperty || e.Property == ScrollViewer.ViewportProperty)
            {
                if (_stickToBottom)
                {
                    RequestScrollToBottom(forceStickToBottom: false);
                    return;
                }

                UpdateScrollAffordance();
            }
        }

        private void RequestScrollToBottom(bool forceStickToBottom)
        {
            if (forceStickToBottom)
            {
                _stickToBottom = true;
            }

            Dispatcher.UIThread.Post(() =>
            {
                ScrollConversationToBottom();
                Dispatcher.UIThread.Post(() =>
                {
                    ScrollConversationToBottom();
                    UpdateScrollAffordance();
                });
            });
        }

        private void ScrollConversationToBottom()
        {
            if (_conversationScrollViewer == null)
            {
                RefreshConversationControls();
                return;
            }

            var maxOffsetY = Math.Max(0, _conversationScrollViewer.Extent.Height - _conversationScrollViewer.Viewport.Height);

            _isAdjustingScroll = true;
            _conversationScrollViewer.Offset = new Vector(_conversationScrollViewer.Offset.X, maxOffsetY);
            _isAdjustingScroll = false;
        }

        private void UpdateScrollAffordance()
        {
            var shouldShowButton = !IsNearBottom();

            if (!_isAdjustingScroll)
            {
                _stickToBottom = !shouldShowButton;
            }

            if (_scrollToBottomButton != null)
            {
                _scrollToBottomButton.IsVisible = shouldShowButton;
            }
        }

        private bool IsNearBottom()
        {
            if (_conversationScrollViewer == null)
            {
                return true;
            }

            var bottomOffset = Math.Max(0, _conversationScrollViewer.Extent.Height - _conversationScrollViewer.Viewport.Height);
            return bottomOffset - _conversationScrollViewer.Offset.Y <= BottomThreshold;
        }

        private void ScrollToBottomButton_Click(object sender, RoutedEventArgs e)
        {
            RequestScrollToBottom(forceStickToBottom: true);
        }

        private async void SendGroupInviteButton_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel == null)
            {
                return;
            }

            var listBox = this.FindControl<ListBox>("InviteFriendsListBox");
            if (listBox == null)
            {
                return;
            }

            var selectedFriends = listBox.SelectedItems?
                .OfType<User>()
                .ToList() ?? new List<User>();

            await _viewModel.SendGroupInvitesAsync(selectedFriends);
        }

        private async void AttachImageButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not SocialViewModel viewModel)
            {
                return;
            }

            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null)
            {
                return;
            }

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "选择要发送的图片",
                AllowMultiple = true,
                FileTypeFilter = new[]
                {
                    FilePickerFileTypes.ImageAll,
                    new FilePickerFileType("图片文件")
                    {
                        Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.webp", "*.gif", "*.bmp" }
                    }
                }
            });

            foreach (var file in files)
            {
                var selectedPath = file.Path.LocalPath;
                if (!string.IsNullOrWhiteSpace(selectedPath))
                {
                    viewModel.AddPendingAttachment(selectedPath, MediaAttachmentType.Image);
                }
            }
        }

        private async void AttachVideoButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not SocialViewModel viewModel)
            {
                return;
            }

            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null)
            {
                return;
            }

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "选择要发送的视频",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("视频文件")
                    {
                        Patterns = new[] { "*.mp4", "*.webm", "*.mov", "*.m4v", "*.ogv" }
                    }
                }
            });

            var selectedPath = files.Count > 0 ? files[0].Path.LocalPath : null;
            if (!string.IsNullOrWhiteSpace(selectedPath))
            {
                viewModel.SetPendingAttachment(selectedPath, MediaAttachmentType.Video);
            }
        }

        private async void AttachFileButton_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not SocialViewModel viewModel)
            {
                return;
            }

            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null)
            {
                return;
            }

            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "选择要发送的文件",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("所有文件") { Patterns = new[] { "*.*" } }
                }
            });

            var selectedPath = files.Count > 0 ? files[0].Path.LocalPath : null;
            if (!string.IsNullOrWhiteSpace(selectedPath))
            {
                viewModel.SetPendingAttachment(selectedPath, MediaAttachmentType.File);
            }
        }

        private void RemovePendingAttachment_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.Tag is not PendingSocialAttachment attachment)
            {
                return;
            }

            if (DataContext is not SocialViewModel viewModel)
            {
                return;
            }

            var index = viewModel.PendingAttachments.IndexOf(attachment);
            if (index >= 0)
            {
                viewModel.RemovePendingAttachment(index);
            }
        }

        private WrapPanel? FindEmojiWrapPanel(Flyout flyout)
        {
            if (flyout.Content is WrapPanel directWrapPanel)
            {
                return directWrapPanel;
            }

            if (flyout.Content is ScrollViewer scrollViewer && scrollViewer.Content is WrapPanel scrollWrapPanel)
            {
                return scrollWrapPanel;
            }

            if (flyout.Content is Control content)
            {
                return content.GetVisualDescendants().OfType<WrapPanel>().FirstOrDefault();
            }

            return null;
        }

        private void EnsureEmojiPickerButtonsPopulated(Flyout flyout, Button emojiHostButton)
        {
            var emojiWrapPanel = FindEmojiWrapPanel(flyout);
            if (emojiWrapPanel == null || emojiWrapPanel.Children.Count > 0)
            {
                return;
            }

            if (DataContext is not SocialViewModel viewModel)
            {
                return;
            }

            foreach (var emoji in EmojiRegistry.AllEmojis)
            {
                var emojiCapture = emoji;
                var button = new Button
                {
                    Content = emoji,
                    FontSize = 18,
                    Padding = new Thickness(4),
                    Background = null
                };

                button.Click += (_, _) =>
                {
                    viewModel.InsertEmoji(emojiCapture);
                };

                emojiWrapPanel.Children.Add(button);
            }
        }

        private void PopulateEmojiPicker(Flyout flyout, Button emojiHostButton)
        {
            EnsureEmojiPickerButtonsPopulated(flyout, emojiHostButton);
        }

        private void EmojiPickerButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button emojiButton)
            {
                return;
            }

            if (emojiButton.Flyout is not Flyout flyout)
            {
                return;
            }

            if (!flyout.IsOpen)
            {
                flyout.ShowAt(emojiButton);
            }

            PopulateEmojiPicker(flyout, emojiButton);
        }

        private ChatMessageItemViewModel _mediaViewerTarget;

        private void MediaCard_Tapped(object sender, Avalonia.Input.PointerPressedEventArgs e)
        {
            if (sender is not Control control || control.DataContext is not ChatMessageItemViewModel messageItem)
            {
                return;
            }

            if (messageItem.IsMediaViewable)
            {
                e.Handled = true;
                OpenMediaViewer(messageItem);
                return;
            }

            if (messageItem.IsFileViewable)
            {
                e.Handled = true;
                try
                {
                    var path = messageItem.MediaUrl;
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path)
                    {
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[SocialView] 打开文件失败：{ex.Message}");
                }
                return;
            }

            // 链接卡片：使用默认浏览器打开原始 URL
            var url = messageItem.OriginalUrl;
            if (!string.IsNullOrWhiteSpace(url))
            {
                e.Handled = true;
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url)
                    {
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[SocialView] 打开链接失败：{ex.Message}");
                }
            }
        }

        private void OpenMediaViewer(ChatMessageItemViewModel messageItem)
        {
            _mediaViewerTarget = messageItem;
            messageItem.CurrentViewerAttachmentIndex = 0;
            messageItem.OpenMediaViewer();

            var overlay = this.FindControl<Border>("MediaViewerOverlay");
            var image = this.FindControl<Image>("MediaViewerImage");
            var title = this.FindControl<TextBlock>("MediaViewerTitle");
            var subtitle = this.FindControl<TextBlock>("MediaViewerSubtitle");

            if (overlay == null)
            {
                return;
            }

            overlay.IsVisible = true;
            if (image != null)
            {
                image.Source = messageItem.PreviewImage;
            }

            if (title != null)
            {
                title.Text = messageItem.PreviewTitle;
            }

            if (subtitle != null)
            {
                subtitle.Text = messageItem.CardProviderLabel;
            }
        }

        private void CloseMediaViewer()
        {
            _mediaViewerTarget?.CloseMediaViewer();
            _mediaViewerTarget = null;

            var overlay = this.FindControl<Border>("MediaViewerOverlay");
            if (overlay != null)
            {
                overlay.IsVisible = false;
            }
        }

        private void ViewerCloseButton_Click(object sender, RoutedEventArgs e)
        {
            CloseMediaViewer();
        }

        private void AttachmentImage_PointerPressed(object sender, Avalonia.Input.PointerPressedEventArgs e)
        {
            if (sender is not Control control) return;
            if (control.FindAncestorOfType<ItemsControl>()?.DataContext is not ChatMessageItemViewModel messageItem) return;

            var itemsControl = control.FindAncestorOfType<ItemsControl>();
            if (itemsControl == null) return;

            var index = 0;
            foreach (var item in itemsControl.Items)
            {
                var container = itemsControl.ContainerFromIndex(index);
                if (container != null)
                {
                    var border = container.FindDescendantOfType<Border>();
                    if (border == control)
                    {
                        break;
                    }
                }
                index++;
            }

            messageItem.CurrentViewerAttachmentIndex = index;
            OpenMediaViewerForAttachment(messageItem, index);
            e.Handled = true;
        }

        private void OpenMediaViewerForAttachment(ChatMessageItemViewModel messageItem, int attachmentIndex)
        {
            _mediaViewerTarget = messageItem;
            messageItem.OpenMediaViewer();

            var overlay = this.FindControl<Border>("MediaViewerOverlay");
            var image = this.FindControl<Image>("MediaViewerImage");
            var title = this.FindControl<TextBlock>("MediaViewerTitle");
            var subtitle = this.FindControl<TextBlock>("MediaViewerSubtitle");

            if (overlay == null) return;

            overlay.IsVisible = true;
            UpdateMediaViewerImage(messageItem, attachmentIndex);

            if (title != null) title.Text = messageItem.PreviewTitle;
            if (subtitle != null) subtitle.Text = messageItem.CardProviderLabel;
        }

        private void UpdateMediaViewerImage(ChatMessageItemViewModel messageItem, int attachmentIndex)
        {
            var image = this.FindControl<Image>("MediaViewerImage");
            if (image == null) return;

            var urls = messageItem.AttachmentMediaUrls;
            if (urls.Count > 0 && attachmentIndex >= 0 && attachmentIndex < urls.Count)
            {
                var source = urls[attachmentIndex];
                var previewImage = PreviewImageService.Instance.LoadAsync(source).ContinueWith(t =>
                {
                    if (t.Result != null)
                    {
                        Avalonia.Threading.Dispatcher.UIThread.Post(() => image.Source = t.Result);
                    }
                }, TaskScheduler.Default);
            }
            else if (messageItem.PreviewImage != null)
            {
                image.Source = messageItem.PreviewImage;
            }
        }

        private void ViewerPrevButton_Click(object sender, RoutedEventArgs e)
        {
            if (_mediaViewerTarget == null) return;
            _mediaViewerTarget.GoToPreviousAttachment();
            UpdateMediaViewerImage(_mediaViewerTarget, _mediaViewerTarget.CurrentViewerAttachmentIndex);
        }

        private void ViewerNextButton_Click(object sender, RoutedEventArgs e)
        {
            if (_mediaViewerTarget == null) return;
            _mediaViewerTarget.GoToNextAttachment();
            UpdateMediaViewerImage(_mediaViewerTarget, _mediaViewerTarget.CurrentViewerAttachmentIndex);
        }

        private async void ViewerForwardButton_Click(object sender, RoutedEventArgs e)
        {
            if (_mediaViewerTarget == null)
            {
                return;
            }

            await ShowForwardDialogAsync(_mediaViewerTarget.SerializedContent);
        }

        private async void ForwardButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.DataContext is not ChatMessageItemViewModel messageItem)
            {
                return;
            }

            await ShowForwardDialogAsync(messageItem.SerializedContent);
        }

        private async Task ShowForwardDialogAsync(string content)
        {
            if (_viewModel == null || string.IsNullOrWhiteSpace(content))
            {
                return;
            }

            var dialog = new ForwardMessageDialog(_viewModel.Friends, _viewModel.Groups);
            var owner = TopLevel.GetTopLevel(this) as Window;
            if (owner == null)
            {
                return;
            }

            var result = await dialog.ShowDialog<ForwardTarget>(owner);
            if (result != null)
            {
                var forwarded = await _viewModel.ForwardMessageAsync(result.Id, result.IsGroup, content);
                if (forwarded)
                {
                    _viewModel.ActionStatusMessage = $"消息已转发给 {result.Name}。";
                }
            }
        }

        private async void ViewerCopyButton_Click(object sender, RoutedEventArgs e)
        {
            if (_mediaViewerTarget == null)
            {
                return;
            }

            await CopyToClipboardAsync(_mediaViewerTarget.CopyableText);
            if (_viewModel != null)
            {
                _viewModel.ActionStatusMessage = "内容已复制到剪贴板。";
            }
        }

        private async void ViewerSaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (_mediaViewerTarget == null)
            {
                return;
            }

            await SaveMediaAsync(_mediaViewerTarget);
        }


        private async void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.DataContext is not ChatMessageItemViewModel messageItem)
            {
                return;
            }

            await CopyToClipboardAsync(messageItem.CopyableText);
            if (_viewModel != null)
            {
                _viewModel.ActionStatusMessage = "内容已复制到剪贴板。";
            }
        }

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button button || button.DataContext is not ChatMessageItemViewModel messageItem)
            {
                return;
            }

            await SaveMediaAsync(messageItem);
        }

        private async Task CopyToClipboardAsync(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.Clipboard == null)
            {
                return;
            }

            await topLevel.Clipboard.SetTextAsync(text);
        }

        private async Task SaveMediaAsync(ChatMessageItemViewModel messageItem)
        {
            if (messageItem == null || !messageItem.CanSaveMedia)
            {
                return;
            }

            var mediaPath = messageItem.SaveableMediaPath;
            if (string.IsNullOrWhiteSpace(mediaPath) || !System.IO.File.Exists(mediaPath))
            {
                if (_viewModel != null)
                {
                    _viewModel.ActionStatusMessage = "无法保存：本地媒体文件不存在。";
                }

                return;
            }

            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel == null)
            {
                return;
            }

            var extension = System.IO.Path.GetExtension(mediaPath);
            var suggestedName = $"horizon_media_{DateTime.Now:yyyyMMddHHmmssfff}_{Guid.NewGuid().ToString("N")[..6]}{extension}";

            var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "保存媒体文件",
                SuggestedFileName = suggestedName
            });

            if (file == null)
            {
                return;
            }

            try
            {
                System.IO.File.Copy(mediaPath, file.Path.LocalPath, overwrite: true);
                if (_viewModel != null)
                {
                    _viewModel.ActionStatusMessage = $"文件已保存到：{file.Path.LocalPath}";
                }
            }
            catch (Exception ex)
            {
                if (_viewModel != null)
                {
                    _viewModel.ActionStatusMessage = $"保存失败：{ex.Message}";
                }
            }
        }
    }
}
