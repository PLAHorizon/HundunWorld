using System.Collections.Generic;
using System.Linq;
using Horizon.Game.GengDi.Data.Storage;
using Horizon.Game.GengDi.Models;
using LiteDB;

namespace Horizon.Game.GengDi.Data.Repositories
{
    public class PlaylistRepository
    {
        private readonly LiteDatabase _database;
        private readonly ILiteCollection<Playlist> _collection;

        public PlaylistRepository()
        {
            _database = DatabaseManager.Database;
            _collection = _database.GetCollection<Playlist>();
        }

        public void Add(Playlist playlist)
        {
            _collection.Insert(playlist);
        }

        public void Update(Playlist playlist)
        {
            _collection.Update(playlist);
        }

        public void Delete(string id)
        {
            _collection.Delete(id);
        }

        public Playlist GetById(string id)
        {
            return _collection.FindById(id);
        }

        public List<Playlist> GetAll()
        {
            return _collection.FindAll().ToList();
        }

        public List<Playlist> GetByCreator(string creatorId)
        {
            return _collection.Find(p => p.CreatorId == creatorId).ToList();
        }

        public List<Playlist> GetFavorites()
        {
            return _collection.Find(p => p.IsFavorite).ToList();
        }

        public List<Playlist> Search(string keyword)
        {
            return _collection.Find(p =>
                p.Name.Contains(keyword) ||
                p.Description.Contains(keyword)).ToList();
        }
    }
}
