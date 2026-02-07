using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Core
{
    /// <summary>
    /// 实体类标注解析
    /// </summary>
    public static class EntityAttributeAnalysis
    {
        /// <summary>
        /// 解析标注上的属性值
        /// </summary>
        /// <typeparam name="T">标注类型</typeparam>
        /// <param name="obj">被标注实体对象</param>
        /// <param name="targetName">标注属性名</param>
        /// <param name="memberName">被标注成员名称</param>
        /// <returns></returns>
        public static object Analysis<T>(this object obj, string targetName, AttributeTargets option = AttributeTargets.Class, string memberName = null) where T : Attribute
        {
            switch (option)
            {
                case AttributeTargets.Property:
                    return obj.GetType().GetProperties()//反射
                                    .FirstOrDefault(m => m.Name == memberName)
                                    .GetCustomAttributesData()
                                    .FirstOrDefault(m => m.AttributeType == typeof(T))//标注类型筛选
                                    .NamedArguments.FirstOrDefault(m => m.MemberName == targetName)//标注属性
                                    .TypedValue.Value;//取值
                default:
                case AttributeTargets.Class:
                    return obj.GetType()//反射
                                   .GetCustomAttributesData()//获取自定义属性数据
                                   .FirstOrDefault(m => m.AttributeType == typeof(T))//标注类型筛选
                                   .NamedArguments.FirstOrDefault(m => m.MemberName == targetName)//标注属性
                                   .TypedValue.Value;//取值
            }



        }

        /// <summary>
        /// 解析标注上的属性值
        /// </summary>
        /// <typeparam name="T">标注类型</typeparam>
        /// <param name="obj">被标注实体对象</param>
        /// <param name="targetName">标注属性名</param>
        /// <param name="memberName">被标注成员名称</param>
        /// <returns></returns>
        public static object Analysis<T>(this Type obj, string targetName, AttributeTargets option = AttributeTargets.Class, string memberName = null) where T : Attribute
        {
            switch (option)
            {
                case AttributeTargets.Property:
                    return obj.GetProperties()//反射
                                    .FirstOrDefault(m => m.Name == memberName)
                                    .GetCustomAttributesData()
                                    .FirstOrDefault(m => m.AttributeType == typeof(T))//标注类型筛选
                                    .NamedArguments.FirstOrDefault(m => m.MemberName == targetName)//标注属性
                                    .TypedValue.Value;//取值
                default:
                case AttributeTargets.Class:
                    return obj.GetCustomAttributesData()//获取自定义属性数据
                                   .FirstOrDefault(m => m.AttributeType == typeof(T))//标注类型筛选
                                   .NamedArguments.FirstOrDefault(m => m.MemberName == targetName)//标注属性
                                   .TypedValue.Value;//取值
            }



        }
    }
}
