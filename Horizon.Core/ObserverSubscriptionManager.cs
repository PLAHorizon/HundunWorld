using Orleans;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Horizon.Core
{
    /// <summary>
    /// 消息订阅管理类
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class ObserverSubscriptionManager<T, M> where T : IBaseCallback<M>
    {
        public ObserverSubscriptionManager()
        {
            _instance = new List<ObserverInstance<T, M>>();
        }
        /// <summary>
        /// 
        /// </summary>
        private List<ObserverInstance<T, M>> _instance;
        public void Subscribe(ObserverInstance<T, M> observer)
        {
            if (!_instance.Any(a => a.Key == observer.Key)) _instance.Add(observer);
        }
        public void Unsubscribe(ObserverInstance<T, M> observer) => _instance.Remove(observer);
        public void Notify(Action<ObserverInstance<T, M>> action, Guid key, Type type) => action(_instance.FirstOrDefault(m => m.Key == key));

    }

    public class ObserverInstance<T, M> where T : IBaseCallback<M>
    {
        public Guid Key { get; set; }
        public Type Type { get; set; }
        public T Instance { get; set; }
    }
}