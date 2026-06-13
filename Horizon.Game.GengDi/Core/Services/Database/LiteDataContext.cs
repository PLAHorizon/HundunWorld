using LiteDB;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Linq.Expressions;

namespace Horizon.Game.GengDi.Core.Services.Database
{
    public enum LiteDatabaseKind
    {
        Game = 0,
        Config = 1,
        Cache = 2
    }

    public static class LiteDataContext
    {
        #region 私有字段
        private static string _dbPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "HorizonGame");
        private static string _gameDbPath => Path.Combine(_dbPath, "game_data.db");
        private static string _configDbPath => Path.Combine(_dbPath, "config.db");
        private static string _cacheDbPath => Path.Combine(_dbPath, "cache.db");

        private const string _dbPassword = "HorizonGame2024!@#";

        private static LiteDatabase _gameDatabase;
        private static LiteDatabase _configDatabase;
        private static LiteDatabase _cacheDatabase;
        private static Dictionary<LiteDatabaseKind, LiteDatabase> _databaseKind;
        private static readonly object _lockObject = new object();
        #endregion

        static LiteDataContext()
        {
            Initialize();
        }

        #region 公共属性
        public static bool IsInitialized { get; private set; }
        public static string DatabaseVersion => "5.0.21";
        #endregion

        #region 数据模型
        public class PassportInfo : LiteDbBaseModel<int>
        {
            public string PassportId { get; set; }
            public string Password { get; set; }
            public ulong UserId { get; set; }
            public bool IsCurrentPassport { get; set; }
            public string Token { get; internal set; }
            public bool RememberPassword { get; internal set; }
        }

        public class GameUserInfo : LiteDbBaseModel<int>
        {
            public string PassportId { get; set; }
            public ulong GameUserId { get; set; }
            public int GameId { get; set; }
            public int ZoneId { get; set; }
            public int ServerId { get; set; }
            public ulong CharacterId { get; set; }
            public string CharacterName { get; set; }
            public int Level { get; set; }
            public string Class { get; set; }
            public DateTime LastLoginTime { get; set; }
            public DateTime LastSyncTime { get; set; }
        }

        public class GameConfig
        {
            public string Id { get; set; } = ObjectId.NewObjectId().ToString();
            public string Key { get; set; }
            public string Value { get; set; }
            public string Category { get; set; }
            public DateTime CreatedAt { get; set; } = DateTime.Now;
            public DateTime UpdatedAt { get; set; } = DateTime.Now;
        }

        public class CacheData
        {
            public string Id { get; set; } = ObjectId.NewObjectId().ToString();
            public string Key { get; set; }
            public string Data { get; set; }
            public DateTime ExpiresAt { get; set; }
            public DateTime CreatedAt { get; set; } = DateTime.Now;
        }
        #endregion

        #region 初始化和配置
        public static bool Initialize()
        {
            lock (_lockObject)
            {
                try
                {
                    if (IsInitialized)
                        return true;

                    if (!Directory.Exists(_dbPath))
                    {
                        Directory.CreateDirectory(_dbPath);
                    }

                    _gameDatabase = new LiteDatabase(new ConnectionString
                    {
                        Filename = _gameDbPath,
                        Password = _dbPassword,
                        Connection = ConnectionType.Shared
                    });

                    _configDatabase = new LiteDatabase(new ConnectionString
                    {
                        Filename = _configDbPath,
                        Password = _dbPassword,
                        Connection = ConnectionType.Shared
                    });

                    _cacheDatabase = new LiteDatabase(new ConnectionString
                    {
                        Filename = _cacheDbPath,
                        Password = _dbPassword,
                        Connection = ConnectionType.Shared
                    });

                    CreateIndexes();
                    IsInitialized = true;
                    CleanExpiredCache();

                    _databaseKind = new Dictionary<LiteDatabaseKind, LiteDatabase> {
                        { LiteDatabaseKind.Game, _gameDatabase},
                        { LiteDatabaseKind.Config, _configDatabase},
                        { LiteDatabaseKind.Cache, _cacheDatabase},
                    };

                    return true;
                }
                catch (Exception ex)
                {
                    Debug.Print($"[LiteDataContext] 数据库初始化失败: {ex.Message}");
                    IsInitialized = false;
                    return false;
                }
            }
        }

