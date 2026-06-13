using Horizon.WebAdmin.Core;
using Horizon.WebAdmin.Modules.Flower.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Horizon.WebAdmin.Modules.Flower;

public class FlowerModule : AdminModuleBase
{
    public override string ModuleId => "flower";
    public override string ModuleName => "花卉产业";
    public override string Icon => "flower";
    public override string RoutePrefix => "/flower";

    public override List<ModuleMenuItem> MenuItems { get; } =
    [
        new()
        {
            Name = "数据总览", Icon = "dashboard",
            Children =
            [
                new() { Name = "Dashboard", Route = "/flower/dashboard" }
            ]
        },
        new()
        {
            Name = "交易管理", Icon = "swap",
            Children =
            [
                new() { Name = "订单管理", Route = "/flower/orders" },
                new() { Name = "退款管理", Route = "/flower/refunds" },
                new() { Name = "投诉管理", Route = "/flower/complaints" },
                new() { Name = "支付对账", Route = "/flower/payments" },
                new() { Name = "结算管理", Route = "/flower/settlements" },
                new() { Name = "店铺账单", Route = "/flower/billing" },
                new() { Name = "对账管理", Route = "/flower/reconciliation" }
            ]
        },
        new()
        {
            Name = "商品管理", Icon = "appstore",
            Children =
            [
                new() { Name = "商品管理", Route = "/flower/products" },
                new() { Name = "商品分类", Route = "/flower/product-categories" },
                new() { Name = "运费模板", Route = "/flower/freight-templates" },
                new() { Name = "商品评价", Route = "/flower/product-comments" }
            ]
        },
        new()
        {
            Name = "商户管理", Icon = "shop",
            Children =
            [
                new() { Name = "商户管理", Route = "/flower/merchants" },
                new() { Name = "店铺等级", Route = "/flower/shop-grades" },
                new() { Name = "品牌管理", Route = "/flower/brands" },
                new() { Name = "经营分类", Route = "/flower/business-categories" },
                new() { Name = "保证金管理", Route = "/flower/cash-deposits" }
            ]
        },
        new()
        {
            Name = "营销管理", Icon = "gift",
            Children =
            [
                new() { Name = "优惠券管理", Route = "/flower/coupons" },
                new() { Name = "满减规则", Route = "/flower/full-discounts" }
            ]
        },
        new()
        {
            Name = "预报与AI", Icon = "robot",
            Children =
            [
                new() { Name = "品种管理", Route = "/flower/species" },
                new() { Name = "行情数据", Route = "/flower/market-data" },
                new() { Name = "预测模型", Route = "/flower/forecast-models" },
                new() { Name = "预警规则", Route = "/flower/alert-rules" },
                new() { Name = "AI报告", Route = "/flower/reports" },
                new() { Name = "订阅管理", Route = "/flower/subscriptions" }
            ]
        },
        new()
        {
            Name = "种植与IoT", Icon = "cloud-server",
            Children =
            [
                new() { Name = "种植管理", Route = "/flower/planting" },
                new() { Name = "IoT设备", Route = "/flower/iot-devices" },
                new() { Name = "物流管理", Route = "/flower/logistics" },
                new() { Name = "收货地址", Route = "/flower/addresses" },
                new() { Name = "交易评价", Route = "/flower/trade-comments" }
            ]
        },
        new()
        {
            Name = "系统设置", Icon = "setting",
            Children =
            [
                new() { Name = "ApiKey管理", Route = "/flower/api-keys" },
                new() { Name = "开放API", Route = "/flower/open-api" },
                new() { Name = "结算配置", Route = "/flower/settled-config" },
                new() { Name = "DataPool", Route = "/flower/data-pool" },
                new() { Name = "知识文档", Route = "/flower/knowledge" }
            ]
        }
    ];

    public override void RegisterServices(IServiceCollection services)
    {
        var serviceTypes = new[]
        {
            typeof(FlowerAIService),
            typeof(FlowerAddressService),
            typeof(FlowerAdminService),
            typeof(FlowerAlertService),
            typeof(FlowerApiKeyService),
            typeof(FlowerBillingService),
            typeof(FlowerBrandService),
            typeof(FlowerBusinessCategoryService),
            typeof(FlowerCashDepositService),
            typeof(FlowerCategoryService),
            typeof(FlowerCommentService),
            typeof(FlowerComplaintService),
            typeof(FlowerCouponService),
            typeof(FlowerDashboardService),
            typeof(FlowerDataPoolService),
            typeof(FlowerForecastService),
            typeof(FlowerFreightService),
            typeof(FlowerFullDiscountService),
            typeof(FlowerIoTService),
            typeof(FlowerLogisticsService),
            typeof(FlowerMarketService),
            typeof(FlowerMerchantService),
            typeof(FlowerOpenApiService),
            typeof(FlowerOrderService),
            typeof(FlowerPaymentService),
            typeof(FlowerPlantingService),
            typeof(FlowerProductService),
            typeof(FlowerReconciliationService),
            typeof(FlowerReportService),
            typeof(FlowerSettledConfigService),
            typeof(FlowerSettlementService),
            typeof(FlowerShopGradeService),
            typeof(FlowerSpeciesService),
            typeof(FlowerSubscriptionService),
            typeof(FlowerTradeCommentService)
        };

        foreach (var type in serviceTypes)
        {
            services.AddScoped(type, sp =>
            {
                var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient("WebApi");
                var configuration = sp.GetRequiredService<IConfiguration>();
                return ActivatorUtilities.CreateInstance(sp, type, httpClient, configuration);
            });
        }
    }
}
