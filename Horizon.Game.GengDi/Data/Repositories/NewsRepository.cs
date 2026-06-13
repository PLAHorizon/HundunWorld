using System.Collections.Generic;
using System.Linq;
using Horizon.Game.GengDi.Data.Storage;
using Horizon.Game.GengDi.Models;
using LiteDB;

namespace Horizon.Game.GengDi.Data.Repositories
{
    public class NewsRepository
    {
        private readonly LiteDatabase _database;
        private readonly ILiteCollection<News> _collection;

        public NewsRepository()
        {
            _database = DatabaseManager.Database;
            _collection = _database.GetCollection<News>();
        }

        public void Add(Models.News news)
        {
            _collection.Insert(news);
        }

        public void Update(Models.News news)
        {
            _collection.Update(news);
        }

        public void Delete(string id)
        {
            _collection.Delete(id);
        }

        public Models.News GetById(string id)
        {
            return _collection.FindById(id);
        }

        public List<Models.News> GetAll()
        {
            return _collection.FindAll().OrderByDescending(n => n.PublishDate).ToList();
        }

        public List<Models.News> GetNewsByGameId(string gameId)
        {
            return _collection.Find(n => n.GameId == gameId).OrderByDescending(n => n.PublishDate).ToList();
        }

        public List<Models.News> GetNewsByCategory(string category)
        {
            return _collection.Find(n => n.Category == category).OrderByDescending(n => n.PublishDate).ToList();
        }
    }
}