        private static void CreateIndexes()
        {
            try
            {
                _configDatabase.GetCollection<GameConfig>().EnsureIndex("Key");
                _configDatabase.GetCollection<GameConfig>().EnsureIndex("Category");
                _configDatabase.GetCollection<PassportInfo>().EnsureIndex("PassportId");
                _gameDatabase.GetCollection<GameUserInfo>().EnsureIndex("PassportId");
                _gameDatabase.GetCollection<GameUserInfo>().EnsureIndex("GameUserId");
                _gameDatabase.GetCollection<GameUserInfo>().EnsureIndex("CharacterId");
                _cacheDatabase.GetCollection<CacheData>().EnsureIndex("Key");
                _cacheDatabase.GetCollection<CacheData>().EnsureIndex("ExpiresAt");
            }
            catch (Exception ex)
            {
                Debug.Print($"[LiteDataContext] 创建索引失败: {ex.Message}");
            }
        }

        public static void SetDatabasePath(string path)
        {
            if (IsInitialized)
            {
                Debug.Print("[LiteDataContext] 数据库已初始化，无法更改路径");
                return;
            }

            _dbPath = path;
        }

        public static void Close()
        {
            lock (_lockObject)
            {
                try
                {
                    _gameDatabase?.Dispose();
                    _configDatabase?.Dispose();
                    _cacheDatabase?.Dispose();

                    _gameDatabase = null;
                    _configDatabase = null;
                    _cacheDatabase = null;

                    IsInitialized = false;
                }
                catch (Exception ex)
                {
                    Debug.Print($"[LiteDataContext] 关闭数据库连接时发生错误: {ex.Message}");
                }
            }
        }
        #endregion

        #region 泛型数据操作
        public static BsonValue GetId()
        {
            return ObjectId.NewObjectId();
        }

        public static T FirstOrDefault<T>(LiteDatabaseKind liteDatabaseKind)
        {
            T model = default;
            var models = _databaseKind[liteDatabaseKind].GetCollection<T>().FindAll()?.GetEnumerator();
            if (models != null && models.MoveNext())
            {
                model = models.Current;
                models.Dispose();
            }
            return model;
        }

        public static IEnumerable<T> Where_<T>(LiteDatabaseKind liteDatabaseKind, Func<T, bool> where = default)
        {
            return _databaseKind[liteDatabaseKind].GetCollection<T>().FindAll().Where(where == default ? m => true : where);
        }

        public static BsonValue Add<T>(LiteDatabaseKind liteDatabaseKind, T model)
        {
            var tem = _databaseKind[liteDatabaseKind].GetCollection<T>().Insert(model);
            return tem;
        }

        public static int AddRange<T>(LiteDatabaseKind liteDatabaseKind, IEnumerable<T> models, bool isAll = false)
        {
            int count = 0;
            if (_databaseKind[liteDatabaseKind].BeginTrans())
            {
                count = _databaseKind[liteDatabaseKind].GetCollection<T>().InsertBulk(models);
                if (isAll && count != models.Count())
                {
                    _databaseKind[liteDatabaseKind].Rollback();
                    return -1;
                }
                _databaseKind[liteDatabaseKind].Commit();
            }
            return count;
        }

        public static bool Remove<T>(LiteDatabaseKind liteDatabaseKind, BsonValue id)
        {
            return _databaseKind[liteDatabaseKind].GetCollection<T>().Delete(id);
        }

