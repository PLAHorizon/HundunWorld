using Horizon.Core.Abstract;
using Horizon.Entities;
using Horizon.Model;
using Horizon.Model.GameModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.IO;

namespace Horizon.Entities
{

    /// <summary>
    /// 仅限用于数据库设计生成或修改数据库结构使用
    /// 不要在生产环境中使用
    /// </summary>
    public class GameEntityContextDes : DbContext, IDesignTimeDbContextFactory<GameEntityContextDes>
    {
        DbContextOptions ContextOptions { get; }
        private static readonly ILoggerFactory _loggerFactory    = LoggerFactory.Create(builder => { builder.AddConsole(); });
        #region 设计
        public GameEntityContextDes()
        {
            Database.Migrate();
        }
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            // 只在设计时配置连接字符串
            if (DesignTimeContextChecker.IsDesignTime())
            {
                var config = new ConfigurationBuilder()
                    .SetBasePath(Directory.GetCurrentDirectory())
                    .AddJsonFile("repository.json")
                    .Build();

                optionsBuilder.UseSqlServer(config.GetConnectionString("GameSqlServer"));
            }
            // 运行时通过依赖注入接收配置，不调用base.OnConfiguring
        }
        public GameEntityContextDes CreateDbContext(string[] args)
        {

            var config = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("repository.json")
                .Build();
            var optionsBuilder = new DbContextOptionsBuilder<GameEntityContextDes>();

            optionsBuilder.UseSqlServer(config.GetConnectionString("GameSqlServer")).UseLoggerFactory(_loggerFactory).UseRootApplicationServiceProvider();
            return FastActivator.Create<GameEntityContextDes>(isnewInstance: true, args: optionsBuilder.Options);
        }
        #endregion
        public GameEntityContextDes(DbContextOptions options) : base(options)
        {
            ContextOptions = options;
            Database.AutoSavepointsEnabled = true;
            Database.AutoTransactionBehavior = AutoTransactionBehavior.WhenNeeded;

            ChangeTracker.AutoDetectChangesEnabled = true;
            ChangeTracker.AcceptAllChanges();

        }

        #region  实体类
        public DbSet<UserEntity> Users { get; set; }
        public DbSet<GameEntity> Games { get; set; }
        public DbSet<ZoneEntity> Zones { get; set; }
        public DbSet<ServerEntity> Servers { get; set; }
        public DbSet<CharacterEntity> Characters { get; set; }
        public DbSet<CharacterAttributeEntity> CharacterAttributes { get; set; }
        public DbSet<ItemEntity> Items { get; set; }
        public DbSet<ItemAttributeEntity> ItemAttributes { get; set; }
        public DbSet<ItemTemplateEntity> ItemTemplates { get; set; }
        public DbSet<ItemGemEntity> ItemGems { get; set; }
        public DbSet<MaterialEntity> Materials { get; set; }
        public DbSet<MaterialSynthesisLogEntity> MaterialSynthesisLogs { get; set; }
        public DbSet<CurrencyEntity> Currencies { get; set; }
        public DbSet<SkillTemplateEntity> SkillTemplates { get; set; }
        public DbSet<CharacterSkillEntity> CharacterSkills { get; set; }
        public DbSet<SkillAdvancePathEntity> SkillAdvancePaths { get; set; }
        public DbSet<SkillCustomCreateEntity> SkillCustomCreates { get; set; }
        public DbSet<SkillBookEntity> SkillBooks { get; set; }
        public DbSet<TitleEntity> Titles { get; set; }
        public DbSet<ChatMessageEntity> ChatMessages { get; set; }
        public DbSet<ChatPrivateMessageEntity> ChatPrivateMessages { get; set; }
        public DbSet<ChatChannelSettingEntity> ChatChannelSettings { get; set; }
        public DbSet<ChatBlacklistEntity> ChatBlacklists { get; set; }
        public DbSet<GuildEntity> Guilds { get; set; }
        public DbSet<ActivityEntity> Activities { get; set; }
        public DbSet<SetItemEntity> SetItems { get; set; }
        public DbSet<BagEntity> Bags { get; set; }
        public DbSet<TradeLogEntity> TradeLogs
        {
            get; set;
        }

