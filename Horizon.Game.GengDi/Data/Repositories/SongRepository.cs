using System.Collections.Generic;
using System.Linq;
using Horizon.Game.GengDi.Data.Storage;
using Horizon.Game.GengDi.Models;
using LiteDB;

namespace Horizon.Game.GengDi.Data.Repositories
{
    public class SongRepository
    {
        private readonly LiteDatabase _database;
        private readonly ILiteCollection<Song> _collection;

        public SongRepository()
        {
            _database = DatabaseManager.Database;
            _collection = _database.GetCollection<Song>();
        }

        public void Add(Song song)
        {
            _collection.Insert(song);
        }

        public void Update(Song song)
        {
            _collection.Update(song);
        }

        public void Delete(string id)
        {
            _collection.Delete(id);
        }

        public Song GetById(string id)
        {
            return _collection.FindById(id);
        }

        public List<Song> GetAll()
        {
            return _collection.FindAll().ToList();
        }

        public List<Song> GetByArtist(string artistId)
        {
            return _collection.Find(s => s.ArtistId == artistId).ToList();
        }

        public List<Song> GetByAlbum(string albumId)
        {
            return _collection.Find(s => s.AlbumId == albumId).ToList();
        }

        public List<Song> GetFavorites()
        {
            return _collection.Find(s => s.IsFavorite).ToList();
        }

        public List<Song> Search(string keyword)
        {
            return _collection.Find(s =>
                s.Title.Contains(keyword) ||
                s.ArtistName.Contains(keyword) ||
                s.AlbumName.Contains(keyword)).ToList();
        }
    }
}
