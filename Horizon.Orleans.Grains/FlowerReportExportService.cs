using Horizon.Core.Abstract;
using Horizon.Entities;
using Horizon.Game.Message.Network;
using Horizon.Model.Flower;
using Microsoft.Extensions.Logging;
using MiniExcelLibs;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Horizon.Orleans.Grains
{
    public class FlowerReportExportService
    {
        private readonly ILogger<FlowerReportExportService> _logger;
        private readonly IDataContext<FlowerEntityContext, FlowerDailyPriceStats, long> _statsContext;
        private readonly IDataContext<FlowerEntityContext, FlowerOrder, long> _orderContext;
        private readonly IDataContext<FlowerEntityContext, FlowerAlertLog, long> _alertContext;

        public FlowerReportExportService(
            ILogger<FlowerReportExportService> logger,
            IDataContext<FlowerEntityContext, FlowerDailyPriceStats, long> statsContext,
            IDataContext<FlowerEntityContext, FlowerOrder, long> orderContext,
            IDataContext<FlowerEntityContext, FlowerAlertLog, long> alertContext)
        {
            _logger = logger;
            _statsContext = statsContext;
            _orderContext = orderContext;
            _alertContext = alertContext;
        }

        public async Task<byte[]> ExportPriceReportToExcelAsync(DateTime startDate, DateTime endDate)
        {
            try
            {
                var stats = await _statsContext.QueryAsync(
                    s => s.StatDate >= startDate.Date && s.StatDate <= endDate.Date);
                var statsList = stats.OrderBy(s => s.StatDate).ThenBy(s => s.SpeciesId).ToList();

                using var stream = new MemoryStream();
                MiniExcel.SaveAs(stream, statsList.Select(s => new
                {
                    日期 = s.StatDate.ToString("yyyy-MM-dd"),
                    品种ID = s.SpeciesId,
                    市场ID = s.MarketId,
                    均价 = s.AvgPrice,
                    最低价 = s.MinPrice,
                    最高价 = s.MaxPrice,
                    总成交量 = s.TotalVolume,
                    总交易笔数 = s.TotalTradeCount,
                    价格标准差 = s.PriceStdDev
                }));

                _logger.LogInformation("导出价格报表Excel: {Start}~{End}, {Count}条记录",
                    startDate.ToString("yyyy-MM-dd"), endDate.ToString("yyyy-MM-dd"), statsList.Count);
                return stream.ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "导出价格报表Excel失败");
                throw;
            }
        }

        public async Task<byte[]> ExportOrderReportToExcelAsync(DateTime startDate, DateTime endDate)
        {
            try
            {
                var orders = await _orderContext.QueryAsync(
                    o => o.CreateTime >= startDate.Date && o.CreateTime < endDate.Date.AddDays(1));
                var orderList = orders.OrderBy(o => o.CreateTime).ToList();

                using var stream = new MemoryStream();
                MiniExcel.SaveAs(stream, orderList.Select(o => new
                {
                    订单号 = o.OrderNo,
                    买家ID = o.BuyerId,
                    商户ID = o.MerchantId,
                    金额 = o.TotalAmount,
                    状态 = GetOrderStatusText(o.Status),
                    是否预售 = o.IsPresale ? "是" : "否",
                    创建时间 = o.CreateTime.ToString("yyyy-MM-dd HH:mm:ss")
                }));

                _logger.LogInformation("导出订单报表Excel: {Start}~{End}, {Count}条记录",
                    startDate.ToString("yyyy-MM-dd"), endDate.ToString("yyyy-MM-dd"), orderList.Count);
                return stream.ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "导出订单报表Excel失败");
                throw;
            }
        }

        public async Task<byte[]> ExportAlertReportToExcelAsync(DateTime startDate, DateTime endDate)
        {
            try
            {
                var alerts = await _alertContext.QueryAsync(
                    a => a.CreatedAt >= startDate.Date && a.CreatedAt < endDate.Date.AddDays(1));
                var alertList = alerts.OrderBy(a => a.CreatedAt).ToList();

                using var stream = new MemoryStream();
                MiniExcel.SaveAs(stream, alertList.Select(a => new
                {
                    规则ID = a.RuleId,
                    品种ID = a.SpeciesId,
                    市场ID = a.MarketId,
                    预警类型 = GetAlertTypeText(a.AlertType),
                    预警消息 = a.AlertMessage,
                    触发值 = a.TriggeredValue,
                    阈值 = a.ThresholdValue,
                    是否已读 = a.IsRead ? "是" : "否",
                    创建时间 = a.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss")
                }));

                _logger.LogInformation("导出预警报表Excel: {Start}~{End}, {Count}条记录",
                    startDate.ToString("yyyy-MM-dd"), endDate.ToString("yyyy-MM-dd"), alertList.Count);
                return stream.ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "导出预警报表Excel失败");
                throw;
            }
        }

        public async Task<string> GenerateMarkdownDailyReportAsync(DateTime reportDate)
        {
            try
            {
                var stats = await _statsContext.QueryAsync(
                    s => s.StatDate == reportDate.Date);
                var statsList = stats.ToList();

                var orders = await _orderContext.QueryAsync(
                    o => o.CreateTime >= reportDate.Date && o.CreateTime < reportDate.Date.AddDays(1));
                var orderList = orders.ToList();

                var alerts = await _alertContext.QueryAsync(
                    a => a.CreatedAt >= reportDate.Date && a.CreatedAt < reportDate.Date.AddDays(1));
                var alertList = alerts.ToList();

                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"# 花卉市场日报 - {reportDate:yyyy年MM月dd日}");
                sb.AppendLine();
                sb.AppendLine("## 一、市场概览");
                sb.AppendLine();
                sb.AppendLine($"- 总成交额：¥{orderList.Where(o => o.Status >= 1).Sum(o => o.TotalAmount):F2}");
                sb.AppendLine($"- 总订单数：{orderList.Count}");
                sb.AppendLine($"- 已完成订单：{orderList.Count(o => o.Status == (int)OrderStatus.Completed)}");
                sb.AppendLine($"- 预警次数：{alertList.Count}");
                sb.AppendLine();

                sb.AppendLine("## 二、品种行情");
                sb.AppendLine();
                sb.AppendLine("| 品种ID | 均价 | 最低价 | 最高价 | 成交量 | 交易笔数 |");
                sb.AppendLine("|--------|------|--------|--------|--------|----------|");
                foreach (var s in statsList.OrderBy(s => s.SpeciesId))
                {
                    sb.AppendLine($"| {s.SpeciesId} | ¥{s.AvgPrice:F2} | ¥{s.MinPrice:F2} | ¥{s.MaxPrice:F2} | {s.TotalVolume} | {s.TotalTradeCount} |");
                }
                sb.AppendLine();

                if (statsList.Count > 0)
                {
                    var topGainer = statsList.OrderByDescending(s => s.AvgPrice).First();
                    var topVolume = statsList.OrderByDescending(s => s.TotalVolume).First();
                    sb.AppendLine("## 三、涨跌榜");
                    sb.AppendLine();
                    sb.AppendLine($"- 最高均价品种：品种{topGainer.SpeciesId}（¥{topGainer.AvgPrice:F2}）");
                    sb.AppendLine($"- 最大成交量品种：品种{topVolume.SpeciesId}（{topVolume.TotalVolume}枝）");
                    sb.AppendLine();
                }

                if (alertList.Count > 0)
                {
                    sb.AppendLine("## 四、预警摘要");
                    sb.AppendLine();
                    foreach (var alert in alertList.Take(10))
                    {
                        sb.AppendLine($"- {alert.AlertMessage}");
                    }
                    sb.AppendLine();
                }

                sb.AppendLine("---");
                sb.AppendLine($"*报告生成时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss} UTC*");

                _logger.LogInformation("生成Markdown日报: {Date}", reportDate.ToString("yyyy-MM-dd"));
                return sb.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "生成Markdown日报失败: {Date}", reportDate.ToString("yyyy-MM-dd"));
                throw;
            }
        }

        private static string GetOrderStatusText(int status)
        {
            return status switch
            {
                0 => "待支付",
                1 => "已支付",
                2 => "已发货",
                3 => "已签收",
                4 => "已完成",
                5 => "已取消",
                6 => "退款中",
                _ => $"未知({status})"
            };
        }

        private static string GetAlertTypeText(int alertType)
        {
            return alertType switch
            {
                0 => "价格高于阈值",
                1 => "价格低于阈值",
                2 => "涨幅超阈值",
                3 => "跌幅超阈值",
                _ => $"未知({alertType})"
            };
        }
    }
}
