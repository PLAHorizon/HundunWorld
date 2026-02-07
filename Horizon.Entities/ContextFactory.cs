using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Horizon.Core.Abstract;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace Horizon.Entities
{
    /// <summary>
    /// 数据库设计阶段数据上下文工厂模板类
    /// 支持 SQL Server 、Mysql、Oracle
    /// </summary>
    /// <typeparam name="T">数据上下文类型参数</typeparam>
    public class ContextFactory<T> : IDesignTimeDbContextFactory<T> where T : DbContext
    {
        /// <summary>
        /// 数据仓库文件
        /// </summary>
        public string RepositoryConfigPath { get; set; } = "repository.json";
        /// <summary>
        /// 数据库配置名称
        /// </summary>
        public string RepositoryType { get; set; } = "SqlServer";
        /// <summary>
        /// 数据库类型
        /// </summary>
        public DataContextType DataContextType { get; } = DataContextType.SqlServer;
        public ContextFactory(DataContextType contextType = DataContextType.SqlServer)
        {
            DataContextType = contextType;
        }
        /// <summary>
        /// 创建数据库访问上下文实例
        /// </summary>
        /// <param name="args"></param>
        /// <returns></returns>
        public T CreateDbContext(string[] args)
        {
            string path = args == null ? RepositoryConfigPath : args[0];
            string repository = (args == null || args.Length < 2) ? RepositoryType : args[1];
            var config = new ConfigurationBuilder()
                 .SetBasePath(Directory.GetCurrentDirectory())
                 .AddJsonFile(path)
                 .Build();
            var optionsBuilder = new DbContextOptionsBuilder<T>();
            switch (DataContextType)
            {
                default:
                case DataContextType.SqlServer:
                    optionsBuilder.UseSqlServer(config.GetConnectionString(repository));
                    break;
                case DataContextType.Npgsql:
                    optionsBuilder.UseSqlServer(config.GetConnectionString(repository));
                    break;
                    //case DataContextType.MySQL:
                    //    optionsBuilder.UseMySQL(config.GetConnectionString(repository));
                    //    break;
                    //case DataContextType.Oracle:
                    //    optionsBuilder.UseOracle(config.GetConnectionString(repository));
                    //    break;
            }
            return FastActivator.Create<T>(args: optionsBuilder.Options);
        }
    }
}
