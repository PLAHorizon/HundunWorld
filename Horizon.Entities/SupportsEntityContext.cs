﻿﻿using Horizon.Core.Abstract;
using Horizon.Entities;
using Horizon.Model;
using Horizon.Model.Article;
using Horizon.Model.Supports;
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
    public class SupportsEntityContext : DbContext, IDesignTimeDbContextFactory<SupportsEntityContext>
    {
        DbContextOptions ContextOptions { get; }
        //public SupportsEntityContext()
        //{
        //    Database.Migrate();
        //}

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
                optionsBuilder.UseSqlServer(config.GetConnectionString("PostgreSqlSupports"));
            }
            // 运行时通过依赖注入接收配置，不调用base.OnConfiguring
        }

        public SupportsEntityContext(DbContextOptions options) : base(options)
        {
            ContextOptions = options;

        }

        #region  实体类

        /// <summary>
        ///文章注释、译文、赏析
        /// </summary>
        public DbSet<ArticleDescriptionSupport> ArticleDescriptionSupports { get; set; }
        /// <summary>
        ///文章
        /// </summary>
        public DbSet<ArticleSupport> ArticleSupports { get; set; }

        /// <summary>
        ///文章评论
        /// </summary>
        public DbSet<ArticleCommentSupport> ArticleCommentSupports { get; set; }
        #endregion


        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
        }

        public SupportsEntityContext CreateDbContext(string[] args)
        {

            var config = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("repository.json")
                .Build();
            var optionsBuilder = new DbContextOptionsBuilder<SupportsEntityContext>();
            optionsBuilder.UseSqlServer(config.GetConnectionString("PostgreSqlSupports"));
            return FastActivator.Create<SupportsEntityContext>(isnewInstance: false, args: optionsBuilder.Options);
        }
    }


}
