using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Horizon.Game.GengDi.Data.Repositories;
using Horizon.Game.GengDi.Enums;
using Horizon.Game.GengDi.Models;

namespace Horizon.Game.GengDi.Core.Services
{
	public class NotificationService
	{
		private readonly MessageRepository _messageRepository;

		public NotificationService()
		{
			_messageRepository = new MessageRepository();
		}

		public Task<List<Horizon.Game.GengDi.Models.IMMessage>> GetUserNotificationsAsync(string userId)
		{
			return ClientAsyncDispatcher.RunLiteDbAsync(() => _messageRepository.GetByReceiverId(userId));
		}

		public Task<Horizon.Game.GengDi.Models.IMMessage> GetNotificationByIdAsync(string id)
		{
			return ClientAsyncDispatcher.RunLiteDbAsync(() => _messageRepository.GetById(id));
		}

		public async Task MarkAsReadAsync(string notificationId)
		{
			var notification = await GetNotificationByIdAsync(notificationId).ConfigureAwait(false);
			if (notification == null)
			{
				return;
			}

			notification.IsRead = true;
			await ClientAsyncDispatcher.RunLiteDbAsync(() => _messageRepository.Update(notification)).ConfigureAwait(false);
		}

		public async Task MarkAllAsReadAsync(string userId)
		{
			var notifications = await GetUserNotificationsAsync(userId).ConfigureAwait(false);
			foreach (var notification in notifications)
			{
				if (!notification.IsRead)
				{
					notification.IsRead = true;
					await ClientAsyncDispatcher.RunLiteDbAsync(() => _messageRepository.Update(notification)).ConfigureAwait(false);
				}
			}
		}

		public Task SendNotificationAsync(string senderId, string receiverId, string content, MessageType type)
		{
			var message = new Horizon.Game.GengDi.Models.IMMessage
			{
				Id = Guid.NewGuid().ToString(),
				SenderId = senderId,
				ReceiverId = receiverId,
				Content = content,
				Timestamp = DateTime.Now,
				IsRead = false,
				Type = type
			};

			return ClientAsyncDispatcher.RunLiteDbAsync(() => _messageRepository.Add(message));
		}

		public Task SendActivityNotificationAsync(string receiverId, string activityName, string activityDescription)
		{
			var content = $"活动通知：{activityName} - {activityDescription}";
			return SendNotificationAsync("system", receiverId, content, MessageType.Activity);
		}

		public Task SendNewsNotificationAsync(string receiverId, string newsTitle)
		{
			var content = $"新闻通知：{newsTitle}";
			return SendNotificationAsync("system", receiverId, content, MessageType.News);
		}
	}
}