        public static int Removes<T, K>(LiteDatabaseKind liteDatabaseKind, IEnumerable<BsonValue> ids, bool isAll = false) where T : LiteDbBaseModel<K>
        {
            int count = 0;
            if (_databaseKind[liteDatabaseKind].BeginTrans())
            {
                count = _databaseKind[liteDatabaseKind].GetCollection<T>().DeleteMany(Query.In("Id", ids));
                if (isAll && count != ids.Count())
                {
                    _databaseKind[liteDatabaseKind].Rollback();
                    return -1;
                }
                _databaseKind[liteDatabaseKind].Commit();
            }
            return count;
        }

        public static bool Upsert<T, K>(LiteDatabaseKind liteDatabaseKind, K id, T model) where K : BsonValue
        {
            return _databaseKind[liteDatabaseKind].GetCollection<T>().Upsert(id, model);
        }

        public static bool Update<T>(LiteDatabaseKind liteDatabaseKind, T model)
        {
            return _databaseKind[liteDatabaseKind].GetCollection<T>().Update(model);
        }

        public static int Updates<T, K>(LiteDatabaseKind liteDatabaseKind, IEnumerable<T> models, bool isAll = false)
        {
            try
            {
                int count = _databaseKind[liteDatabaseKind].GetCollection<T>().Update(models);
                if (isAll && _databaseKind[liteDatabaseKind].BeginTrans())
                {
                    if (count != models.Count())
                    {
                        _databaseKind[liteDatabaseKind].Rollback();
                        count = -1;
                    }
                    _databaseKind[liteDatabaseKind].Commit();
                }
                return count;
            }
            catch (Exception e)
            {
                return -1;
            }
        }

        public static int DeletedAll<T>(LiteDatabaseKind liteDatabaseKind)
        {
            return _databaseKind[liteDatabaseKind].GetCollection<T>().DeleteAll();
        }

        public static int DeletedMany<T>(LiteDatabaseKind liteDatabaseKind, Expression<Func<T, bool>> predicate)
        {
            var collection = _databaseKind[liteDatabaseKind].GetCollection<T>();
            var items = collection.Find(predicate);
            int count = 0;
            foreach (var item in items)
            {
                if (collection.Delete(new BsonValue(GetIdValue(item))))
                    count++;
            }
            return count;
        }

        private static object GetIdValue<T>(T item)
        {
            var idProperty = typeof(T).GetProperty("Id");
            return idProperty?.GetValue(item) ?? item;
        }

        public static List<T> Where<T>(LiteDatabaseKind liteDatabaseKind, Expression<Func<T, bool>> where)
        {
            return _databaseKind[liteDatabaseKind].GetCollection<T>().Find(where)?.ToList();
        }

        public static T FirstOrDefault<T>(LiteDatabaseKind liteDatabaseKind, Expression<Func<T, bool>> predicate)
        {
            return _databaseKind[liteDatabaseKind].GetCollection<T>().FindOne(predicate);
        }
        #endregion

        #region 通行证管理
        private const int PassportRecordId = 1;
        private const string PassportCollection = "passport_info";

        public static bool SavePassportInfo(string passportId, string password, bool rememberPassword = true, ulong userId = 0, string token = "")
        {
            if (string.IsNullOrEmpty(passportId)) return false;
            if (!EnsureInitialized()) return false;

            try
            {
                var col = _configDatabase.GetCollection<PassportInfo>(PassportCollection);

                var passportInfo = new PassportInfo
                {
                    Id = PassportRecordId,
                    PassportId = passportId,
                    RememberPassword = rememberPassword,
                    UserId = userId,
                    IsCurrentPassport = true
                };

                if (rememberPassword)
                {
                    passportInfo.Password = password ?? string.Empty;
                    passportInfo.Token = token ?? string.Empty;
                }
                else
                {
                    passportInfo.Password = string.Empty;
                    passportInfo.Token = string.Empty;
                }

                col.Upsert(passportInfo);
                return true;
            }
            catch (Exception ex)
            {
                Debug.Print($"[LiteDataContext] SavePassportInfo 失败: {ex.Message}");
                return false;
            }
        }

