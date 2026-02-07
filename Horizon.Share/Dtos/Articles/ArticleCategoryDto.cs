using Horizon.Core.Abstract.Enums;
using Horizon.Share.Commones;
using System;
using System.Collections.Generic;
using System.Text;

namespace Horizon.Share.Dtos.Articles
{
    /// <summary>
    /// 新建文章类型Dto
    /// </summary>
    public class CreateArticleCategoryDto
    {
        /// <summary>
        /// 父级Id
        /// </summary>
        public int? ParentId { get; set; }
        /// <summary>
        /// 文章类型名
        /// </summary>
        public string? Name { get; set; }
    }


    /// <summary>
    /// 修改文章类型Dto
    /// </summary>
    public class UpdateArticleCategoryDto : ArticleCategoryDto
    {
        public PassportType PassportType { get; set; }
        public string Passport { get; set; }
    }
    /// <summary>
    /// 文章类型Dto
    /// </summary>
    public class ArticleCategoryDto
    {
        public int Id { get; set; }
        /// <summary>
        /// 父级Id
        /// </summary>
        public int? ParentId { get; set; }
        /// <summary>
        /// 文章类型名
        /// </summary>
        public string? Name { get; set; }
    }
    /// <summary>
    /// 查询
    /// </summary>
    public class ArticleCategoryQueryDto : PageQuery
    {

    }
}
