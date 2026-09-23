using System;
using System.Collections.Generic;
using System.Linq;
using Horizon.Game.GengDi.Data.Storage;
using Horizon.Game.GengDi.Models;
using LiteDB;

namespace Horizon.Game.GengDi.Data.Repositories
{
    public class MessageRepository
    {
        private readonly LiteDatabase _database;
        private readonly ILiteCollection<Horizon.Game.GengDi.Models.IMMessage> _collection;

        public MessageRepository()
        {
            _database = DatabaseManager.Database;
            _collection = _database.GetCollection<Horizon.Game.GengDi.Models.IMMessage>();
        }

        /// <summary>测试用注入构造（临时库隔离，不触达产品数据目录）。</summary>
        internal MessageRepository(LiteDatabase database)
        {
            _database = database ?? throw new ArgumentNullException(nameof(database));
            _collection = _database.GetCollection<Horizon.Game.GengDi.Models.IMMessage>();
        }

        public void Add(Models.IMMessage message)
        {
            ArgumentNullException.ThrowIfNull(message);

            if (string.IsNullOrWhiteSpace(message.Id))
            {
                message.Id = Guid.NewGuid().ToString("N");
            }

            var existingMessage = _collection.FindById(message.Id);
            if (existingMessage != null)
            {
                MergeMessage(existingMessage, message);
                _collection.Update(existingMessage);
                return;
            }

            try
            {
                _collection.Insert(message);
            }
            catch (LiteException ex) when (ex.Message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase))
            {
                var duplicatedMessage = _collection.FindById(message.Id);
                if (duplicatedMessage != null)
                {
                    MergeMessage(duplicatedMessage, message);
                    _collection.Update(duplicatedMessage);
                    return;
                }

                _collection.Upsert(message);
            }
        }

        public void Update(Models.IMMessage message)
        {
            _collection.Update(message);
        }

        public void Delete(string id)
        {
            _collection.Delete(id);
        }

        public Models.IMMessage GetById(string id)
        {
            return _collection.FindById(id);
        }

        public List<Models.IMMessage> GetAll()
        {
            return _collection.FindAll().ToList();
        }

        public List<Models.IMMessage> GetDirectConversationStates(string userId, IEnumerable<string> peerIds)
        {
            var peerArray = peerIds?
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(BsonValue)
                .ToArray() ?? Array.Empty<BsonValue>();

            if (peerArray.Length == 0)
            {
                return new List<Models.IMMessage>();
            }

            var query = Query.And(
                Query.EQ("IsGroupConversation", false),
                Query.Or(
                    Query.And(Query.EQ("SenderId", userId), Query.In("ReceiverId", peerArray)),
                    Query.And(Query.EQ("ReceiverId", userId), Query.In("SenderId", peerArray))));

            return _collection.Find(query).ToList();
        }

        public List<Models.IMMessage> GetGroupConversationStates(IEnumerable<string> groupIds)
        {
            var groupArray = groupIds?
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(BsonValue)
                .ToArray() ?? Array.Empty<BsonValue>();

            if (groupArray.Length == 0)
            {
                return new List<Models.IMMessage>();
            }

            var query = Query.And(
                Query.EQ("IsGroupConversation", true),
                Query.In("ReceiverId", groupArray));

            return _collection.Find(query).ToList();
        }

        private static BsonValue BsonValue(string value) => new BsonValue(value);

        public List<Models.IMMessage> GetMessagesBetweenUsers(string userId1, string userId2, int limit = 50)
        {
            var query = Query.And(
                Query.EQ("IsGroupConversation", false),
                Query.Or(
                    Query.And(Query.EQ("SenderId", userId1), Query.EQ("ReceiverId", userId2)),
                    Query.And(Query.EQ("SenderId", userId2), Query.EQ("ReceiverId", userId1))));

            return _collection.Find(query)
                .OrderByDescending(m => m.Timestamp)
                .Take(limit)
                .ToList()
                .OrderBy(m => m.Timestamp)
                .ToList();
        }

        public List<Models.IMMessage> GetGroupMessages(string groupId, int limit = 50)
        {
            var query = Query.And(
                Query.EQ("IsGroupConversation", true),
                Query.EQ("ReceiverId", groupId));

            return _collection.Find(query)
                .OrderByDescending(m => m.Timestamp)
                .Take(limit)
                .ToList()
                .OrderBy(m => m.Timestamp)
                .ToList();
        }

        public List<Models.IMMessage> GetMessagesBetweenUsers(string userId1, string userId2)
        {
            return GetMessagesBetweenUsers(userId1, userId2, 50);
        }

