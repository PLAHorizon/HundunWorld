using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using System.Reflection;

namespace Horizon.Core.Helper
{
    /// <summary>
    /// ObjectHelper 类，提供对象操作的工具方法，包括序列化、反射更新和枚举描述。
    /// </summary>
    public static class ObjectHelper
    {
        /// <summary>
        /// 将对象序列化为 JSON 格式的字节数组。
        /// </summary>
        /// <param name="obj">要序列化的对象</param>
        /// <returns>序列化后的字节数组</returns>
        public static byte[] ObjectToBytesForJson(this object obj)
        {
            return Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(obj));
        }

        /// <summary>
        /// 将 JSON 格式的字节数组反序列化为对象。
        /// </summary>
        /// <typeparam name="T">目标对象的类型</typeparam>
        /// <param name="bytes">JSON 格式的字节数组</param>
        /// <returns>反序列化后的对象</returns>
        public static T BytesToObjectForJson<T>(this byte[] bytes)
        {
            return JsonConvert.DeserializeObject<T>(Encoding.UTF8.GetString(bytes));
        }

        /// <summary>
        /// 使用反射更新对象的属性值。
        /// </summary>
        /// <typeparam name="U">更新数据的类型</typeparam>
        /// <typeparam name="T">目标对象的类型</typeparam>
        /// <param name="updateDto">包含更新数据的对象</param>
        /// <param name="entity">要更新的目标对象</param>
        /// <param name="ignore">需要忽略的属性名称</param>
        public static void ObjectUpdate<U, T>(this U updateDto, ref T entity, params string[] ignore)
        {
            string[] defaultIgnore = { "Id" };
            ignore = ignore.Concat(defaultIgnore).Distinct().ToArray();

            var entityProperties = entity.GetType().GetProperties();
            var dtoProperties = updateDto.GetType().GetProperties();

            foreach (var dtoProp in dtoProperties)
            {
                if (ignore.Contains(dtoProp.Name))
                    continue;

                var value = dtoProp.GetValue(updateDto);
                if (value != null)
                {
                    var entityProp = entityProperties.FirstOrDefault(p => p.Name == dtoProp.Name);
                    entityProp?.SetValue(entity, value);
                }
            }
        }

        /// <summary>
        /// 根据指定的键选择器对集合进行去重。
        /// </summary>
        /// <typeparam name="TSource">集合元素的类型</typeparam>
        /// <typeparam name="TKey">键的类型</typeparam>
        /// <param name="source">要去重的集合</param>
        /// <param name="keySelector">用于选择键的函数</param>
        /// <returns>去重后的集合</returns>
        public static IEnumerable<TSource> DistinctBy<TSource, TKey>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector)
        {
            HashSet<TKey> seenKeys = new HashSet<TKey>();
            foreach (var element in source)
            {
                if (seenKeys.Add(keySelector(element)))
                {
                    yield return element;
                }
            }
        }

        /// <summary>
        /// 获取枚举值的描述信息。
        /// </summary>
        /// <typeparam name="T">枚举的类型</typeparam>
        /// <typeparam name="A">描述属性的类型</typeparam>
        /// <param name="value">枚举值</param>
        /// <returns>描述信息字符串</returns>
        public static string Description<T, A>(this T value) where A : Attribute
        {
            var type = typeof(T);
            var member = type.GetMember(value.ToString()).FirstOrDefault();
            var attribute = member?.GetCustomAttribute<A>();
            return attribute?.ToString() ?? value.ToString();
        }
    }
}