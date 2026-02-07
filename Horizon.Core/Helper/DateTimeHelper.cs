using System;
using System.Text;

namespace Horizon.Core.Helper
{
    public static class DateTimeHelper
    {
        public static DateTime GetStartDayOfWeeks(int year, int month, int index)
        {
            DateTime minValue;
            if (year < 1600 || year > 9999)
            {
                minValue = DateTime.MinValue;
            }
            else if (month < 0 || month > 12)
            {
                minValue = DateTime.MinValue;
            }
            else if (index >= 1)
            {
                DateTime dateTime = new DateTime(year, month, 1);
                int num = 7;
                if (Convert.ToInt32(dateTime.DayOfWeek.ToString("d")) > 0)
                {
                    num = Convert.ToInt32(dateTime.DayOfWeek.ToString("d"));
                }
                DateTime dateTime1 = dateTime.AddDays(1 - num);
                DateTime dateTime2 = dateTime1.AddDays(index * 7);
                minValue = ((dateTime2 - dateTime.AddMonths(1)).Days <= 0 ? dateTime2 : DateTime.MinValue);
            }
            else
            {
                minValue = DateTime.MinValue;
            }
            return minValue;
        }

        public static string GetWeekSpanOfMonth(int year, int month)
        {

            if (year < 1600 || year > 9999)
            {
                return "";
            }
            if (month < 0 || month > 12)
            {
                return "";
            }
            StringBuilder builder = new StringBuilder();
            for (int i = 1; i < 5; i++)
            {
                DateTime time = new DateTime(year, month, 1);
                int num = 7;
                if (Convert.ToInt32(time.DayOfWeek.ToString("d")) > 0)
                {
                    num = Convert.ToInt32(time.DayOfWeek.ToString("d"));
                }
                DateTime time2 = time.AddDays(1 - num).AddDays(i * 7);
                TimeSpan span = time2 - time.AddMonths(1);
                if (span.Days > 0)
                {
                    return "";
                }
                builder.Append(time2.ToString("yyyy-MM-dd"));
                builder.Append(" ~ ");
                builder.Append(time2.AddDays(6.0).ToString("yyyy-MM-dd"));
                builder.Append(Environment.NewLine);
            }
            return builder.ToString();

        }        /// <summary>
                 /// 日期转换为unix时间戳
                 /// </summary>
                 /// <param name="dateTime"></param>
                 /// <returns></returns>
        public static long ToUnix(this DateTime dateTime)
        {
            var start = new DateTime(1970, 1, 1, 0, 0, 0, dateTime.Kind);
            return Convert.ToInt64((dateTime - start).TotalSeconds);
        }

        /// <summary>
        /// unix时间戳转换为日期
        /// </summary>
        /// <param name="unixTimeStamp">时间戳(秒)</param>
        /// <returns></returns>
        public static DateTime UnixToDateTime(this DateTime target, long timestamp)
        {
            var start = new DateTime(1970, 1, 1, 0, 0, 0, target.Kind);
            return start.AddSeconds(timestamp);
        }        /// <summary>
                 /// 设置加轮巡回天数
                 /// </summary>
                 /// <param name="cycleDay">轮巡天数</param>
                 /// <returns>新的时间</returns>
        public static DateTime SetCycledDate(this DateTime date, int cycleDay)
        {
            int year = date.Year;
            int month = date.Month;
            int day = date.Day + cycleDay;
            if (month == 1 || month == 3 || month == 5 || month == 7 || month == 8 || month == 10 || month == 12)
            {
                if (day > 31)
                {
                    day = day - 31;
                    month++;
                    if (month > 12)
                    {
                        month = 1;
                        year++;
                    }
                }
            }
            if (month == 2)
            {
                if (day > 28)
                {
                    day = day - 28;
                    month++;
                }
            }
            else
            {
                if (day > 31)
                {
                    day = day - 31;
                    month++;
                    if (month > 12)
                    {
                        month = 1;
                        year++;
                    }
                }
            }
            return new DateTime(year, month, day);
        }
        /// <summary>
        /// 返回中文的当前日期 2000年01月01日 字符串式
        /// </summary>
        public static string GetChineseDate(this DateTime time)
        {
            return time.ToString("yyyy年MM月dd日");
        }
        /// <summary>
        /// 获得当前日期 2000-01-01
        /// </summary>
        public static string GetDate(this DateTime time)
        {
            return time.ToString("yyyy-MM-dd");
        }
        /// <summary>
        /// 获得当前时间(不包括日期部分) 12:00:00
        /// </summary>
        public static string GetTime()
        {
            return DateTime.Now.ToString("HH:mm:ss");
        }
        /// <summary>
        /// 获得当前时间"yyyy-MM-dd HH:mm:ss"格式字符串
        /// </summary>
        public static string GetDateTime(this DateTime time)
        {
            return time.ToString("yyyy-MM-dd HH:mm:ss");
        }

        /// <summary>
        /// 获得当前时间"yyyy-MM-dd HH:mm:ss:fffffff"格式字符串
        /// </summary>
        public static string GetDateTimeMS(this DateTime time)
        {
            return time.ToString("yyyy-MM-dd HH:mm:ss:fffffff");
        }

        /// <summary>
        /// 获得当前时间"yyyy年MM月dd日 HH:mm:ss"格式字符串
        /// </summary>
        public static string GetDateTimeU(this DateTime time)
        {
            return string.Format("{0:U}", time);
        }
    }
}