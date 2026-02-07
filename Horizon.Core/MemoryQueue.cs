using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Core
{
    /// <summary>
    /// 内存管理
    /// </summary>
    public class MemoryManager
    {
        /// <summary>
        /// 回调实列队列，存储可接收推送任务的回调实列,心跳包信息
        /// </summary>
        public static Hashtable CallbackInstanceQueue { get; set; } = Hashtable.Synchronized(new Hashtable());
        /// <summary>
        /// 分布式服务容器
        /// </summary>
        public static Hashtable DAServiceQueue { get; set; } = Hashtable.Synchronized(new Hashtable());
        /// <summary>
        ///SocketClient容器
        /// </summary>
        public static Hashtable SocketClientQueue { get; set; } = Hashtable.Synchronized(new Hashtable());


        /// <summary>
        /// 当前缓存是否可用
        /// </summary>
        /// <param name="queue"></param>
        /// <returns></returns>
        public static bool IsCacheing(CacheQueue queue)
        {
            return queue.IsCacheing;
        }
    }

    /// <summary>
    /// 缓存队列
    /// </summary>
    public class CacheQueue
    {
        /// <summary>
        /// 
        /// </summary>
        public Queue Cache { get; set; }
        public int MaxItem { get; set; }
        public int Count { get { return Cache.Count; } }
        /// <summary>
        /// 当前是否真该提供缓存
        /// </summary>
        public bool IsCacheing { get { return Count < MaxItem; } }

        public void Clear()
        {
            Cache.Clear();
        }

        /// <summary>
        /// 并发安全的入队操作
        /// </summary>
        public void EnqueueSafe(object item)
        {
            lock (Cache)
            {
                if (Cache.Count < MaxItem)
                {
                    Cache.Enqueue(item);
                }
            }
        }

        /// <summary>
        /// 并发安全的出队操作
        /// </summary>
        public object DequeueSafe()
        {
            lock (Cache)
            {
                return Cache.Count > 0 ? Cache.Dequeue() : null;
            }
        }
    }

}
