using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Horizon.Core.Abstract;

namespace Horizon.Core
{
    /// <summary>
    /// Cache 类，提供静态方法封装缓存操作，包括获取、设置、删除缓存，以及队列和集合操作。
    /// </summary>
    public static class Cache
    {
        private static ICache _cache;
        private static object cacheLocker = new object();

        /// <summary>
        /// 获取或设置当前缓存实例。
        /// </summary>
        public static ICache Current
        {
            get { return _cache; }
            set { _cache = value; }
        }

        /// <summary>
        /// 获取或设置缓存的默认超时时间（以秒为单位）。
        /// </summary>
        public static int TimeOut
        {
            get { return _cache.TimeOut; }
            set { lock (cacheLocker) { _cache.TimeOut = value; } }
        }

        /// <summary>
        /// 获取或设置是否启用集群模式。
        /// </summary>
        public static bool IsClusterOpen
        {
            get { return _cache.IsClusterOpen; }
            set { lock (cacheLocker) { _cache.IsClusterOpen = value; } }
        }

        /// <summary>
        /// 异步获取分布式锁。
        /// </summary>
        /// <param name="key">锁的键</param>
        /// <returns>分布式锁对象</returns>
        public static async Task<IDisposable> AcquireLockAsync(string key)
        {
            return await _cache.AcquireLockAsync(key);
        }

        /// <summary>
        /// 异步获取分布式锁，并指定超时时间。
        /// </summary>
        /// <param name="key">锁的键</param>
        /// <param name="timeOut">超时时间</param>
        /// <returns>分布式锁对象</returns>
        public static async Task<IDisposable> AcquireLockAsync(string key, TimeSpan timeOut)
        {
            return await _cache.AcquireLockAsync(key, timeOut);
        }

        /// <summary>
        /// 异步获取缓存值。
        /// </summary>
        /// <param name="key">缓存键</param>
        /// <returns>缓存值</returns>
        public static async Task<string> GetAsync(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return null;
            }
            return await _cache.GetAsync(key);
        }

        /// <summary>
        /// 异步获取缓存值并反序列化为指定类型。
        /// </summary>
        /// <typeparam name="T">目标类型</typeparam>
        /// <param name="key">缓存键</param>
        /// <returns>反序列化后的对象</returns>
        public static async Task<T> GetAsync<T>(string key)
        {
            return await _cache.GetAsync<T>(key);
        }

        /// <summary>
        /// 异步设置缓存值。
        /// </summary>
        /// <param name="key">缓存键</param>
        /// <param name="data">缓存值</param>
        public static async Task InsertAsync(string key, object data)
        {
            if (!string.IsNullOrWhiteSpace(key) && data != null)
            {
                await _cache.InsertAsync(key, data);
            }
        }

        /// <summary>
        /// 异步设置缓存值，并指定过期时间。
        /// </summary>
        /// <param name="key">缓存键</param>
        /// <param name="data">缓存值</param>
        /// <param name="cacheTime">过期时间（分钟）</param>
        public static async Task<bool> InsertAsync(string key, object data, int cacheTime)
        {
            if (!string.IsNullOrWhiteSpace(key) && data != null)
            {
                return await _cache.InsertAsync(key, data, cacheTime);
            }
            return false;
        }

        /// <summary>
        /// 异步删除指定键的缓存值。
        /// </summary>
        /// <param name="key">缓存键</param>
        public static async Task RemoveAsync(string key)
        {
            if (!string.IsNullOrWhiteSpace(key))
            {
                lock (cacheLocker)
                {
                    _cache.RemoveAsync(key);
                }
            }
            await Task.CompletedTask;
        }

        /// <summary>
        /// 异步检查缓存键是否存在。
        /// </summary>
        /// <param name="key">缓存键</param>
        /// <returns>是否存在</returns>
        public static async Task<bool> ExistsAsync(string key)
        {
            return await _cache.ExistsAsync(key);
        }

        /// <summary>
        /// 异步获取缓存的过期时间。
        /// </summary>
        /// <param name="key">缓存键</param>
        /// <returns>缓存的过期时间</returns>
        public static async Task<TimeSpan?> GetTimeToLiveAsync(string key)
        {
            return await _cache.GetTimeToLiveAsync(key);
        }

        /// <summary>
        /// 获取当前缓存实例。
        /// </summary>
        /// <returns>当前缓存实例</returns>
        public static ICache GetCache()
        {
            return _cache;
        }