        public List<Models.IMMessage> GetGroupMessages(string groupId)
        {
            return GetGroupMessages(groupId, 50);
        }

        /// <summary>
        /// 私聊分页查询（对齐服务端 ChatHistory 的锚点翻页语义）：
        /// 返回 Timestamp 严格早于 <paramref name="anchorTimestamp"/> 的最近 <paramref name="limit"/> 条（时间升序）。
        /// anchor 传 null 时取最新一页。
        /// </summary>
        public List<Models.IMMessage> GetMessagesBetweenUsersBefore(
            string userId1, string userId2, DateTime? anchorTimestamp, int limit = 50)
        {
            var query = Query.And(
                Query.EQ("IsGroupConversation", false),
                Query.Or(
                    Query.And(Query.EQ("SenderId", userId1), Query.EQ("ReceiverId", userId2)),
                    Query.And(Query.EQ("SenderId", userId2), Query.EQ("ReceiverId", userId1))),
                anchorTimestamp.HasValue
                    ? Query.LT("Timestamp", anchorTimestamp.Value)
                    : Query.GT("Timestamp", DateTime.MinValue));

            return _collection.Find(query)
                .OrderByDescending(m => m.Timestamp)
                .Take(limit)
                .ToList()
                .OrderBy(m => m.Timestamp)
                .ToList();
        }

        /// <summary>群聊分页查询（锚点语义同上）。</summary>
        public List<Models.IMMessage> GetGroupMessagesBefore(
            string groupId, DateTime? anchorTimestamp, int limit = 50)
        {
            var query = Query.And(
                Query.EQ("IsGroupConversation", true),
                Query.EQ("ReceiverId", groupId),
                anchorTimestamp.HasValue
                    ? Query.LT("Timestamp", anchorTimestamp.Value)
                    : Query.GT("Timestamp", DateTime.MinValue));

            return _collection.Find(query)
                .OrderByDescending(m => m.Timestamp)
                .Take(limit)
                .ToList()
                .OrderBy(m => m.Timestamp)
                .ToList();
        }

        /// <summary>按会话统计未读数（私聊：对方→我；群聊：群→我）。</summary>
        public int GetUnreadCount(string userId, string peerId, bool isGroup)
        {
            var query = isGroup
                ? Query.And(Query.EQ("IsGroupConversation", true), Query.EQ("ReceiverId", userId),
                    Query.EQ("SenderId", peerId), Query.EQ("IsRead", false))
                : Query.And(Query.EQ("IsGroupConversation", false), Query.EQ("ReceiverId", userId),
                    Query.EQ("SenderId", peerId), Query.EQ("IsRead", false));
            return _collection.Count(query);
        }

        /// <summary>标记会话全部消息已读，返回更新条数。</summary>
        public int MarkConversationRead(string userId, string peerId, bool isGroup)
        {
            var query = isGroup
                ? Query.And(Query.EQ("IsGroupConversation", true), Query.EQ("ReceiverId", userId), Query.EQ("IsRead", false))
                : Query.And(Query.EQ("IsGroupConversation", false), Query.EQ("ReceiverId", userId),
                    Query.EQ("SenderId", peerId), Query.EQ("IsRead", false));
            return _collection.UpdateMany("{ IsRead: true }", query);
        }

        public List<Models.IMMessage> GetUnreadMessages(string userId)
        {
            return _collection.Find(m => m.ReceiverId == userId && !m.IsRead).ToList();
        }

        public List<Models.IMMessage> GetByReceiverId(string receiverId)
        {
            return _collection.Find(m => m.ReceiverId == receiverId).OrderByDescending(m => m.Timestamp).ToList();
        }

        private static void MergeMessage(Models.IMMessage target, Models.IMMessage incoming)
        {
            target.SenderId = string.IsNullOrWhiteSpace(incoming.SenderId) ? target.SenderId : incoming.SenderId;
            target.ReceiverId = string.IsNullOrWhiteSpace(incoming.ReceiverId) ? target.ReceiverId : incoming.ReceiverId;
            target.IsGroupConversation = target.IsGroupConversation || incoming.IsGroupConversation;

            if (!string.IsNullOrWhiteSpace(incoming.Content) || string.IsNullOrWhiteSpace(target.Content))
            {
                target.Content = incoming.Content ?? string.Empty;
            }

            if (incoming.Timestamp != default)
            {
                target.Timestamp = incoming.Timestamp;
            }

            target.IsRead = target.IsRead || incoming.IsRead;
            target.Type = incoming.Type;
        }
    }
}