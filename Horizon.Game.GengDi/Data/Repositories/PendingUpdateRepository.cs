using System.Collections.Generic;
using System.Linq;
using Horizon.Game.GengDi.Data.Storage;
using Horizon.Game.GengDi.Models;
using LiteDB;

namespace Horizon.Game.GengDi.Data.Repositories
{
    /// <summary>
    /// <see cref="PendingUpdateItem"/> 的 LiteDB 仓库。对应 <c>pending_updates</c> 集合。
    /// 所有写操作应通过 <c>ClientAsyncDispatcher.RunLiteDbAsync</c> 串行化，避免跨线程访问 LiteDB。
    /// </summary>
    public class PendingUpdateRepository
    {
        private const string CollectionName = "pending_updates";

        private readonly LiteDatabase _database;
        private readonly ILiteCollection<PendingUpdateItem> _collection;

        public PendingUpdateRepository()
        {
            _database = DatabaseManager.Database;
            _collection = _database.GetCollection<PendingUpdateItem>(CollectionName);
            _collection.EnsureIndex(x => x.GameId);
            _collection.EnsureIndex(x => x.OrderIndex);
        }

        public void Add(PendingUpdateItem item)
        {
            _collection.Insert(item);
        }

        public void Update(PendingUpdateItem item)
        {
            _collection.Update(item);
        }

        public void Delete(string id)
        {
            _collection.Delete(id);
        }

        public PendingUpdateItem GetById(string id)
        {
            return _collection.FindById(id);
        }

        public List<PendingUpdateItem> GetAll()
        {
            return _collection.FindAll().ToList();
        }

        /// <summary>
        /// 返回指定游戏的待更新条目，已按 <see cref="PendingUpdateItem.OrderIndex"/> 升序排列。
        /// </summary>
        public List<PendingUpdateItem> GetByGameIdOrdered(string gameId)
        {
            return _collection.Find(x => x.GameId == gameId).OrderBy(x => x.OrderIndex).ToList();
        }

        /// <summary>
        /// 事务式替换指定游戏的待更新列表：先删除旧条目，再写入新条目，整体在同一事务内完成。
        /// </summary>
        public void ReplaceListForGame(string gameId, IEnumerable<PendingUpdateItem> items)
        {
            _database.BeginTrans();
            try
            {
                _collection.DeleteMany(x => x.GameId == gameId);
                var ordered = items?.ToList() ?? new List<PendingUpdateItem>();
                for (var i = 0; i < ordered.Count; i++)
                {
                    var item = ordered[i];
                    item.GameId = gameId;
                    if (item.OrderIndex == 0)
                    {
                        item.OrderIndex = i;
                    }

                    if (string.IsNullOrEmpty(item.Id))
                    {
                        item.Id = PendingUpdateItem.BuildId(gameId, item.ToVersion);
                    }
                }

                if (ordered.Count > 0)
                {
                    _collection.InsertBulk(ordered);
                }

                _database.Commit();
            }
            catch
            {
                _database.Rollback();
                throw;
            }
        }

        /// <summary>
        /// 标记指定条目已应用，便于断点续更时跳过。
        /// </summary>
        public void MarkApplied(string id)
        {
            var item = _collection.FindById(id);
            if (item == null)
            {
                return;
            }

            item.Applied = true;
            _collection.Update(item);
        }

        /// <summary>
        /// 清空指定游戏的待更新列表。
        /// </summary>
        public void ClearForGame(string gameId)
        {
            _collection.DeleteMany(x => x.GameId == gameId);
        }
    }
}