        /// <summary>
        /// 异步设置缓存值。
        /// </summary>
        /// <typeparam name="T">目标类型</typeparam>
        /// <param name="key">缓存键</param>
        /// <param name="data">缓存值</param>
        /// <returns>是否成功</returns>
        public static async Task<bool> InsertAsync<T>(string key, T data)
        {
            if (!string.IsNullOrWhiteSpace(key) && data != null)
            {
                return await _cache.InsertAsync(key, data);
            }
            return false;
        }

        /// <summary>
        /// 异步设置缓存值，并指定过期时间。
        /// </summary>
        /// <typeparam name="T">目标类型</typeparam>
        /// <param name="key">缓存键</param>
        /// <param name="data">缓存值</param>
        /// <param name="cacheTime">过期时间（分钟）</param>
        /// <returns>是否成功</returns>
        public static async Task<bool> InsertAsync<T>(string key, T data, int cacheTime)
        {
            if (!string.IsNullOrWhiteSpace(key) && data != null)
            {
                return await _cache.InsertAsync(key, data, cacheTime);
            }
            return false;
        }

        /// <summary>
        /// 异步设置缓存值，并指定过期时间。
        /// </summary>
        /// <param name="key">缓存键</param>
        /// <param name="data">缓存值</param>
        /// <param name="cacheTime">过期时间</param>
        /// <returns>是否成功</returns>
        public static async Task<bool> InsertAsync(string key, object data, DateTime cacheTime)
        {
            if (!string.IsNullOrWhiteSpace(key) && data != null)
            {
                return await _cache.InsertAsync(key, data, cacheTime);
            }
            return false;
        }

        /// <summary>
        /// 异步设置缓存值，并指定过期时间。
        /// </summary>
        /// <typeparam name="T">目标类型</typeparam>
        /// <param name="key">缓存键</param>
        /// <param name="data">缓存值</param>
        /// <param name="cacheTime">过期时间</param>
        /// <returns>是否成功</returns>
        public static async Task<bool> InsertAsync<T>(string key, T data, DateTime cacheTime)
        {
            if (!string.IsNullOrWhiteSpace(key) && data != null)
            {
                return await _cache.InsertAsync(key, data, cacheTime);
            }
            return false;
        }

        /// <summary>
        /// 异步注册订阅方法。
        /// </summary>
        /// <typeparam name="T">目标类型</typeparam>
        /// <param name="key">缓存键</param>
        /// <param name="dosub">订阅方法</param>
        public static async Task RegisterSubscribeAsync<T>(string key, RegisterSubscribeEvent dosub)
        {
            await _cache.RegisterSubscribeAsync<T>(key, dosub);
        }

        /// <summary>
        /// 异步发送缓存消息。
        /// </summary>
        /// <param name="key">缓存键</param>
        /// <param name="data">消息数据</param>
        public static async Task SendAsync(string key, object data)
        {
            await _cache.SendAsync(key, data);
        }

        /// <summary>
        /// 异步注销订阅方法。
        /// </summary>
        /// <param name="key">缓存键</param>
        public static async Task UnRegisterSubscribAsync(string key)
        {
            await _cache.UnRegisterSubscribAsync(key);
        }

        /// <summary>
        /// 异步将元素入队到列表底部。
        /// </summary>
        /// <typeparam name="T">目标类型</typeparam>
        /// <param name="key">缓存键</param>
        /// <param name="value">元素值</param>
        public static async Task EnqueueItemOnListAsync<T>(string key, T value)
        {
            await _cache.EnqueueItemOnListAsync(key, value);
        }

        /// <summary>
        /// 异步从列表顶部出队元素。
        /// </summary>
        /// <typeparam name="T">目标类型</typeparam>
        /// <param name="key">缓存键</param>
        /// <returns>出队元素</returns>
        public static async Task<T> DequeueItemFromListAsync<T>(string key)
        {
            return await _cache.DequeueItemFromListAsync<T>(key);
        }

        /// <summary>
        /// 异步获取列表中的所有元素。
        /// </summary>
        /// <typeparam name="T">目标类型</typeparam>
        /// <param name="key">缓存键</param>
        /// <returns>元素列表</returns>
        public static async Task<List<T>> GetAllItemsFromListAsync<T>(string key)
        {
            return await _cache.GetAllItemsFromListAsync<T>(key);
        }

        /// <summary>
        /// 异步将元素入队到列表底部。
        /// </summary>
        /// <param name="key">缓存键</param>
        /// <param name="value">元素值</param>
        public static async Task EnqueueItemOnListAsync(string key, string value)
        {
            await _cache.EnqueueItemOnListAsync(key, value);
        }

