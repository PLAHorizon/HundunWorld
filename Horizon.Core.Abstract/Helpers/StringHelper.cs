using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Horizon.Core.Abstract.Helpers
{
    /// <summary>
    /// 字符串帮助类
    /// </summary>
    public static class StringHelper
    {

        /// <summary>
        /// 获得字符串的长度,一个汉字的长度为1
        /// </summary>
        public static int GetStringLength(string s)
        {
            if (!string.IsNullOrEmpty(s))
                return Encoding.Default.GetBytes(s).Length;

            return 0;
        }

        #region 分割字符串

        /// <summary>
        /// 分割字符串
        /// </summary>
        /// <param name="sourceStr">源字符串</param>
        /// <param name="splitStr">分隔字符串</param>
        /// <returns></returns>
        public static string[] SplitString(string sourceStr, string splitStr)
        {
            if (string.IsNullOrEmpty(sourceStr) || string.IsNullOrEmpty(splitStr))
                return new string[0] { };

            if (sourceStr.IndexOf(splitStr) == -1)
                return new string[] { sourceStr };

            if (splitStr.Length == 1)
                return sourceStr.Split(splitStr[0]);
            else
                return Regex.Split(sourceStr, Regex.Escape(splitStr), RegexOptions.IgnoreCase);

        }

        /// <summary>
        /// 分割字符串
        /// </summary>
        /// <param name="sourceStr">源字符串</param>
        /// <returns></returns>
        public static string[] SplitString(string sourceStr)
        {
            return SplitString(sourceStr, ",");
        }

        #endregion

        #region 截取字符串

        /// <summary>
        /// 截取字符串
        /// </summary>
        /// <param name="sourceStr">源字符串</param>
        /// <param name="startIndex">开始位置的索引</param>
        /// <param name="length">子字符串的长度</param>
        /// <returns></returns>
        public static string SubString(string sourceStr, int startIndex, int length)
        {
            if (!string.IsNullOrEmpty(sourceStr))
            {
                if (sourceStr.Length >= (startIndex + length))
                    return sourceStr.Substring(startIndex, length);
                else
                    return sourceStr.Substring(startIndex);
            }

            return "";
        }

        /// <summary>
        /// 截取字符串
        /// </summary>
        /// <param name="sourceStr">源字符串</param>
        /// <param name="length">子字符串的长度</param>
        /// <returns></returns>
        public static string SubString(string sourceStr, int length)
        {
            return SubString(sourceStr, 0, length);
        }

        #endregion

        #region 移除前导/后导字符串

        /// <summary>
        /// 移除前导字符串
        /// </summary>
        /// <param name="sourceStr">源字符串</param>
        /// <param name="trimStr">移除字符串</param>
        /// <returns></returns>
        public static string TrimStart(string sourceStr, string trimStr)
        {
            return TrimStart(sourceStr, trimStr, true);
        }

        /// <summary>
        /// 移除前导字符串
        /// </summary>
        /// <param name="sourceStr">源字符串</param>
        /// <param name="trimStr">移除字符串</param>
        /// <param name="ignoreCase">是否忽略大小写</param>
        /// <returns></returns>
        public static string TrimStart(string sourceStr, string trimStr, bool ignoreCase)
        {
            if (string.IsNullOrEmpty(sourceStr))
                return string.Empty;

            if (string.IsNullOrEmpty(trimStr) || !sourceStr.StartsWith(trimStr, ignoreCase, CultureInfo.CurrentCulture))
                return sourceStr;

            return sourceStr.Remove(0, trimStr.Length);
        }

        /// <summary>
        /// 移除后导字符串
        /// </summary>
        /// <param name="sourceStr">源字符串</param>
        /// <param name="trimStr">移除字符串</param>
        /// <returns></returns>
        public static string TrimEnd(string sourceStr, string trimStr)
        {
            return TrimEnd(sourceStr, trimStr, true);
        }

        /// <summary>
        /// 移除后导字符串
        /// </summary>
        /// <param name="sourceStr">源字符串</param>
        /// <param name="trimStr">移除字符串</param>
        /// <param name="ignoreCase">是否忽略大小写</param>
        /// <returns></returns>
        public static string TrimEnd(string sourceStr, string trimStr, bool ignoreCase)
        {
            if (string.IsNullOrEmpty(sourceStr))
                return string.Empty;

            if (string.IsNullOrEmpty(trimStr) || !sourceStr.EndsWith(trimStr, ignoreCase, CultureInfo.CurrentCulture))
                return sourceStr;

            return sourceStr.Substring(0, sourceStr.Length - trimStr.Length);
        }

        /// <summary>
        /// 移除前导和后导字符串
        /// </summary>
        /// <param name="sourceStr">源字符串</param>
        /// <param name="trimStr">移除字符串</param>
        /// <returns></returns>
        public static string Trim(string sourceStr, string trimStr)
        {
            return Trim(sourceStr, trimStr, true);
        }

        /// <summary>
        /// 移除前导和后导字符串
        /// </summary>
        /// <param name="sourceStr">源字符串</param>
        /// <param name="trimStr">移除字符串</param>
        /// <param name="ignoreCase">是否忽略大小写</param>
        /// <returns></returns>
        public static string Trim(string sourceStr, string trimStr, bool ignoreCase)
        {
            if (string.IsNullOrEmpty(sourceStr))
                return string.Empty;

            if (string.IsNullOrEmpty(trimStr))
                return sourceStr;

            if (sourceStr.StartsWith(trimStr, ignoreCase, CultureInfo.CurrentCulture))
                sourceStr = sourceStr.Remove(0, trimStr.Length);

            if (sourceStr.EndsWith(trimStr, ignoreCase, CultureInfo.CurrentCulture))
                sourceStr = sourceStr.Substring(0, sourceStr.Length - trimStr.Length);

            return sourceStr;
        }

        #endregion

        /// <summary>
        /// 获得指定顺序的字符在字符串中的位置索引
        /// </summary>
        /// <param name="s">字符串</param>
        /// <param name="order">顺序</param>
        /// <returns></returns>
        public static int IndexOf(string s, int order)
        {
            return IndexOf(s, '-', order);
        }

        /// <summary>
        /// 获得指定顺序的字符在字符串中的位置索引
        /// </summary>
        /// <param name="s">字符串</param>
        /// <param name="c">字符</param>
        /// <param name="order">顺序</param>
        /// <returns></returns>
        public static int IndexOf(string s, char c, int order)
        {
            int length = s.Length;
            for (int i = 0; i < length; i++)
            {
                if (c == s[i])
                {
                    if (order == 1)
                        return i;
                    order--;
                }
            }
            return -1;
        }
        /// <summary>
        ///字符串转化为long 数值
        /// </summary>
        /// <param name="source">字符串</param>
        /// <returns>返回 long类型的数值</returns>
        public static long ToLong(this string source)
        {
            long t;
            long.TryParse(source, out t);
            return t;
        }

        /// <summary>
        ///字符串转化为int 数值
        /// </summary>
        /// <param name="source">字符串</param>
        /// <returns>返回int类型的数值</returns>
        public static int ToInt(this string source)
        {
            int t;
            int.TryParse(source, out t);
            return t;
        }
        /// <summary>
        ///字符串转化为decimal 数值
        /// </summary>
        /// <param name="source">字符串</param>
        /// <returns>返回decimal类型的数值</returns>
        public static decimal ToDecimal(this string source)
        {
            decimal t;
            decimal.TryParse(source, out t);
            return t;
        }

        /// <summary>
        ///字符串转化为double 数值
        /// </summary>
        /// <param name="source">字符串</param>
        /// <returns>返回 double类型的数值</returns>
        public static double ToDouble(this string source)
        {
            double t;
            double.TryParse(source, out t);
            return t;
        }

        /// <summary>
        ///字符串转化为Guid值
        /// </summary>
        /// <param name="source">字符串</param>
        /// <returns>返回Guid类型的值</returns>
        public static Guid ToGuid(this string source)
        {
            Guid t;
            Guid.TryParse(source, out t);
            return t;
        }

        /// <summary>
        ///字符串转化为DateTime 数值
        /// </summary>
        /// <param name="source">字符串</param>
        /// <returns>返回DateTime类型的数值</returns>
        public static DateTime ToDateTime(this string source)
        {
            DateTime t;
            DateTime.TryParse(source, out t);
            return t;
        }
        /// <summary>
        /// 字符串转化为bool 值
        /// </summary>
        /// <param name="source"></param>
        /// <returns></returns>
        public static bool? ToBool(this string source)
        {
            if (string.IsNullOrWhiteSpace(source)) return default;
            string tem = source.ToLower();
            if (tem != "true" && tem != "false" && tem != "0" && tem != "1") return default;
            if (tem == "0") tem = "false";
            if (tem == "1") tem = "true";
            bool.TryParse(tem, out bool result);
            return result;
        }

        /// <summary>
		/// 将string类型转换成datetime类型
		/// </summary>
		/// <param name="s">目标字符串</param>
		/// <param name="defaultValue">默认值</param>
		/// <returns></returns>
		public static DateTime StringToDateTime(this string s, DateTime? defaultValue = null)
        {
            DateTime time;
            if (!string.IsNullOrWhiteSpace(s) && DateTime.TryParse(s, out time))
            {
                return time;
            }
            return defaultValue.HasValue ? defaultValue.Value : DateTime.MinValue;
        }
    }
}
