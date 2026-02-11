using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using Horizon.Core.Abstract;
using StackExchange.Redis;
using System.Linq;
using Newtonsoft.Json;

namespace Horizon.Strategy.Storage.Redis
{
    public class RedisCache : ICache
    {
        private readonly RedisConnection _redisConnection;
        private readonly int _defaultDb;

        public bool IsClusterOpen { get; set; }
        public int TimeOut { get; set; }

        public RedisCache(string connectionString, int db = -1)
        {
            try
            {
                _redisConnection = new RedisConnection(connectionString);
                _defaultDb = db;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to initialize Redis connection with connection string: {connectionString}", ex);
            }
        }

        private RedisValue SerializeValue<T>(T value)
        {
            if (value == null)
                return RedisValue.Null;

            var type = typeof(T);
            
            // 基础类型直接使用 RedisValue.Unbox
            if (type.IsPrimitive || type == typeof(string) || type == typeof(byte[]))
            {
                return RedisValue.Unbox(value);
            }

            // 复杂对象使用 JSON 序列化
            return JsonConvert.SerializeObject(value);
        }

        private T DeserializeValue<T>(RedisValue value)
        {
            if (!value.HasValue)
                return default;

            var type = typeof(T);

            // 基础类型直接使用 Box
            if (type.IsPrimitive || type == typeof(string) || type == typeof(byte[]))
            {
                return (T)value.Box();
            }

            // 复杂对象使用 JSON 反序列化
            return JsonConvert.DeserializeObject<T>(value.ToString());
        }

        public async Task<IDisposable> AcquireLockAsync(string key)
        {
            return await AcquireLockAsync(key, TimeSpan.FromSeconds(30));
        }

        public async Task<IDisposable> AcquireLockAsync(string key, TimeSpan timeOut)
        {
            var database = await _redisConnection.GetDatabaseAsync(_defaultDb);
            var lockKey = $"lock:{key}";
            var token = Guid.NewGuid().ToString();

            if (await database.LockTakeAsync(lockKey, token, timeOut))
            {
                return new RedisLock(database, lockKey, token);
            }

            throw new TimeoutException($"Failed to acquire lock for key: {key}");
        }

        public async Task<bool> ExtendLockAsync(IDisposable lockObj, TimeSpan extensionTime)
        {
            if (lockObj is RedisLock redisLock)
            {
                return await redisLock.ExtendAsync(extensionTime);
            }
            return false;
        }

        public async Task<bool> KeyExpireAsync(string key, TimeSpan expiry)
        {
            var database = await _redisConnection.GetDatabaseAsync(_defaultDb);
            return await database.KeyExpireAsync(key, expiry);
        }

        public async Task<bool> KeyPersistAsync(string key)
        {
            var database = await _redisConnection.GetDatabaseAsync(_defaultDb);
            return await database.KeyPersistAsync(key);
        }

        public async Task<TimeSpan?> KeyTimeToLiveAsync(string key)
        {
            var database = await _redisConnection.GetDatabaseAsync(_defaultDb);
            return await database.KeyTimeToLiveAsync(key);
        }

        public async Task<long> IncrementValueAsync(string key)
        {
            var database = await _redisConnection.GetDatabaseAsync(_defaultDb);
            return await database.StringIncrementAsync(key);
        }

        public async Task<bool> SetAsync(string key, object value, TimeSpan? expiry = null)
        {
            var database = await _redisConnection.GetDatabaseAsync(_defaultDb);
            var expiration = expiry.HasValue ? (Expiration?)expiry.Value : null;
            return await database.StringSetAsync(key, RedisValue.Unbox(value), expiration.Value);
        }

        public async Task<bool> SetAllAsync(IDictionary<string, object> keyValues, TimeSpan? expiry = null)
        {
            var database = await _redisConnection.GetDatabaseAsync(_defaultDb);
            var batch = database.CreateBatch();
            var expiration = expiry.HasValue ? (Expiration?)expiry.Value : null;

            foreach (var kv in keyValues)
            {
                batch.StringSetAsync(kv.Key, RedisValue.Unbox(kv.Value), expiration.Value);
            }

            batch.Execute();
            return await Task.FromResult(true);
        }

