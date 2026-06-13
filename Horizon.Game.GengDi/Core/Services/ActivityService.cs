using System.Collections.Generic;
using System.Threading.Tasks;
using Horizon.Game.GengDi.Data.Repositories;
using Horizon.Game.GengDi.Models;

namespace Horizon.Game.GengDi.Core.Services
{
	public class ActivityService
	{
		private readonly ActivityRepository _activityRepository;
		private readonly NotificationService _notificationService;

		public ActivityService()
		{
			_activityRepository = new ActivityRepository();
			_notificationService = new NotificationService();
		}

		public Task<List<Activity>> GetAllActivitiesAsync()
		{
			return ClientAsyncDispatcher.RunLiteDbAsync(() => _activityRepository.GetAll());
		}

		public Task<List<Activity>> GetActiveActivitiesAsync()
		{
			return ClientAsyncDispatcher.RunLiteDbAsync(() => _activityRepository.GetActiveActivities());
		}

		public Task<Activity> GetActivityByIdAsync(string id)
		{
			return ClientAsyncDispatcher.RunLiteDbAsync(() => _activityRepository.GetById(id));
		}

		public Task AddActivityAsync(Activity activity)
		{
			return ClientAsyncDispatcher.RunLiteDbAsync(() => _activityRepository.Add(activity));
		}

		public Task UpdateActivityAsync(Activity activity)
		{
			return ClientAsyncDispatcher.RunLiteDbAsync(() => _activityRepository.Update(activity));
		}

		public Task DeleteActivityAsync(string id)
		{
			return ClientAsyncDispatcher.RunLiteDbAsync(() => _activityRepository.Delete(id));
		}

		public async Task PushActivityNotificationAsync(string userId, string activityId)
		{
			var activity = await GetActivityByIdAsync(activityId).ConfigureAwait(false);
			if (activity != null)
			{
				await _notificationService.SendActivityNotificationAsync(userId, activity.Name, activity.Description).ConfigureAwait(false);
			}
		}

		public async Task PushAllActiveActivitiesAsync(string userId)
		{
			var activities = await GetActiveActivitiesAsync().ConfigureAwait(false);
			foreach (var activity in activities)
			{
				await _notificationService.SendActivityNotificationAsync(userId, activity.Name, activity.Description).ConfigureAwait(false);
			}
		}
	}
}