using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Horizon.Core.Helper
{
    /// <summary>
    /// 数据传输对象映射
    /// </summary>
    public static class DTOAutoMapper
    {

        /// <summary>
        /// 使用反射作对象映射
        /// </summary>
        /// <typeparam name="TIn"></typeparam>
        /// <typeparam name="TOut"></typeparam>
        /// <param name="tIn"></param>
        /// <returns></returns>
        public static TOut TransReflection<TIn, TOut>(this TIn tIn)
        {
            TOut tOut = Activator.CreateInstance<TOut>();
            var tInType = tIn.GetType();
            foreach (var itemOut in tOut.GetType().GetProperties())
            {
                var itemIn = tInType.GetProperty(itemOut.Name);
                if (itemIn != null)
                {
                    itemOut.SetValue(tOut, itemIn.GetValue(tIn));
                }
            }
            return tOut;
        }
        /// <summary>
        /// 使用系列化的方式深度克隆
        /// </summary>
        /// <typeparam name="TIn">克隆源</typeparam>
        /// <typeparam name="TOut">克隆体</typeparam>
        /// <param name="tIn">克隆源</param>
        /// <returns></returns>
        public static TOut JsonClone<TIn, TOut>(this TIn tIn)
        {
            TOut ss = JsonConvert.DeserializeObject<TOut>(JsonConvert.SerializeObject(tIn));
            return ss;
        }

        private static Dictionary<string, object> _Dic = new Dictionary<string, object>();
        /// <summary>
        /// 使用反射+lamda 的方式作对象映射
        /// </summary>
        /// <typeparam name="TIn"></typeparam>
        /// <typeparam name="TOut"></typeparam>
        /// <param name="tIn"></param>
        /// <returns></returns>
        public static TOut TransExpression<TIn, TOut>(this TIn tIn)
        {
            string key = string.Format("trans_exp_{0}_{1}", typeof(TIn).FullName, typeof(TOut).FullName);
            if (!_Dic.ContainsKey(key))
            {
                ParameterExpression parameterExpression = Expression.Parameter(typeof(TIn), "p");
                List<MemberBinding> memberBindingList = new List<MemberBinding>();

                foreach (var item in typeof(TOut).GetProperties())
                {

                    if (!item.CanWrite)
                        continue;
                    MemberExpression property = Expression.Property(parameterExpression, typeof(TIn).GetProperty(item.Name));
                    MemberBinding memberBinding = Expression.Bind(item, property);
                    memberBindingList.Add(memberBinding);
                }

                MemberInitExpression memberInitExpression = Expression.MemberInit(Expression.New(typeof(TOut)), memberBindingList.ToArray());
                Expression<Func<TIn, TOut>> lambda = Expression.Lambda<Func<TIn, TOut>>(memberInitExpression, new ParameterExpression[] { parameterExpression });
                Func<TIn, TOut> func = lambda.Compile();

                _Dic[key] = func;
            }
            return ((Func<TIn, TOut>)_Dic[key])(tIn);
        }
    }
}
