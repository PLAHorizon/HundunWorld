using LiteDB;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Diagnostics.CodeAnalysis;
using FlaxEngine;
using System.IO;
using System.Threading.Tasks;
using System.Reflection;

namespace Game.Database
{

    public enum LiteDatabaseKind
    {
        Game = 0,
        Config = 1,
        Cache = 2
    }
    /// <summary>
    /// LiteDB 本地数据库上下文 - 游戏本地化数据存储解决方案
    /// 主要用于存储游戏配置、角色数据、用户偏好等，减少网络访问，提升游戏体验
    /// </summary>
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
        /// <summary>
        /// 数据库是否已初始化
        /// </summary>
        public static bool IsInitialized { get; private set; }

        /// <summary>
        /// 数据库版本
        /// </summary>
        public static string DatabaseVersion => "5.0.21";
        #endregion

        #region 数据模型
        /// <summary>
        /// 游戏配置数据模型
        /// </summary>
        public class GameConfig
        {
            public string Id { get; set; } = ObjectId.NewObjectId().ToString();
            public string Key { get; set; }
            public string Value { get; set; }
            public string Category { get; set; }
            public DateTime CreatedAt { get; set; } = DateTime.Now;
            public DateTime UpdatedAt { get; set; } = DateTime.Now;
        }
        public class PassportInfo : LiteDbBaseModel<int>
        {

            public string PassportId { get; set; }
            public string Password { get; set; }
            public ulong UserId { get; set; }
            public bool IsCurrentPassport { get; set; }
            public string Token { get; internal set; }
            public bool RememberPassword { get; internal set; }
        }
        /// <summary>
        /// 角色本地数据模型
        /// </summary>
        public class CharacterLocalData
        {
            public string Id { get; set; } = ObjectId.NewObjectId().ToString();
            public string PassportId { get; set; }
            public ulong GameUserId { get; set; }
            public int GameId { get; set; }
            public int ZoneId { get; set; }
            public int ServerId { get; set; }
            public ulong CharacterId { get; set; }
            public string CharacterName { get; set; }
            public int Level { get; set; }
            public ulong Exp { get; set; }
            public string Class { get; set; }
            public Dictionary<string, object> Attributes { get; set; } = new Dictionary<string, object>();
            public Dictionary<string, object> Equipment { get; set; } = new Dictionary<string, object>();
            public List<string> Skills { get; set; } = new List<string>();
            public DateTime LastLoginTime { get; set; }
            public DateTime LastSyncTime { get; set; }
            public bool IsDirty { get; set; } // 标记是否需要同步到服务器
            public byte d { get; set; }
        }

        /// <summary>
        /// 用户偏好设置模型
        /// </summary>
        public class UserPreferences
        {
            public string Id { get; set; } = ObjectId.NewObjectId().ToString();
            public string UserId { get; set; }
            public Dictionary<string, object> GraphicsSettings { get; set; } = new Dictionary<string, object>();
            public Dictionary<string, object> AudioSettings { get; set; } = new Dictionary<string, object>();
            public Dictionary<string, object> ControlSettings { get; set; } = new Dictionary<string, object>();
            public Dictionary<string, object> UISettings { get; set; } = new Dictionary<string, object>();
            public DateTime CreatedAt { get; set; } = DateTime.Now;
            public DateTime UpdatedAt { get; set; } = DateTime.Now;
        }

        /// <summary>
        /// 缓存数据模型
        /// </summary>
        public class CacheData
        {
            public string Id { get; set; } = ObjectId.NewObjectId().ToString();
            public string Key { get; set; }
            public string Data { get; set; }
            public DateTime ExpiresAt { get; set; }
            public DateTime CreatedAt { get; set; } = DateTime.Now;
        }

        /// <summary>
        /// 游戏统计数据模型
        /// </summary>
        public class GameStatistics
        {
            public string Id { get; set; } = ObjectId.NewObjectId().ToString();
            public ulong CharacterId { get; set; }
            public string StatType { get; set; }
            public long Value { get; set; }
            public DateTime RecordedAt { get; set; } = DateTime.Now;
        }
        #endregion

