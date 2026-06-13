using Horizon.Core.Abstract;
using Horizon.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.IO;

namespace Horizon.Entities
{
    /// <summary>
    /// 仅限用于数据库设计生成或修改数据库结构使用
    /// 不要在生产环境中使用
    /// </summary>
    public class IMEntityContextDes : DbContext, IDesignTimeDbContextFactory<IMEntityContextDes>
    {
        DbContextOptions ContextOptions { get; }
        private static readonly ILoggerFactory _loggerFactory = LoggerFactory.Create(builder => { builder.AddConsole(); });

        public IMEntityContextDes()
        {
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            if (optionsBuilder.IsConfigured) return;

            string cs = null;
            string lastError = null;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[IMEntityContextDes] 尝试查找 IMSqlServer 连接字符串：");

            try
            {
                var config = BuildRepositoryConfiguration();
                cs = config.GetConnectionString("IMSqlServer");
                sb.AppendLine($"  BuildRepositoryConfiguration() 成功，ConnectionString={cs ?? "(null)"}");
            }
            catch (Exception ex)
            {
                lastError = ex.Message;
                sb.AppendLine($"  BuildRepositoryConfiguration() 异常: {lastError}");
            }

            if (string.IsNullOrWhiteSpace(cs))
            {
                cs = "Data Source=.;Initial Catalog=IM;User Id=sa;Password=tkhjkdh#%!)[}Akjdh8;Integrated Security=True;Pooling=False;Max Pool Size=200;MultipleActiveResultSets=True;Encrypt=True;TrustServerCertificate=True";
                sb.AppendLine($"  使用默认连接字符串: {cs}");
                sb.AppendLine($"  当前工作目录: {System.IO.Directory.GetCurrentDirectory()}");
                System.Console.WriteLine(sb.ToString());
            }

            optionsBuilder.UseSqlServer(cs);
        }

        public IMEntityContextDes CreateDbContext(string[] args)
        {
            var config = BuildRepositoryConfiguration();
            var optionsBuilder = new DbContextOptionsBuilder<IMEntityContextDes>();

            optionsBuilder.UseSqlServer(config.GetConnectionString("IMSqlServer")).UseLoggerFactory(_loggerFactory).UseRootApplicationServiceProvider();
            return FastActivator.Create<IMEntityContextDes>(isnewInstance: true, args: optionsBuilder.Options);
        }

        public IMEntityContextDes(DbContextOptions options) : base(options)
        {
            ContextOptions = options;
            Database.AutoSavepointsEnabled = true;
            Database.AutoTransactionBehavior = AutoTransactionBehavior.WhenNeeded;
            ChangeTracker.AutoDetectChangesEnabled = true;
            ChangeTracker.AcceptAllChanges();
        }

        public DbSet<AddRelationshipLog> AddRelationshipLogs { get; set; }
        public DbSet<ChatComplaint> ChatComplaints { get; set; }
        public DbSet<ChatGroup> ChatGroups { get; set; }
        public DbSet<ChatGroupMember> ChatGroupMembers { get; set; }
        public DbSet<ContactData> ContactDatas { get; set; }
        public DbSet<CustomContactData> CustomContactDatas { get; set; }
        public DbSet<CustomPreference> CustomPreferences { get; set; }
        public DbSet<IMConversation> Conversations { get; set; }
        public DbSet<IMGift> IMGifts { get; set; }
        public DbSet<Invitation> Invitations { get; set; }
        public DbSet<InvitationLog> InvitationLogs { get; set; }
        public DbSet<M2BChatMessage> M2BChatMessages { get; set; }
        public DbSet<M2GChatMessage> M2GChatMessages { get; set; }
        public DbSet<M2MChatMessage> M2MChatMessages { get; set; }
        public DbSet<MemberContactData> MemberContactDatas { get; set; }
        public DbSet<MemberGift> MemberGifts { get; set; }
        public DbSet<MemberPreference> MemberPreferences { get; set; }
        public DbSet<MoneyPackage> MoneyPackages { get; set; }
        public DbSet<Relationship> Relationships { get; set; }
        public DbSet<Reward> Rewards { get; set; }
        public DbSet<SocailPreference> SocailPreferences { get; set; }
        public DbSet<SysMoneyPackage> SysMoneyPackages { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            IMEntityContextModelBuilder.Configure(modelBuilder);
            base.OnModelCreating(modelBuilder);
        }

