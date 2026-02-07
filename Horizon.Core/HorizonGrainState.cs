using Horizon.Core.Abstract;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace Horizon.Core
{
    /// <summary>
    /// 状态管理
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class HorizonGrainState<Call, T> where Call : IBaseCallback<T>
    {
        /// <summary>
        /// 流
        /// </summary>
       // public IAsyncStream<TransactionMessage<T>> Stream { get; set; }
        /// <summary>
        /// 客户端消息订阅者
        /// </summary>
        public ObserverSubscriptionManager<Call, T> SubscriptionManager { get; set; }
        /// <summary>
        /// 流订阅句柄
        /// </summary>
        //public StreamSubscriptionHandle<TransactionMessage<T>> SubscriptionHandle { get; set; }
    }
}
