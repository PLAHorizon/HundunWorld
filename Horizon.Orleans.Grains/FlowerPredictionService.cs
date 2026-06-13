using Horizon.Game.Message.Network;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Horizon.Orleans.Grains
{
    /// <summary>
    /// 花卉价格预测服务 - 基于指数平滑的ARIMA近似原型
    /// </summary>
    public static class FlowerPredictionService
    {
        private const string ModelVersion = "arima-es-v1";
        private const double AlphaShortTerm = 0.3;
        private const double AlphaMediumTerm = 0.1;
        private const int FestivalEffectWindowDays = 7;

        /// <summary>
        /// 执行价格预测
        /// </summary>
        public static FlowerPriceForecast Predict(
            List<FlowerPriceSnapshot> history,
            ForecastTimeScale timeScale,
            int horizonDays,
            List<FestivalFactor> festivals,
            Dictionary<int, double> searchHotnessIndices = null)
        {
            var now = DateTime.Now;
            double alpha = timeScale == ForecastTimeScale.ShortTerm ? AlphaShortTerm : AlphaMediumTerm;

            if (history == null || history.Count == 0)
            {
                return new FlowerPriceForecast
                {
                    PredictedPrices = new List<PredictedPricePoint>(),
                    TimeScale = timeScale,
                    ModelVersion = ModelVersion,
                    Confidence = 0.0,
                    GeneratedAt = now
                };
            }

            var sortedHistory = history
                .OrderBy(s => s.SnapshotTime)
                .ToList();

            var prices = sortedHistory.Select(s => (double)s.AvgPrice).ToList();
            double smoothedValue = ExponentialSmoothing(prices, alpha);
            double volatility = CalculateVolatility(prices);
            double trend = EstimateTrend(prices);

            var predictedPrices = new List<PredictedPricePoint>();

            for (int i = 1; i <= horizonDays; i++)
            {
                var targetDate = now.AddDays(i).Date;
                double basePredicted = smoothedValue + trend * i;

                double festivalMultiplier = GetFestivalMultiplier(targetDate, festivals, (int)sortedHistory[0].SpeciesId);
                double searchMultiplier = GetSearchHotnessMultiplier(targetDate, searchHotnessIndices, (int)sortedHistory[0].SpeciesId);
                double predicted = basePredicted * festivalMultiplier * searchMultiplier;

                double intervalWidth = volatility * Math.Sqrt(i) * 1.96;
                double lower = predicted - intervalWidth;
                double upper = predicted + intervalWidth;

                if (lower < 0) lower = 0;

                predictedPrices.Add(new PredictedPricePoint
                {
                    Date = targetDate,
                    PredictedPrice = (decimal)Math.Round(predicted, 4),
                    LowerBound = (decimal)Math.Round(lower, 4),
                    UpperBound = (decimal)Math.Round(upper, 4)
                });
            }

            double confidence = CalculateConfidence(prices.Count, volatility, timeScale);

            return new FlowerPriceForecast
            {
                SpeciesId = sortedHistory[0].SpeciesId,
                MarketId = sortedHistory[0].MarketId,
                PredictedPrices = predictedPrices,
                TimeScale = timeScale,
                ModelVersion = ModelVersion,
                Confidence = Math.Round(confidence, 4),
                GeneratedAt = now
            };
        }

        private static double ExponentialSmoothing(List<double> prices, double alpha)
        {
            if (prices.Count == 0) return 0;
            if (prices.Count == 1) return prices[0];

            double smoothed = prices[0];
            for (int i = 1; i < prices.Count; i++)
            {
                smoothed = alpha * prices[i] + (1 - alpha) * smoothed;
            }
            return smoothed;
        }

        private static double CalculateVolatility(List<double> prices)
        {
            if (prices.Count < 2) return 0;

            var returns = new List<double>();
            for (int i = 1; i < prices.Count; i++)
            {
                if (prices[i - 1] != 0)
                    returns.Add((prices[i] - prices[i - 1]) / prices[i - 1]);
            }

            if (returns.Count == 0) return 0;

            double mean = returns.Average();
            double sumSquares = returns.Sum(r => (r - mean) * (r - mean));
            double stdDev = Math.Sqrt(sumSquares / returns.Count);

            double lastPrice = prices[prices.Count - 1];
            return stdDev * lastPrice;
        }

        private static double EstimateTrend(List<double> prices)
        {
            if (prices.Count < 3) return 0;

            int windowSize = Math.Min(prices.Count, Math.Max(7, prices.Count / 4));
            var recentPrices = prices.Skip(prices.Count - windowSize).ToList();

            double firstHalf = recentPrices.Take(windowSize / 2).Average();
            double secondHalf = recentPrices.Skip(windowSize / 2).Average();

            return (secondHalf - firstHalf) / (windowSize / 2.0);
        }

        private static double GetFestivalMultiplier(DateTime targetDate, List<FestivalFactor> festivals, int speciesId)
        {
            if (festivals == null || festivals.Count == 0) return 1.0;

            double maxMultiplier = 1.0;

            foreach (var festival in festivals)
            {
                if (festival.AffectedSpecies == null || !festival.AffectedSpecies.Contains(speciesId))
                    continue;

                int daysDiff = (targetDate - festival.FestivalDate).Days;

                if (daysDiff >= -FestivalEffectWindowDays && daysDiff <= 0)
                {
                    double rampUp = 1.0 + (festival.DemandMultiplier - 1.0) * ((FestivalEffectWindowDays + daysDiff) / (double)FestivalEffectWindowDays);
                    if (rampUp > maxMultiplier)
                        maxMultiplier = rampUp;
                }
                else if (daysDiff > 0 && daysDiff <= 3)
                {
                    double coolDown = 1.0 + (festival.DemandMultiplier - 1.0) * (1.0 - daysDiff / 3.0);
                    if (coolDown > maxMultiplier)
                        maxMultiplier = coolDown;
                }
            }

            return maxMultiplier;
        }

        private static double CalculateConfidence(int dataPoints, double volatility, ForecastTimeScale timeScale)
        {
            double dataConfidence = Math.Min(1.0, dataPoints / 90.0);
            double volatilityPenalty = Math.Min(0.3, volatility / 100.0);
            double scalePenalty = timeScale == ForecastTimeScale.MediumTerm ? 0.1 : 0.0;

            double confidence = dataConfidence - volatilityPenalty - scalePenalty;
            return Math.Max(0.1, Math.Min(0.95, confidence));
        }

        private static double GetSearchHotnessMultiplier(DateTime targetDate, Dictionary<int, double> searchHotnessIndices, int speciesId)
        {
            if (searchHotnessIndices == null || !searchHotnessIndices.TryGetValue(speciesId, out var index))
                return 1.0;

            if (index > 2.0) return 1.5;
            if (index > 1.5) return 1.2;
            if (index < 0.5) return 0.8;
            return 1.0;
        }
    }
}