        public async Task<T> GetAsync<T>(string key)
        {
            try
            {
                var database = await _redisConnection.GetDatabaseAsync(_defaultDb);
                var value = await database.StringGetAsync(key);
                return DeserializeValue<T>(value);
            }
            catch(Exception ex)
            {
                return default;
            }
            finally
            {

            }
        }

        public async Task<Dictionary<string, T>> GetAllAsync<T>(IEnumerable<string> keys)
        {
            var database = await _redisConnection.GetDatabaseAsync(_defaultDb);
            var values = await database.StringGetAsync(keys.Select(k => (RedisKey)k).ToArray());

            var result = new Dictionary<string, T>();
            for (int i = 0; i < keys.Count(); i++)
            {
                if (values[i].HasValue)
                {
                    result.Add(keys.ElementAt(i), DeserializeValue<T>(values[i]));
                }
            }
            return result;
        }

        public async Task<bool> RemoveAsync(string key)
        {
            var database = await _redisConnection.GetDatabaseAsync(_defaultDb);
            return await database.KeyDeleteAsync(key);
        }

        public async Task<long> RemoveAllAsync(IEnumerable<string> keys)
        {
            var database = await _redisConnection.GetDatabaseAsync(_defaultDb);
            return await database.KeyDeleteAsync(keys.Select(k => (RedisKey)k).ToArray());
        }

        public async Task ClearAsync()
        {
            var database = await _redisConnection.GetDatabaseAsync(_defaultDb);
            var endpoints = _redisConnection.GetEndPoints();

            foreach (var endpoint in endpoints)
            {
                var server = _redisConnection.GetServer(endpoint);
                await server.FlushDatabaseAsync(_defaultDb);
            }
        }

        public async Task<bool> ExistsAsync(string key)
        {
            var database = await _redisConnection.GetDatabaseAsync(_defaultDb);
            return await database.KeyExistsAsync(key);
        }

        public async Task<bool> BooleanValueAsync(string key, bool value)
        {
            var database = await _redisConnection.GetDatabaseAsync(_defaultDb);
            return await database.StringSetAsync(key, value);
        }

        public async Task<string> GetAsync(string key)
        {
            var database = await _redisConnection.GetDatabaseAsync(_defaultDb);
            var value = await database.StringGetAsync(key);
            return value.HasValue ? value.ToString() : null;
        }

        public async Task<TimeSpan?> GetTimeToLiveAsync(string key)
        {
            var database = await _redisConnection.GetDatabaseAsync(_defaultDb);
            return await database.KeyTimeToLiveAsync(key);
        }

        public async Task<bool> InsertAsync(string key, object data)
        {
            var database = await _redisConnection.GetDatabaseAsync(_defaultDb);
            return await database.StringSetAsync(key, RedisValue.Unbox(data));
        }

        public async Task<bool> InsertAsync<T>(string key, T data)
        {
            var database = await _redisConnection.GetDatabaseAsync(_defaultDb);
            return await database.StringSetAsync(key, SerializeValue(data));
        }

        public async Task<bool> InsertAsync(string key, object data, int cacheTime)
        {
            var database = await _redisConnection.GetDatabaseAsync(_defaultDb);
            return await database.StringSetAsync(key, RedisValue.Unbox(data), TimeSpan.FromSeconds(cacheTime));
        }

        public async Task<bool> InsertAsync<T>(string key, T data, int cacheTime)
        {
            var database = await _redisConnection.GetDatabaseAsync(_defaultDb);
            return await database.StringSetAsync(key, SerializeValue(data), TimeSpan.FromSeconds(cacheTime));
        }

