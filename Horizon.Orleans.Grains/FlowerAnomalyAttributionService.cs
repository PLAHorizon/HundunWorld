using Horizon.Game.Message.Network;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Horizon.Orleans.Grains
{
    public static class FlowerAnomalyAttributionService
    {
        private static readonly string[] SpeciesNames = { "", "红玫瑰", "百合", "康乃馨", "混合花束", "红绿搭配" };
        private static readonly string[] RegionNames = { "昆明", "斗南", "玉溪", "楚雄", "大理" };

        public static string GenerateAttribution(int speciesId, decimal priceChangePercent, Dictionary<string, object> context)
        {
            var speciesName = speciesId > 0 && speciesId < SpeciesNames.Length
                ? SpeciesNames[speciesId]
                : $"品种{speciesId}";

            var direction = priceChangePercent > 0 ? "上涨" : "下跌";
            var absChange = Math.Abs(priceChangePercent);

            var reasons = new List<string>();

            if (context != null)
            {
                if (context.TryGetValue("FestivalProximity", out var festivalObj) && festivalObj is string festivalName && !string.IsNullOrEmpty(festivalName))
                {
                    reasons.Add($"临近{festivalName}，{speciesName}需求预计显著增长");
                }

                if (context.TryGetValue("WeatherImpact", out var weatherObj) && weatherObj is string weatherDesc && !string.IsNullOrEmpty(weatherDesc))
                {
                    var region = context.TryGetValue("Region", out var r) && r is string rn ? rn : "产区";
                    reasons.Add($"近期{region}地区{weatherDesc}，可能影响{speciesName}产量");
                }

                if (context.TryGetValue("SupplyDemandStatus", out var sdObj) && sdObj is string sdStatus && !string.IsNullOrEmpty(sdStatus))
                {
                    var volumeChange = context.TryGetValue("VolumeChangePercent", out var vc) && vc is double vcd
                        ? $"{Math.Abs(vcd):F1}%"
                        : "显著";
                    reasons.Add($"当前{speciesName}供应{sdStatus}，市场成交量{(sdStatus == "紧张" ? "减少" : "增加")}{volumeChange}");
                }
            }

            if (reasons.Count == 0)
            {
                if (absChange > 20)
                    reasons.Add($"市场供需出现较大波动，{speciesName}价格剧烈调整");
                else
                    reasons.Add($"市场正常波动，{speciesName}价格{direction}属于合理区间");
            }

            return $"价格{direction}{absChange:F1}%，可能原因：{string.Join("；", reasons)}。";
        }

        public static Dictionary<string, object> BuildContext(List<FestivalFactor> festivals, int speciesId, string weatherDesc = null, string region = null)
        {
            var context = new Dictionary<string, object>();

            if (festivals != null)
            {
                var now = DateTime.Now;
                var upcoming = festivals
                    .Where(f => f.AffectedSpecies != null && f.AffectedSpecies.Contains(speciesId))
                    .Where(f => (f.FestivalDate - now).TotalDays > 0 && (f.FestivalDate - now).TotalDays <= 14)
                    .OrderBy(f => f.FestivalDate)
                    .FirstOrDefault();

                if (upcoming != null)
                    context["FestivalProximity"] = upcoming.FestivalName;
            }

            if (!string.IsNullOrEmpty(weatherDesc))
                context["WeatherImpact"] = weatherDesc;

            if (!string.IsNullOrEmpty(region))
                context["Region"] = region;

            return context;
        }
    }
}