        /// <summary>
        /// 异步从列表顶部出队元素。
        /// </summary>
        /// <param name="key">缓存键</param>
        /// <returns>出队元素</returns>
        public static async Task<string> DequeueItemFromListAsync(string key)
        {
            return await _cache.DequeueItemFromListAsync(key);
        }

        /// <summary>
        /// 异步获取列表中的所有元素。
        /// </summary>
        /// <param name="key">缓存键</param>
        /// <returns>元素列表</returns>
        public static async Task<List<string>> GetAllItemsFromListAsync(string key)
        {
            return await _cache.GetAllItemsFromListAsync(key);
        }

        /// <summary>
        /// 异步获取所有缓存键。
        /// </summary>
        /// <returns>缓存键列表</returns>
        public static async Task<List<string>> GetAllKeysAsync()
        {
            return await _cache.GetAllKeysAsync();
        }

        /// <summary>
        /// 异步在列表的指定索引位置设置元素值。
        /// </summary>
        /// <param name="listId">列表Id</param>
        /// <param name="listIndex">索引位置</param>
        /// <param name="value">元素值</param>
        public static async Task SetItemInListAsync(string listId, int listIndex, string value)
        {
            await _cache.SetItemInListAsync(listId, listIndex, value);
        }

        /// <summary>
        /// 异步从列表中移除指定元素。
        /// </summary>
        /// <param name="key">缓存键</param>
        /// <param name="value">元素值</param>
        public static async Task RemoveAtFromListAsync(string key, string value)
        {
            await _cache.RemoveItemFromListAsync(key, value);
        }

        /// <summary>
        /// 异步获取列表中指定索引位置的元素值。
        /// </summary>
        /// <param name="listId">列表Id</param>
        /// <param name="listIndex">索引位置</param>
        /// <returns>元素值</returns>
        public static async Task<string> GetItemFromListAsync(string listId, int listIndex)
        {
            return await _cache.GetItemFromListAsync(listId, listIndex);
        }

        /// <summary>
        /// 异步获取列表中指定索引位置的元素值。
        /// </summary>
        /// <typeparam name="T">目标类型</typeparam>
        /// <param name="listId">列表Id</param>
        /// <param name="listIndex">索引位置</param>
        /// <returns>元素值</returns>
        public static async Task<T> GetItemFromListAsync<T>(string listId, int listIndex)
        {
            return await _cache.GetItemFromListAsync<T>(listId, listIndex);
        }

        /// <summary>
        /// 异步向列表中添加元素。
        /// </summary>
        /// <param name="listId">列表Id</param>
        /// <param name="value">元素值</param>
        public static async Task AddItemToListAsync(string listId, string value)
        {
            await _cache.AddItemToListAsync(listId, value);
        }

        /// <summary>
        /// 异步从列表中移除指定元素。
        /// </summary>
        /// <typeparam name="T">目标类型</typeparam>
        /// <param name="listId">列表Id</param>
        /// <param name="value">元素值</param>
        public static async Task RemoveItemFromListAsync<T>(string listId, T value)
        {
            await _cache.RemoveItemFromListAsync(listId, value);
        }

        /// <summary>
        /// 异步向集合中添加元素。
        /// </summary>
        /// <param name="setId">集合Id</param>
        /// <param name="item">元素值</param>
        public static async Task AddItemToSetAsync(string setId, string item)
        {
            await _cache.AddItemToSetAsync(setId, item);
        }

        /// <summary>
        /// 异步批量向列表中添加元素。
        /// </summary>
        /// <param name="listId">列表Id</param>
        /// <param name="values">元素列表</param>
        public static async Task AddRangeToListAsync(string listId, List<string> values)
        {
            await _cache.AddRangeToListAsync(listId, values);
        }

        /// <summary>
        /// 异步批量向集合中添加元素。
        /// </summary>
        /// <param name="listId">集合Id</param>
        /// <param name="items">元素列表</param>
        public static async Task AddRangeToSetAsync(string listId, List<string> items)
        {
            await _cache.AddRangeToSetAsync(listId, items);
        }

        /// <summary>
        /// 异步从集合中移除指定元素。
        /// </summary>
        /// <param name="setId">集合Id</param>
        /// <param name="item">元素值</param>
        public static async Task RemoveItemFromSetAsync(string setId, string item)
        {
            await _cache.RemoveItemFromSetAsync(setId, item);
        }

        /// <summary>
        /// 异步从集合中移除指定元素。
        /// </summary>
        /// <typeparam name="T">目标类型</typeparam>
        /// <param name="setId">集合Id</param>
        /// <param name="item">元素值</param>
        public static async Task RemoveItemFromSetAsync<T>(string setId, T item)
        {
            await _cache.RemoveItemFromSetAsync(setId, item);
        }