        public async Task<bool> InsertAsync(string key, object data, DateTime cacheTime)
        {
            var database = await _redisConnection.GetDatabaseAsync(_defaultDb);
            return await database.StringSetAsync(key, RedisValue.Unbox(data), cacheTime - DateTime.Now);
        }

        public async Task<bool> InsertAsync<T>(string key, T data, DateTime cacheTime)
        {
            var database = await _redisConnection.GetDatabaseAsync(_defaultDb);
            return await database.StringSetAsync(key, SerializeValue(data), cacheTime - DateTime.Now);
        }

        public async Task RegisterSubscribeAsync<T>(string key, RegisterSubscribeEvent dosub)
        {
            var database = await _redisConnection.GetDatabaseAsync(_defaultDb);
            var subscriber = database.Multiplexer.GetSubscriber();
            await subscriber.SubscribeAsync(key, (channel, value) =>
            {
                dosub?.Invoke((T)value.Box());
            });
        }

        Task ICache.RemoveAsync(string key)
        {
            return RemoveAsync(key);
        }

        public async Task SendAsync(string key, object data)
        {
            var database = await _redisConnection.GetDatabaseAsync(_defaultDb);
            var subscriber = database.Multiplexer.GetSubscriber();
            await subscriber.PublishAsync(key, RedisValue.Unbox(data));
        }

        public async Task UnRegisterSubscribAsync(string key)
        {
            var database = await _redisConnection.GetDatabaseAsync(_defaultDb);
            var subscriber = database.Multiplexer.GetSubscriber();
            await subscriber.UnsubscribeAsync(key);
        }

        public async Task EnqueueItemOnListAsync<T>(string key, T value)
        {
            var database = await _redisConnection.GetDatabaseAsync(_defaultDb);
            await database.ListRightPushAsync(key, SerializeValue(value));
        }

        public async Task<T> DequeueItemFromListAsync<T>(string key)
        {
            var database = await _redisConnection.GetDatabaseAsync(_defaultDb);
            var value = await database.ListLeftPopAsync(key);
            return DeserializeValue<T>(value);
        }

        public async Task<List<T>> GetAllItemsFromListAsync<T>(string key)
        {
            var database = await _redisConnection.GetDatabaseAsync(_defaultDb);
            var values = await database.ListRangeAsync(key);
            return values.Select(v => DeserializeValue<T>(v)).ToList();
        }

        public async Task EnqueueItemOnListAsync(string key, string value)
        {
            var database = await _redisConnection.GetDatabaseAsync(_defaultDb);
            await database.ListRightPushAsync(key, value);
        }

        public async Task<string> DequeueItemFromListAsync(string key)
        {
            var database = await _redisConnection.GetDatabaseAsync(_defaultDb);
            var value = await database.ListLeftPopAsync(key);
            return value.ToString();
        }

        public async Task<List<string>> GetAllItemsFromListAsync(string key)
        {
            var database = await _redisConnection.GetDatabaseAsync(_defaultDb);
            var values = await database.ListRangeAsync(key);
            return values.Select(v => v.ToString()).ToList();
        }

        public async Task<List<string>> GetAllKeysAsync()
        {
            var database = await _redisConnection.GetDatabaseAsync(_defaultDb);
            var endpoints = _redisConnection.GetEndPoints();
            var keys = new List<string>();

            foreach (var endpoint in endpoints)
            {
                var server = _redisConnection.GetServer(endpoint);
                foreach (var key in server.Keys(_defaultDb))
                {
                    keys.Add(key.ToString());
                }
            }
            return keys;
        }

        public async Task SetItemInListAsync(string listId, int listIndex, string value)
        {
            var database = await _redisConnection.GetDatabaseAsync(_defaultDb);
            await database.ListSetByIndexAsync(listId, listIndex, value);
        }

        public async Task<string> GetItemFromListAsync(string listId, int listIndex)
        {
            var database = await _redisConnection.GetDatabaseAsync(_defaultDb);
            var value = await database.ListGetByIndexAsync(listId, listIndex);
            return value.ToString();
        }

