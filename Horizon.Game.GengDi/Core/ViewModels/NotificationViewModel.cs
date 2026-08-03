using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Horizon.Game.GengDi.Models;
using Horizon.Game.GengDi.Enums;
using Horizon.Game.GengDi.Core.Services;

namespace Horizon.Game.GengDi.Core.ViewModels
{
    public class NotificationViewModel : ViewModelBase
    {
        private readonly AsyncRelayCommand _markAllAsReadCommand;
        private ObservableCollection<Horizon.Game.GengDi.Models.IMMessage> _notifications;
        private Horizon.Game.GengDi.Models.IMMessage _selectedNotification;
        private bool _isLoading;
        private int _unreadCount;
        private ObservableCollection<NotificationItemViewModel> _sampleNotifications;

        public ObservableCollection<Horizon.Game.GengDi.Models.IMMessage> Notifications
        {
            get => _notifications;
            set => SetProperty(ref _notifications, value);
        }

        /// <summary>
        /// 示例通知数据集合（对应设计稿 7 条通知：5 未读 + 2 已读）。
        /// 用于设计时预览和无网络时的占位展示。
        /// </summary>
        public ObservableCollection<NotificationItemViewModel> SampleNotifications
        {
            get => _sampleNotifications;
            set => SetProperty(ref _sampleNotifications, value);
        }

        public Horizon.Game.GengDi.Models.IMMessage SelectedNotification
        {
            get => _selectedNotification;
            set => SetProperty(ref _selectedNotification, value);
        }

        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                if (SetProperty(ref _isLoading, value))
                {
                    _markAllAsReadCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public int UnreadCount
        {
            get => _unreadCount;
            set
            {
                if (SetProperty(ref _unreadCount, value))
                {
                    _markAllAsReadCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public ICommand MarkAllAsReadCommand { get; }

        private readonly NotificationService _notificationService;
        private string _currentUserId = "user123"; // 模拟用户ID

        public NotificationViewModel()
        {
            _notificationService = new NotificationService();
            Notifications = new ObservableCollection<Horizon.Game.GengDi.Models.IMMessage>();
            _markAllAsReadCommand = new AsyncRelayCommand(MarkAllAsReadCurrentUserAsync, CanMarkAllAsRead);
            MarkAllAsReadCommand = _markAllAsReadCommand;

            // 加载设计稿示例数据（7条：5未读 + 2已读）
            SampleNotifications = new ObservableCollection<NotificationItemViewModel>(
                NotificationItemViewModel.CreateSampleData());
            UnreadCount = SampleNotifications.Count(n => !n.IsRead);
        }

        public async Task LoadNotificationsAsync(string userId)
        {
            _currentUserId = userId;
            IsLoading = true;
            try
            {
                var notifications = await _notificationService.GetUserNotificationsAsync(userId);
                Notifications.Clear();
                foreach (var item in notifications)
                {
                    Notifications.Add(item);
                }
                UpdateUnreadCount();
            }
            catch (Exception)
            {
                // 处理异常
            }
            finally
            {
                IsLoading = false;
            }
        }

        public async Task MarkAsReadAsync(string notificationId)
        {
            try
            {
                await _notificationService.MarkAsReadAsync(notificationId);
                var notification = Notifications.FirstOrDefault(n => n.Id == notificationId);
                if (notification != null)
                {
                    notification.IsRead = true;
                    UpdateUnreadCount();
                }
            }
            catch (Exception)
            {
                // 处理异常
            }
        }

        public async Task MarkAllAsReadAsync(string userId)
        {
            try
            {
                await _notificationService.MarkAllAsReadAsync(userId);
                foreach (var notification in Notifications)
                {
                    notification.IsRead = true;
                }
                UpdateUnreadCount();
            }
            catch (Exception)
            {
                // 处理异常
            }
        }

        private void UpdateUnreadCount()
        {
            UnreadCount = Notifications.Count(n => !n.IsRead);
        }

        private Task MarkAllAsReadCurrentUserAsync()
        {
            return MarkAllAsReadAsync(_currentUserId);
        }

        private bool CanMarkAllAsRead()
        {
            return !IsLoading && UnreadCount > 0;
        }
    }
}