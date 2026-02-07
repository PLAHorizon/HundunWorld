using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Entities
{
    public class DatabaseName
    {
        /// <summary>
        /// 基础数据
        /// </summary>
        public const string Basic = nameof(Basic);
        /// <summary>
        /// 游戏数据
        /// </summary>
        public const string Game = nameof(Game);
        /// <summary>
        /// 文章
        /// </summary>
        public const string Article = nameof(Article);
        /// <summary>
        /// 点赞数据库
        /// </summary>
        public const string Supports = nameof(Supports);
        /// <summary>
        /// 星光数据库
        /// </summary>
        public const string Xingguang = nameof(Xingguang);
        /// <summary>
        /// 数据实体类类库程序集名称
        /// </summary>
        public const string ModelAssembly = "Horizon.Model";

    }
}
