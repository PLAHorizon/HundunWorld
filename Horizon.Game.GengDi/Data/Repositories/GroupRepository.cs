using System.Collections.Generic;
using System.Linq;
using Horizon.Game.GengDi.Data.Storage;
using Horizon.Game.GengDi.Models;
using LiteDB;

namespace Horizon.Game.GengDi.Data.Repositories
{
    public class GroupRepository
    {
        private readonly LiteDatabase _database;
        private readonly ILiteCollection<Group> _collection;

        public GroupRepository()
        {
            _database = DatabaseManager.Database;
            _collection = _database.GetCollection<Group>();
        }

        public void Add(Models.Group group)
        {
            _collection.Insert(group);
        }

        public void Update(Models.Group group)
        {
            _collection.Update(group);
        }

        public void Delete(string id)
        {
            _collection.Delete(id);
        }

        public Models.Group GetById(string id)
        {
            return _collection.FindById(id);
        }

        public List<Models.Group> GetAll()
        {
            return _collection.FindAll().ToList();
        }

        public List<Models.Group> GetGroupsByMember(string userId)
        {
            return _collection.FindAll().Where(g => g.Members.Contains(userId)).ToList();
        }

        public List<Models.Group> GetGroupsByCreator(string creatorId)
        {
            return _collection.Find(g => g.CreatorId == creatorId).ToList();
        }
    }
}