        private static IConfiguration BuildRepositoryConfiguration()
        {
            return RepositoryConfigurationLocator.Build();
        }
    }

    public class IMEntityContext : DbContext
    {
        DbContextOptions ContextOptions { get; }
        private static readonly ILoggerFactory _loggerFactory = LoggerFactory.Create(builder => { builder.AddConsole(); });

        public IMEntityContext()
        {
        }

        public IMEntityContext CreateDbContext(string[] args)
        {
            var config = RepositoryConfigurationLocator.Build();
            var optionsBuilder = new DbContextOptionsBuilder<IMEntityContext>();

            optionsBuilder.UseSqlServer(config.GetConnectionString("IMSqlServer")).UseLoggerFactory(_loggerFactory).UseRootApplicationServiceProvider();
            return FastActivator.Create<IMEntityContext>(isnewInstance: true, args: optionsBuilder.Options);
        }

        public IMEntityContext(DbContextOptions options) : base(options)
        {
            ContextOptions = options;
            Database.AutoSavepointsEnabled = true;
            Database.AutoTransactionBehavior = AutoTransactionBehavior.WhenNeeded;
            ChangeTracker.AutoDetectChangesEnabled = true;
            ChangeTracker.AcceptAllChanges();
        }

        public DbSet<AddRelationshipLog> AddRelationshipLogs { get; set; }
        public DbSet<ChatComplaint> ChatComplaints { get; set; }
        public DbSet<ChatGroup> ChatGroups { get; set; }
        public DbSet<ChatGroupMember> ChatGroupMembers { get; set; }
        public DbSet<ContactData> ContactDatas { get; set; }
        public DbSet<CustomContactData> CustomContactDatas { get; set; }
        public DbSet<CustomPreference> CustomPreferences { get; set; }
        public DbSet<IMConversation> Conversations { get; set; }
        public DbSet<IMGift> IMGifts { get; set; }
        public DbSet<Invitation> Invitations { get; set; }
        public DbSet<InvitationLog> InvitationLogs { get; set; }
        public DbSet<M2BChatMessage> M2BChatMessages { get; set; }
        public DbSet<M2GChatMessage> M2GChatMessages { get; set; }
        public DbSet<M2MChatMessage> M2MChatMessages { get; set; }
        public DbSet<MemberContactData> MemberContactDatas { get; set; }
        public DbSet<MemberGift> MemberGifts { get; set; }
        public DbSet<MemberPreference> MemberPreferences { get; set; }
        public DbSet<MoneyPackage> MoneyPackages { get; set; }
        public DbSet<Relationship> Relationships { get; set; }
        public DbSet<Reward> Rewards { get; set; }
        public DbSet<SocailPreference> SocailPreferences { get; set; }
        public DbSet<SysMoneyPackage> SysMoneyPackages { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            IMEntityContextModelBuilder.Configure(modelBuilder);
            base.OnModelCreating(modelBuilder);
        }
    }

    internal static class IMEntityContextModelBuilder
    {
        public static void Configure(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<AddRelationshipLog>().Property(item => item.RedMoney).HasPrecision(18, 2);
            modelBuilder.Entity<IMGift>().Property(item => item.ExchangeRatio).HasPrecision(18, 2);
            modelBuilder.Entity<IMGift>().Property(item => item.Price).HasPrecision(18, 2);
            modelBuilder.Entity<MoneyPackage>().Property(item => item.Balance).HasPrecision(18, 2);
            modelBuilder.Entity<Reward>().Property(item => item.Money).HasPrecision(18, 2);
            modelBuilder.Entity<SysMoneyPackage>().Property(item => item.Amount).HasPrecision(18, 2);
            modelBuilder.Entity<Relationship>().HasIndex(item => new { item.PassportId, item.RelationshipPassportId });
            modelBuilder.Entity<AddRelationshipLog>().HasIndex(item => new { item.SourceId, item.TargetId, item.IsAccpet });
            modelBuilder.Entity<ChatGroup>().HasIndex(item => item.Id).IsUnique();
            modelBuilder.Entity<ChatGroupMember>().HasIndex(item => new { item.GroupId, item.PassportId });
            modelBuilder.Entity<IMConversation>().HasIndex(item => new { item.OwnerPassportId, item.ConversationId });
            modelBuilder.Entity<M2MChatMessage>().HasIndex(item => new { item.SourceId, item.TargetId, item.Date });
            modelBuilder.Entity<M2GChatMessage>().HasIndex(item => new { item.GroupId, item.Date });
        }
    }

