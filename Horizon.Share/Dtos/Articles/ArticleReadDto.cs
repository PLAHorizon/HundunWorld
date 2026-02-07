using Horizon.Core.Abstract;
using Horizon.Core.Abstract.Enums;
using Horizon.Core.Abstract.Helper;
using Horizon.Share.Commones;
using System;
using System.Collections.Generic;
using System.Text;

namespace Horizon.Share.Dtos.Articles
{
    /// <summary>
    /// 新建文章阅读进度Dto
    /// </summary>
    public class CreateArticleReadDto
    {
        public string Passport { get; set; }
        /// <summary>
        /// 文章Id
        /// </summary>
        public Guid ArticleId { get; set; }

        /// <summary>
        /// 章节Id
        /// </summary>
        public Guid ChapterId { get; set; }
        /// <summary>
        /// 章节序号
        /// </summary>
        public int Index { get; set; }

        /// <summary>
        /// 是否可以继续阅读下一章节
        /// </summary>
        public bool IsNext { get; set; }

        /// <summary>
        /// 是否是最后一章节
        /// </summary>
        public bool IsEnd { get; set; }


    }


    /// <summary>
    /// 修改文章阅读进度Dto
    /// </summary>
    public class UpdateArticleReadDto : CreateArticleReadDto
    {


    }
    /// <summary>
    /// 文章阅读进度Dto
    /// </summary>
    public class ArticleReadDto : CreateArticleReadDto
    {
        public Guid Id { get; set; }
        /// <summary>
        /// 阅读时间
        /// </summary>
        public DateTime ReadTime { get; set; }
        /// <summary>
        /// 阅读进度
        /// </summary>
        public decimal Progress { get; set; }

    }
    /// <summary>
    /// 查询阅读进度
    /// </summary>
    public class ArticleReadQueryDto : PageQuery
    {
        public string Passport { get; set; }
        /// <summary>
        /// 文章章节Id
        /// </summary>
        public Guid? ChapterId { get; set; }
        /// <summary>
        /// 文章Id
        /// </summary>
        public Guid? ArticleId { get; set; }
    }


}
