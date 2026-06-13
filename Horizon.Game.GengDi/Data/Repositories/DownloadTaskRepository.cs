using System.Collections.Generic;
using System.Linq;
using Horizon.Game.GengDi.Data.Storage;
using Horizon.Game.GengDi.Enums;
using Horizon.Game.GengDi.Models;
using LiteDB;

namespace Horizon.Game.GengDi.Data.Repositories
{
    public class DownloadTaskRepository
    {
        private readonly LiteDatabase _database;
        private readonly ILiteCollection<DownloadTask> _collection;

        public DownloadTaskRepository()
        {
            _database = DatabaseManager.Database;
            _collection = _database.GetCollection<DownloadTask>();
        }

        public void Add(Models.DownloadTask task)
        {
            _collection.Insert(task);
        }

        public void Update(Models.DownloadTask task)
        {
            _collection.Update(task);
        }

        public void Delete(string id)
        {
            _collection.Delete(id);
        }

        public Models.DownloadTask GetById(string id)
        {
            return _collection.FindById(id);
        }

        public List<Models.DownloadTask> GetAll()
        {
            return _collection.FindAll().ToList();
        }

        public List<Models.DownloadTask> GetActiveTasks()
        {
            return _collection.Find(t => t.Status == DownloadStatus.Downloading || t.Status == DownloadStatus.Pending).ToList();
        }

        public List<Models.DownloadTask> GetTasksByGameId(string gameId)
        {
            return _collection.Find(t => t.GameId == gameId).ToList();
        }

        /// <summary>
        /// 删除指定游戏的全部下载任务记录（卸载时用于清理本地数据）。
        /// </summary>
        public int DeleteByGameId(string gameId)
        {
            return _collection.DeleteMany(t => t.GameId == gameId);
        }
    }
}