using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Core
{
    /// <summary>
    /// 值之比较
    /// </summary>
    public static class ValueComparison
    {
        /// <summary>
        /// 比较最小值
        /// </summary>
        /// <typeparam name="T">类型参数</typeparam>
        /// <typeparam name="U">参与比较的类型参数</typeparam>
        /// <typeparam name="D">比较数据类型</typeparam>
        /// <param name="obj1">实例对象，比较对象</param>
        /// <param name="obj2">实例对象，参与比较对象</param>
        /// <param name="cpNames">需要比较属性的名称</param>
        /// <param name="targetDataNames">参与比较对象需要提供的而外辅助值属性的名称</param>
        public static T VMin<T, U, D>(this T obj1, U obj2, string[] cpNames, string[] targetDataNames) where D : struct
        {
            var value = (D)obj1.GetType().GetProperties()//反射
                                         .FirstOrDefault(m => m.Name == cpNames[0])
                                         .GetValue(obj1);
            var value2 = (D)obj2.GetType().GetProperties()//反射
                                        .FirstOrDefault(m => m.Name == cpNames[1])
                                        .GetValue(obj2);
            var target = obj2.GetType().GetProperties()//反射
                                       .FirstOrDefault(m => m.Name == targetDataNames[1])
                                       .GetValue(obj2);
            if (value.Equals(value2))
            {
                obj1.GetType().GetProperties()//反射
                           .FirstOrDefault(m => m.Name == targetDataNames[0])
                           .SetValue(obj1, target);
                return obj1;
            }
            List<D> ds = new List<D> { value, value2 };
            D d = ds.Min(m => m);
            if (d.Equals(value))
                return obj1;
            else
            {
                obj1.GetType().GetProperties()//反射
                          .FirstOrDefault(m => m.Name == cpNames[0])
                          .SetValue(obj1, value2);
                obj1.GetType().GetProperties()//反射
                         .FirstOrDefault(m => m.Name == targetDataNames[0])
                         .SetValue(obj1, target);
                return obj1;
            }
        }

        /// <summary>
        /// 比较最大值
        /// </summary>
        /// <typeparam name="T">类型参数</typeparam>
        /// <typeparam name="U">参与比较的类型参数</typeparam>
        /// <typeparam name="D">比较数据类型</typeparam>
        /// <param name="obj1">实例对象，比较对象</param>
        /// <param name="obj2">实例对象，参与比较对象</param>
        /// <param name="cpNames">需要比较属性的名称</param>
        /// <param name="targetDataNames">参与比较对象需要提供的而外辅助值属性的名称</param>
        public static T VMax<T, U, D>(this T obj1, U obj2, string[] cpNames, string[] targetDataNames) where D : struct
        {
            var value = (D)obj1.GetType().GetProperties()//反射
                                         .FirstOrDefault(m => m.Name == cpNames[0])
                                         .GetValue(obj1);
            var value2 = (D)obj2.GetType().GetProperties()//反射
                                        .FirstOrDefault(m => m.Name == cpNames[1])
                                        .GetValue(obj2);
            var target = obj2.GetType().GetProperties()//反射
                                       .FirstOrDefault(m => m.Name == targetDataNames[1])
                                       .GetValue(obj2);
            if (value.Equals(value2))
            {
                obj1.GetType().GetProperties()//反射
                           .FirstOrDefault(m => m.Name == targetDataNames[0])
                           .SetValue(obj1, target);
                return obj1;
            }
            List<D> ds = new List<D> { value, value2 };
            D d = ds.Max(m => m);
            if (d.Equals(value))
                return obj1;
            else
            {
                obj1.GetType().GetProperties()//反射
                          .FirstOrDefault(m => m.Name == cpNames[0])
                          .SetValue(obj1, value2);
                obj1.GetType().GetProperties()//反射
                         .FirstOrDefault(m => m.Name == targetDataNames[0])
                         .SetValue(obj1, target);
                return obj1;
            }
        }
    }
}
