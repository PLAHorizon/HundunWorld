using System.Collections.Generic;
using System.Linq;
using Horizon.Game.GengDi.Data.Storage;
using Horizon.Game.GengDi.Models;
using LiteDB;

namespace Horizon.Game.GengDi.Data.Repositories
{
    public class UserRepository
    {
        private readonly LiteDatabase _database;
        private readonly ILiteCollection<User> _collection;

        public UserRepository()
        {
            _database = DatabaseManager.Database;
            _collection = _database.GetCollection<User>();
        }

        public void Add(Models.User user)
        {
            _collection.Insert(user);
        }

        public void Update(Models.User user)
        {
            _collection.Update(user);
        }

        public void Delete(string id)
        {
            _collection.Delete(id);
        }

        public Models.User GetById(string id)
        {
            return _collection.FindById(id);
        }

        public Models.User GetByPassportId(string passportId)
        {
            return _collection.FindAll().FirstOrDefault(u => u.PassportId == passportId);
        }

        public Models.User GetByUsername(string username)
        {
            return _collection.FindAll().FirstOrDefault(u => u.Username == username);
        }

        public Models.User GetByEmail(string email)
        {
            return _collection.FindAll().FirstOrDefault(u => u.Email == email);
        }

        public List<Models.User> GetAll()
        {
            return _collection.FindAll().ToList();
        }
    }
}