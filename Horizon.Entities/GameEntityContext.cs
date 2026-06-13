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
        private static readonly ILoggerFactory _loggerFactory = LoggerFactory.Create(builder => { builder.AddConsole(); });
        #region 设计
        public GameEntityContextDes()
        {
            // 不在这里调用 Database.Migrate()，因为此时 DbContext 还没有配置数据库连接
            // 迁移应该在 OnConfiguring 之后或通过其他方式执行
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

        // Arena System
        public DbSet<Horizon.Model.Arena.ArenaSeason> ArenaSeasons { get; set; }
        public DbSet<Horizon.Model.Arena.ArenaPlayerRecord> ArenaPlayerRecords { get; set; }
        public DbSet<Horizon.Model.Arena.ArenaMatchRecord> ArenaMatchRecords { get; set; }

        // Cross Server System
        public DbSet<Horizon.Model.CrossServer.CrossServerMatch> CrossServerMatches { get; set; }
        public DbSet<Horizon.Model.CrossServer.CrossServerPlayer> CrossServerPlayers { get; set; }

        // World Sync
        public DbSet<ChunkStateEntity> ChunkStates { get; set; }
        public DbSet<DiffLogEntity> DiffLogs { get; set; }

        #endregion



        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            GameEntityIndexConfiguration.ConfigureIndexes(modelBuilder);
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

        // Arena System
        public DbSet<Horizon.Model.Arena.ArenaSeason> ArenaSeasons { get; set; }
        public DbSet<Horizon.Model.Arena.ArenaPlayerRecord> ArenaPlayerRecords { get; set; }
        public DbSet<Horizon.Model.Arena.ArenaMatchRecord> ArenaMatchRecords { get; set; }

        // Cross Server System
        public DbSet<Horizon.Model.CrossServer.CrossServerMatch> CrossServerMatches { get; set; }
        public DbSet<Horizon.Model.CrossServer.CrossServerPlayer> CrossServerPlayers { get; set; }

        // World Sync
        public DbSet<ChunkStateEntity> ChunkStates { get; set; }
        public DbSet<DiffLogEntity> DiffLogs { get; set; }
        #endregion



        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            GameEntityIndexConfiguration.ConfigureIndexes(modelBuilder);
        }


    }

    /// <summary>
    /// 数据库索引配置（共享方法）
    /// </summary>
    internal static class GameEntityIndexConfiguration
    {
        /// <summary>
        /// 配置数据库索引以优化查询性能
        /// </summary>
        public static void ConfigureIndexes(ModelBuilder modelBuilder)
        {
            // Characters表索引
            modelBuilder.Entity<CharacterEntity>(entity =>
            {
                entity.HasIndex(e => e.UserId).HasDatabaseName("IX_Character_UserId");
                entity.HasIndex(e => new { e.UserId, e.GameId }).HasDatabaseName("IX_Character_UserId_GameId");
                entity.HasIndex(e => e.LastLoginTime).HasDatabaseName("IX_Character_LastLoginTime");
                entity.HasIndex(e => e.CharacterName).HasDatabaseName("IX_Character_CharacterName");
            });

            // TradeLogs表索引
            modelBuilder.Entity<TradeLogEntity>(entity =>
            {
                entity.HasIndex(e => e.SellerId).HasDatabaseName("IX_TradeLog_SellerId");
                entity.HasIndex(e => e.BuyerId).HasDatabaseName("IX_TradeLog_BuyerId");
                entity.HasIndex(e => e.TradeTime).HasDatabaseName("IX_TradeLog_TradeTime");
            });

            // Bags表索引
            modelBuilder.Entity<BagEntity>(entity =>
            {
                entity.HasIndex(e => e.CharacterId).HasDatabaseName("IX_Bag_CharacterId");
            });

            // ChatMessages表索引
            modelBuilder.Entity<ChatMessageEntity>(entity =>
            {
                entity.HasIndex(e => e.SendTime).HasDatabaseName("IX_ChatMessage_SendTime");
                entity.HasIndex(e => new { e.Channel, e.SendTime }).HasDatabaseName("IX_ChatMessage_Channel_SendTime");
                entity.HasIndex(e => e.SenderId).HasDatabaseName("IX_ChatMessage_SenderId");
            });

            // Guilds表索引
            modelBuilder.Entity<GuildEntity>(entity =>
            {
                entity.HasIndex(e => e.LeaderId).HasDatabaseName("IX_Guild_LeaderId");
                entity.HasIndex(e => e.GuildName).HasDatabaseName("IX_Guild_GuildName");
            });

            // Users表索引
            modelBuilder.Entity<UserEntity>(entity =>
            {
                entity.HasIndex(e => e.AccountName).IsUnique().HasDatabaseName("IX_User_AccountName");
                entity.HasIndex(e => e.LastLoginTime).HasDatabaseName("IX_User_LastLoginTime");
            });

            // ChunkState（世界 Chunk 快照）：复合主键 (morton_bucket, morton_key)
            modelBuilder.Entity<ChunkStateEntity>(entity =>
            {
                entity.HasKey(e => new { e.MortonBucket, e.MortonKey })
                      .HasName("PK_chunk_state");
                entity.HasIndex(e => e.MortonKey).HasDatabaseName("IX_chunk_state_morton_key");
                // 对齐 DDL: updated_at DEFAULT SYSUTCDATETIME()
                entity.Property(e => e.UpdatedAt)
                      .HasDefaultValueSql("SYSUTCDATETIME()");
            });

            // DiffLog（世界 Diff 追加日志）：复合主键 (morton_bucket, seq)
            modelBuilder.Entity<DiffLogEntity>(entity =>
            {
                entity.HasKey(e => new { e.MortonBucket, e.Seq })
                      .HasName("PK_diff_log");
                entity.HasIndex(e => new { e.MortonKey, e.Seq }).HasDatabaseName("IX_diff_log_morton_key");
                entity.HasIndex(e => e.Seq).HasDatabaseName("IX_diff_log_seq");
                entity.HasIndex(e => e.CreatedAt).HasDatabaseName("IX_diff_log_created_at");
                // 对齐 DDL: created_at DEFAULT SYSUTCDATETIME()
                entity.Property(e => e.CreatedAt)
                      .HasDefaultValueSql("SYSUTCDATETIME()");
            });
        }
    }

}