        public static bool TryLoadPassportInfo(out string passportId, out string password)
        {
            passportId = string.Empty;
            password = string.Empty;

            if (!EnsureInitialized()) return false;

            try
            {
                var col = _configDatabase.GetCollection<PassportInfo>(PassportCollection);
                var record = col.FindById(PassportRecordId);
                if (record == null || !record.RememberPassword || string.IsNullOrEmpty(record.PassportId))
                    return false;

                passportId = record.PassportId;
                password = record.Password;
                return true;
            }
            catch (Exception ex)
            {
                Debug.Print($"[LiteDataContext] TryLoadPassportInfo 失败: {ex.Message}");
                return false;
            }
        }

        public static void ClearPassportInfo()
        {
            if (!EnsureInitialized()) return;

            try
            {
                var col = _configDatabase.GetCollection<PassportInfo>(PassportCollection);
                col.Delete(PassportRecordId);
            }
            catch (Exception ex)
            {
                Debug.Print($"[LiteDataContext] ClearPassportInfo 失败: {ex.Message}");
            }
        }
        #endregion

        #region 游戏用户管理
        private const string GameUserCollection = "game_user_info";

        public static bool SaveGameUserInfo(GameUserInfo gameUserInfo)
        {
            if (gameUserInfo == null) return false;
            if (!EnsureInitialized()) return false;

            try
            {
                var col = _gameDatabase.GetCollection<GameUserInfo>(GameUserCollection);
                var existing = col.FindOne(x => x.GameUserId == gameUserInfo.GameUserId);

                if (existing != null)
                {
                    gameUserInfo.Id = existing.Id;
                    col.Update(gameUserInfo);
                }
                else
                {
                    col.Insert(gameUserInfo);
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.Print($"[LiteDataContext] SaveGameUserInfo 失败: {ex.Message}");
                return false;
            }
        }

        public static List<GameUserInfo> GetGameUserInfos(string passportId)
        {
            if (string.IsNullOrEmpty(passportId)) return new List<GameUserInfo>();
            if (!EnsureInitialized()) return new List<GameUserInfo>();

            try
            {
                var col = _gameDatabase.GetCollection<GameUserInfo>(GameUserCollection);
                return col.Find(x => x.PassportId == passportId).ToList();
            }
            catch (Exception ex)
            {
                Debug.Print($"[LiteDataContext] GetGameUserInfos 失败: {ex.Message}");
                return new List<GameUserInfo>();
            }
        }

        public static GameUserInfo GetGameUserInfo(ulong gameUserId)
        {
            if (gameUserId <= 0) return null;
            if (!EnsureInitialized()) return null;

            try
            {
                var col = _gameDatabase.GetCollection<GameUserInfo>(GameUserCollection);
                return col.FindOne(x => x.GameUserId == gameUserId);
            }
            catch (Exception ex)
            {
                Debug.Print($"[LiteDataContext] GetGameUserInfo 失败: {ex.Message}");
                return null;
            }
        }

        public static bool DeleteGameUserInfo(ulong gameUserId)
        {
            if (gameUserId <= 0) return false;
            if (!EnsureInitialized()) return false;

            try
            {
                var col = _gameDatabase.GetCollection<GameUserInfo>(GameUserCollection);
                return col.DeleteMany(Query.EQ("GameUserId", gameUserId)) > 0;
            }
            catch (Exception ex)
            {
                Debug.Print($"[LiteDataContext] DeleteGameUserInfo 失败: {ex.Message}");
                return false;
            }
        }
        #endregion

        #region 缓存管理
        public static bool SetCache(string key, string data, TimeSpan? expiration = null)
        {
            if (string.IsNullOrEmpty(key)) return false;
            if (!EnsureInitialized()) return false;

            try
            {
                var collection = _cacheDatabase.GetCollection<CacheData>();
                var existing = collection.FindOne(x => x.Key == key);
                var expiresAt = expiration.HasValue ? DateTime.Now.Add(expiration.Value) : DateTime.Now.AddDays(30);

                if (existing != null)
                {
                    existing.Data = data;
                    existing.ExpiresAt = expiresAt;
                    collection.Update(existing);
                }
                else
                {
                    var cache = new CacheData
                    {
                        Key = key,
                        Data = data,
                        ExpiresAt = expiresAt
                    };
                    collection.Insert(cache);
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.Print($"[LiteDataContext] 设置缓存失败: {ex.Message}");
                return false;
            }
        }

        public static string GetCache(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (!EnsureInitialized()) return null;

            try
            {
                var collection = _cacheDatabase.GetCollection<CacheData>();
                var cache = collection.FindOne(x => x.Key == key);

                if (cache == null || cache.ExpiresAt < DateTime.Now)
                {
                    if (cache != null)
                    {
                        collection.Delete(cache.Id);
                    }
                    return null;
                }

                return cache.Data;
            }
            catch (Exception ex)
            {
                Debug.Print($"[LiteDataContext] 获取缓存失败: {ex.Message}");
                return null;
            }
        }

        public static bool RemoveCache(string key)
        {
            if (string.IsNullOrEmpty(key)) return false;
            if (!EnsureInitialized()) return false;

            try
            {
                var collection = _cacheDatabase.GetCollection<CacheData>();
                return collection.DeleteMany(Query.EQ("Key", key)) > 0;
            }
            catch (Exception ex)
            {
                Debug.Print($"[LiteDataContext] 删除缓存失败: {ex.Message}");
                return false;
            }
        }

        public static void CleanExpiredCache()
        {
            if (!EnsureInitialized()) return;

            try
            {
                var collection = _cacheDatabase.GetCollection<CacheData>();
                var deletedCount = collection.DeleteMany(Query.LT("ExpiresAt", DateTime.Now));
                if (deletedCount > 0)
                {
                    Debug.Print($"[LiteDataContext] 清理了 {deletedCount} 个过期缓存");
                }
            }
            catch (Exception ex)
            {
                Debug.Print($"[LiteDataContext] 清理过期缓存失败: {ex.Message}");
            }
        }
        #endregion

        #region 数据库维护
        public static void CompactDatabase()
        {
            if (!EnsureInitialized()) return;

            try
            {
                _gameDatabase.Rebuild();
                _configDatabase.Rebuild();
                _cacheDatabase.Rebuild();
                Debug.Print("[LiteDataContext] 数据库压缩完成");
            }
            catch (Exception ex)
            {
                Debug.Print($"[LiteDataContext] 数据库压缩失败: {ex.Message}");
            }
        }

        public static Dictionary<string, object> GetDatabaseInfo()
        {
            var info = new Dictionary<string, object>();

            if (!EnsureInitialized())
            {
                info["Status"] = "Not Initialized";
                return info;
            }

            try
            {
                info["Status"] = "Initialized";
                info["Version"] = DatabaseVersion;
                info["DatabasePath"] = _dbPath;
                info["GameDatabaseSize"] = new FileInfo(_gameDbPath).Length;
                info["ConfigDatabaseSize"] = new FileInfo(_configDbPath).Length;
                info["CacheDatabaseSize"] = new FileInfo(_cacheDbPath).Length;

                info["PassportCount"] = _configDatabase.GetCollection<PassportInfo>(PassportCollection).Count();
                info["GameUserCount"] = _gameDatabase.GetCollection<GameUserInfo>(GameUserCollection).Count();
                info["CacheCount"] = _cacheDatabase.GetCollection<CacheData>().Count();
            }
            catch (Exception ex)
            {
                Debug.Print($"[LiteDataContext] 获取数据库信息失败: {ex.Message}");
                info["Error"] = ex.Message;
            }

            return info;
        }
        #endregion

        #region 私有辅助方法
        private static bool EnsureInitialized()
        {
            if (!IsInitialized)
            {
                Debug.Print("[LiteDataContext] 数据库未初始化，正在尝试初始化...");
                return Initialize();
            }
            return true;
        }
        #endregion
    }
}
