using Horizon.Game.Message.Network;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Horizon.Orleans.Grains
{
    /// <summary>
    /// 花卉节日日历 - 提供节日需求数据
    /// </summary>
    public static class FlowerFestivalCalendar
    {
        private static readonly int[] RedRoseSpecies = { 1 };
        private static readonly int[] LilySpecies = { 2 };
        private static readonly int[] CarnationSpecies = { 3 };
        private static readonly int[] MixedBouquetSpecies = { 4 };
        private static readonly int[] RedGreenArrangementSpecies = { 5 };

        /// <summary>
        /// 获取当前和下一年的节日因子列表
        /// </summary>
        public static List<FestivalFactor> GetFestivals()
        {
            var now = DateTime.Now;
            var festivals = new List<FestivalFactor>();

            for (int yearOffset = 0; yearOffset <= 1; yearOffset++)
            {
                int year = now.Year + yearOffset;
                festivals.AddRange(GetFestivalsForYear(year));
            }

            return festivals;
        }

        private static List<FestivalFactor> GetFestivalsForYear(int year)
        {
            var festivals = new List<FestivalFactor>();

            var springFestivalDate = GetLunarDate(year, 1, 1);
            festivals.Add(new FestivalFactor
            {
                FestivalName = "春节",
                FestivalDate = springFestivalDate,
                DemandMultiplier = 2.5,
                AffectedSpecies = RedRoseSpecies.Concat(LilySpecies).ToList()
            });

            festivals.Add(new FestivalFactor
            {
                FestivalName = "情人节",
                FestivalDate = new DateTime(year, 2, 14),
                DemandMultiplier = 3.0,
                AffectedSpecies = RedRoseSpecies.ToList()
            });

            festivals.Add(new FestivalFactor
            {
                FestivalName = "妇女节",
                FestivalDate = new DateTime(year, 3, 8),
                DemandMultiplier = 1.8,
                AffectedSpecies = MixedBouquetSpecies.ToList()
            });

            festivals.Add(new FestivalFactor
            {
                FestivalName = "母亲节",
                FestivalDate = GetNthDayOfWeek(year, 5, DayOfWeek.Sunday, 2),
                DemandMultiplier = 2.5,
                AffectedSpecies = CarnationSpecies.Concat(LilySpecies).ToList()
            });

            var qixiDate = GetLunarDate(year, 7, 7);
            festivals.Add(new FestivalFactor
            {
                FestivalName = "七夕节",
                FestivalDate = qixiDate,
                DemandMultiplier = 2.8,
                AffectedSpecies = RedRoseSpecies.ToList()
            });

            festivals.Add(new FestivalFactor
            {
                FestivalName = "教师节",
                FestivalDate = new DateTime(year, 9, 10),
                DemandMultiplier = 1.5,
                AffectedSpecies = CarnationSpecies.ToList()
            });

            festivals.Add(new FestivalFactor
            {
                FestivalName = "圣诞节",
                FestivalDate = new DateTime(year, 12, 25),
                DemandMultiplier = 1.8,
                AffectedSpecies = RedGreenArrangementSpecies.ToList()
            });

            return festivals;
        }

        private static DateTime GetLunarDate(int solarYear, int lunarMonth, int lunarDay)
        {
            try
            {
                var cal = new ChineseLunisolarCalendar();
                int minYear = cal.MinSupportedDateTime.Year;
                int maxYear = cal.MaxSupportedDateTime.Year;

                if (solarYear < minYear || solarYear > maxYear)
                    return solarYear <= minYear
                        ? new DateTime(minYear, 1, 1)
                        : new DateTime(maxYear, 12, 31);

                int leapMonth = cal.GetLeapMonth(solarYear);
                int actualMonth = leapMonth > 0 && lunarMonth >= leapMonth
                    ? lunarMonth + 1
                    : lunarMonth;

                return cal.ToDateTime(solarYear, actualMonth, lunarDay, 0, 0, 0, 0);
            }
            catch
            {
                return new DateTime(solarYear, lunarMonth, lunarDay);
            }
        }

        private static DateTime GetNthDayOfWeek(int year, int month, DayOfWeek dayOfWeek, int n)
        {
            var first = new DateTime(year, month, 1);
            int offset = (dayOfWeek - first.DayOfWeek + 7) % 7;
            return first.AddDays(offset + (n - 1) * 7);
        }
    }
}