        #region 初始化和配置
        /// <summary>
        /// 初始化数据库
        /// </summary>
        public static bool Initialize()
        {
            lock (_lockObject)
            {
                try
                {
                    if (IsInitialized)
                        return true;

                    // 确保数据库目录存在
                    if (!Directory.Exists(_dbPath))
                    {
                        Directory.CreateDirectory(_dbPath);
                    }

                    // 初始化游戏数据库
                    _gameDatabase = new LiteDatabase(new ConnectionString
                    {
                        Filename = _gameDbPath,
                        Password = _dbPassword,
                        Connection = ConnectionType.Shared
                    });

                    // 初始化配置数据库
                    _configDatabase = new LiteDatabase(new ConnectionString
                    {
                        Filename = _configDbPath,
                        Password = _dbPassword,
                        Connection = ConnectionType.Shared
                    });

                    // 初始化缓存数据库
                    _cacheDatabase = new LiteDatabase(new ConnectionString
                    {
                        Filename = _cacheDbPath,
                        Password = _dbPassword,
                        Connection = ConnectionType.Shared
                    });

                    // 创建索引
                    CreateIndexes();
                    IsInitialized = true;
                    // 清理过期缓存
                    CleanExpiredCache();


                    _databaseKind = new Dictionary<LiteDatabaseKind, LiteDatabase> {
            { LiteDatabaseKind .Game,_gameDatabase},
            { LiteDatabaseKind .Config,_configDatabase},
            { LiteDatabaseKind .Cache,_cacheDatabase},
        };
                    Debug.Log("[LiteDataContext] 数据库初始化成功");
                    return true;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[LiteDataContext] 数据库初始化失败: {ex.Message}");
                    IsInitialized = false;
                    return false;
                }
            }
        }
        #region 泛型数据操作

        /// <summary>
        /// 取Id
        /// </summary>
        /// <returns></returns>
        public static BsonValue GetId()
        {
            return ObjectId.NewObjectId();
        }
        /// <summary>
        /// 获取第一个值
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
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
        /// <summary>
        /// 条件筛选
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="where"></param>
        /// <returns></returns>
        public static IEnumerable<T> Where_<T>(LiteDatabaseKind liteDatabaseKind, Func<T, bool> where = default)
        {
            return _databaseKind[liteDatabaseKind].GetCollection<T>().FindAll().Where(where == default ? m => true : where);
        }
        /// <summary>
        /// 添加单条数据
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="model"></param>
        /// <returns></returns>
        public static BsonValue Add<T>(LiteDatabaseKind liteDatabaseKind, T model)
        {
            var tem = _databaseKind[liteDatabaseKind].GetCollection<T>().Insert(model);
            return tem;
        }
        /// <summary>
        /// 批量添加数据
        /// </summary>
        /// <typeparam name="T">数据类型</typeparam>
        /// <param name="models"></param>
        /// <param name="isAll">是否使用事务，使用事务则插入数量与提交数量不一致时回滚</param>
        /// <returns>添加成功的数量</returns>
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


        /// <summary>
        ///移除数据
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="id"></param>
        /// <returns></returns>
        public static bool Remove<T>(LiteDatabaseKind liteDatabaseKind, BsonValue id)
        {
            return _databaseKind[liteDatabaseKind].GetCollection<T>().Delete(id);
        }
        /// <summary>
        /// 批量移除数据
        /// </summary>
        /// <typeparam name="T">数据类型</typeparam>
        /// <param name="models"></param>
        /// <param name="isAll">是否使用事务，使用事务则插入数量与提交数量不一致时回滚</param>
        /// <returns>移除成功的数量</returns>
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
        /// <summary>
        /// 更新或新增数据记录值
        /// </summary>
        /// <typeparam name="T">数据类型</typeparam>
        /// <param name="id">实列的Id</param>
        /// <param name="model">数据实列</param>
        /// <returns></returns>
        public static bool Upsert<T, K>(LiteDatabaseKind liteDatabaseKind, K id, T model) where K : BsonValue
        {

            return _databaseKind[liteDatabaseKind].GetCollection<T>().Upsert(id, model);
        }
        /// <summary>
        ///更新数据
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="model"></param>
        /// <returns></returns>
        public static bool Update<T>(LiteDatabaseKind liteDatabaseKind, T model)
        {
            return _databaseKind[liteDatabaseKind].GetCollection<T>().Update(model);
        }
        /// <summary>
        /// 批量更新数据
        /// </summary>
        /// <typeparam name="T">数据类型</typeparam>
        /// <param name="models"></param>
        /// <param name="isAll">是否使用事务，使用事务则插入数量与提交数量不一致时回滚</param>
        /// <returns>更新成功的数量</returns>
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

