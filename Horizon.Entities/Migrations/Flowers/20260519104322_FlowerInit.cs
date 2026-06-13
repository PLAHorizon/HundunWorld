using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Horizon.Entities.Migrations.Flowers
{
    /// <inheritdoc />
    public partial class FlowerInit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Flower_AlertLog",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    RuleId = table.Column<long>(type: "bigint", nullable: false, comment: "规则ID"),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, comment: "用户ID"),
                    SpeciesId = table.Column<long>(type: "bigint", nullable: false, comment: "品类ID"),
                    MarketId = table.Column<long>(type: "bigint", nullable: false, comment: "市场ID"),
                    AlertType = table.Column<int>(type: "int", nullable: false, comment: "预警类型"),
                    AlertMessage = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false, comment: "预警消息"),
                    TriggeredValue = table.Column<decimal>(type: "decimal(18,4)", nullable: false, comment: "触发值"),
                    ThresholdValue = table.Column<decimal>(type: "decimal(18,4)", nullable: false, comment: "阈值"),
                    IsRead = table.Column<bool>(type: "bit", nullable: false, comment: "是否已读"),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, comment: "创建时间")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Flower_AlertLog", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Flower_AlertRule",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Passport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    CreateTime = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    ModifyPassport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true),
                    ModifyTime = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, comment: "用户ID"),
                    SpeciesId = table.Column<long>(type: "bigint", nullable: false, comment: "品类ID"),
                    MarketId = table.Column<long>(type: "bigint", nullable: false, comment: "市场ID"),
                    ConditionType = table.Column<int>(type: "int", nullable: false, comment: "条件类型"),
                    ThresholdValue = table.Column<decimal>(type: "decimal(18,4)", nullable: false, comment: "阈值"),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false, comment: "是否启用"),
                    LastTriggeredAt = table.Column<DateTime>(type: "datetime2", nullable: true, comment: "上次触发时间"),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, comment: "是否已删除，true : 已删除，false : 未删除")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Flower_AlertRule", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Flower_ApiKey",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Passport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    CreateTime = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    ModifyPassport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true),
                    ModifyTime = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    ApiKey = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false, comment: "API Key"),
                    Name = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, comment: "密钥名称"),
                    OwnerPassportId = table.Column<long>(type: "bigint", nullable: false, comment: "所属用户PassportId"),
                    Plan = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false, comment: "套餐类型"),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false, comment: "是否启用"),
                    TotalCallCount = table.Column<long>(type: "bigint", nullable: false, comment: "总调用次数"),
                    LastCallTime = table.Column<DateTime>(type: "datetime2", nullable: true, comment: "最后调用时间"),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: true, comment: "过期时间"),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, comment: "是否已删除")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Flower_ApiKey", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Flower_Brand",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Passport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    CreateTime = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    ModifyPassport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true),
                    ModifyTime = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    Name = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, comment: "品牌名称"),
                    Logo = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false, comment: "品牌Logo"),
                    Description = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false, comment: "品牌描述"),
                    DisplaySequence = table.Column<long>(type: "bigint", nullable: false, comment: "排序"),
                    IsRecommend = table.Column<bool>(type: "bit", nullable: false, comment: "是否推荐"),
                    AuditStatus = table.Column<int>(type: "int", nullable: false, comment: "审核状态"),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Flower_Brand", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Flower_BusinessCategory",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Passport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    CreateTime = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    ModifyPassport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true),
                    ModifyTime = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    ShopId = table.Column<long>(type: "bigint", nullable: false, comment: "店铺ID"),
                    CategoryId = table.Column<long>(type: "bigint", nullable: false, comment: "类目ID"),
                    CommissionRate = table.Column<decimal>(type: "decimal(18,4)", nullable: false, comment: "佣金率"),
                    AuditStatus = table.Column<int>(type: "int", nullable: false, comment: "审核状态0=待审核1=已通过2=已拒绝"),
                    AuditRemark = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false, comment: "审核备注")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Flower_BusinessCategory", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Flower_CashDeposit",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Passport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    CreateTime = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    ModifyPassport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true),
                    ModifyTime = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    ShopId = table.Column<long>(type: "bigint", nullable: false, comment: "店铺ID"),
                    CategoryId = table.Column<long>(type: "bigint", nullable: false, comment: "类目ID"),
                    Amount = table.Column<decimal>(type: "decimal(18,4)", nullable: false, comment: "保证金金额"),
                    Status = table.Column<int>(type: "int", nullable: false, comment: "状态0=待缴纳1=已缴纳2=已扣罚3=已退还"),
                    PaidAt = table.Column<DateTime>(type: "datetime2", nullable: true, comment: "缴纳时间"),
                    DeductedAt = table.Column<DateTime>(type: "datetime2", nullable: true, comment: "扣罚时间"),
                    NoReasonReturn = table.Column<bool>(type: "bit", nullable: false, comment: "七天无理由退换标识")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Flower_CashDeposit", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Flower_ChatHistory",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Passport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    CreateTime = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    ModifyPassport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true),
                    ModifyTime = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, comment: "用户ID"),
                    ConversationId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, comment: "会话ID"),
                    Role = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false, comment: "角色"),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: false, comment: "内容"),
                    ModelVersion = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false, comment: "模型版本")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Flower_ChatHistory", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Flower_CostRecord",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Passport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    CreateTime = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    ModifyPassport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true),
                    ModifyTime = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    BatchId = table.Column<long>(type: "bigint", nullable: false, comment: "关联批次ID"),
                    Category = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false, comment: "成本分类(Seedling/Fertilizer/Pesticide/Labor/Utility/Depreciation/Other)"),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false, comment: "金额"),
                    CostDate = table.Column<DateTime>(type: "datetime2", nullable: false, comment: "日期"),
                    Remark = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false, comment: "备注"),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, comment: "是否软删除")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Flower_CostRecord", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Flower_Coupon",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Passport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    CreateTime = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    ModifyPassport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true),
                    ModifyTime = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    ShopId = table.Column<long>(type: "bigint", nullable: false, comment: "店铺ID，0=平台券"),
                    CouponName = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, comment: "优惠券名称"),
                    CouponType = table.Column<int>(type: "int", nullable: false, comment: "优惠券类型0=满减券1=折扣券"),
                    Denomination = table.Column<decimal>(type: "decimal(18,4)", nullable: false, comment: "面额/折扣率"),
                    UseCondition = table.Column<decimal>(type: "decimal(18,4)", nullable: false, comment: "使用条件满X元"),
                    StartDate = table.Column<DateTime>(type: "datetime2", nullable: false, comment: "开始日期"),
                    EndDate = table.Column<DateTime>(type: "datetime2", nullable: false, comment: "结束日期"),
                    TotalCount = table.Column<int>(type: "int", nullable: false, comment: "发放总数"),
                    ReceivedCount = table.Column<int>(type: "int", nullable: false, comment: "已领取数"),
                    UsedCount = table.Column<int>(type: "int", nullable: false, comment: "已使用数"),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, comment: "是否启用"),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Flower_Coupon", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Flower_CouponRecord",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Passport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    CreateTime = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    ModifyPassport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true),
                    ModifyTime = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    CouponId = table.Column<long>(type: "bigint", nullable: false, comment: "优惠券ID"),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, comment: "用户ID"),
                    Status = table.Column<int>(type: "int", nullable: false, comment: "状态0=未使用1=已使用2=已过期"),
                    UsedOrderId = table.Column<long>(type: "bigint", nullable: true, comment: "使用的订单ID"),
                    ReceivedAt = table.Column<DateTime>(type: "datetime2", nullable: false, comment: "领取时间"),
                    UsedAt = table.Column<DateTime>(type: "datetime2", nullable: true, comment: "使用时间")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Flower_CouponRecord", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Flower_DailyPriceStats",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    SpeciesId = table.Column<long>(type: "bigint", nullable: false, comment: "品类ID"),
                    MarketId = table.Column<long>(type: "bigint", nullable: false, comment: "市场ID"),
                    StatDate = table.Column<DateTime>(type: "datetime2", nullable: false, comment: "统计日期"),
                    OpenPrice = table.Column<decimal>(type: "decimal(18,4)", nullable: false, comment: "开盘价"),
                    ClosePrice = table.Column<decimal>(type: "decimal(18,4)", nullable: false, comment: "收盘价"),
                    HighPrice = table.Column<decimal>(type: "decimal(18,4)", nullable: false, comment: "最高价"),
                    LowPrice = table.Column<decimal>(type: "decimal(18,4)", nullable: false, comment: "最低价"),
                    AvgPrice = table.Column<decimal>(type: "decimal(18,4)", nullable: false, comment: "均价"),
                    TotalVolume = table.Column<int>(type: "int", nullable: false, comment: "总成交量"),
                    TotalTradeCount = table.Column<int>(type: "int", nullable: false, comment: "总成交笔数"),
                    PriceChange = table.Column<decimal>(type: "decimal(18,4)", nullable: false, comment: "涨跌额"),
                    PriceChangePercent = table.Column<decimal>(type: "decimal(18,4)", nullable: false, comment: "涨跌幅"),
                    MinPrice = table.Column<decimal>(type: "decimal(18,4)", nullable: false, comment: "最低成交价"),
                    MaxPrice = table.Column<decimal>(type: "decimal(18,4)", nullable: false, comment: "最高成交价"),
                    PriceStdDev = table.Column<decimal>(type: "decimal(18,4)", nullable: true, comment: "价格标准差")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Flower_DailyPriceStats", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Flower_DataPool",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Passport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    CreateTime = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    ModifyPassport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true),
                    ModifyTime = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    DataType = table.Column<int>(type: "int", nullable: false, comment: "数据类型"),
                    DataSource = table.Column<int>(type: "int", nullable: false, comment: "数据来源"),
                    RawPayload = table.Column<string>(type: "nvarchar(max)", nullable: false, comment: "原始数据"),
                    Timestamp = table.Column<DateTime>(type: "datetime2", nullable: false, comment: "时间戳"),
                    RelatedEntityId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false, comment: "关联实体ID"),
                    ModelVersion = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false, comment: "模型版本"),
                    Confidence = table.Column<double>(type: "float", nullable: true, comment: "置信度")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Flower_DataPool", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Flower_DeviceGroup",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Passport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    CreateTime = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    ModifyPassport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true),
                    ModifyTime = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    GroupName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, comment: "分组名称"),
                    Description = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false, comment: "分组描述"),
                    GreenhouseId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, comment: "温室ID"),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, comment: "是否软删除")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Flower_DeviceGroup", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Flower_Document",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Passport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    CreateTime = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    ModifyPassport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true),
                    ModifyTime = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false, comment: "标题"),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: false, comment: "内容"),
                    Source = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, comment: "来源"),
                    IsIndexed = table.Column<bool>(type: "bit", nullable: false, comment: "是否已索引"),
                    IndexedAt = table.Column<DateTime>(type: "datetime2", nullable: true, comment: "索引时间"),
                    ChunkCount = table.Column<int>(type: "int", nullable: false, comment: "分块数量"),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, comment: "是否已删除")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Flower_Document", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Flower_DocumentChunk",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    DocumentId = table.Column<long>(type: "bigint", nullable: false),
                    ChunkIndex = table.Column<int>(type: "int", nullable: false),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TokenCount = table.Column<int>(type: "int", nullable: false),
                    IsIndexed = table.Column<bool>(type: "bit", nullable: false),
                    EmbeddingVector = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Flower_DocumentChunk", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Flower_FreightTemplate",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Passport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    CreateTime = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    ModifyPassport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true),
                    ModifyTime = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    MerchantId = table.Column<long>(type: "bigint", nullable: false, comment: "商户ID"),
                    Name = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, comment: "模板名称"),
                    ValuationMethod = table.Column<int>(type: "int", nullable: false, comment: "计价方式: 0=按件数, 1=按重量, 2=按体积"),
                    IsFree = table.Column<bool>(type: "bit", nullable: false, comment: "是否包邮"),
                    FirstUnit = table.Column<decimal>(type: "decimal(18,4)", nullable: false, comment: "首件/首重/首体积"),
                    FirstPrice = table.Column<decimal>(type: "decimal(18,4)", nullable: false, comment: "首费"),
                    ContinueUnit = table.Column<decimal>(type: "decimal(18,4)", nullable: false, comment: "续件/续重/续体积"),
                    ContinuePrice = table.Column<decimal>(type: "decimal(18,4)", nullable: false, comment: "续费"),
                    FreeConditionAmount = table.Column<decimal>(type: "decimal(18,4)", nullable: true, comment: "包邮条件金额"),
                    AreaRules = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false, comment: "地区规则JSON"),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Flower_FreightTemplate", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Flower_FullDiscountRule",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Passport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    CreateTime = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    ModifyPassport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true),
                    ModifyTime = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    ShopId = table.Column<long>(type: "bigint", nullable: false, comment: "店铺ID"),
                    RuleName = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, comment: "规则名称"),
                    StartDate = table.Column<DateTime>(type: "datetime2", nullable: false, comment: "开始日期"),
                    EndDate = table.Column<DateTime>(type: "datetime2", nullable: false, comment: "结束日期"),
                    LimitValue = table.Column<decimal>(type: "decimal(18,4)", nullable: false, comment: "满X元"),
                    DiscountValue = table.Column<decimal>(type: "decimal(18,4)", nullable: false, comment: "减Y元"),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, comment: "是否启用"),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Flower_FullDiscountRule", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Flower_GeneratedReport",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Passport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    CreateTime = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    ModifyPassport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true),
                    ModifyTime = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    ReportType = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false, comment: "报告类型"),
                    ReportDate = table.Column<DateTime>(type: "datetime2", nullable: false, comment: "报告日期"),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: false, comment: "内容"),
                    ModelVersion = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false, comment: "模型版本")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Flower_GeneratedReport", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Flower_HarvestListing",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Passport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    CreateTime = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    ModifyPassport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true),
                    ModifyTime = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    YieldRecordId = table.Column<long>(type: "bigint", nullable: false, comment: "关联采收记录ID"),
                    ProductId = table.Column<long>(type: "bigint", nullable: true, comment: "关联商品ID"),
                    BatchId = table.Column<long>(type: "bigint", nullable: false, comment: "关联批次ID"),
                    MerchantId = table.Column<long>(type: "bigint", nullable: false, comment: "商户ID"),
                    SpeciesId = table.Column<int>(type: "int", nullable: false, comment: "品种ID"),
                    SpeciesName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, comment: "品种名称"),
                    Grade = table.Column<string>(type: "varchar(8)", maxLength: 8, nullable: false, comment: "等级(A/B/C)"),
                    Quantity = table.Column<decimal>(type: "decimal(18,2)", nullable: false, comment: "采收数量"),
                    Unit = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false, comment: "数量单位(Stems/Kg)"),
                    Status = table.Column<int>(type: "int", nullable: false, comment: "状态: 0=草稿, 1=已上架, 2=已下架"),
                    SuggestedPrice = table.Column<decimal>(type: "decimal(18,4)", nullable: false, comment: "AI建议价格"),
                    ActualPrice = table.Column<decimal>(type: "decimal(18,4)", nullable: false, comment: "实际上架价格"),
                    GreenhouseId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, comment: "来源温室ID"),
                    HarvestDate = table.Column<DateTime>(type: "datetime2", nullable: false, comment: "采收日期"),
                    ListedDate = table.Column<DateTime>(type: "datetime2", nullable: true, comment: "上架确认时间"),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, comment: "是否软删除")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Flower_HarvestListing", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Flower_InventoryChangeLog",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    ProductId = table.Column<long>(type: "bigint", nullable: false, comment: "商品ID"),
                    BeforeQuantity = table.Column<int>(type: "int", nullable: false, comment: "变更前数量"),
                    AfterQuantity = table.Column<int>(type: "int", nullable: false, comment: "变更后数量"),
                    ChangeReason = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, comment: "变更原因"),
                    OrderId = table.Column<long>(type: "bigint", nullable: true, comment: "关联订单ID"),
                    ChangedAt = table.Column<DateTime>(type: "datetime2", nullable: false, comment: "变更时间")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Flower_InventoryChangeLog", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Flower_IoTDevice",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Passport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    CreateTime = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    ModifyPassport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true),
                    ModifyTime = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    DeviceCode = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, comment: "设备唯一标识"),
                    DeviceName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, comment: "设备名称"),
                    DeviceType = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false, comment: "设备类型(Sensor/Gateway/Controller)"),
                    GreenhouseId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, comment: "所属温室ID"),
                    GroupId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, comment: "所属分组ID"),
                    Protocol = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false, comment: "通信协议(MQTT/Modbus/HTTP)"),
                    MqttTopic = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false, comment: "MQTT Topic"),
                    ApiKey = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false, comment: "接入API Key"),
                    OnlineStatus = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false, comment: "在线状态(Online/Offline)"),
                    FirmwareVersion = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false, comment: "固件版本"),
                    LastHeartbeatTime = table.Column<DateTime>(type: "datetime2", nullable: true, comment: "最后心跳时间"),
                    IsEnabled = table.Column<bool>(type: "bit", nullable: false, comment: "是否启用"),
                    BindingStatus = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false, comment: "绑定状态(Unbound/Bound/Disabled)"),
                    BoundAt = table.Column<DateTime>(type: "datetime2", nullable: true, comment: "绑定时间"),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, comment: "是否软删除"),
                    TwinDesiredProperties = table.Column<string>(type: "nvarchar(max)", nullable: false, comment: "设备孪生期望属性(JSON)"),
                    TwinReportedProperties = table.Column<string>(type: "nvarchar(max)", nullable: false, comment: "设备孪生报告属性(JSON)")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Flower_IoTDevice", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Flower_LogisticsTrack",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    OrderId = table.Column<long>(type: "bigint", nullable: false, comment: "订单ID"),
                    ExpressCompanyName = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, comment: "物流公司"),
                    ShipOrderNumber = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, comment: "运单号"),
                    TrackData = table.Column<string>(type: "nvarchar(max)", nullable: false, comment: "物流轨迹数据JSON"),
                    LastQueriedAt = table.Column<DateTime>(type: "datetime2", nullable: true, comment: "最后查询时间"),
                    LogisticsStatus = table.Column<int>(type: "int", nullable: false, comment: "物流状态: 0=无轨迹, 1=已揽收, 2=运输中, 3=派送中, 4=已签收, 5=异常"),
                    IsReturn = table.Column<bool>(type: "bit", nullable: false, comment: "是否退货物流"),
                    RefundId = table.Column<long>(type: "bigint", nullable: true, comment: "关联退款单ID(退货物流时)"),
                    OriginCity = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, comment: "始发城市"),
                    DestinationCity = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, comment: "目的城市"),
                    CurrentLocation = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, comment: "当前位置描述")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Flower_LogisticsTrack", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Flower_Market",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Passport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    CreateTime = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    ModifyPassport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true),
                    ModifyTime = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    MarketCode = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false, comment: "市场编码"),
                    Name = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false, comment: "市场名称"),
                    Region = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, comment: "地区"),
                    Latitude = table.Column<double>(type: "float", nullable: false, comment: "纬度"),
                    Longitude = table.Column<double>(type: "float", nullable: false, comment: "经度"),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, comment: "是否启用"),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, comment: "是否已删除，true : 已删除，false : 未删除")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Flower_Market", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Flower_MarketSnapshot",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    SpeciesId = table.Column<long>(type: "bigint", nullable: false, comment: "品类ID"),
                    MarketId = table.Column<long>(type: "bigint", nullable: false, comment: "市场ID"),
                    AvgPrice = table.Column<decimal>(type: "decimal(18,4)", nullable: false, comment: "均价"),
                    MinPrice = table.Column<decimal>(type: "decimal(18,4)", nullable: false, comment: "最低价"),
                    MaxPrice = table.Column<decimal>(type: "decimal(18,4)", nullable: false, comment: "最高价"),
                    Volume = table.Column<int>(type: "int", nullable: false, comment: "成交量"),
                    TradeCount = table.Column<int>(type: "int", nullable: false, comment: "成交笔数"),
                    SnapshotTime = table.Column<DateTime>(type: "datetime2", nullable: false, comment: "快照时间"),
                    DataSource = table.Column<int>(type: "int", nullable: false, comment: "数据来源")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Flower_MarketSnapshot", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Flower_Merchant",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Passport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    CreateTime = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    ModifyPassport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true),
                    ModifyTime = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, comment: "用户ID"),
                    MerchantType = table.Column<int>(type: "int", nullable: false, comment: "商户类型"),
                    ShopName = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false, comment: "店铺名称"),
                    ShopDescription = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false, comment: "店铺描述"),
                    ContactPhone = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false, comment: "联系电话"),
                    BusinessLicense = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, comment: "营业执照"),
                    IsVerified = table.Column<bool>(type: "bit", nullable: false, comment: "是否认证"),
                    VerifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true, comment: "认证时间"),
                    GradeId = table.Column<long>(type: "bigint", nullable: true, comment: "店铺等级ID"),
                    AuditStatus = table.Column<int>(type: "int", nullable: false, comment: "审核状态: 0=不可用, 1=待审核, 2=审核通过, 3=审核拒绝, 4=已开启, 5=已冻结, 6=已过期"),
                    Stage = table.Column<int>(type: "int", nullable: false, comment: "入驻步骤: 0=协议, 1=公司信息, 2=银行账户, 3=店铺信息, 4=完成"),
                    EndDate = table.Column<DateTime>(type: "datetime2", nullable: true, comment: "到期时间"),
                    CompanyName = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false, comment: "公司名称"),
                    CompanyRegionId = table.Column<int>(type: "int", nullable: true, comment: "公司地区ID"),
                    CompanyAddress = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false, comment: "公司地址"),
                    BusinessLicenceNumber = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, comment: "营业执照号"),
                    BankAccountName = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, comment: "银行开户名"),
                    BankAccountNumber = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, comment: "银行账号"),
                    BankName = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, comment: "开户银行"),
                    BankRegionId = table.Column<int>(type: "int", nullable: true, comment: "开户行地区ID"),
                    RefuseReason = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false, comment: "拒绝原因"),
                    BusinessCategory = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false, comment: "经营类目JSON"),
                    IDCard = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false, comment: "身份证号"),
                    IDCardUrl = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false, comment: "身份证正面照"),
                    IDCardUrl2 = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false, comment: "身份证反面照"),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, comment: "是否已删除，true : 已删除，false : 未删除")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Flower_Merchant", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Flower_MerchantSettlementAccount",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Passport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    CreateTime = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    ModifyPassport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true),
                    ModifyTime = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    MerchantId = table.Column<long>(type: "bigint", nullable: false, comment: "商户ID"),
                    BankName = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, comment: "银行名称"),
                    AccountNo = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, comment: "银行账号"),
                    AccountName = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, comment: "账户名"),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false, comment: "是否默认")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Flower_MerchantSettlementAccount", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Flower_Order",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Passport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    CreateTime = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    ModifyPassport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true),
                    ModifyTime = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    OrderNo = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false, comment: "订单号"),
                    BuyerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, comment: "买家ID"),
                    MerchantId = table.Column<long>(type: "bigint", nullable: false, comment: "商户ID"),
                    TotalAmount = table.Column<decimal>(type: "decimal(18,4)", nullable: false, comment: "总金额"),
                    Status = table.Column<int>(type: "int", nullable: false, comment: "订单状态"),
                    PaymentMethod = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false, comment: "支付方式"),
                    PaymentTime = table.Column<DateTime>(type: "datetime2", nullable: true, comment: "支付时间"),
                    ShippingAddress = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false, comment: "收货地址"),
                    IsPresale = table.Column<bool>(type: "bit", nullable: false, comment: "是否预售"),
                    PresaleDeliveryDate = table.Column<DateTime>(type: "datetime2", nullable: true, comment: "预售发货日期"),
                    RelatedBatchId = table.Column<long>(type: "bigint", nullable: true, comment: "关联种植批次ID"),
                    PresaleReadyNotifiedAt = table.Column<DateTime>(type: "datetime2", nullable: true, comment: "预售就绪通知时间"),
                    ShipTo = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false, comment: "收货人"),
                    CellPhone = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false, comment: "收货手机"),
                    RegionId = table.Column<int>(type: "int", nullable: true, comment: "地区ID"),
                    Address = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false, comment: "详细地址"),
                    ExpressCompanyName = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, comment: "物流公司"),
                    ShipOrderNumber = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, comment: "物流单号"),
                    Freight = table.Column<decimal>(type: "decimal(18,4)", nullable: false, comment: "运费"),
                    ProductTotalAmount = table.Column<decimal>(type: "decimal(18,4)", nullable: false, comment: "商品总金额"),
                    OrderTotalAmount = table.Column<decimal>(type: "decimal(18,4)", nullable: false, comment: "订单实付金额"),
                    DiscountAmount = table.Column<decimal>(type: "decimal(18,4)", nullable: false, comment: "优惠金额"),
                    FullDiscount = table.Column<decimal>(type: "decimal(18,4)", nullable: false, comment: "满减优惠"),
                    IntegralDiscount = table.Column<decimal>(type: "decimal(18,4)", nullable: false, comment: "积分抵扣"),
                    InvoiceTitle = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false, comment: "发票抬头"),
                    InvoiceCode = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, comment: "发票税号"),
                    Tax = table.Column<decimal>(type: "decimal(18,4)", nullable: false, comment: "税费"),
                    RefundStatus = table.Column<int>(type: "int", nullable: false, comment: "退款状态: 0=无, 1=退款中, 2=已退款"),
                    SellerRemark = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false, comment: "卖家备注"),
                    ShippingDate = table.Column<DateTime>(type: "datetime2", nullable: true, comment: "发货时间"),
                    CompletionTime = table.Column<DateTime>(type: "datetime2", nullable: true, comment: "收货时间"),
                    DeliveredAt = table.Column<DateTime>(type: "datetime2", nullable: true, comment: "确认收货时间"),
                    Platform = table.Column<int>(type: "int", nullable: false, comment: "下单平台: 0=PC, 1=移动, 2=小程序"),
                    SenderName = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false, comment: "发货人姓名"),
                    SenderPhone = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false, comment: "发货人电话"),
                    SenderAddress = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false, comment: "发货人地址")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Flower_Order", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Flower_OrderComplaint",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Passport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    CreateTime = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    ModifyPassport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true),
                    ModifyTime = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    OrderId = table.Column<long>(type: "bigint", nullable: false, comment: "订单ID"),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, comment: "用户ID"),
                    ShopId = table.Column<long>(type: "bigint", nullable: false, comment: "店铺ID"),
                    ComplaintReason = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false, comment: "投诉原因"),
                    ComplaintContent = table.Column<string>(type: "nvarchar(max)", nullable: false, comment: "投诉内容"),
                    Status = table.Column<int>(type: "int", nullable: false, comment: "状态0=待处理1=处理中2=已解决3=已关闭"),
                    ReplyContent = table.Column<string>(type: "nvarchar(max)", nullable: false, comment: "回复内容"),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, comment: "创建时间"),
                    ResolvedAt = table.Column<DateTime>(type: "datetime2", nullable: true, comment: "解决时间")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Flower_OrderComplaint", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Flower_OrderItem",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    OrderId = table.Column<long>(type: "bigint", nullable: false, comment: "订单ID"),
                    ProductId = table.Column<long>(type: "bigint", nullable: false, comment: "商品ID"),
                    SpeciesId = table.Column<int>(type: "int", nullable: false, comment: "品种ID"),
                    ProductName = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false, comment: "商品名称"),
                    Price = table.Column<decimal>(type: "decimal(18,4)", nullable: false, comment: "单价"),
                    Quantity = table.Column<int>(type: "int", nullable: false, comment: "数量"),
                    Subtotal = table.Column<decimal>(type: "decimal(18,4)", nullable: false, comment: "小计")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Flower_OrderItem", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Flower_OrderLog",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    OrderId = table.Column<long>(type: "bigint", nullable: false, comment: "订单ID"),
                    ActionType = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false, comment: "操作类型"),
                    BeforeSnapshot = table.Column<string>(type: "nvarchar(max)", nullable: false, comment: "操作前快照"),
                    AfterSnapshot = table.Column<string>(type: "nvarchar(max)", nullable: false, comment: "操作后快照"),
                    OperatorPassport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false, comment: "操作人通行证"),
                    OperatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, comment: "操作时间")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Flower_OrderLog", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Flower_OrderRefund",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Passport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    CreateTime = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    ModifyPassport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true),
                    ModifyTime = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    OrderId = table.Column<long>(type: "bigint", nullable: false, comment: "订单ID"),
                    OrderItemId = table.Column<long>(type: "bigint", nullable: false, comment: "订单明细ID"),
                    RefundNo = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false, comment: "退款号"),
                    RefundAmount = table.Column<decimal>(type: "decimal(18,4)", nullable: false, comment: "退款金额"),
                    Reason = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false, comment: "退款原因"),
                    Status = table.Column<int>(type: "int", nullable: false, comment: "退款状态: 0=待审核, 1=商户同意, 2=商户拒绝, 3=退款中, 4=退款完成, 5=退款关闭"),
                    RefundMode = table.Column<int>(type: "int", nullable: false, comment: "退款类型: 0=仅退款, 1=退货退款"),
                    SellerAuditRemark = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false, comment: "商户审核备注"),
                    SellerAuditTime = table.Column<DateTime>(type: "datetime2", nullable: true, comment: "商户审核时间"),
                    PlatformRemark = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false, comment: "平台处理备注"),
                    PlatformAuditTime = table.Column<DateTime>(type: "datetime2", nullable: true, comment: "平台处理时间"),
                    BuyerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, comment: "买家ID"),
                    MerchantId = table.Column<long>(type: "bigint", nullable: false, comment: "商户ID"),
                    EnabledRefundAmount = table.Column<decimal>(type: "decimal(18,4)", nullable: false, comment: "可退金额"),
                    ReturnQuantity = table.Column<int>(type: "int", nullable: false, comment: "退货数量"),
                    ReturnShipmentId = table.Column<long>(type: "bigint", nullable: true, comment: "退货物流ID"),
                    ReturnDeadline = table.Column<DateTime>(type: "datetime2", nullable: true, comment: "买家退货截止时间"),
                    SellerConfirmDeadline = table.Column<DateTime>(type: "datetime2", nullable: true, comment: "商户确认收货截止时间"),
                    ReturnAddress = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false, comment: "退货地址JSON")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Flower_OrderRefund", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Flower_PaymentStatusChangeLog",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    TransactionId = table.Column<long>(type: "bigint", nullable: false, comment: "交易ID"),
                    BeforeStatus = table.Column<int>(type: "int", nullable: false, comment: "变更前状态"),
                    AfterStatus = table.Column<int>(type: "int", nullable: false, comment: "变更后状态"),
                    ChannelResponse = table.Column<string>(type: "nvarchar(max)", nullable: false, comment: "渠道响应"),
                    ChangedAt = table.Column<DateTime>(type: "datetime2", nullable: false, comment: "变更时间")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Flower_PaymentStatusChangeLog", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Flower_PaymentTransaction",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Passport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    CreateTime = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    ModifyPassport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true),
                    ModifyTime = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    OrderId = table.Column<long>(type: "bigint", nullable: false, comment: "订单ID"),
                    TransactionNo = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, comment: "交易号"),
                    Channel = table.Column<int>(type: "int", nullable: false, comment: "支付渠道"),
                    Amount = table.Column<decimal>(type: "decimal(18,4)", nullable: false, comment: "金额"),
                    Status = table.Column<int>(type: "int", nullable: false, comment: "状态"),
                    PrepayId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, comment: "预支付ID"),
                    ChannelTransactionNo = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, comment: "渠道交易号"),
                    PaidAt = table.Column<DateTime>(type: "datetime2", nullable: true, comment: "支付时间"),
                    ExpiredAt = table.Column<DateTime>(type: "datetime2", nullable: true, comment: "过期时间")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Flower_PaymentTransaction", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Flower_PendingSettlement",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Passport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    CreateTime = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    ModifyPassport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true),
                    ModifyTime = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    OrderId = table.Column<long>(type: "bigint", nullable: false, comment: "订单ID"),
                    ShopId = table.Column<long>(type: "bigint", nullable: false, comment: "店铺ID"),
                    OrderAmount = table.Column<decimal>(type: "decimal(18,4)", nullable: false, comment: "订单金额"),
                    PlatformCommission = table.Column<decimal>(type: "decimal(18,4)", nullable: false, comment: "平台佣金"),
                    RefundAmount = table.Column<decimal>(type: "decimal(18,4)", nullable: false, comment: "退款金额"),
                    SettleableAmount = table.Column<decimal>(type: "decimal(18,4)", nullable: false, comment: "可结算金额"),
                    Status = table.Column<int>(type: "int", nullable: false, comment: "状态0=待结算1=已结算"),
                    SettlementId = table.Column<long>(type: "bigint", nullable: true, comment: "结算单ID"),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, comment: "创建时间"),
                    SettledAt = table.Column<DateTime>(type: "datetime2", nullable: true, comment: "结算时间"),
                    RefundDeducted = table.Column<bool>(type: "bit", nullable: false, comment: "退款是否已扣减")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Flower_PendingSettlement", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Flower_PlantingAdvice",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Passport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    CreateTime = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    ModifyPassport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true),
                    ModifyTime = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    BatchId = table.Column<long>(type: "bigint", nullable: false, comment: "关联批次ID"),
                    AdviceType = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, comment: "建议类型(Irrigation/Ventilation/Pest/Harvest/General)"),
                    Title = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false, comment: "建议标题"),
                    Content = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false, comment: "建议内容"),
                    Source = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, comment: "数据来源"),
                    Priority = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false, comment: "优先级(High/Normal/Low)"),
                    Status = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false, comment: "状态(Pending/Executed/Ignored)"),
                    GeneratedTime = table.Column<DateTime>(type: "datetime2", nullable: false, comment: "生成时间"),
                    ExecutedTime = table.Column<DateTime>(type: "datetime2", nullable: true, comment: "执行时间"),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, comment: "是否软删除")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Flower_PlantingAdvice", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Flower_PlantingBatch",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Passport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    CreateTime = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    ModifyPassport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true),
                    ModifyTime = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    BatchName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, comment: "批次名称"),
                    SpeciesId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, comment: "品种ID"),
                    SpeciesName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, comment: "品种名称"),
                    GreenhouseId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, comment: "温室ID"),
                    PlantingDate = table.Column<DateTime>(type: "datetime2", nullable: false, comment: "种植日期"),
                    ExpectedHarvestDate = table.Column<DateTime>(type: "datetime2", nullable: true, comment: "预计采收日期"),
                    ActualHarvestDate = table.Column<DateTime>(type: "datetime2", nullable: true, comment: "实际采收日期"),
                    Status = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false, comment: "批次状态(Planted/Growing/Harvesting/Completed/Abandoned)"),
                    PlantingQuantity = table.Column<int>(type: "int", nullable: false, comment: "种植数量"),
                    Remark = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false, comment: "备注"),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, comment: "是否软删除")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Flower_PlantingBatch", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Flower_PredictionModel",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Passport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    CreateTime = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    ModifyPassport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true),
                    ModifyTime = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    SpeciesId = table.Column<long>(type: "bigint", nullable: false, comment: "品类ID"),
                    ModelType = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false, comment: "模型类型"),
                    ModelVersion = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false, comment: "模型版本"),
                    ModelParams = table.Column<string>(type: "nvarchar(max)", nullable: false, comment: "模型参数JSON"),
                    TrainingDataRange = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false, comment: "训练数据范围"),
                    Accuracy = table.Column<double>(type: "float", nullable: false, comment: "准确度"),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, comment: "是否启用"),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, comment: "是否已删除，true : 已删除，false : 未删除")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Flower_PredictionModel", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Flower_PricePrediction",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    SpeciesId = table.Column<long>(type: "bigint", nullable: false, comment: "品类ID"),
                    MarketId = table.Column<long>(type: "bigint", nullable: false, comment: "市场ID"),
                    ModelId = table.Column<long>(type: "bigint", nullable: false, comment: "模型ID"),
                    PredictDate = table.Column<DateTime>(type: "datetime2", nullable: false, comment: "预测日期"),
                    PredictedPrice = table.Column<decimal>(type: "decimal(18,4)", nullable: false, comment: "预测价格"),
                    LowerBound = table.Column<decimal>(type: "decimal(18,4)", nullable: false, comment: "预测下界"),
                    UpperBound = table.Column<decimal>(type: "decimal(18,4)", nullable: false, comment: "预测上界"),
                    Confidence = table.Column<double>(type: "float", nullable: false, comment: "置信度"),
                    TimeScale = table.Column<int>(type: "int", nullable: false, comment: "时间尺度"),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, comment: "创建时间")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Flower_PricePrediction", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Flower_Product",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Passport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    CreateTime = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    ModifyPassport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true),
                    ModifyTime = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    MerchantId = table.Column<long>(type: "bigint", nullable: false, comment: "商户ID"),
                    SpeciesId = table.Column<int>(type: "int", nullable: false, comment: "品种ID"),
                    ProductName = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false, comment: "商品名称"),
                    Description = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false, comment: "商品描述"),
                    Price = table.Column<decimal>(type: "decimal(18,4)", nullable: false, comment: "价格"),
                    Stock = table.Column<int>(type: "int", nullable: false, comment: "库存"),
                    Unit = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false, comment: "单位"),
                    Images = table.Column<string>(type: "varchar(1024)", maxLength: 1024, nullable: false, comment: "图片"),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, comment: "是否上架"),
                    Version = table.Column<int>(type: "int", nullable: false, comment: "版本号"),
                    CategoryId = table.Column<long>(type: "bigint", nullable: true, comment: "商品分类ID"),
                    TypeId = table.Column<long>(type: "bigint", nullable: true, comment: "商品类型ID"),
                    BrandId = table.Column<long>(type: "bigint", nullable: true, comment: "品牌ID"),
                    AuditStatus = table.Column<int>(type: "int", nullable: false, comment: "审核状态: 0=待审核, 1=审核通过, 2=审核拒绝"),
                    FreightTemplateId = table.Column<long>(type: "bigint", nullable: true, comment: "运费模板ID"),
                    Weight = table.Column<decimal>(type: "decimal(18,4)", nullable: true, comment: "重量kg"),
                    Volume = table.Column<decimal>(type: "decimal(18,4)", nullable: true, comment: "体积m3"),
                    MaxBuyCount = table.Column<int>(type: "int", nullable: false, comment: "最大购买数"),
                    IsOpenLadder = table.Column<bool>(type: "bit", nullable: false, comment: "是否开启阶梯价"),
                    ProductType = table.Column<int>(type: "int", nullable: false, comment: "商品类型: 0=实物, 1=虚拟"),
                    MarketPrice = table.Column<decimal>(type: "decimal(18,4)", nullable: true, comment: "市场价"),
                    MinSalePrice = table.Column<decimal>(type: "decimal(18,4)", nullable: false, comment: "最低销售价"),
                    VisitCount = table.Column<long>(type: "bigint", nullable: false, comment: "浏览量"),
                    SaleCount = table.Column<long>(type: "bigint", nullable: false, comment: "销量"),
                    IsPresale = table.Column<bool>(type: "bit", nullable: false, comment: "是否预售"),
                    PresaleDeliveryDate = table.Column<DateTime>(type: "datetime2", nullable: true, comment: "预售发货日期"),
                    RelatedBatchId = table.Column<long>(type: "bigint", nullable: true, comment: "关联种植批次ID"),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, comment: "是否已删除，true : 已删除，false : 未删除")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Flower_Product", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Flower_ProductCategory",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Passport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    CreateTime = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    ModifyPassport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true),
                    ModifyTime = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    Name = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, comment: "分类名称"),
                    Depth = table.Column<int>(type: "int", nullable: false, comment: "分类深度1/2/3"),
                    Path = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false, comment: "分类路径"),
                    ParentCategoryId = table.Column<long>(type: "bigint", nullable: false, comment: "父分类ID"),
                    DisplaySequence = table.Column<long>(type: "bigint", nullable: false, comment: "排序"),
                    Icon = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false, comment: "图标"),
                    Image = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false, comment: "图片"),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Flower_ProductCategory", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Flower_ProductComment",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Passport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    CreateTime = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    ModifyPassport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true),
                    ModifyTime = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    ProductId = table.Column<long>(type: "bigint", nullable: false, comment: "商品ID"),
                    OrderId = table.Column<long>(type: "bigint", nullable: false, comment: "订单ID"),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, comment: "用户ID"),
                    Rank = table.Column<int>(type: "int", nullable: false, comment: "评分1-5"),
                    Content = table.Column<string>(type: "varchar(1024)", maxLength: 1024, nullable: false, comment: "评价内容"),
                    Images = table.Column<string>(type: "varchar(1024)", maxLength: 1024, nullable: false, comment: "评价图片"),
                    ReplyContent = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false, comment: "商户回复"),
                    ReplyTime = table.Column<DateTime>(type: "datetime2", nullable: true, comment: "回复时间"),
                    IsAnonymous = table.Column<bool>(type: "bit", nullable: false, comment: "是否匿名")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Flower_ProductComment", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Flower_ProductDescriptionTemplate",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Passport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    CreateTime = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    ModifyPassport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true),
                    ModifyTime = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    ShopId = table.Column<long>(type: "bigint", nullable: false, comment: "店铺ID"),
                    TemplateName = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, comment: "模板名称"),
                    TopContent = table.Column<string>(type: "nvarchar(max)", nullable: false, comment: "顶部内容"),
                    BottomContent = table.Column<string>(type: "nvarchar(max)", nullable: false, comment: "底部内容"),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Flower_ProductDescriptionTemplate", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Flower_ProductLadderPrice",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Passport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    CreateTime = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    ModifyPassport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true),
                    ModifyTime = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    ProductId = table.Column<long>(type: "bigint", nullable: false, comment: "商品ID"),
                    MinBatch = table.Column<int>(type: "int", nullable: false, comment: "最小批量"),
                    MaxBatch = table.Column<int>(type: "int", nullable: false, comment: "最大批量"),
                    Price = table.Column<decimal>(type: "decimal(18,4)", nullable: false, comment: "价格"),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Flower_ProductLadderPrice", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Flower_ProductRelation",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Passport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    CreateTime = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    ModifyPassport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true),
                    ModifyTime = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    ProductId = table.Column<long>(type: "bigint", nullable: false, comment: "商品ID"),
                    RelatedProductId = table.Column<long>(type: "bigint", nullable: false, comment: "关联商品ID"),
                    DisplaySequence = table.Column<int>(type: "int", nullable: false, comment: "排序")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Flower_ProductRelation", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Flower_ProductSKU",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Passport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    CreateTime = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    ModifyPassport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true),
                    ModifyTime = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    ProductId = table.Column<long>(type: "bigint", nullable: false, comment: "商品ID"),
                    SkuCode = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false, comment: "SKU编码"),
                    Color = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, comment: "颜色"),
                    Size = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, comment: "尺码"),
                    Version = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, comment: "版本"),
                    SalePrice = table.Column<decimal>(type: "decimal(18,4)", nullable: false, comment: "销售价"),
                    CostPrice = table.Column<decimal>(type: "decimal(18,4)", nullable: false, comment: "成本价"),
                    Stock = table.Column<long>(type: "bigint", nullable: false, comment: "库存"),
                    SafeStock = table.Column<long>(type: "bigint", nullable: true, comment: "安全库存"),
                    ShowPic = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false, comment: "展示图片"),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Flower_ProductSKU", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Flower_RefundOrder",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Passport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    CreateTime = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    ModifyPassport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true),
                    ModifyTime = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    OrderId = table.Column<long>(type: "bigint", nullable: false, comment: "订单ID"),
                    PaymentTransactionId = table.Column<long>(type: "bigint", nullable: false, comment: "支付交易ID"),
                    RefundNo = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, comment: "退款号"),
                    RefundAmount = table.Column<decimal>(type: "decimal(18,4)", nullable: false, comment: "退款金额"),
                    Reason = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false, comment: "退款原因"),
                    Status = table.Column<int>(type: "int", nullable: false, comment: "退款状态"),
                    ChannelRefundNo = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, comment: "渠道退款号"),
                    RefundedAt = table.Column<DateTime>(type: "datetime2", nullable: true, comment: "退款时间")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Flower_RefundOrder", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Flower_RepurchaseRecord",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    BuyerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, comment: "买家ID"),
                    OriginalOrderId = table.Column<long>(type: "bigint", nullable: false, comment: "原订单ID"),
                    NewOrderId = table.Column<long>(type: "bigint", nullable: true, comment: "新订单ID"),
                    RepurchaseTime = table.Column<DateTime>(type: "datetime2", nullable: false, comment: "复购时间")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Flower_RepurchaseRecord", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Flower_ReturnShipment",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Passport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    CreateTime = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    ModifyPassport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true),
                    ModifyTime = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    RefundId = table.Column<long>(type: "bigint", nullable: false, comment: "退款单ID"),
                    ExpressCompanyName = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, comment: "退货物流公司"),
                    ShipOrderNumber = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, comment: "退货运单号"),
                    ReturnAddress = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false, comment: "退货地址JSON"),
                    ShippedAt = table.Column<DateTime>(type: "datetime2", nullable: true, comment: "退货发货时间"),
                    ReceivedAt = table.Column<DateTime>(type: "datetime2", nullable: true, comment: "商户确认收货时间"),
                    Status = table.Column<int>(type: "int", nullable: false, comment: "退货物流状态: 0=待退货, 1=已发货, 2=已收货")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Flower_ReturnShipment", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Flower_SensorReading",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    DeviceId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, comment: "设备ID"),
                    GreenhouseId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, comment: "温室ID"),
                    Temperature = table.Column<double>(type: "float", nullable: false, comment: "温度"),
                    Humidity = table.Column<double>(type: "float", nullable: false, comment: "湿度"),
                    LightIntensity = table.Column<double>(type: "float", nullable: false, comment: "光照强度"),
                    Co2Level = table.Column<double>(type: "float", nullable: false, comment: "二氧化碳浓度"),
                    SoilMoisture = table.Column<double>(type: "float", nullable: false, comment: "土壤湿度"),
                    ReadingTime = table.Column<DateTime>(type: "datetime2", nullable: false, comment: "读数时间"),
                    DataQuality = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false, comment: "数据质量标识(Normal/Abnormal/Missing)"),
                    DataSource = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false, comment: "数据来源(Device/Manual)"),
                    BatchId = table.Column<long>(type: "bigint", nullable: true, comment: "关联批次ID")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Flower_SensorReading", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Flower_SettledConfig",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Passport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    CreateTime = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    ModifyPassport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true),
                    ModifyTime = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    BusinessType = table.Column<int>(type: "int", nullable: false, comment: "商家类型0=企业1=个体2=均可"),
                    SettlementAccountType = table.Column<int>(type: "int", nullable: false, comment: "结算账户类型0=银行1=微信2=均支持"),
                    TrialDays = table.Column<int>(type: "int", nullable: false, comment: "试用天数"),
                    IsCity = table.Column<bool>(type: "bit", nullable: false, comment: "地址城市是否必填"),
                    IsPeopleNumber = table.Column<bool>(type: "bit", nullable: false, comment: "人数是否必填"),
                    IsAddress = table.Column<bool>(type: "bit", nullable: false, comment: "详细地址是否必填"),
                    IsBusinessLicenseCode = table.Column<bool>(type: "bit", nullable: false, comment: "营业执照号是否必填"),
                    IsBusinessScope = table.Column<bool>(type: "bit", nullable: false, comment: "经营范围是否必填"),
                    IsBusinessLicense = table.Column<bool>(type: "bit", nullable: false, comment: "营业执照是否必填")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Flower_SettledConfig", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Flower_SettlementBill",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Passport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    CreateTime = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    ModifyPassport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true),
                    ModifyTime = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    MerchantId = table.Column<long>(type: "bigint", nullable: false, comment: "商户ID"),
                    PeriodStart = table.Column<DateTime>(type: "datetime2", nullable: false, comment: "结算周期开始"),
                    PeriodEnd = table.Column<DateTime>(type: "datetime2", nullable: false, comment: "结算周期结束"),
                    TotalAmount = table.Column<decimal>(type: "decimal(18,4)", nullable: false, comment: "总金额"),
                    PlatformFee = table.Column<decimal>(type: "decimal(18,4)", nullable: false, comment: "平台手续费"),
                    SettledAmount = table.Column<decimal>(type: "decimal(18,4)", nullable: false, comment: "结算金额"),
                    Status = table.Column<int>(type: "int", nullable: false, comment: "状态"),
                    SettledAt = table.Column<DateTime>(type: "datetime2", nullable: true, comment: "结算时间")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Flower_SettlementBill", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Flower_SettlementDetail",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    SettlementBillId = table.Column<long>(type: "bigint", nullable: false, comment: "结算账单ID"),
                    OrderId = table.Column<long>(type: "bigint", nullable: false, comment: "订单ID"),
                    OrderNo = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false, comment: "订单号"),
                    OrderAmount = table.Column<decimal>(type: "decimal(18,4)", nullable: false, comment: "订单金额"),
                    PlatformCommission = table.Column<decimal>(type: "decimal(18,4)", nullable: false, comment: "平台佣金"),
                    RefundAmount = table.Column<decimal>(type: "decimal(18,4)", nullable: false, comment: "退款金额"),
                    SettleableAmount = table.Column<decimal>(type: "decimal(18,4)", nullable: false, comment: "可结算金额")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Flower_SettlementDetail", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Flower_ShippingAddress",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Passport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    CreateTime = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    ModifyPassport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true),
                    ModifyTime = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, comment: "用户ID"),
                    ShipTo = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false, comment: "收货人姓名"),
                    Phone = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false, comment: "联系电话"),
                    ProvinceId = table.Column<int>(type: "int", nullable: true, comment: "省ID"),
                    ProvinceName = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false, comment: "省名称"),
                    CityId = table.Column<int>(type: "int", nullable: true, comment: "市ID"),
                    CityName = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false, comment: "市名称"),
                    DistrictId = table.Column<int>(type: "int", nullable: true, comment: "区/县ID"),
                    DistrictName = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false, comment: "区/县名称"),
                    Address = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false, comment: "详细地址"),
                    IsDefault = table.Column<bool>(type: "bit", nullable: false, comment: "是否默认地址"),
                    Latitude = table.Column<double>(type: "float", nullable: true, comment: "纬度"),
                    Longitude = table.Column<double>(type: "float", nullable: true, comment: "经度")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Flower_ShippingAddress", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Flower_ShopAccountItem",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Passport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    CreateTime = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    ModifyPassport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true),
                    ModifyTime = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    ShopId = table.Column<long>(type: "bigint", nullable: false, comment: "店铺ID"),
                    AccountType = table.Column<int>(type: "int", nullable: false, comment: "账户类型0=收入1=支出"),
                    Amount = table.Column<decimal>(type: "decimal(18,4)", nullable: false, comment: "金额"),
                    BalanceAfter = table.Column<decimal>(type: "decimal(18,4)", nullable: false, comment: "变动后余额"),
                    Description = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false, comment: "描述"),
                    RelatedId = table.Column<long>(type: "bigint", nullable: false, comment: "关联订单/提现ID"),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, comment: "创建时间")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Flower_ShopAccountItem", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Flower_ShopBrandApply",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Passport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    CreateTime = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    ModifyPassport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true),
                    ModifyTime = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    ShopId = table.Column<long>(type: "bigint", nullable: false, comment: "店铺ID"),
                    BrandName = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, comment: "品牌名称"),
                    ProofMaterial = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false, comment: "证明材料"),
                    AuditStatus = table.Column<int>(type: "int", nullable: false, comment: "审核状态"),
                    AuditRemark = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false, comment: "审核备注")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Flower_ShopBrandApply", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Flower_ShopCategory",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Passport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    CreateTime = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    ModifyPassport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true),
                    ModifyTime = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    ShopId = table.Column<long>(type: "bigint", nullable: false, comment: "店铺ID"),
                    Name = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, comment: "分类名称"),
                    ParentCategoryId = table.Column<long>(type: "bigint", nullable: false, comment: "父分类ID"),
                    DisplaySequence = table.Column<long>(type: "bigint", nullable: false, comment: "排序"),
                    IsShow = table.Column<bool>(type: "bit", nullable: false, comment: "是否显示")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Flower_ShopCategory", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Flower_ShopGrade",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Passport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    CreateTime = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    ModifyPassport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true),
                    ModifyTime = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    Name = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, comment: "等级名称"),
                    ProductLimit = table.Column<int>(type: "int", nullable: false, comment: "最大商品数"),
                    ImageLimit = table.Column<int>(type: "int", nullable: false, comment: "最大图片空间MB"),
                    TemplateLimit = table.Column<int>(type: "int", nullable: false, comment: "最大模板数"),
                    ChargeStandard = table.Column<decimal>(type: "decimal(18,4)", nullable: false, comment: "收费标准"),
                    Remark = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false, comment: "备注"),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Flower_ShopGrade", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Flower_ShoppingCart",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ProductId = table.Column<long>(type: "bigint", nullable: false),
                    SKUId = table.Column<long>(type: "bigint", nullable: true),
                    ProductName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    Price = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    ImageUrl = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MerchantId = table.Column<long>(type: "bigint", nullable: false),
                    SpeciesId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Flower_ShoppingCart", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Flower_ShopShipper",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Passport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    CreateTime = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    ModifyPassport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true),
                    ModifyTime = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    ShopId = table.Column<long>(type: "bigint", nullable: false, comment: "店铺ID"),
                    ShipperTag = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, comment: "发货点名称"),
                    ShipperName = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false, comment: "发货人姓名"),
                    RegionId = table.Column<int>(type: "int", nullable: false, comment: "地区ID"),
                    Address = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false, comment: "详细地址"),
                    TelPhone = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false, comment: "电话"),
                    IsDefaultSendGoods = table.Column<bool>(type: "bit", nullable: false, comment: "是否默认发货点"),
                    Longitude = table.Column<float>(type: "real", nullable: true, comment: "经度"),
                    Latitude = table.Column<float>(type: "real", nullable: true, comment: "纬度"),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Flower_ShopShipper", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Flower_ShopWithdraw",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Passport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    CreateTime = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    ModifyPassport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true),
                    ModifyTime = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    ShopId = table.Column<long>(type: "bigint", nullable: false, comment: "店铺ID"),
                    Amount = table.Column<decimal>(type: "decimal(18,4)", nullable: false, comment: "提现金额"),
                    BankName = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, comment: "银行名称"),
                    AccountNo = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, comment: "银行账号"),
                    AccountName = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, comment: "账户名"),
                    Status = table.Column<int>(type: "int", nullable: false, comment: "状态0=待审核1=已通过2=已拒绝3=已打款"),
                    AuditRemark = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false, comment: "审核备注"),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, comment: "创建时间"),
                    AuditedAt = table.Column<DateTime>(type: "datetime2", nullable: true, comment: "审核时间"),
                    PaidAt = table.Column<DateTime>(type: "datetime2", nullable: true, comment: "打款时间")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Flower_ShopWithdraw", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Flower_Species",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Passport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    CreateTime = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    ModifyPassport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true),
                    ModifyTime = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    SpeciesCode = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false, comment: "品类编码"),
                    Category = table.Column<int>(type: "int", nullable: false, comment: "品类分类"),
                    Name = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, comment: "品类名称"),
                    DisplayName = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false, comment: "显示名称"),
                    OriginRegion = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, comment: "产地"),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, comment: "是否启用"),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, comment: "是否已删除，true : 已删除，false : 未删除")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Flower_Species", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Flower_Subscription",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ModifyPassport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true),
                    ModifyTime = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, comment: "用户ID"),
                    Level = table.Column<int>(type: "int", nullable: false, comment: "订阅等级"),
                    StartDate = table.Column<DateTime>(type: "datetime2", nullable: false, comment: "开始日期"),
                    EndDate = table.Column<DateTime>(type: "datetime2", nullable: false, comment: "结束日期"),
                    AutoRenew = table.Column<bool>(type: "bit", nullable: false, comment: "自动续费"),
                    PaymentMethod = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false, comment: "支付方式"),
                    Passport = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, comment: "通行证号"),
                    CreateTime = table.Column<DateTime>(type: "datetime2", nullable: false, comment: "创建时间"),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, comment: "是否已删除，true : 已删除，false : 未删除")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Flower_Subscription", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Flower_TradeArchive",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    ArchiveType = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false, comment: "归档类型"),
                    RelatedId = table.Column<long>(type: "bigint", nullable: true, comment: "关联ID"),
                    ArchiveData = table.Column<byte[]>(type: "varbinary(max)", nullable: false, comment: "归档数据"),
                    ArchivedAt = table.Column<DateTime>(type: "datetime2", nullable: false, comment: "归档时间")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Flower_TradeArchive", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Flower_TradeComment",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Passport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    CreateTime = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    ModifyPassport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true),
                    ModifyTime = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    OrderId = table.Column<long>(type: "bigint", nullable: false, comment: "订单ID"),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, comment: "用户ID"),
                    ShopId = table.Column<long>(type: "bigint", nullable: false, comment: "店铺ID"),
                    DescriptionScore = table.Column<int>(type: "int", nullable: false, comment: "描述相符1-5"),
                    ServiceScore = table.Column<int>(type: "int", nullable: false, comment: "服务态度1-5"),
                    LogisticsScore = table.Column<int>(type: "int", nullable: false, comment: "物流速度1-5"),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: false, comment: "评价内容"),
                    IsAnonymous = table.Column<bool>(type: "bit", nullable: false, comment: "是否匿名")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Flower_TradeComment", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Flower_User",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ModifyPassport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true),
                    ModifyTime = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false, comment: "用户ID"),
                    UserType = table.Column<int>(type: "int", nullable: false, comment: "用户类型"),
                    MerchantId = table.Column<long>(type: "bigint", nullable: true, comment: "商户ID"),
                    DisplayName = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, comment: "显示名称"),
                    Phone = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false, comment: "手机号"),
                    Region = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, comment: "地区"),
                    SubscriptionLevel = table.Column<int>(type: "int", nullable: false, comment: "订阅等级"),
                    Passport = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, comment: "通行证号"),
                    CreateTime = table.Column<DateTime>(type: "datetime2", nullable: false, comment: "创建时间"),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, comment: "是否已删除，true : 已删除，false : 未删除")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Flower_User", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Flower_YieldRecord",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Passport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    CreateTime = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    ModifyPassport = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true),
                    ModifyTime = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    IsValid = table.Column<bool>(type: "bit", nullable: false),
                    BatchId = table.Column<long>(type: "bigint", nullable: false, comment: "关联批次ID"),
                    SpeciesId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false, comment: "品种ID"),
                    SpeciesName = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false, comment: "品种名称"),
                    Quantity = table.Column<decimal>(type: "decimal(18,2)", nullable: false, comment: "采收数量"),
                    Unit = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false, comment: "数量单位(Stems/Kg)"),
                    Grade = table.Column<string>(type: "varchar(8)", maxLength: 8, nullable: false, comment: "等级(A/B/C)"),
                    HarvestDate = table.Column<DateTime>(type: "datetime2", nullable: false, comment: "采收日期"),
                    Remark = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false, comment: "备注"),
                    IsDeleted = table.Column<bool>(type: "bit", nullable: false, comment: "是否软删除")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Flower_YieldRecord", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FlowerAlertLog_CreatedAt",
                table: "Flower_AlertLog",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerAlertLog_RuleId",
                table: "Flower_AlertLog",
                column: "RuleId");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerAlertLog_UserId",
                table: "Flower_AlertLog",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerAlertRule_SpeciesId_MarketId",
                table: "Flower_AlertRule",
                columns: new[] { "SpeciesId", "MarketId" });

            migrationBuilder.CreateIndex(
                name: "IX_FlowerAlertRule_UserId",
                table: "Flower_AlertRule",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerApiKey_ApiKey",
                table: "Flower_ApiKey",
                column: "ApiKey");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerApiKey_OwnerPassportId",
                table: "Flower_ApiKey",
                column: "OwnerPassportId");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerBrand_AuditStatus",
                table: "Flower_Brand",
                column: "AuditStatus");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerBrand_Name",
                table: "Flower_Brand",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerBusinessCategory_AuditStatus",
                table: "Flower_BusinessCategory",
                column: "AuditStatus");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerBusinessCategory_CategoryId",
                table: "Flower_BusinessCategory",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerBusinessCategory_ShopId",
                table: "Flower_BusinessCategory",
                column: "ShopId");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerCashDeposit_CategoryId",
                table: "Flower_CashDeposit",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerCashDeposit_ShopId",
                table: "Flower_CashDeposit",
                column: "ShopId");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerCashDeposit_Status",
                table: "Flower_CashDeposit",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerChatHistory_ConversationId",
                table: "Flower_ChatHistory",
                column: "ConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerChatHistory_UserId",
                table: "Flower_ChatHistory",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerCostRecord_BatchId",
                table: "Flower_CostRecord",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerCostRecord_Category",
                table: "Flower_CostRecord",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerCostRecord_CostDate",
                table: "Flower_CostRecord",
                column: "CostDate");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerCoupon_CouponType",
                table: "Flower_Coupon",
                column: "CouponType");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerCoupon_IsActive",
                table: "Flower_Coupon",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerCoupon_ShopId",
                table: "Flower_Coupon",
                column: "ShopId");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerCouponRecord_CouponId",
                table: "Flower_CouponRecord",
                column: "CouponId");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerCouponRecord_Status",
                table: "Flower_CouponRecord",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerCouponRecord_UserId",
                table: "Flower_CouponRecord",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerDailyPriceStats_SpeciesId_MarketId_StatDate",
                table: "Flower_DailyPriceStats",
                columns: new[] { "SpeciesId", "MarketId", "StatDate" });

            migrationBuilder.CreateIndex(
                name: "IX_FlowerDailyPriceStats_StatDate",
                table: "Flower_DailyPriceStats",
                column: "StatDate");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerDataPool_DataType",
                table: "Flower_DataPool",
                column: "DataType");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerDataPool_Timestamp",
                table: "Flower_DataPool",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerDeviceGroup_GreenhouseId",
                table: "Flower_DeviceGroup",
                column: "GreenhouseId");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerDocument_IsIndexed",
                table: "Flower_Document",
                column: "IsIndexed");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerDocument_Source",
                table: "Flower_Document",
                column: "Source");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerDocumentChunk_DocumentId",
                table: "Flower_DocumentChunk",
                column: "DocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerDocumentChunk_DocumentId_ChunkIndex",
                table: "Flower_DocumentChunk",
                columns: new[] { "DocumentId", "ChunkIndex" });

            migrationBuilder.CreateIndex(
                name: "IX_FlowerDocumentChunk_IsIndexed",
                table: "Flower_DocumentChunk",
                column: "IsIndexed");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerFreightTemplate_MerchantId",
                table: "Flower_FreightTemplate",
                column: "MerchantId");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerFullDiscountRule_IsActive",
                table: "Flower_FullDiscountRule",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerFullDiscountRule_ShopId",
                table: "Flower_FullDiscountRule",
                column: "ShopId");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerGeneratedReport_ReportDate",
                table: "Flower_GeneratedReport",
                column: "ReportDate");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerGeneratedReport_ReportType",
                table: "Flower_GeneratedReport",
                column: "ReportType");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerHarvestListing_BatchId",
                table: "Flower_HarvestListing",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerHarvestListing_HarvestDate",
                table: "Flower_HarvestListing",
                column: "HarvestDate");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerHarvestListing_MerchantId",
                table: "Flower_HarvestListing",
                column: "MerchantId");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerHarvestListing_ProductId",
                table: "Flower_HarvestListing",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerHarvestListing_SpeciesId",
                table: "Flower_HarvestListing",
                column: "SpeciesId");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerHarvestListing_Status",
                table: "Flower_HarvestListing",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerHarvestListing_YieldRecordId",
                table: "Flower_HarvestListing",
                column: "YieldRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerInventoryChangeLog_ChangedAt",
                table: "Flower_InventoryChangeLog",
                column: "ChangedAt");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerInventoryChangeLog_ProductId",
                table: "Flower_InventoryChangeLog",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerIoTDevice_BindingStatus",
                table: "Flower_IoTDevice",
                column: "BindingStatus");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerIoTDevice_DeviceCode",
                table: "Flower_IoTDevice",
                column: "DeviceCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FlowerIoTDevice_GreenhouseId",
                table: "Flower_IoTDevice",
                column: "GreenhouseId");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerIoTDevice_GroupId",
                table: "Flower_IoTDevice",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerIoTDevice_OnlineStatus",
                table: "Flower_IoTDevice",
                column: "OnlineStatus");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerLogisticsTrack_Express_ShipNo",
                table: "Flower_LogisticsTrack",
                columns: new[] { "ExpressCompanyName", "ShipOrderNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_FlowerLogisticsTrack_LogisticsStatus",
                table: "Flower_LogisticsTrack",
                column: "LogisticsStatus");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerLogisticsTrack_OrderId",
                table: "Flower_LogisticsTrack",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerMarket_MarketCode",
                table: "Flower_Market",
                column: "MarketCode");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerMarket_Region",
                table: "Flower_Market",
                column: "Region");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerMarketSnapshot_SnapshotTime",
                table: "Flower_MarketSnapshot",
                column: "SnapshotTime");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerMarketSnapshot_SpeciesId_MarketId_SnapshotTime",
                table: "Flower_MarketSnapshot",
                columns: new[] { "SpeciesId", "MarketId", "SnapshotTime" });

            migrationBuilder.CreateIndex(
                name: "IX_FlowerMerchant_ShopName",
                table: "Flower_Merchant",
                column: "ShopName");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerMerchant_UserId",
                table: "Flower_Merchant",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerMerchantSettlementAccount_MerchantId",
                table: "Flower_MerchantSettlementAccount",
                column: "MerchantId");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerOrder_BuyerId",
                table: "Flower_Order",
                column: "BuyerId");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerOrder_MerchantId",
                table: "Flower_Order",
                column: "MerchantId");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerOrder_OrderNo",
                table: "Flower_Order",
                column: "OrderNo");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerOrder_RelatedBatchId",
                table: "Flower_Order",
                column: "RelatedBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerOrder_Status",
                table: "Flower_Order",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerOrderComplaint_OrderId",
                table: "Flower_OrderComplaint",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerOrderComplaint_ShopId",
                table: "Flower_OrderComplaint",
                column: "ShopId");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerOrderComplaint_Status",
                table: "Flower_OrderComplaint",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerOrderComplaint_UserId",
                table: "Flower_OrderComplaint",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerOrderItem_OrderId",
                table: "Flower_OrderItem",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerOrderItem_ProductId",
                table: "Flower_OrderItem",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerOrderLog_OperatedAt",
                table: "Flower_OrderLog",
                column: "OperatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerOrderLog_OrderId",
                table: "Flower_OrderLog",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerOrderRefund_MerchantId",
                table: "Flower_OrderRefund",
                column: "MerchantId");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerOrderRefund_OrderId",
                table: "Flower_OrderRefund",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerOrderRefund_RefundNo",
                table: "Flower_OrderRefund",
                column: "RefundNo");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerOrderRefund_Status",
                table: "Flower_OrderRefund",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerPaymentStatusChangeLog_TransactionId",
                table: "Flower_PaymentStatusChangeLog",
                column: "TransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerPaymentTransaction_Channel",
                table: "Flower_PaymentTransaction",
                column: "Channel");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerPaymentTransaction_OrderId",
                table: "Flower_PaymentTransaction",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerPaymentTransaction_TransactionNo",
                table: "Flower_PaymentTransaction",
                column: "TransactionNo");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerPendingSettlement_OrderId",
                table: "Flower_PendingSettlement",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerPendingSettlement_ShopId",
                table: "Flower_PendingSettlement",
                column: "ShopId");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerPendingSettlement_Status",
                table: "Flower_PendingSettlement",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerPlantingAdvice_AdviceType",
                table: "Flower_PlantingAdvice",
                column: "AdviceType");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerPlantingAdvice_BatchId",
                table: "Flower_PlantingAdvice",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerPlantingAdvice_Status",
                table: "Flower_PlantingAdvice",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerPlantingBatch_GreenhouseId",
                table: "Flower_PlantingBatch",
                column: "GreenhouseId");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerPlantingBatch_PlantingDate",
                table: "Flower_PlantingBatch",
                column: "PlantingDate");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerPlantingBatch_SpeciesId",
                table: "Flower_PlantingBatch",
                column: "SpeciesId");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerPlantingBatch_Status",
                table: "Flower_PlantingBatch",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerPredictionModel_SpeciesId",
                table: "Flower_PredictionModel",
                column: "SpeciesId");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerPredictionModel_SpeciesId_ModelType_ModelVersion",
                table: "Flower_PredictionModel",
                columns: new[] { "SpeciesId", "ModelType", "ModelVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_FlowerPricePrediction_ModelId",
                table: "Flower_PricePrediction",
                column: "ModelId");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerPricePrediction_SpeciesId_MarketId_PredictDate",
                table: "Flower_PricePrediction",
                columns: new[] { "SpeciesId", "MarketId", "PredictDate" });

            migrationBuilder.CreateIndex(
                name: "IX_FlowerProduct_IsActive",
                table: "Flower_Product",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerProduct_MerchantId",
                table: "Flower_Product",
                column: "MerchantId");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerProduct_RelatedBatchId",
                table: "Flower_Product",
                column: "RelatedBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerProduct_SpeciesId",
                table: "Flower_Product",
                column: "SpeciesId");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerProductCategory_Depth",
                table: "Flower_ProductCategory",
                column: "Depth");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerProductCategory_ParentCategoryId",
                table: "Flower_ProductCategory",
                column: "ParentCategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerProductComment_OrderId",
                table: "Flower_ProductComment",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerProductComment_ProductId",
                table: "Flower_ProductComment",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerProductDescriptionTemplate_ShopId",
                table: "Flower_ProductDescriptionTemplate",
                column: "ShopId");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerProductLadderPrice_ProductId",
                table: "Flower_ProductLadderPrice",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerProductRelation_ProductId",
                table: "Flower_ProductRelation",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerProductSKU_ProductId",
                table: "Flower_ProductSKU",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerProductSKU_SkuCode",
                table: "Flower_ProductSKU",
                column: "SkuCode");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerRefundOrder_OrderId",
                table: "Flower_RefundOrder",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerRefundOrder_RefundNo",
                table: "Flower_RefundOrder",
                column: "RefundNo");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerRepurchaseRecord_BuyerId",
                table: "Flower_RepurchaseRecord",
                column: "BuyerId");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerRepurchaseRecord_OriginalOrderId",
                table: "Flower_RepurchaseRecord",
                column: "OriginalOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerReturnShipment_RefundId",
                table: "Flower_ReturnShipment",
                column: "RefundId");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerReturnShipment_Status",
                table: "Flower_ReturnShipment",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerSensorReading_BatchId",
                table: "Flower_SensorReading",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerSensorReading_DeviceId",
                table: "Flower_SensorReading",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerSensorReading_GreenhouseId",
                table: "Flower_SensorReading",
                column: "GreenhouseId");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerSensorReading_ReadingTime",
                table: "Flower_SensorReading",
                column: "ReadingTime");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerSettlementBill_MerchantId",
                table: "Flower_SettlementBill",
                column: "MerchantId");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerSettlementBill_Status",
                table: "Flower_SettlementBill",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerSettlementDetail_OrderId",
                table: "Flower_SettlementDetail",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerSettlementDetail_SettlementBillId",
                table: "Flower_SettlementDetail",
                column: "SettlementBillId");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerShopAccountItem_CreatedAt",
                table: "Flower_ShopAccountItem",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerShopAccountItem_ShopId",
                table: "Flower_ShopAccountItem",
                column: "ShopId");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerShopBrandApply_AuditStatus",
                table: "Flower_ShopBrandApply",
                column: "AuditStatus");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerShopBrandApply_ShopId",
                table: "Flower_ShopBrandApply",
                column: "ShopId");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerShopCategory_ShopId",
                table: "Flower_ShopCategory",
                column: "ShopId");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerShopGrade_Name",
                table: "Flower_ShopGrade",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerShoppingCart_UserId",
                table: "Flower_ShoppingCart",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerShoppingCart_UserId_ProductId",
                table: "Flower_ShoppingCart",
                columns: new[] { "UserId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_FlowerShopShipper_ShopId",
                table: "Flower_ShopShipper",
                column: "ShopId");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerShopWithdraw_ShopId",
                table: "Flower_ShopWithdraw",
                column: "ShopId");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerShopWithdraw_Status",
                table: "Flower_ShopWithdraw",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerSpecies_Category",
                table: "Flower_Species",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerSpecies_SpeciesCode",
                table: "Flower_Species",
                column: "SpeciesCode");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerSubscription_EndDate",
                table: "Flower_Subscription",
                column: "EndDate");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerSubscription_UserId",
                table: "Flower_Subscription",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerTradeArchive_ArchivedAt",
                table: "Flower_TradeArchive",
                column: "ArchivedAt");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerTradeArchive_ArchiveType",
                table: "Flower_TradeArchive",
                column: "ArchiveType");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerTradeComment_OrderId",
                table: "Flower_TradeComment",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerTradeComment_ShopId",
                table: "Flower_TradeComment",
                column: "ShopId");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerTradeComment_UserId",
                table: "Flower_TradeComment",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerUser_MerchantId",
                table: "Flower_User",
                column: "MerchantId");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerUser_UserId",
                table: "Flower_User",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerYieldRecord_BatchId",
                table: "Flower_YieldRecord",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerYieldRecord_HarvestDate",
                table: "Flower_YieldRecord",
                column: "HarvestDate");

            migrationBuilder.CreateIndex(
                name: "IX_FlowerYieldRecord_SpeciesId",
                table: "Flower_YieldRecord",
                column: "SpeciesId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Flower_AlertLog");

            migrationBuilder.DropTable(
                name: "Flower_AlertRule");

            migrationBuilder.DropTable(
                name: "Flower_ApiKey");

            migrationBuilder.DropTable(
                name: "Flower_Brand");

            migrationBuilder.DropTable(
                name: "Flower_BusinessCategory");

            migrationBuilder.DropTable(
                name: "Flower_CashDeposit");

            migrationBuilder.DropTable(
                name: "Flower_ChatHistory");

            migrationBuilder.DropTable(
                name: "Flower_CostRecord");

            migrationBuilder.DropTable(
                name: "Flower_Coupon");

            migrationBuilder.DropTable(
                name: "Flower_CouponRecord");

            migrationBuilder.DropTable(
                name: "Flower_DailyPriceStats");

            migrationBuilder.DropTable(
                name: "Flower_DataPool");

            migrationBuilder.DropTable(
                name: "Flower_DeviceGroup");

            migrationBuilder.DropTable(
                name: "Flower_Document");

            migrationBuilder.DropTable(
                name: "Flower_DocumentChunk");

            migrationBuilder.DropTable(
                name: "Flower_FreightTemplate");

            migrationBuilder.DropTable(
                name: "Flower_FullDiscountRule");

            migrationBuilder.DropTable(
                name: "Flower_GeneratedReport");

            migrationBuilder.DropTable(
                name: "Flower_HarvestListing");

            migrationBuilder.DropTable(
                name: "Flower_InventoryChangeLog");

            migrationBuilder.DropTable(
                name: "Flower_IoTDevice");

            migrationBuilder.DropTable(
                name: "Flower_LogisticsTrack");

            migrationBuilder.DropTable(
                name: "Flower_Market");

            migrationBuilder.DropTable(
                name: "Flower_MarketSnapshot");

            migrationBuilder.DropTable(
                name: "Flower_Merchant");

            migrationBuilder.DropTable(
                name: "Flower_MerchantSettlementAccount");

            migrationBuilder.DropTable(
                name: "Flower_Order");

            migrationBuilder.DropTable(
                name: "Flower_OrderComplaint");

            migrationBuilder.DropTable(
                name: "Flower_OrderItem");

            migrationBuilder.DropTable(
                name: "Flower_OrderLog");

            migrationBuilder.DropTable(
                name: "Flower_OrderRefund");

            migrationBuilder.DropTable(
                name: "Flower_PaymentStatusChangeLog");

            migrationBuilder.DropTable(
                name: "Flower_PaymentTransaction");

            migrationBuilder.DropTable(
                name: "Flower_PendingSettlement");

            migrationBuilder.DropTable(
                name: "Flower_PlantingAdvice");

            migrationBuilder.DropTable(
                name: "Flower_PlantingBatch");

            migrationBuilder.DropTable(
                name: "Flower_PredictionModel");

            migrationBuilder.DropTable(
                name: "Flower_PricePrediction");

            migrationBuilder.DropTable(
                name: "Flower_Product");

            migrationBuilder.DropTable(
                name: "Flower_ProductCategory");

            migrationBuilder.DropTable(
                name: "Flower_ProductComment");

            migrationBuilder.DropTable(
                name: "Flower_ProductDescriptionTemplate");

            migrationBuilder.DropTable(
                name: "Flower_ProductLadderPrice");

            migrationBuilder.DropTable(
                name: "Flower_ProductRelation");

            migrationBuilder.DropTable(
                name: "Flower_ProductSKU");

            migrationBuilder.DropTable(
                name: "Flower_RefundOrder");

            migrationBuilder.DropTable(
                name: "Flower_RepurchaseRecord");

            migrationBuilder.DropTable(
                name: "Flower_ReturnShipment");

            migrationBuilder.DropTable(
                name: "Flower_SensorReading");

            migrationBuilder.DropTable(
                name: "Flower_SettledConfig");

            migrationBuilder.DropTable(
                name: "Flower_SettlementBill");

            migrationBuilder.DropTable(
                name: "Flower_SettlementDetail");

            migrationBuilder.DropTable(
                name: "Flower_ShippingAddress");

            migrationBuilder.DropTable(
                name: "Flower_ShopAccountItem");

            migrationBuilder.DropTable(
                name: "Flower_ShopBrandApply");

            migrationBuilder.DropTable(
                name: "Flower_ShopCategory");

            migrationBuilder.DropTable(
                name: "Flower_ShopGrade");

            migrationBuilder.DropTable(
                name: "Flower_ShoppingCart");

            migrationBuilder.DropTable(
                name: "Flower_ShopShipper");

            migrationBuilder.DropTable(
                name: "Flower_ShopWithdraw");

            migrationBuilder.DropTable(
                name: "Flower_Species");

            migrationBuilder.DropTable(
                name: "Flower_Subscription");

            migrationBuilder.DropTable(
                name: "Flower_TradeArchive");

            migrationBuilder.DropTable(
                name: "Flower_TradeComment");

            migrationBuilder.DropTable(
                name: "Flower_User");

            migrationBuilder.DropTable(
                name: "Flower_YieldRecord");
        }
    }
}
