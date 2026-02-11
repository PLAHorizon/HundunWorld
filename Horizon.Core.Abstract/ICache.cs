using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Horizon.Core.Abstract
{
    /// <summary>
    /// 注册缓存订阅后接收数据处理事件委托
    /// </summary>
    /// <param name="d">数据</param>
    public delegate void RegisterSubscribeEvent(object d);
    /// <summary>
    /// 缓存接口
    /// </summary>
    // 在ICache接口中增加以下方法
    public interface ICache : IStrategy
    {
        /// <summary>
        /// 是否开启了集群模式
        /// </summary>
        bool IsClusterOpen { get; set; }
        /// <summary>
        /// 缓存过期时间
        /// </summary>
        int TimeOut { get; set; }
        /// <summary>
        /// 获取锁
        /// </summary>
        /// <param name="key">锁键</param>
        /// <returns></returns>
        Task<IDisposable> AcquireLockAsync(string key);
        /// <summary>
        /// 获取锁
        /// </summary>
        /// <param name="key">锁键</param>
        /// <param name="timeOut">获取锁后超时时间</param>
        /// <returns></returns>
        Task<IDisposable> AcquireLockAsync(string key, TimeSpan timeOut);
        /// <summary>
        /// 递增缓存，步进值：1
        /// </summary>
        /// <param name="key">缓存键</param>
        /// <returns></returns>
        Task<long> IncrementValueAsync(string key);
        /// <summary>
        /// 分布式Boolean 类型锁赋值
        /// </summary>
        /// <param name="key">缓存键</param>
        /// <param name="value">设置值</param>
        /// <returns></returns>
        Task<bool> BooleanValueAsync(string key, bool value);
        /// <summary>
        /// 判断key是否存在
        /// </summary>
        Task<bool> ExistsAsync(string key);

        /// <summary>
        /// 获得指定键的缓存值
        /// </summary>
        /// <param name="key">缓存键</param>
        /// <returns>缓存值</returns>
        Task<string> GetAsync(string key);

        /// <summary>
        /// 获得指定键的缓存值
        /// </summary>
        Task<T> GetAsync<T>(string key);
        /// <summary>
        /// 获取缓存的过期时间
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        Task<TimeSpan?> GetTimeToLiveAsync(string key);
        /// <summary>
        /// 将指定键的对象添加到缓存中
        /// </summary>
        /// <param name="key">缓存键</param>
        /// <param name="data">缓存值</param>
        Task<bool> InsertAsync(string key, object data);

        /// <summary>
        /// 将指定键的对象添加到缓存中
        /// </summary>
        /// <param name="key">缓存键</param>
        /// <param name="data">缓存值</param>
        Task<bool> InsertAsync<T>(string key, T data);

        /// <summary>
        /// 将指定键的对象添加到缓存中，并指定过期时间
        /// </summary>
        /// <param name="key">缓存键</param>
        /// <param name="data">缓存值</param>
        /// <param name="cacheTime">缓存过期时间(秒钟)</param>
        Task<bool> InsertAsync(string key, object data, int cacheTime);

        /// <summary>
        /// 将指定键的对象添加到缓存中，并指定过期时间
        /// </summary>
        /// <param name="key">缓存键</param>
        /// <param name="data">缓存值</param>
        /// <param name="cacheTime">缓存过期时间(秒钟)</param>
        Task<bool> InsertAsync<T>(string key, T data, int cacheTime);

        /// <summary>
        /// 将指定键的对象添加到缓存中，并指定过期时间
        /// </summary>
        /// <param name="key">缓存键</param>
        /// <param name="data">缓存值</param>
        /// <param name="cacheTime">缓存过期时间</param>
        Task<bool> InsertAsync(string key, object data, DateTime cacheTime);

        /// <summary>
        /// 将指定键的对象添加到缓存中，并指定过期时间
        /// </summary>
        /// <param name="key">缓存键</param>
        /// <param name="data">缓存值</param>
        /// <param name="cacheTime">缓存过期时间</param>
        Task<bool> InsertAsync<T>(string key, T data, DateTime cacheTime);

        /// <summary>
        /// 注册订阅方法 
        /// </summary>
        Task RegisterSubscribeAsync<T>(string key, RegisterSubscribeEvent dosub);

        /// <summary>
        /// 从缓存中移除指定键的缓存值
        /// </summary>
        /// <param name="key">缓存键</param>
        Task RemoveAsync(string key);

        /// <summary>
        /// 缓存队列发送信息
        /// </summary>
        /// <param name="key"></param>
        /// <param name="data"></param>
        Task SendAsync(string key, object data);

        /// <summary>
        /// 注销订阅方法
        /// </summary>
        Task UnRegisterSubscribAsync(string key);
        /// <summary>
        /// 入队到list中底部最后一个
        /// </summary>
        /// <param name="key"></param>
        /// <param name="value"></param>
        Task EnqueueItemOnListAsync<T>(string key, T value);
        /// <summary>
        ///从 list 中出队,顶部第一个
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        Task<T> DequeueItemFromListAsync<T>(string key);
        /// <summary>
        /// 获取缓存队列所有元素
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="key"></param>
        /// <returns></returns>
        Task<List<T>> GetAllItemsFromListAsync<T>(string key);
        /// <summary>
        /// 入队到list中底部最后一个
        /// </summary>
        /// <param name="key"></param>
        /// <param name="value"></param>
        Task EnqueueItemOnListAsync(string key, string value);
        /// <summary>
        ///从 list 中出队,顶部第一个
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        Task<string> DequeueItemFromListAsync(string key);
        /// <summary>
        /// 获取缓存队列所有元素
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="key"></param>
        /// <returns></returns>
        Task<List<string>> GetAllItemsFromListAsync(string key);
        /// <summary>
        /// 获取所有的缓存键
        /// </summary>
        /// <returns></returns>
        Task<List<string>> GetAllKeysAsync();
        /// <summary>
        /// 在索引位置上设置列表值
        /// </summary>
        /// <param name="listId">列表Id</param>
        /// <param name="listIndex">元素在列表中的索引</param>
        /// <param name="value">元素值</param>
        Task SetItemInListAsync(string listId, int listIndex, string value);

        /// <summary>
        /// 获取列表中指定索引值
        /// </summary>
        /// <param name="listId">列表Id</param>
        /// <param name="listIndex">元素在列表中的索引</param>
        /// <returns>返回该集合中索引位置的元素值</returns>
        Task<string> GetItemFromListAsync(string listId, int listIndex);

        /// <summary>
        /// 添加元素到列表
        /// </summary>
        /// <param name="listId">列表Id</param>
        /// <param name="value">元素值</param>
        Task AddItemToListAsync(string listId, string value);
        /// <summary>
        /// 向集合中添加项
        /// </summary>
        /// <param name="setId">集合Id</param>
        /// <param name="item">项值</param>
        Task AddItemToSetAsync(string setId, string item);
        /// <summary>
        /// 批量向列表中添加元素列
        /// </summary>
        /// <param name="listId">列表Id</param>
        /// <param name="values">元素列</param>
        Task AddRangeToListAsync(string listId, List<string> values);
        /// <summary>
        /// 批量向集合中添加项
        /// </summary>
        /// <param name="listId">集合Id</param>
        /// <param name="items">项列</param>
        Task AddRangeToSetAsync(string listId, List<string> items);

        /// <summary>
        /// 在索引位置上设置列表值
        /// </summary>
        /// <typeparam name="T">类型参数</typeparam>
        /// <param name="listId">列表Id</param>
        /// <param name="listIndex">元素在列表中的索引</param>
        /// <param name="value">元素值</param>
        Task SetItemInListAsync<T>(string listId, int listIndex, T value);
        /// <summary>
        /// 获取列表中指定索引值
        /// </summary>
        /// <typeparam name="T">类型参数</typeparam>
        /// <param name="listId">列表Id</param>
        /// <param name="listIndex">元素在列表中的索引</param>
        /// <returns>返回该集合中索引位置的元素值</returns>
        Task<T> GetItemFromListAsync<T>(string listId, int listIndex);
        /// <summary>
        /// 添加元素到列表
        /// </summary>
        ///  <typeparam name="T">类型参数</typeparam>
        /// <param name="listId">列表Id</param>
        /// <param name="value">元素值</param>
        Task AddItemToListAsync<T>(string listId, T value);
        /// <summary>
        /// 从列表中移除元素
        /// </summary>
        /// <typeparam name="T">元素的类型参数</typeparam>
        /// <param name="listId">列表Id</param>
        /// <param name="value">元素实例</param>
        Task RemoveItemFromListAsync<T>(string listId, T value);
        /// <summary>
        /// 向集合中添加项
        /// </summary>
        ///  <typeparam name="T">类型参数</typeparam>
        /// <param name="setId">集合Id</param>
        /// <param name="item">项值</param>
        Task AddItemToSetAsync<T>(string setId, T item);
        /// <summary>
        /// 批量向列表中添加元素列
        /// </summary>
        ///  <typeparam name="T">类型参数</typeparam>
        /// <param name="listId">列表Id</param>
        /// <param name="values">元素列</param>
        Task AddRangeToListAsync<T>(string listId, List<T> values);
        /// <summary>
        /// 批量向集合中添加项
        /// </summary>
        /// <typeparam name="T">类型参数</typeparam>
        /// <param name="listId">集合Id</param>
        /// <param name="items">项列</param>
        Task AddRangeToSetAsync<T>(string listId, List<T> items);

        /// <summary>
        /// 获取Set集合
        /// </summary>
        /// <param name="setId"></param>
        /// <returns></returns>
        Task<HashSet<string>> GetAllItemsFromSetAsync(string setId);
        /// <summary>
        /// 获取Set集合
        /// </summary>
        /// <param name="setId"></param>
        /// <returns></returns>
        Task<HashSet<T>> GetAllItemsFromSetAsync<T>(string setId);
        /// <summary>
        /// 从集合中移除项
        /// </summary>
        /// <param name="setId"></param>
        /// <param name="item"></param>
        Task RemoveItemFromSetAsync(string setId, string item);
        /// <summary>
        /// 从集合中移除项
        /// </summary>
        /// <param name="setId"></param>
        /// <param name="item"></param>
        Task RemoveItemFromSetAsync<T>(string setId, T item);
        /// <summary>
        /// 更新键的过期时间
        /// </summary>
        /// <param name="key"></param>
        /// <param name="expireIn"></param>
        /// <returns></returns>
        Task<bool> ExpireEntryInAsync(string key, TimeSpan expireIn);
        /// <summary>
        /// 更新键的过期时间
        /// </summary>
        /// <param name="key"></param>
        /// <param name="expireAt"></param>
        /// <returns></returns>
        Task<bool> ExpireEntryAtAsync(string key, DateTime expireAt);

        Task<double> GetCacheHitRate();
        Task<Dictionary<string, string>> GetMultipleAsync(IEnumerable<string> keys);
        Task SetMultipleAsync(Dictionary<string, object> keyValuePairs, TimeSpan? expiration = null);

        /// <summary>
        /// 缓存读写模式（Cache-Aside Pattern）
        /// 先从缓存读取，如果缓存未命中则从数据源获取并写入缓存
        /// 支持空值缓存以防止缓存穿透
        /// </summary>
        /// <typeparam name="T">缓存数据类型</typeparam>
        /// <param name="key">缓存键</param>
        /// <param name="factory">数据源获取方法</param>
        /// <param name="expiration">缓存过期时间</param>
        /// <param name="cacheNullValue">是否缓存空值（防止缓存穿透）</param>
        /// <param name="nullValueExpiration">空值缓存过期时间（默认较短）</param>
        /// <returns>缓存的数据或从数据源获取的数据</returns>
        Task<T> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiration = null, bool cacheNullValue = true, TimeSpan? nullValueExpiration = null);
    }
}



// 新增熔断策略类
public class CircuitBreakerPolicy
{
    public int FailureThreshold { get; set; } = 5;
    public TimeSpan DurationOfBreak { get; set; } = TimeSpan.FromSeconds(30);
    public int SamplingDurationSeconds { get; set; } = 10;
}