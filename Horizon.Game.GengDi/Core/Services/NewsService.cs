using System.Collections.Generic;
using System.Threading.Tasks;
using Horizon.Game.GengDi.Data.Repositories;
using Horizon.Game.GengDi.Models;

namespace Horizon.Game.GengDi.Core.Services
{
	public class NewsService
	{
		private readonly NewsRepository _newsRepository;

		public NewsService()
		{
			_newsRepository = new NewsRepository();
		}

		public Task<List<News>> GetAllNewsAsync()
		{
			return ClientAsyncDispatcher.RunLiteDbAsync(() => _newsRepository.GetAll());
		}

		public Task<List<News>> GetNewsByGameAsync(string gameId)
		{
			return ClientAsyncDispatcher.RunLiteDbAsync(() => _newsRepository.GetNewsByGameId(gameId));
		}

		public Task<List<News>> GetNewsByCategoryAsync(string category)
		{
			return ClientAsyncDispatcher.RunLiteDbAsync(() => _newsRepository.GetNewsByCategory(category));
		}

		public Task<News> GetNewsByIdAsync(string id)
		{
			return ClientAsyncDispatcher.RunLiteDbAsync(() => _newsRepository.GetById(id));
		}

		public Task AddNewsAsync(News news)
		{
			return ClientAsyncDispatcher.RunLiteDbAsync(() => _newsRepository.Add(news));
		}

		public Task UpdateNewsAsync(News news)
		{
			return ClientAsyncDispatcher.RunLiteDbAsync(() => _newsRepository.Update(news));
		}

		public Task DeleteNewsAsync(string id)
		{
			return ClientAsyncDispatcher.RunLiteDbAsync(() => _newsRepository.Delete(id));
		}
	}
}