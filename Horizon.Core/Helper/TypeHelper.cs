using System;

namespace Horizon.Core.Helper
{
    /// <summary>
    /// 类型帮助类
    /// </summary>
    public class TypeHelper
    {
        /// <summary>
        /// 将object类型转换成bool类型
        /// </summary>
        /// <param name="s">目标对象</param>
        /// <param name="defaultValue">默认值</param>
        /// <returns></returns>
        public static bool ObjectToBool(object s, bool defaultValue)
        {
            if (s != null)
            {
                return StringToBool(s.ToString(), defaultValue);
            }
            return defaultValue;
        }

        /// <summary>
        /// 将object类型转换成bool类型
        /// </summary>
        /// <param name="s">目标对象</param>
        /// <returns></returns>
        public static bool ObjectToBool(object s)
        {
            return ObjectToBool(s, false);
        }

        /// <summary>
        /// 将object类型转换成datetime类型
        /// </summary>
        /// <param name="s">目标对象</param>
        /// <param name="defaultValue">默认值</param>
        /// <returns></returns>
        public static DateTime ObjectToDateTime(object s, DateTime defaultValue)
        {
            if (s != null)
            {
                return StringToDateTime(s.ToString(), defaultValue);
            }
            return defaultValue;
        }

        /// <summary>
        /// 将object类型转换成datetime类型
        /// </summary>
        /// <param name="s">目标对象</param>
        /// <returns></returns>
        public static DateTime ObjectToDateTime(object s)
        {
            return ObjectToDateTime(s, DateTime.Now);
        }

        /// <summary>
        /// 将object类型转换成decimal类型
        /// </summary>
        /// <param name="s">目标对象</param>
        /// <param name="defaultValue">默认值</param>
        /// <returns></returns>
        public static decimal ObjectToDecimal(object s, decimal defaultValue)
        {
            if (s != null)
            {
                return StringToDecimal(s.ToString(), defaultValue);
            }
            return defaultValue;
        }

        /// <summary>
        /// 将object类型转换成decimal类型
        /// </summary>
        /// <param name="s">目标对象</param>
        /// <returns></returns>
        public static decimal ObjectToDecimal(object s)
        {
            return ObjectToDecimal(s, 0);
        }

        /// <summary>
        /// 将object类型转换成int类型
        /// </summary>
        /// <param name="s">目标对象</param>
        /// <param name="defaultValue">默认值</param>
        /// <returns></returns>
        public static int ObjectToInt(object s, int defaultValue)
        {
            if (s != null)
            {
                return StringToInt(s.ToString(), defaultValue);
            }
            return defaultValue;
        }

        /// <summary>
        /// 将object类型转换成int类型
        /// </summary>
        /// <param name="s">目标对象</param>
        /// <returns></returns>
        public static int ObjectToInt(object s)
        {
            return ObjectToInt(s, 0);
        }

        /// <summary>
        /// 将string类型转换成bool类型
        /// </summary>
        /// <param name="s">目标字符串</param>
        /// <param name="defaultValue">默认值</param>
        /// <returns></returns>
        public static bool StringToBool(string s, bool defaultValue)
        {
            if (s == "false")
            {
                return false;
            }
            return ((s == "true") || defaultValue);
        }

        /// <summary>
        /// 将string类型转换成datetime类型
        /// </summary>
        /// <param name="s">目标字符串</param>
        /// <param name="defaultValue">默认值</param>
        /// <returns></returns>
        public static DateTime StringToDateTime(string s, DateTime defaultValue)
        {
            DateTime time;
            if (!string.IsNullOrWhiteSpace(s) && DateTime.TryParse(s, out time))
            {
                return time;
            }
            return defaultValue;
        }

        /// <summary>
        /// 将string类型转换成datetime类型
        /// </summary>
        /// <param name="s">目标字符串</param>
        /// <returns></returns>
        public static DateTime StringToDateTime(string s)
        {
            return StringToDateTime(s, DateTime.Now);
        }

        /// <summary>
        /// 将string类型转换成decimal类型
        /// </summary>
        /// <param name="s">目标字符串</param>
        /// <param name="defaultValue">默认值</param>
        /// <returns></returns>
        public static decimal StringToDecimal(string s, decimal defaultValue)
        {
            decimal num;
            if (!string.IsNullOrWhiteSpace(s) && decimal.TryParse(s, out num))
            {
                return num;
            }
            return defaultValue;
        }

        /// <summary>
        /// 将string类型转换成decimal类型
        /// </summary>
        /// <param name="s">目标字符串</param>
        /// <returns></returns>
        public static decimal StringToDecimal(string s)
        {
            return StringToDecimal(s, new decimal(0));
        }

        /// <summary>
        /// 将string类型转换成int类型
        /// </summary>
        /// <param name="s">目标字符串</param>
        /// <param name="defaultValue">默认值</param>
        /// <returns></returns>
        public static int StringToInt(string s, int defaultValue)
        {
            int num;
            if (!string.IsNullOrWhiteSpace(s) && int.TryParse(s, out num))
            {
                return num;
            }
            return defaultValue;
        }

        /// <summary>
        /// 将string类型转换成int类型
        /// </summary>
        /// <param name="s">目标字符串</param>
        /// <returns></returns>
        public static int StringToInt(string s)
        {
            return StringToInt(s, 0);
        }

        /// <summary>
        /// 将string类型转换成bool类型
        /// </summary>
        /// <param name="s">目标字符串</param>
        /// <returns></returns>
        public static bool ToBool(string s)
        {
            return StringToBool(s, false);
        }
    }
}