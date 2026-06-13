using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Horizon.Model
{
    /// <summary>
    /// 群成员关系
    /// </summary>
    [Table("IM_ChatGroupMember")]
    [Horizon.Core.Abstract.EntityStorage("IM")]
    public class ChatGroupMember : Horizon.Core.Abstract.BaseNoneModel<Guid>
    {
        private Guid _id;

        [Key]
        [Required]
        [DatabaseGenerated(DatabaseGeneratedOption.None), Column(Order = 1)]
        public new Guid Id
        {
            get { return _id; }
            set { _id = value; base.Id = value; }
        }

        /// <summary>
        /// 群组Id
        /// </summary>
        public string GroupId { get; set; } = string.Empty;

        /// <summary>
        /// 成员通行证Id
        /// </summary>
        public string PassportId { get; set; } = string.Empty;

        /// <summary>
        /// 成员昵称
        /// </summary>
        public string Nickname { get; set; } = string.Empty;

        /// <summary>
        /// 成员头像
        /// </summary>
        public string Avatar { get; set; } = string.Empty;

        /// <summary>
        /// 群内昵称
        /// </summary>
        public string GroupNickname { get; set; } = string.Empty;

        /// <summary>
        /// 群角色，取值见 IMGroupMemberRole
        /// </summary>
        public int Role { get; set; }

        /// <summary>
        /// 加入时间戳
        /// </summary>
        public long JoinTime { get; set; }
    }
}