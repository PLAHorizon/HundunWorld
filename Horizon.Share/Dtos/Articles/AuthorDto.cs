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
    /// 新建作者Dto
    /// </summary>
    public class CreateAuthorDto
    {
        public string Passport { get; set; }
        /// <summary>
        /// 作者别名
        /// </summary>
        public string? Name { get; set; }

        /// <summary>
        /// 作者头像
        /// </summary>
        public string? Avatar { get; set; }

        /// <summary>
        /// 国家
        /// </summary>
        public string? Country { get; set; }


        /// <summary>
        /// 国家代码
        /// </summary>
        public string? CountryCode { get; set; }


        /// <summary>
        /// 区域码
        /// </summary>
        public string? AreaCode { get; set; }

        /// <summary>
        /// 省
        /// </summary>
        public string? Province { get; set; }

        /// <summary>
        /// 市
        /// </summary>
        public string? City { get; set; }


        /// <summary>
        /// 区
        /// </summary>
        public string? Area { get; set; }


        /// <summary>
        /// 性别
        /// </summary>
        public Gender Gender { get; set; }
        /// <summary>
        /// 邮箱
        /// </summary>
        public string? Email { get; set; }

        /// <summary>
        /// 手机号
        /// </summary>
        public string? Phone { get; set; }
        /// <summary>
        /// 审核状态
        /// </summary>
        public AuditStatus AuditStatus { get; set; }
        /// <summary>
        /// 简介
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// 简介
        /// </summary>
        public string? ShortDescription { get; set; }
    }


    /// <summary>
    /// 修改作者Dto
    /// </summary>
    public class UpdateAuthorDto : CreateAuthorDto
    {
        public Guid Id { get; set; }



    }
    /// <summary>
    /// 作者Dto
    /// </summary>
    public class AuthorDto : CreateAuthorDto
    {
        public Guid Id { get; set; }
        public string GenderString => Gender.GetDescription();


        public string AuditStatusString => AuditStatus.GetDescription();
    }
    /// <summary>
    /// 查询作者
    /// </summary>
    public class AuthorQueryDto : PageQuery
    {
        /// <summary>
        /// 区域码
        /// </summary>
        public string? AreaCode { get; set; }
        /// <summary>
        /// 邮箱
        /// </summary>
        public string? Email { get; set; }

        /// <summary>
        /// 手机号
        /// </summary>
        public string? Phone { get; set; }
        /// <summary>
        /// 简介
        /// </summary>
        public string? Description { get; set; }
    }


}
