using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text;

namespace Horizon.Core.Abstract.Helper
{
    public static class EnumHelper
    {
        private static Hashtable enumDesciption;

        static EnumHelper()
        {
            enumDesciption = GetDescriptionContainer();
        }

        private static void AddToEnumDescription(this Type enumType)
        {
            if (enumType == null) return;
            enumDesciption.Add(enumType, GetEnumDic(enumType));
        }

        /// <summary>
        /// 根据枚举类型和枚举值获取枚举描述
        /// </summary>
        /// <param name="enumType">枚举类型</param>
        /// <param name="enumText">枚举值</param>
        /// <returns></returns>
        private static string GetDescription(this Type enumType, string enumText)
        {
            if (enumType == null) return null;
            if (string.IsNullOrEmpty(enumText))
            {
                return null;
            }
            if (!enumDesciption.ContainsKey(enumType))
            {
                AddToEnumDescription(enumType);
            }
            object obj = enumDesciption[enumType];
            if ((obj == null) || string.IsNullOrEmpty(enumText))
            {
                throw new ApplicationException("不存在枚举的描述");
            }
            Dictionary<string, string> dictionary = (Dictionary<string, string>)obj;
            return dictionary[enumText];
        }

        private static Hashtable GetDescriptionContainer()
        {
            enumDesciption = new Hashtable();
            return enumDesciption;
        }

        /// <summary>
        ///  返回 Dic&lt;枚举项，描述&gt;
        /// </summary>
        /// <param name="enumType">枚举的类型</param>
        /// <returns>Dic&lt;枚举项，描述&gt;</returns>
        private static Dictionary<string, string> GetEnumDic(this Type enumType)
        {
            if (enumType == null) return default(Dictionary<string, string>);
            Dictionary<string, string> strs = new Dictionary<string, string>();
            FieldInfo[] fields = enumType.GetFields();
            for (int i = 0; i < fields.Length; i++)
            {
                FieldInfo fieldInfo = fields[i];
                if (fieldInfo.FieldType.IsEnum)
                {
                    object[] customAttributes = fieldInfo.GetCustomAttributes(typeof(DescriptionAttribute), false);
                    strs.Add(fieldInfo.Name, ((DescriptionAttribute)customAttributes[0]).Description);
                }
            }
            return strs;
        }

        private static bool IsIntType(double d)
        {
            return (int)d != d;
        }

        /// <summary>
        /// 根据枚举类型和枚举值获取枚举描述
        /// </summary>
        /// <returns></returns>
        public static string ToDescription(this Enum value)
        {
            if (value != null)
            {
                Type type = value.GetType();
                string name = Enum.GetName(type, value);
                return GetDescription(type, name);
            }
            else
            {
                return "";
            }
        }

        /// <summary>
        /// 转化枚举及其描述为字典类型
        /// </summary>
        /// <typeparam name="TEnum"></typeparam>
        /// <returns></returns>
        public static Dictionary<int, string> ToDescriptionDictionary<TEnum>()
        {
            Array values = Enum.GetValues(typeof(TEnum));
            Dictionary<int, string> nums = new Dictionary<int, string>();
            foreach (Enum value in values)
            {
                nums.Add(Convert.ToInt32(value), value.ToDescription());
            }
            return nums;
        }

        /// <summary>
        /// 转化枚举及其Text值转为字典类型
        /// </summary>
        /// <typeparam name="TEnum"></typeparam>
        /// <returns></returns>
        public static Dictionary<int, string> ToDictionary<TEnum>()
        {
            Array values = Enum.GetValues(typeof(TEnum));
            Dictionary<int, string> nums = new Dictionary<int, string>();
            foreach (Enum value in values)
            {
                nums.Add(Convert.ToInt32(value), value.ToString());
            }
            return nums;
        }

        /// <summary>
        /// 获取枚举值说明字符串
        /// </summary>
        /// <typeparam name="T">枚举类型</typeparam>
        /// <param name="item">枚举值</param>
        /// <returns></returns>
        public static string GetDescription<T>(this T item)
        {
            if (item == null) return null;
            return ((DescriptionAttribute)typeof(T)
                 .GetField(item.ToString())
                 .GetCustomAttributes(typeof(DescriptionAttribute), false)
                 .FirstOrDefault())?.Description;
        }

        /// <summary>
        /// 获取枚举类型的说明字典
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static Dictionary<int, string> GetDescription<T>()
        {
            Dictionary<int, string> dic = new Dictionary<int, string>();
            foreach (var item in typeof(T).GetEnumValues())
            {
                dic.Add((int)item, GetDescription(item));
            }
            return dic;
        }

    }
}