        #endregion



        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
        }


    }


    public class GameEntityContext : DbContext
    {
        DbContextOptions ContextOptions { get; }
        private static readonly ILoggerFactory _loggerFactory
    = LoggerFactory.Create(builder => { builder.AddConsole(); });
        #region 设计
        public GameEntityContext()
        {
            //Database.Migrate();
        }

        public GameEntityContext CreateDbContext(string[] args)
        {

            var config = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("repository.json")
                .Build();
            var optionsBuilder = new DbContextOptionsBuilder<GameEntityContext>();

            optionsBuilder.UseSqlServer(config.GetConnectionString("GameSqlServer")).UseLoggerFactory(_loggerFactory).UseRootApplicationServiceProvider();
            return FastActivator.Create<GameEntityContext>(isnewInstance: true, args: optionsBuilder.Options);
        }
        #endregion
        public GameEntityContext(DbContextOptions options) : base(options)
        {
            ContextOptions = options;
            Database.AutoSavepointsEnabled = true;
            Database.AutoTransactionBehavior = AutoTransactionBehavior.WhenNeeded;

            ChangeTracker.AutoDetectChangesEnabled = true;
            ChangeTracker.AcceptAllChanges();

        }

        #region  实体类
        public DbSet<GameEntity> Games { get; set; }
        public DbSet<ZoneEntity> Zones { get; set; }
        public DbSet<ServerEntity> Servers { get; set; }
        public DbSet<UserEntity> Users { get; set; }
        public DbSet<CharacterEntity> Characters { get; set; }
        public DbSet<CharacterAttributeEntity> CharacterAttributes { get; set; }
        public DbSet<ItemEntity> Items { get; set; }
        public DbSet<ItemAttributeEntity> ItemAttributes { get; set; }
        public DbSet<ItemTemplateEntity> ItemTemplates { get; set; }
        public DbSet<ItemGemEntity> ItemGems { get; set; }
        public DbSet<MaterialEntity> Materials { get; set; }
        public DbSet<MaterialSynthesisLogEntity> MaterialSynthesisLogs { get; set; }
        public DbSet<CurrencyEntity> Currencies { get; set; }
        public DbSet<SkillTemplateEntity> SkillTemplates { get; set; }
        public DbSet<CharacterSkillEntity> CharacterSkills { get; set; }
        public DbSet<SkillAdvancePathEntity> SkillAdvancePaths { get; set; }
        public DbSet<SkillCustomCreateEntity> SkillCustomCreates { get; set; }
        public DbSet<SkillBookEntity> SkillBooks { get; set; }
        public DbSet<TitleEntity> Titles { get; set; }
        public DbSet<ChatMessageEntity> ChatMessages { get; set; }
        public DbSet<ChatPrivateMessageEntity> ChatPrivateMessages { get; set; }
        public DbSet<ChatChannelSettingEntity> ChatChannelSettings { get; set; }
        public DbSet<ChatBlacklistEntity> ChatBlacklists { get; set; }
        public DbSet<GuildEntity> Guilds { get; set; }
        public DbSet<ActivityEntity> Activities { get; set; }
        public DbSet<SetItemEntity> SetItems { get; set; }
        public DbSet<BagEntity> Bags { get; set; }
        public DbSet<TradeLogEntity> TradeLogs
        {
            get; set;
        }
        #endregion



        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // 角色表索引 - 按用户和游戏查询角色
            modelBuilder.Entity<CharacterEntity>()
                .HasIndex(c => new { c.UserId, c.GameId })
                .HasDatabaseName("IX_Character_UserId_GameId");

            // 角色表索引 - 按最后登录时间排序
            modelBuilder.Entity<CharacterEntity>()
                .HasIndex(c => c.LastLoginTime)
                .HasDatabaseName("IX_Character_LastLoginTime");

            // 交易日志索引 - 按卖家查询
            modelBuilder.Entity<TradeLogEntity>()
                .HasIndex(t => t.SellerId)
                .HasDatabaseName("IX_TradeLog_SellerId");

            // 交易日志索引 - 按买家查询
            modelBuilder.Entity<TradeLogEntity>()
                .HasIndex(t => t.BuyerId)
                .HasDatabaseName("IX_TradeLog_BuyerId");

            // 交易日志索引 - 按交易时间排序
            modelBuilder.Entity<TradeLogEntity>()
                .HasIndex(t => t.TradeTime)
                .HasDatabaseName("IX_TradeLog_TradeTime");

            // 背包索引 - 按角色查询背包
            modelBuilder.Entity<BagEntity>()
                .HasIndex(b => b.CharacterId)
                .HasDatabaseName("IX_Bag_CharacterId");

            // 聊天消息索引 - 按发送时间排序
            modelBuilder.Entity<ChatMessageEntity>()
                .HasIndex(m => m.SendTime)
                .HasDatabaseName("IX_ChatMessage_SendTime");

            // 聊天消息索引 - 按频道和发送时间查询
            modelBuilder.Entity<ChatMessageEntity>()
                .HasIndex(m => new { m.Channel, m.SendTime })
                .HasDatabaseName("IX_ChatMessage_Channel_SendTime");

            // 公会索引 - 按会长查询
            modelBuilder.Entity<GuildEntity>()
                .HasIndex(g => g.LeaderId)
                .HasDatabaseName("IX_Guild_LeaderId");
        }


    }


}