    internal static class RepositoryConfigurationLocator
    {
        public static IConfiguration Build()
        {
            var basePath = ResolveBasePath();
            return new ConfigurationBuilder()
                .SetBasePath(basePath)
                .AddJsonFile("repository.json", optional: false)
                .Build();
        }

        private static string ResolveBasePath()
        {
            var assemblyFile = new Uri(typeof(IMEntityContext).Assembly.CodeBase).LocalPath;
            var assemblyDirectory = Path.GetDirectoryName(assemblyFile);
            var currentDirectory = Directory.GetCurrentDirectory();
            var appBaseDirectory = AppContext.BaseDirectory;

            var candidates = new[]
            {
                currentDirectory,
                Path.Combine(currentDirectory, "Horizon.Entities"),
                Path.Combine(currentDirectory, "..", "Horizon.Entities"),
                Path.Combine(currentDirectory, "..", "..", "Horizon.Entities"),
                Path.Combine(currentDirectory, "..", "..", "..", "Horizon.Entities"),
                Path.Combine(currentDirectory, "..", "..", "..", "..", "Horizon.Entities"),
                assemblyDirectory,
                Path.Combine(assemblyDirectory, "Horizon.Entities"),
                Path.Combine(assemblyDirectory, "..", "..", "..", "..", "Horizon.Entities"),
                Path.Combine(assemblyDirectory, "..", "..", "..", "..", "..", "Horizon.Entities"),
                appBaseDirectory,
                Path.Combine(appBaseDirectory, "Horizon.Entities"),
                Path.Combine(appBaseDirectory, "..", "..", "..", "..", "Horizon.Entities"),
                Path.Combine(appBaseDirectory, "..", "..", "..", "..", "..", "Horizon.Entities"),
                Path.Combine(System.AppContext.BaseDirectory, "Horizon.Entities"),
                Path.GetFullPath(Path.Combine(assemblyDirectory, "..", "..", "..", "..", "Horizon.Entities")),
            };

            foreach (var candidate in candidates)
            {
                try
                {
                    var fullPath = Path.GetFullPath(candidate);
                    var filePath = Path.Combine(fullPath, "repository.json");
                    System.Console.WriteLine($"[IMEntityContextDes] Checking: {filePath} Exists={File.Exists(filePath)}");
                    if (File.Exists(filePath))
                    {
                        return fullPath;
                    }
                }
                catch
                {
                }
            }

            throw new FileNotFoundException("未找到 Horizon.Entities/repository.json。", "repository.json");
        }

        private static string[] GetCandidateDirectories()
        {
            var currentDirectory = Directory.GetCurrentDirectory();
            var assemblyDirectory = Path.GetDirectoryName(typeof(IMEntityContext).Assembly.Location) ?? System.AppContext.BaseDirectory;
            var appBaseDirectory = System.AppContext.BaseDirectory;
            var assemblyFile = Path.GetDirectoryName(new Uri(typeof(IMEntityContext).Assembly.CodeBase).LocalPath);

            return new[]
            {
                currentDirectory,
                Path.Combine(currentDirectory, "Horizon.Entities"),
                Path.Combine(currentDirectory, "..", "Horizon.Entities"),
                Path.Combine(currentDirectory, "..", "..", "Horizon.Entities"),
                Path.Combine(currentDirectory, "..", "..", "..", "Horizon.Entities"),
                assemblyDirectory,
                Path.Combine(assemblyDirectory, "Horizon.Entities"),
                Path.Combine(assemblyDirectory, "..", "..", "..", "..", "Horizon.Entities"),
                assemblyFile,
                Path.Combine(assemblyFile, "Horizon.Entities"),
                appBaseDirectory,
                Path.Combine(appBaseDirectory, "Horizon.Entities"),
                Path.Combine(appBaseDirectory, "..", "..", "..", "..", "Horizon.Entities")
            };
        }
    }
}