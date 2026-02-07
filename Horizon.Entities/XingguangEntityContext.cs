﻿﻿using Horizon.Core.Abstract;
using Horizon.Entities;
using Horizon.Model;
using Horizon.Model.Article;
using Horizon.Model.Supports;
using Horizon.Model.Xinggaung;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Horizon.Entities
{

    /// <summary>
    /// 仅限用于数据库设计生成或修改数据库结构使用
    /// 不要在生产环境中使用
    /// </summary>
    public class XingguangEntityContext : DbContext, IDesignTimeDbContextFactory<XingguangEntityContext>
    {
        DbContextOptions ContextOptions { get; }
        public XingguangEntityContext()
        {
            Database.Migrate();
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            // 只在设计时配置连接字符串
            if (DesignTimeContextChecker.IsDesignTime())
            {
                // 从 appsetting.json 中获取配置信息
                var config = new ConfigurationBuilder()
                    .SetBasePath(Directory.GetCurrentDirectory())
                    .AddJsonFile("repository.json")
                    .Build();

                // 定义要使用的数据库
                optionsBuilder.UseSqlServer(config.GetConnectionString("PostgreSqlXingguang"));
            }
            // 运行时通过依赖注入接收配置，不调用base.OnConfiguring
        }

        public XingguangEntityContext(DbContextOptions options) : base(options)
        {
            ContextOptions = options;

        }

        #region  实体类
        /// <summary>
        ///  用户关注集合
        /// </summary>
        public DbSet<Follows> FollowCollects { get; set; }
        /// <summary>
        /// 粉丝集合
        /// </summary>
        public DbSet<Fans> Fans { get; set; }
        /// <summary>
        /// 星光
        /// </summary>
        public DbSet<Stars> Stars { get; set; }
        #endregion


        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
        }

        public XingguangEntityContext CreateDbContext(string[] args)
        {

            var config = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("repository.json")
                .Build();
            var optionsBuilder = new DbContextOptionsBuilder<XingguangEntityContext>();
            optionsBuilder.UseSqlServer(config.GetConnectionString("PostgreSqlXingguang"));
            return FastActivator.Create<XingguangEntityContext>(isnewInstance: false, args: optionsBuilder.Options);
        }

    }

    // Add-Migration InitXingguang -Context XingguangEntityContext -OutputDir Migrations\Xingguang
}