        /// <summary>
        /// 异步在列表的指定索引位置设置元素值。
        /// </summary>
        /// <typeparam name="T">目标类型</typeparam>
        /// <param name="listId">列表Id</param>
        /// <param name="listIndex">索引位置</param>
        /// <param name="value">元素值</param>
        public static async Task SetItemInListAsync<T>(string listId, int listIndex, T value)
        {
            await _cache.SetItemInListAsync(listId, listIndex, value);
        }

        /// <summary>
        /// 异步向列表中添加元素。
        /// </summary>
        /// <typeparam name="T">目标类型</typeparam>
        /// <param name="listId">列表Id</param>
        /// <param name="value">元素值</param>
        public static async Task AddItemToListAsync<T>(string listId, T value)
        {
            await _cache.AddItemToListAsync(listId, value);
        }

        /// <summary>
        /// 异步向集合中添加元素。
        /// </summary>
        /// <typeparam name="T">目标类型</typeparam>
        /// <param name="setId">集合Id</param>
        /// <param name="item">元素值</param>
        public static async Task AddItemToSetAsync<T>(string setId, T item)
        {
            await _cache.AddItemToSetAsync(setId, item);
        }

        /// <summary>
        /// 异步批量向列表中添加元素。
        /// </summary>
        /// <typeparam name="T">目标类型</typeparam>
        /// <param name="listId">列表Id</param>
        /// <param name="values">元素列表</param>
        public static async Task AddRangeToListAsync<T>(string listId, List<T> values)
        {
            await _cache.AddRangeToListAsync(listId, values);
        }

        /// <summary>
        /// 异步批量向集合中添加元素。
        /// </summary>
        /// <typeparam name="T">目标类型</typeparam>
        /// <param name="listId">集合Id</param>
        /// <param name="items">元素列表</param>
        public static async Task AddRangeToSetAsync<T>(string listId, List<T> items)
        {
            await _cache.AddRangeToSetAsync(listId, items);
        }

        /// <summary>
        /// 异步获取集合中的所有元素。
        /// </summary>
        /// <param name="setId">集合Id</param>
        /// <returns>元素集合</returns>
        public static async Task<HashSet<string>> GetAllItemsFromSetAsync(string setId)
        {
            return await _cache.GetAllItemsFromSetAsync(setId);
        }

        /// <summary>
        /// 异步获取集合中的所有元素。
        /// </summary>
        /// <typeparam name="T">目标类型</typeparam>
        /// <param name="setId">集合Id</param>
        /// <returns>元素集合</returns>
        public static async Task<HashSet<T>> GetAllItemsFromSetAsync<T>(string setId)
        {
            return await _cache.GetAllItemsFromSetAsync<T>(setId);
        }

        /// <summary>
        /// 异步更新缓存键的过期时间。
        /// </summary>
        /// <param name="key">缓存键</param>
        /// <param name="expireIn">过期时间</param>
        /// <returns>是否成功</returns>
        public static async Task<bool> ExpireEntryInAsync(string key, TimeSpan expireIn)
        {
            return await _cache.ExpireEntryInAsync(key, expireIn);
        }

        /// <summary>
        /// 异步更新缓存键的过期时间。
        /// </summary>
        /// <param name="key">缓存键</param>
        /// <param name="expireAt">过期时间</param>
        /// <returns>是否成功</returns>
        public static async Task<bool> ExpireEntryAtAsync(string key, DateTime expireAt)
        {
            return await _cache.ExpireEntryAtAsync(key, expireAt);
        }

        /// <summary>
        /// 获取缓存命中率。
        /// </summary>
        /// <returns>缓存命中率</returns>
        public static Task<double> GetCacheHitRate()
        {
            return _cache.GetCacheHitRate();
        }

        /// <summary>
        /// 异步批量获取缓存值。
        /// </summary>
        /// <param name="keys">缓存键列表</param>
        /// <returns>缓存值字典</returns>
        public static async Task<Dictionary<string, string>> GetMultipleAsync(IEnumerable<string> keys)
        {
            return await _cache.GetMultipleAsync(keys);
        }

        /// <summary>
        /// 异步批量设置缓存值。
        /// </summary>
        /// <param name="keyValuePairs">缓存键值对</param>
        /// <param name="expiration">过期时间</param>
        public static async Task SetMultipleAsync(Dictionary<string, object> keyValuePairs, TimeSpan? expiration = null)
        {
            await _cache.SetMultipleAsync(keyValuePairs, expiration);
        }
    }
}