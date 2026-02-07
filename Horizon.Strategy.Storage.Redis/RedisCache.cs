using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using Horizon.Core.Abstract;
using StackExchange.Redis;
using System.Linq;

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
            return await database.StringSetAsync(key, RedisValue.Unbox(value), expiry);
        }

        public async Task<bool> SetAllAsync(IDictionary<string, object> keyValues, TimeSpan? expiry = null)
        {
            var database = await _redisConnection.GetDatabaseAsync(_defaultDb);
            var batch = database.CreateBatch();

            foreach (var kv in keyValues)
            {
                batch.StringSetAsync(kv.Key, RedisValue.Unbox(kv.Value), expiry);
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
                return value.HasValue ? (T)value.Box() : default;
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
                    result.Add(keys.ElementAt(i), (T)values[i].Box());
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
            return await database.StringSetAsync(key, RedisValue.Unbox(data));
        }

        public async Task<bool> InsertAsync(string key, object data, int cacheTime)
        {
            var database = await _redisConnection.GetDatabaseAsync(_defaultDb);
            return await database.StringSetAsync(key, RedisValue.Unbox(data), TimeSpan.FromSeconds(cacheTime));
        }

        public async Task<bool> InsertAsync<T>(string key, T data, int cacheTime)
        {
            var database = await _redisConnection.GetDatabaseAsync(_defaultDb);
            return await database.StringSetAsync(key, RedisValue.Unbox(data), TimeSpan.FromSeconds(cacheTime));
        }

        public async Task<bool> InsertAsync(string key, object data, DateTime cacheTime)
        {
            var database = await _redisConnection.GetDatabaseAsync(_defaultDb);
            return await database.StringSetAsync(key, RedisValue.Unbox(data), cacheTime - DateTime.Now);
        }

        public async Task<bool> InsertAsync<T>(string key, T data, DateTime cacheTime)
        {
            var database = await _redisConnection.GetDatabaseAsync(_defaultDb);
            return await database.StringSetAsync(key, RedisValue.Unbox(data), cacheTime - DateTime.Now);
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
            await database.ListRightPushAsync(key, RedisValue.Unbox(value));
        }

        public async Task<T> DequeueItemFromListAsync<T>(string key)
        {
            var database = await _redisConnection.GetDatabaseAsync(_defaultDb);
            var value = await database.ListLeftPopAsync(key);
            return value.HasValue ? (T)value.Box() : default;
        }

        public async Task<List<T>> GetAllItemsFromListAsync<T>(string key)
        {
            var database = await _redisConnection.GetDatabaseAsync(_defaultDb);
            var values = await database.ListRangeAsync(key);
            return values.Select(v => (T)v.Box()).ToList();
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
            await database.ListSetByIndexAsync(listId, listIndex, RedisValue.Unbox(value));
        }

        public async Task<T> GetItemFromListAsync<T>(string listId, int listIndex)
        {
            var database = await _redisConnection.GetDatabaseAsync(_defaultDb);
            var value = await database.ListGetByIndexAsync(listId, listIndex);
            return value.HasValue ? (T)value.Box() : default;
        }

        public async Task AddItemToListAsync<T>(string listId, T value)
        {
            var database = await _redisConnection.GetDatabaseAsync(_defaultDb);
            await database.ListRightPushAsync(listId, RedisValue.Unbox(value));
        }

        public async Task RemoveItemFromListAsync<T>(string listId, T value)
        {
            var database = await _redisConnection.GetDatabaseAsync(_defaultDb);
            await database.ListRemoveAsync(listId, RedisValue.Unbox(value));
        }

        public async Task AddItemToSetAsync<T>(string setId, T item)
        {
            var database = await _redisConnection.GetDatabaseAsync(_defaultDb);
            await database.SetAddAsync(setId, RedisValue.Unbox(item));
        }

        public async Task AddRangeToListAsync<T>(string listId, List<T> values)
        {
            var database = await _redisConnection.GetDatabaseAsync(_defaultDb);
            await database.ListRightPushAsync(listId, values.Select(v => RedisValue.Unbox(v)).ToArray());
        }

        public async Task AddRangeToSetAsync<T>(string listId, List<T> items)
        {
            var database = await _redisConnection.GetDatabaseAsync(_defaultDb);
            await database.SetAddAsync(listId, items.Select(i => RedisValue.Unbox(i)).ToArray());
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
            return new HashSet<T>(values.Select(v => (T)v.Box()));
        }

        public async Task RemoveItemFromSetAsync(string setId, string item)
        {
            var database = await _redisConnection.GetDatabaseAsync(_defaultDb);
            await database.SetRemoveAsync(setId, item);
        }

        public async Task RemoveItemFromSetAsync<T>(string setId, T item)
        {
            var database = await _redisConnection.GetDatabaseAsync(_defaultDb);
            await database.SetRemoveAsync(setId, RedisValue.Unbox(item));
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

            foreach (var kv in keyValuePairs)
            {
                batch.StringSetAsync(kv.Key, RedisValue.Unbox(kv.Value), expiration);
            }

            batch.Execute();
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
