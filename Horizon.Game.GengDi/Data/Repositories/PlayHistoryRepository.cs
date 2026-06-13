using System;
using System.Collections.Generic;
using System.Linq;
using Horizon.Game.GengDi.Data.Storage;
using LiteDB;

namespace Horizon.Game.GengDi.Data.Repositories
{
    public class PlayHistoryRecord
    {
        [LiteDB.BsonId]
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string SongId { get; set; }
        public string UserId { get; set; }
        public DateTime PlayedAt { get; set; } = DateTime.UtcNow;
    }

    public class PlayHistoryRepository
    {
        private readonly LiteDatabase _database;
        private readonly ILiteCollection<PlayHistoryRecord> _collection;

        public PlayHistoryRepository()
        {
            _database = DatabaseManager.Database;
            _collection = _database.GetCollection<PlayHistoryRecord>();
        }

        public void Add(PlayHistoryRecord record)
        {
            _collection.Insert(record);
        }

        public void Delete(string id)
        {
            _collection.Delete(id);
        }

        public List<PlayHistoryRecord> GetByUser(string userId, int limit = 100)
        {
            return _collection.Find(h => h.UserId == userId)
                .OrderByDescending(h => h.PlayedAt)
                .Take(limit)
                .ToList();
        }

        public List<PlayHistoryRecord> GetRecent(int limit = 50)
        {
            return _collection.FindAll()
                .OrderByDescending(h => h.PlayedAt)
                .Take(limit)
                .ToList();
        }

        public void ClearByUser(string userId)
        {
            _collection.DeleteMany(h => h.UserId == userId);
        }
    }
}
