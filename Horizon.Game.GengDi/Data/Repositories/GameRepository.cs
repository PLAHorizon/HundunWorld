using System.Collections.Generic;
using System.Linq;
using Horizon.Game.GengDi.Data.Storage;
using Horizon.Game.GengDi.Models;
using LiteDB;

namespace Horizon.Game.GengDi.Data.Repositories
{
    public class GameRepository
    {
        private readonly LiteDatabase _database;
        private readonly ILiteCollection<Models.GameInfo> _collection;

        public GameRepository()
        {
            _database = DatabaseManager.Database;
            _collection = _database.GetCollection<Models.GameInfo>();
        }

        public void Add(Models.GameInfo game)
        {
            _collection.Insert(game);
        }

        public void Update(Models.GameInfo game)
        {
            _collection.Update(game);
        }

        public void Delete(string id)
        {
            _collection.Delete(id);
        }

        public Models.GameInfo GetById(string id)
        {
            return _collection.FindById(id);
        }

        public List<Models.GameInfo> GetAll()
        {
            return _collection.FindAll().ToList();
        }

        public List<Models.GameInfo> GetInstalledGames()
        {
            return _collection.Find(g => g.IsInstalled).ToList();
        }

        public List<Models.GameInfo> GetGamesByCategory(string category)
        {
            return _collection.Find(g => g.Category == category).ToList();
        }
    }
}
