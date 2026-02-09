﻿using Horizon.Core.Abstract;
using Horizon.Entities;
using Horizon.Model;
using Horizon.Model.Basic;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Logging.Debug;
using System.IO;

namespace Horizon.Entities
{

    /// <summary>
    /// 仅限用于数据库设计生成或修改数据库结构使用
    /// 不要在生产环境中使用
    /// </summary>
    public class BasicEntityContextDes : DbContext, IDesignTimeDbContextFactory<BasicEntityContextDes>
    {
        DbContextOptions ContextOptions { get; }

        private static readonly ILoggerFactory _loggerFactory
     = LoggerFactory.Create(builder => { builder.AddConsole(); });
        #region 设计
        public BasicEntityContextDes()
        {
            // Database.Migrate();
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

                optionsBuilder.UseSqlServer(config.GetConnectionString("BasicSqlServer"));
            }
            // 运行时通过依赖注入接收配置，不调用base.OnConfiguring
        }
        public BasicEntityContextDes CreateDbContext(string[] args)
        {

            var config = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("repository.json")
                .Build();
            var optionsBuilder = new DbContextOptionsBuilder<BasicEntityContextDes>();
            optionsBuilder.UseSqlServer(config.GetConnectionString("BasicSqlServer")).UseLoggerFactory(_loggerFactory).UseRootApplicationServiceProvider();
            return FastActivator.Create<BasicEntityContextDes>(isnewInstance: true, args: optionsBuilder.Options);
        }
        #endregion
        public BasicEntityContextDes(DbContextOptions options) : base(options)
        {
            ContextOptions = options;
            Database.AutoSavepointsEnabled = true;
            Database.AutoTransactionsEnabled = true;
            ChangeTracker.AutoDetectChangesEnabled = true;
            ChangeTracker.AcceptAllChanges();

        }

        #region  实体类
        public DbSet<Apps> Apps { get; set; }
        public DbSet<DistrictDatabase> DistrictDatabases { get; set; }
        public DbSet<Labe> Labes { get; set; }
        public DbSet<MemberLabe> MemberLabes { get; set; }
        public DbSet<Passport> Passports { get; set; }
        public DbSet<PassportIds> PassportIds { get; set; }
        public DbSet<PassportFlag> PassportFlags { get; set; }
        public DbSet<Region> Regions { get; set; }
        public DbSet<RoleInfo> RoleInfos { get; set; }
        public DbSet<RolePrivilegeInfo> RolePrivilegeInfos { get; set; }
        public DbSet<SysManager> SysManagers { get; set; }
        public DbSet<User> Users { get; set; }
        public DbSet<Organization> Organizations { get; set; }
        public DbSet<OrganizationCategory> OrganizationCategories { get; set; }

        #endregion



        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // 配置自引用外键，避免循环级联删除
            modelBuilder.Entity<OrganizationCategory>()
                .HasOne(x => x.Parent)
                .WithMany()
                .HasForeignKey(x => x.ParentId)
                .OnDelete(DeleteBehavior.NoAction);

            modelBuilder.Entity<Organization>()
                .HasOne(x => x.Parent)
                .WithMany()
                .HasForeignKey(x => x.ParentId)
                .OnDelete(DeleteBehavior.NoAction);

            modelBuilder.Entity<Region>()
                .HasOne(x => x.Parent)
                .WithMany()
                .HasForeignKey(x => x.ParentId)
                .OnDelete(DeleteBehavior.NoAction);
        }


    }


    public class BasicEntityContext : DbContext
    {
        DbContextOptions ContextOptions { get; }

        private static readonly ILoggerFactory _loggerFactory
     = LoggerFactory.Create(builder => { builder.AddConsole(); });
        #region 设计
        public BasicEntityContext()
        {
            //  Database.Migrate();
        }
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            //if (!optionsBuilder.IsConfigured)
            //{
            //    var config = new ConfigurationBuilder()
            //        .SetBasePath(Directory.GetCurrentDirectory())
            //        .AddJsonFile("repository.json")
            //        .Build();

            //    optionsBuilder.UseSqlServer(config.GetConnectionString("BasicSqlServer"));
            //}
            base.OnConfiguring(optionsBuilder);
        }
        public BasicEntityContext CreateDbContext(string[] args)
        {

            var config = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("repository.json")
                .Build();
            var optionsBuilder = new DbContextOptionsBuilder<BasicEntityContext>();
            optionsBuilder.UseSqlServer(config.GetConnectionString("BasicSqlServer")).UseLoggerFactory(_loggerFactory).UseRootApplicationServiceProvider();
            return FastActivator.Create<BasicEntityContext>(isnewInstance: true, args: optionsBuilder.Options);
        }
        #endregion
        public BasicEntityContext(DbContextOptions options) : base(options)
        {
            ContextOptions = options;
            Database.AutoSavepointsEnabled = true;
            Database.AutoTransactionBehavior = AutoTransactionBehavior.WhenNeeded;
            ChangeTracker.AutoDetectChangesEnabled = true;
            ChangeTracker.AcceptAllChanges();

        }

        #region  实体类
        public DbSet<Apps> Apps { get; set; }
        public DbSet<DistrictDatabase> DistrictDatabases { get; set; }
        public DbSet<Labe> Labes { get; set; }
        public DbSet<MemberLabe> MemberLabes { get; set; }
        public DbSet<Passport> Passports { get; set; }
        public DbSet<PassportIds> PassportIds { get; set; }
        public DbSet<PassportFlag> PassportFlags { get; set; }
        public DbSet<Region> Regions { get; set; }
        public DbSet<RoleInfo> RoleInfos { get; set; }
        public DbSet<RolePrivilegeInfo> RolePrivilegeInfos { get; set; }
        public DbSet<SysManager> SysManagers { get; set; }
        public DbSet<User> Users { get; set; }
        public DbSet<Organization> Organizations { get; set; }
        public DbSet<OrganizationCategory> OrganizationCategories { get; set; }

        #endregion



        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // 配置自引用外键，避免循环级联删除
            modelBuilder.Entity<OrganizationCategory>()
                .HasOne(x => x.Parent)
                .WithMany()
                .HasForeignKey(x => x.ParentId)
                .OnDelete(DeleteBehavior.NoAction);

            modelBuilder.Entity<Organization>()
                .HasOne(x => x.Parent)
                .WithMany()
                .HasForeignKey(x => x.ParentId)
                .OnDelete(DeleteBehavior.NoAction);

            modelBuilder.Entity<Region>()
                .HasOne(x => x.Parent)
                .WithMany()
                .HasForeignKey(x => x.ParentId)
                .OnDelete(DeleteBehavior.NoAction);
        }


    }


}