        public async Task AddItemToListAsync(string listId, string value)
        {
            var database = await _redisConnection.GetDatabaseAsync(_defaultDb);
            await database.ListRightPushAsync(listId, value);
        }

        public async Task AddItemToSetAsync(string setId, string item)
        {
            var database = await _redisConnection.GetDatabaseAsync(_defaultDb);
            await database.SetAddAsync(setId, item);
        }

        public async Task AddRangeToListAsync(string listId, List<string> values)
        {
            var database = await _redisConnection.GetDatabaseAsync(_defaultDb);
            await database.ListRightPushAsync(listId, values.Select(v => (RedisValue)v).ToArray());
        }

        public async Task AddRangeToSetAsync(string listId, List<string> items)
        {
            var database = await _redisConnection.GetDatabaseAsync(_defaultDb);
            await database.SetAddAsync(listId, items.Select(i => (RedisValue)i).ToArray());
        }

        public async Task SetItemInListAsync<T>(string listId, int listIndex, T value)
        {
            var database = await _redisConnection.GetDatabaseAsync(_defaultDb);
            await database.ListSetByIndexAsync(listId, listIndex, SerializeValue(value));
        }

        public async Task<T> GetItemFromListAsync<T>(string listId, int listIndex)
        {
            var database = await _redisConnection.GetDatabaseAsync(_defaultDb);
            var value = await database.ListGetByIndexAsync(listId, listIndex);
            return DeserializeValue<T>(value);
        }

        public async Task AddItemToListAsync<T>(string listId, T value)
        {
            var database = await _redisConnection.GetDatabaseAsync(_defaultDb);
            await database.ListRightPushAsync(listId, SerializeValue(value));
        }

        public async Task RemoveItemFromListAsync<T>(string listId, T value)
        {
            var database = await _redisConnection.GetDatabaseAsync(_defaultDb);
            await database.ListRemoveAsync(listId, SerializeValue(value));
        }

        public async Task AddItemToSetAsync<T>(string setId, T item)
        {
            var database = await _redisConnection.GetDatabaseAsync(_defaultDb);
            await database.SetAddAsync(setId, SerializeValue(item));
        }

        public async Task AddRangeToListAsync<T>(string listId, List<T> values)
        {
            var database = await _redisConnection.GetDatabaseAsync(_defaultDb);
            await database.ListRightPushAsync(listId, values.Select(v => SerializeValue(v)).ToArray());
        }

        public async Task AddRangeToSetAsync<T>(string listId, List<T> items)
        {
            var database = await _redisConnection.GetDatabaseAsync(_defaultDb);
            await database.SetAddAsync(listId, items.Select(i => SerializeValue(i)).ToArray());
        }

        public async Task<HashSet<string>> GetAllItemsFromSetAsync(string setId)
        {
            var database = await _redisConnection.GetDatabaseAsync(_defaultDb);
            var values = await database.SetMembersAsync(setId);
            return new HashSet<string>(values.Select(v => v.ToString()));
        }

        public async Task<HashSet<T>> GetAllItemsFromSetAsync<T>(string setId)
        {
            var database = await _redisConnection.GetDatabaseAsync(_defaultDb);
            var values = await database.SetMembersAsync(setId);
            return new HashSet<T>(values.Select(v => DeserializeValue<T>(v)));
        }

        public async Task RemoveItemFromSetAsync(string setId, string item)
        {
            var database = await _redisConnection.GetDatabaseAsync(_defaultDb);
            await database.SetRemoveAsync(setId, item);
        }

        public async Task RemoveItemFromSetAsync<T>(string setId, T item)
        {
            var database = await _redisConnection.GetDatabaseAsync(_defaultDb);
            await database.SetRemoveAsync(setId, SerializeValue(item));
        }

