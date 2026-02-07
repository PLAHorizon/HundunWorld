using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Horizon.Core.Abstract
{
    /// <summary>
    /// 快速激活
    /// </summary>
    public static class FastActivator
    {
        /// <summary>
        /// 对象构造函数
        /// </summary>
        private static ConcurrentDictionary<Type, Func<object[], object>> factoryCache = new ConcurrentDictionary<Type, Func<object[], object>>();
        /// <summary>
        /// 对象实例缓存，使用频次有限的对象实例不推荐使用此字段
        /// </summary>
        private static ConcurrentDictionary<Type, object> objectCache = new ConcurrentDictionary<Type, object>();
        /// <summary>
        /// 使用频次有限的对象实例
        /// </summary>
        private static ConcurrentDictionary<string, object> objectCacheOfHasCode = new ConcurrentDictionary<string, object>();
        /// <summary>
        /// 使用频次有限的对象实例过期时间
        /// </summary>
        private static ConcurrentDictionary<string, DateTime> objectExpired = new ConcurrentDictionary<string, DateTime>();
        /// <summary>
        /// 清理使用频次有限的对象实例定时器
        /// </summary>
        private static Timer timer;
        /// <summary>
        /// 对象过期时间资源锁标志
        /// </summary>
        private static int oexResource = 0;
        static FastActivator()
        {
            Stopwatch stopwatch = new Stopwatch();
            long expired = 50;
            timer = new Timer(obj => //定时清理使用频次有限的对象实例
            {
                stopwatch.Start();
                DateTime dateTime = DateTime.Now;
                try
                {
                    if (0 == Interlocked.Exchange(ref oexResource, 1))
                    {
                        var stepping = objectExpired.GetEnumerator();
                        while (stepping.MoveNext())
                        {
                            if (stepping.Current.Value <= dateTime)
                            {
                                objectExpired.TryRemove(stepping.Current.Key, out DateTime t);
                                if (objectCacheOfHasCode.TryRemove(stepping.Current.Key, out object instance))
                                    instance = null;
                            }
                        }
                        Interlocked.Exchange(ref oexResource, 0);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(Log.CommRepository, ex.Message);
                }
                finally
                {
                    Interlocked.Exchange(ref oexResource, 0);
                    expired = stopwatch.ElapsedMilliseconds;
                    stopwatch.Reset();
                }
            }, null, 500, expired * 2);
        }
        /// <summary>
        /// Creates an instance of the specified type using a generated factory to avoid using Reflection.
        /// </summary>
        /// <param Name="type">The type to be created.</param>
        /// <param name="type"></param>
        /// <param name="isnewInstance">创建新实例</param>
        /// <param name="args"></param>
        /// <returns>The newly created instance.</returns>
        public static object Create(Type type, bool isnewInstance = true, params object[] args)
        {
            Func<object[], object> f;
            if (isnewInstance)
            {
                Type[] typeArray = args.Select(obj => obj.GetType()).ToArray();
                f = BuildDeletgateObj(type, typeArray);
                return f(args);
            }
            if (!factoryCache.TryGetValue(type, out f))
            {
                lock (factoryCache)
                    if (!factoryCache.TryGetValue(type, out f))
                    {
                        Type[] typeArray = args.Select(obj => obj.GetType()).ToArray();
                        factoryCache[type] = f = BuildDeletgateObj(type, typeArray);
                        objectCache[type] = f(args);
                    }
                return objectCache[type];
            }
            else
                return objectCache[type];
        }
        /// <summary>
        /// 从常驻缓存中移除类型
        /// </summary>
        /// <param name="type">类型</param>
        public static void RemoveType<T>(Type type, out T obj)
        {
            objectCache.TryRemove(type, out object item);
            obj = (T)item;
        }
        /// <summary>
        /// 快速激活类型实例
        /// </summary>
        /// <typeparam name="T">类型参数</typeparam>
        /// <param name="isnewInstance">是否使用全新的实例,不替换原有实列</param>
        /// <param name="args">类型实例化时使用的构造函数的参数列表</param>
        /// <returns></returns>
        public static T Create<T>(bool isnewInstance = true, params object[] args)
        {
            Func<object[], object> f;
            if (isnewInstance)
            {
                Type[] typeArray = args.Select(obj => obj.GetType()).ToArray();
                f = BuildDeletgateObj(typeof(T), typeArray);
                return (T)f(args);
            }
            if (!factoryCache.TryGetValue(typeof(T), out f))
            {
                lock (factoryCache)
                    if (!factoryCache.TryGetValue(typeof(T), out f))
                    {
                        Type[] typeArray = args.Select(obj => obj.GetType()).ToArray();
                        factoryCache[typeof(T)] = f = BuildDeletgateObj(typeof(T), typeArray);
                        objectCache[typeof(T)] = f(args);
                    }
                return (T)objectCache[typeof(T)];
            }
            else
                return (T)objectCache[typeof(T)];
        }

        /// <summary>
        /// 快速激活类型实例
        /// 使用哈希值区分实例
        /// 在过期时间内可用，过期时间到则无法获取到该实例，默认过期时间是30秒
        /// </summary>
        /// <typeparam name="T">类型参数</typeparam>
        /// <param name="hashCode">实例哈希值</param>
        /// <param name="expired">实例过期时间，单位毫秒，默认30秒过期</param>
        /// <param name="args">类型实例化时使用的构造函数的参数列表</param>
        /// <returns></returns>
        public static T ActivatorHashInstance<T>(out int hashCode, int expired = 30000, params object[] args)
        {
            Func<object[], object> f;
            T t = ProvideInstance<T>(args, out f);
            hashCode = t.GetHashCode();
            int hc = hashCode;
            objectCacheOfHasCode[$"{typeof(T)}-{hashCode}"] = t;
            Task.Factory.StartNew(() =>
            {
                objectExpired[$"{typeof(T)}-{hc}"] = DateTime.Now.AddMilliseconds(expired);
            });
            return t;
        }
        /// <summary>
        /// 快速激活类型实例
        /// 使用哈希值区分实例
        /// </summary>
        /// <typeparam name="T">类型参数</typeparam>
        /// <param name="hashCode">实例哈希值</param>
        /// <returns></returns>
        public static T ActivatorHashInstance<T>(int hashCode)
        {
            return (T)objectCacheOfHasCode[$"{typeof(T)}-{hashCode}"];
        }
        /// <summary>
        /// 提供实例
        /// </summary>
        /// <typeparam name="T">类型参数</typeparam>
        /// <param name="args">实例构造函数参数列表</param>
        /// <param name="f">实例构造函数</param>
        /// <returns></returns>
        private static T ProvideInstance<T>(object[] args, out Func<object[], object> f)
        {
            if (!factoryCache.TryGetValue(typeof(T), out f))
            {
                lock (factoryCache)
                    if (!factoryCache.TryGetValue(typeof(T), out f))
                    {
                        Type[] typeArray = args.Select(obj => obj.GetType()).ToArray();
                        factoryCache[typeof(T)] = f = BuildDeletgateObj(typeof(T), typeArray);
                    }
            }
            return (T)factoryCache[typeof(T)](args);
        }

        /// <summary>
        /// 销毁使用哈希识别的类型实例
        /// </summary>
        /// <typeparam name="T">类型参数</typeparam>
        /// <param name="hashCode">实列哈希值</param>
        public static void DestructionHash<T>(int hashCode)
        {
            lock (objectCacheOfHasCode)
                if (objectCacheOfHasCode.TryRemove($"{typeof(T)}-{hashCode}", out object t))
                    t = null;
        }

        private static Func<object[], object> BuildDeletgateObj(Type type, Type[] typeList)
        {
            ConstructorInfo constructor = type.GetConstructor(typeList);
            ParameterExpression paramExp = Expression.Parameter(typeof(object[]), "args_");
            Expression[] expList = GetExpressionArray(typeList, paramExp);
            NewExpression newExp = Expression.New(constructor, expList);
            Expression<Func<object[], object>> expObj = Expression.Lambda<Func<object[], object>>(newExp, paramExp);
            return expObj.Compile();
        }

        private static Expression[] GetExpressionArray(Type[] typeList, ParameterExpression paramExp)
        {
            List<Expression> expList = new List<Expression>();
            if (typeList != null)
                for (int i = 0; i < typeList.Length; i++)
                {
                    var paramObj = Expression.ArrayIndex(paramExp, Expression.Constant(i));
                    var expObj = Expression.Convert(paramObj, typeList[i]);
                    expList.Add(expObj);
                }
            return expList.ToArray();
        }
    }
}