        /// <summary>
        /// 删除整个数据类型的数据集
        /// </summary>
        /// <typeparam name="T">数据类型参数</typeparam>
        /// <returns></returns>
        public static int DeletedAll<T>(LiteDatabaseKind liteDatabaseKind)
        {
            return _databaseKind[liteDatabaseKind].GetCollection<T>().DeleteAll();
        }
        /// <summary>
        /// 使用删除条件删除多项数据项
        /// </summary>
        /// <typeparam name="T">数据类型参数</typeparam>
        /// <param name="predicate">删除条件</param>
        /// <returns></returns>
        public static int DeletedMany<T>(LiteDatabaseKind liteDatabaseKind, Expression<Func<T, bool>> predicate)
        {
            // 由于LiteDB API变更，使用Find + Delete的方式替代DeleteMany
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

        /// <summary>
        /// 获取实体的Id值
        /// </summary>
        private static object GetIdValue<T>(T item)
        {
            var idProperty = typeof(T).GetProperty("Id");
            return idProperty?.GetValue(item) ?? item;
        }
        /// <summary>
        ///请求数据集
        /// </summary>
        /// <typeparam name="T">数据类型参数</typeparam>
        /// <param name="where">数据查询请求条件</param>
        /// <returns></returns>
        public static List<T> Where<T>(LiteDatabaseKind liteDatabaseKind, Expression<Func<T, bool>> where)
        {
            return _databaseKind[liteDatabaseKind].GetCollection<T>().Find(where)?.ToList();
        }

        /// <summary>
        /// 查询与条件匹配的第一项数据值
        /// </summary>
        /// <typeparam name="T">数据类型参数</typeparam>
        /// <param name="predicate">数据查询请求条件</param>
        /// <returns>如果查询结果为空则返回 null</returns>
        public static T FirstOrDefault<T>(LiteDatabaseKind liteDatabaseKind, Expression<Func<T, bool>> predicate)
        {
            return _databaseKind[liteDatabaseKind].GetCollection<T>().Find(predicate).FirstOrDefault();
        }
        #endregion
        /// <summary>
        /// 创建数据库索引
        /// </summary>
        private static void CreateIndexes()
        {
            try
            {
                // 游戏配置索引
                _configDatabase.GetCollection<GameConfig>().EnsureIndex("Key");
                _configDatabase.GetCollection<GameConfig>().EnsureIndex("Category");

                // 角色数据索引
                _gameDatabase.GetCollection<CharacterLocalData>().EnsureIndex("CharacterId");
                _gameDatabase.GetCollection<CharacterLocalData>().EnsureIndex("CharacterName");
                _gameDatabase.GetCollection<CharacterLocalData>().EnsureIndex("LastLoginTime");

                // 用户偏好索引
                _configDatabase.GetCollection<UserPreferences>().EnsureIndex("UserId");

                // 缓存数据索引
                _cacheDatabase.GetCollection<CacheData>().EnsureIndex("Key");
                _cacheDatabase.GetCollection<CacheData>().EnsureIndex("ExpiresAt");

                // 统计数据索引
                _gameDatabase.GetCollection<GameStatistics>().EnsureIndex("CharacterId");
                _gameDatabase.GetCollection<GameStatistics>().EnsureIndex("StatType");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LiteDataContext] 创建索引失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 设置数据库路径
        /// </summary>
        public static void SetDatabasePath(string path)
        {
            if (IsInitialized)
            {
                Debug.LogWarning("[LiteDataContext] 数据库已初始化，无法更改路径");
                return;
            }

            _dbPath = path;
        }

        /// <summary>
        /// 关闭数据库连接
        /// </summary>
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
                    Debug.Log("[LiteDataContext] 数据库连接已关闭");
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[LiteDataContext] 关闭数据库连接时发生错误: {ex.Message}");
                }
            }
        }
        #endregion

        #region 游戏配置管理
        /// <summary>
        /// 保存游戏配置
        /// </summary>
        public static bool SaveGameConfig(string key, string value, string category = "General")
        {
            if (!EnsureInitialized()) return false;

            try
            {
                var collection = _configDatabase.GetCollection<GameConfig>();
                var existing = collection.Find(x => x.Key == key && x.Category == category).FirstOrDefault();

                if (existing != null)
                {
                    existing.Value = value;
                    existing.UpdatedAt = DateTime.Now;
                    collection.Update(existing);
                }
                else
                {
                    var config = new GameConfig
                    {
                        Key = key,
                        Value = value,
                        Category = category
                    };
                    collection.Insert(config);
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LiteDataContext] 保存游戏配置失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 获取游戏配置
        /// </summary>
        public static string GetGameConfig(string key, string category = "General", string defaultValue = null)
        {
            if (!EnsureInitialized()) return defaultValue;

            try
            {
                var collection = _configDatabase.GetCollection<GameConfig>();
                var config = collection.Find(x => x.Key == key && x.Category == category).FirstOrDefault();
                return config?.Value ?? defaultValue;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LiteDataContext] 获取游戏配置失败: {ex.Message}");
                return defaultValue;
            }
        }

        /// <summary>
        /// 获取分类下的所有配置
        /// </summary>
        public static Dictionary<string, string> GetGameConfigsByCategory(string category)
        {
            if (!EnsureInitialized()) return new Dictionary<string, string>();

            try
            {
                var collection = _configDatabase.GetCollection<GameConfig>();
                var configs = collection.Find(x => x.Category == category);
                return configs.ToDictionary(c => c.Key, c => c.Value);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LiteDataContext] 获取分类配置失败: {ex.Message}");
                return new Dictionary<string, string>();
            }
        }
        #endregion

        #region 角色数据管理
        /// <summary>
        /// 保存角色本地数据
        /// </summary>
        public static bool SaveCharacterData(CharacterLocalData characterData)
        {
            if (!EnsureInitialized() || characterData == null) return false;

            try
            {
                var collection = _gameDatabase.GetCollection<CharacterLocalData>();
                var existing = collection.Find(x => x.CharacterId == characterData.CharacterId).FirstOrDefault();

                if (existing != null)
                {
                    characterData.Id = existing.Id;
                    characterData.LastSyncTime = existing.LastSyncTime;
                    collection.Update(characterData);
                }
                else
                {
                    collection.Insert(characterData);
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LiteDataContext] 保存角色数据失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 获取角色本地数据
        /// </summary>
        public static CharacterLocalData GetCharacterData(ulong characterId)
        {
            if (!EnsureInitialized() || characterId <= 0) return null;

            try
            {
                var collection = _gameDatabase.GetCollection<CharacterLocalData>();
                return collection.Find(x => x.CharacterId == characterId).FirstOrDefault();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LiteDataContext] 获取角色数据失败: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 获取所有角色数据
        /// </summary>
        public static List<CharacterLocalData> GetAllCharacterData()
        {
            if (!EnsureInitialized()) return new List<CharacterLocalData>();

            try
            {
                var collection = _gameDatabase.GetCollection<CharacterLocalData>();
                return collection.FindAll().OrderByDescending(x => x.LastLoginTime).ToList();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LiteDataContext] 获取所有角色数据失败: {ex.Message}");
                return new List<CharacterLocalData>();
            }
        }

        /// <summary>
        /// 删除角色数据
        /// </summary>
        public static bool DeleteCharacterData(ulong characterId)
        {
            if (!EnsureInitialized() || characterId <= 0) return false;

            try
            {
                var collection = _gameDatabase.GetCollection<CharacterLocalData>();
                return collection.DeleteMany(Query.EQ("CharacterId", characterId)) > 0;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LiteDataContext] 删除角色数据失败: {ex.Message}");
                return false;
            }
        }
        #endregion

        #region 用户偏好管理
        /// <summary>
        /// 保存用户偏好设置
        /// </summary>
        public static bool SaveUserPreferences(UserPreferences preferences)
        {
            if (!EnsureInitialized() || preferences == null) return false;

            try
            {
                var collection = _configDatabase.GetCollection<UserPreferences>();
                var existing = collection.Find(x => x.UserId == preferences.UserId).FirstOrDefault();

                if (existing != null)
                {
                    preferences.Id = existing.Id;
                    preferences.CreatedAt = existing.CreatedAt;
                    preferences.UpdatedAt = DateTime.Now;
                    collection.Update(preferences);
                }
                else
                {
                    collection.Insert(preferences);
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LiteDataContext] 保存用户偏好失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 获取用户偏好设置
        /// </summary>
        public static UserPreferences GetUserPreferences(string userId)
        {
            if (!EnsureInitialized() || string.IsNullOrEmpty(userId)) return null;

            try
            {
                var collection = _configDatabase.GetCollection<UserPreferences>();
                return collection.Find(x => x.UserId == userId).FirstOrDefault();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LiteDataContext] 获取用户偏好失败: {ex.Message}");
                return null;
            }
        }
        #endregion

        #region 缓存管理
        /// <summary>
        /// 设置缓存数据
        /// </summary>
        public static bool SetCache(string key, string data, TimeSpan? expiration = null)
        {
            if (!EnsureInitialized() || string.IsNullOrEmpty(key)) return false;

            try
            {
                var collection = _cacheDatabase.GetCollection<CacheData>();
                var existing = collection.Find(x => x.Key == key).FirstOrDefault();
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
                Debug.LogError($"[LiteDataContext] 设置缓存失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 获取缓存数据
        /// </summary>
        public static string GetCache(string key)
        {
            if (!EnsureInitialized() || string.IsNullOrEmpty(key)) return null;

            try
            {
                var collection = _cacheDatabase.GetCollection<CacheData>();
                var cache = collection.Find(x => x.Key == key).FirstOrDefault();

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
                Debug.LogError($"[LiteDataContext] 获取缓存失败: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// 删除缓存
        /// </summary>
        public static bool RemoveCache(string key)
        {
            if (!EnsureInitialized() || string.IsNullOrEmpty(key)) return false;

            try
            {
                var collection = _cacheDatabase.GetCollection<CacheData>();
                return collection.DeleteMany(Query.EQ("Key", key)) > 0;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LiteDataContext] 删除缓存失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 清理过期缓存
        /// </summary>
        public static void CleanExpiredCache()
        {
            if (!EnsureInitialized()) return;

            try
            {
                var collection = _cacheDatabase.GetCollection<CacheData>();
                var deletedCount = collection.DeleteMany(Query.LT("ExpiresAt", DateTime.Now));
                if (deletedCount > 0)
                {
                    Debug.Log($"[LiteDataContext] 清理了 {deletedCount} 个过期缓存");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LiteDataContext] 清理过期缓存失败: {ex.Message}");
            }
        }
        #endregion

        #region 统计数据管理
        /// <summary>
        /// 记录游戏统计数据
        /// </summary>
        public static bool RecordStatistic(ulong characterId, string statType, long value)
        {
            if (!EnsureInitialized() || characterId <= 0 || string.IsNullOrEmpty(statType)) return false;

            try
            {
                var collection = _gameDatabase.GetCollection<GameStatistics>();
                var stat = new GameStatistics
                {
                    CharacterId = characterId,
                    StatType = statType,
                    Value = value
                };
                collection.Insert(stat);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LiteDataContext] 记录统计数据失败: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 获取统计数据
        /// </summary>
        public static List<GameStatistics> GetStatistics(ulong characterId, string statType = null, DateTime? fromDate = null)
        {
            if (!EnsureInitialized()) return new List<GameStatistics>();

            try
            {
                var collection = _gameDatabase.GetCollection<GameStatistics>();
                var query = collection.Query().Where(x => x.CharacterId == characterId);

                if (!string.IsNullOrEmpty(statType))
                {
                    query = query.Where(x => x.StatType == statType);
                }

                if (fromDate.HasValue)
                {
                    query = query.Where(x => x.RecordedAt >= fromDate.Value);
                }

                return query.OrderByDescending(x => x.RecordedAt).ToList();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LiteDataContext] 获取统计数据失败: {ex.Message}");
                return new List<GameStatistics>();
            }
        }
        #endregion

        #region 数据库维护
        /// <summary>
        /// 压缩数据库
        /// </summary>
        public static void CompactDatabase()
        {
            if (!EnsureInitialized()) return;

            try
            {
                _gameDatabase.Rebuild();
                _configDatabase.Rebuild();
                _cacheDatabase.Rebuild();
                Debug.Log("[LiteDataContext] 数据库压缩完成");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LiteDataContext] 数据库压缩失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 获取数据库信息
        /// </summary>
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

                // 统计记录数量
                info["CharacterCount"] = _gameDatabase.GetCollection<CharacterLocalData>().Count();
                info["ConfigCount"] = _configDatabase.GetCollection<GameConfig>().Count();
                info["CacheCount"] = _cacheDatabase.GetCollection<CacheData>().Count();
                info["StatisticsCount"] = _gameDatabase.GetCollection<GameStatistics>().Count();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LiteDataContext] 获取数据库信息失败: {ex.Message}");
                info["Error"] = ex.Message;
            }

            return info;
        }
        #endregion

        #region 私有辅助方法
        /// <summary>
        /// 确保数据库已初始化
        /// </summary>
        private static bool EnsureInitialized()
        {
            if (!IsInitialized)
            {
                Debug.LogWarning("[LiteDataContext] 数据库未初始化，正在尝试初始化...");
                return Initialize();
            }
            return true;
        }
        #endregion
    }
}