        public async Task<bool> ExpireEntryInAsync(string key, TimeSpan expireIn)
        {
            var database = await _redisConnection.GetDatabaseAsync(_defaultDb);
            return await database.KeyExpireAsync(key, expireIn);
        }

        public async Task<bool> ExpireEntryAtAsync(string key, DateTime expireAt)
        {
            var database = await _redisConnection.GetDatabaseAsync(_defaultDb);
            return await database.KeyExpireAsync(key, expireAt);
        }

        public async Task<double> GetCacheHitRate()
        {
            //var database = await _redisConnection.GetDatabaseAsync(_defaultDb);
            //var stats =await  database.Multiplexer.GetServer(_redisConnection.GetEndPoints().First()).();
            //return (double)stats.Hits / (stats.Hits + stats.Misses);
            return 1;
        }



        public async Task<Dictionary<string, string>> GetMultipleAsync(IEnumerable<string> keys)
        {
            var database = await _redisConnection.GetDatabaseAsync(_defaultDb);
            var values = await database.StringGetAsync(keys.Select(k => (RedisKey)k).ToArray());

            var result = new Dictionary<string, string>();
            for (int i = 0; i < keys.Count(); i++)
            {
                if (values[i].HasValue)
                {
                    result.Add(keys.ElementAt(i), values[i].ToString());
                }
            }
            return result;
        }

        public async Task SetMultipleAsync(Dictionary<string, object> keyValuePairs, TimeSpan? expiration = null)
        {
            var database = await _redisConnection.GetDatabaseAsync(_defaultDb);
            var batch = database.CreateBatch();
            var exp = expiration.HasValue ? (Expiration?)expiration.Value : null;

            foreach (var kv in keyValuePairs)
            {
                await batch.StringSetAsync(kv.Key, RedisValue.Unbox(kv.Value), exp.Value);
            }

            batch.Execute();
        }

        /// <summary>
        /// 缓存读写模式（Cache-Aside Pattern）
        /// 先从缓存读取，如果缓存未命中则从数据源获取并写入缓存
        /// 支持空值缓存以防止缓存穿透
        /// </summary>
        public async Task<T> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiration = null, bool cacheNullValue = true, TimeSpan? nullValueExpiration = null)
        {
            var database = await _redisConnection.GetDatabaseAsync(_defaultDb);

            // 尝试从缓存获取
            var cachedValue = await database.StringGetAsync(key);
            if (cachedValue.HasValue)
            {
                var strValue = cachedValue.ToString();
                // 检查是否为空值标记
                if (strValue == "__NULL__")
                {
                    return default;
                }
                return JsonConvert.DeserializeObject<T>(strValue);
            }

            // 缓存未命中，从数据源获取
            var result = await factory();

            // 设置缓存
            if (result != null)
            {
                var serialized = JsonConvert.SerializeObject(result);
                var exp = expiration ?? TimeSpan.FromMinutes(TimeOut > 0 ? TimeOut : 30);
                await database.StringSetAsync(key, serialized, exp);
            }
            else if (cacheNullValue)
            {
                // 缓存空值，使用较短的过期时间防止缓存穿透
                var nullExp = nullValueExpiration ?? TimeSpan.FromMinutes(5);
                await database.StringSetAsync(key, "__NULL__", nullExp);
            }

            return result;
        }



        private class RedisLock : IDisposable
        {
            private readonly IDatabase _database;
            private readonly string _key;
            private readonly string _token;
            private bool _disposed;

            public RedisLock(IDatabase database, string key, string token)
            {
                _database = database;
                _key = key;
                _token = token;
            }

            public async Task<bool> ExtendAsync(TimeSpan extensionTime)
            {
                return await _database.LockExtendAsync(_key, _token, extensionTime);
            }

            public void Dispose()
            {
                if (_disposed) return;
#pragma warning disable CS4014 // Fire-and-forget
                _database.LockRelease(_key, _token);
#pragma warning restore CS4014
                _disposed = true;
            }
        }
    }
}
