using Horizon.Core.Abstract;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Core
{
    /// <summary>
    /// 
    /// </summary>
    public static class ObjectHelper
    {
        //public static  Newtonsoft.Json.JsonSerializerSettings setting = new Newtonsoft.Json.JsonSerializerSettings();
        //   static ObjectHelper()
        //  {
        //      JsonConvert.DefaultSettings = new Func<JsonSerializerSettings>(() =>
        //      {
        //          //空值处理
        //          setting.NullValueHandling = NullValueHandling.Ignore;
        //          setting.MaxDepth = 9;
        //          return setting;
        //      });
        //      JsonConvert.DefaultSettings.Invoke();
        //  }
        /// <summary>
        /// 检查对象是否未实例化
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="obj"></param>
        /// <returns></returns>
        public static bool IsNull<T>(this T obj) where T : class
        {
            if (obj == default || obj == null) return true;
            else
                return false;
        }

        /// <summary>
        /// 对象间浅复制
        /// </summary>
        /// <param name="obj">待复制的对象</param>
        /// <returns>返回新对象</returns>
        public static T ShallowColne<RefT, T>(this RefT obj)
        {
            return JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(obj));
        }

        /// <summary>
        /// 对象间创建新对象复制数据源对象数据
        /// </summary>
        /// <param name="obj">待复制的对象</param>
        /// <param name="objs">创建新类型对象使用的构造函数参数列表实参</param>
        /// <returns>返回新对象</returns>
        public static T CreateColne<RefT, T>(this RefT obj, params object[] objs)
        {
            T model = (T)FastActivator.Create(typeof(T), true, objs);
            var tem = obj.GetType().GetProperties();
            model.GetType().GetProperties().ToList().ForEach(m =>
            {
                m.SetValue(model, tem.FirstOrDefault(r => r.Name == m.Name).GetValue(obj));
            });
            return model;
        }
        /// <summary>
        /// 对象间浅复制
        /// </summary>
        /// <param name="obj">待复制的对象</param>
        /// <returns>返回新对象</returns>
        public static List<T> ShallowColnes<RefT, T>(this List<RefT> obj)
        {
            return JsonConvert.DeserializeObject<List<T>>(JsonConvert.SerializeObject(obj));
        }


        /// <summary> 
        /// 将一个object对象序列化，返回一个byte[]         
        /// </summary> 
        /// <param name="obj">能序列化的对象</param>         
        /// <returns></returns> 
        public static byte[] ObjectToBytesForJson(this object obj)
        {

            return Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(obj));
        }

        /// <summary> 
        /// 将一个序列化后的byte[]数组还原         
        /// </summary>
        /// <param name="Bytes"></param>         
        /// <returns></returns> 
        public static T BytesToObjectForJson<T>(this byte[] Bytes)
        {
            return JsonConvert.DeserializeObject<T>(Encoding.UTF8.GetString(Bytes));
        }
        /// <summary>
        /// 对象映射
        /// </summary>
        /// <typeparam name="T">数据源类型类型参数</typeparam>
        /// <typeparam name="TDTO">映射转换类型类型参数</typeparam>
        /// <param name="item">数据源实例</param>
        /// <returns>返回 TDTO 实例</returns>
        public static TDTO ObjectTo<T, TDTO>(this T item) where TDTO : class, new() where T : class, new()
        {
            if (item == null) return default(TDTO);
            if (MapperInstance.Current == null)
                throw new NullReferenceException();
            return MapperInstance.Current.Map<T, TDTO>(item);
        }

        static string[] _ignore = new string[1] { "Id" };

        /// <summary>
        /// 同名实例属性成员赋值,数据源属性值空时忽略该属性
        /// 不需要更新的值设置为空即可
        /// </summary>
        /// <typeparam name="U"></typeparam>
        /// <typeparam name="T"></typeparam>
        /// <param name="updateDto"></param>
        /// <param name="entity"></param>
        /// <param name="ignore">更新时需要忽略的属性,Id 强制不允许更新</param>
        /// <returns></returns>
        public static void ObjectUpdate<U, T>(this U updateDto, ref T entity, params string[] ignore)
        {
            if (ignore.Intersect(_ignore).Count() == 0) ignore = ignore.Concat(_ignore).ToArray();
            var properties = entity.GetType().GetProperties();
            var cproperties = updateDto.GetType().GetProperties();
            foreach (var item in cproperties)
            {
                if (ignore.Any(m => m.Equals(item.Name)))
                    continue;
                var tem = item.GetValue(updateDto);
                if (tem != default && tem != null)
                {
                    properties.FirstOrDefault(m => m.Name == item.Name)?.SetValue(entity, tem, null);
                }
            }
        }

        /// <summary>
		/// 去重复项
		/// </summary>
		/// <typeparam name="TSource"></typeparam>
		/// <typeparam name="TKey"></typeparam>
		/// <param name="source"></param>
		/// <param name="keySelector"></param>
		/// <returns></returns>
		public static IEnumerable<TSource> DistinctBy<TSource, TKey>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector)
        {
            HashSet<TKey> tKeys = new HashSet<TKey>();
            foreach (TSource tSource in source)
            {
                if (tKeys.Add(keySelector(tSource)))
                {
                    yield return tSource;
                }
            }
        }

        private static Dictionary<Type, HashSet<KeyValuePair<string, string[]>>> _enumDescriptions = new Dictionary<Type, HashSet<KeyValuePair<string, string[]>>>();
        /// <summary>
        /// 枚举描述
        /// </summary>
        /// <typeparam name="T">枚举类型</typeparam>
        /// <param name="t">枚举实列</param>
        /// <returns>返回 枚举 属性 Description 的文本值 </returns>
        public static string Description<T, A>(this T t) where A : DescriptionAttribute
        {
            Descriptions<A>(typeof(T));
            return _enumDescriptions[typeof(T)].FirstOrDefault(m => m.Key == $"{t}").Value[0];
        }
        /// <summary>
        /// 枚举描述
        /// </summary>
        /// <typeparam name="T">枚举类型</typeparam>
        /// <param name="t">枚举实列</param>
        /// <returns>返回 枚举 属性 Description 的文本值集合 </returns>
        public static string[] Descriptions<T, A>(this T t) where A : DescriptionAttribute
        {
            Descriptions<A>(typeof(T));
            return _enumDescriptions[typeof(T)].FirstOrDefault(m => m.Key == $"{t}").Value;

        }
        /// <summary>
        /// 枚举的描述字典
        /// </summary>
        /// <param name="enum"></param>
        private static void Descriptions<A>(Type @enum) where A : DescriptionAttribute
        {
            if (!_enumDescriptions.ContainsKey(@enum))
            {
                _enumDescriptions.Add(@enum, new HashSet<KeyValuePair<string, string[]>>());
                foreach (var item in @enum.GetMembers())
                {
                    var atts = item.GetCustomAttributes(typeof(A), true);
                    if (atts != null && atts.Length > 0)
                    {
                        string[] tem = new string[atts.Length];
                        for (int i = 0; i < tem.Length; i++)
                            tem[i] = ((A)atts[i]).Description;
                        var kv = new KeyValuePair<string, string[]>(item.Name, tem);
                        _enumDescriptions[@enum].Add(kv);
                    }
                }
            }
        }

    }
}
