using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;
using Horizon.Core.Abstract;
using Horizon.Core.Abstract.Enums;

namespace Horizon.Model
{
    /// <summary>
    /// 好友关系
    /// </summary>
    [Table("IM_Relationship"), DataContract]
    public class Relationship : BaseNoneModel<Guid>
    {
        public Relationship()
        {
            Id = Guid.NewGuid();
            Date = DateTime.Now;
        }
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
        /// 自己Id
        /// </summary>
        public string PassportId { get; set; }
        /// <summary>
        /// 对方Id
        /// </summary>
        public string RelationshipPassportId { get; set; }
        /// <summary>
        /// 备注名
        /// </summary>
        public string RemarkName { get; set; }
        /// <summary>
        /// 好友关系状态
        /// </summary>
        public RelationshipStatus RelationshipStatus { get; set; }
        /// <summary>
        /// 首次添加对方日期
        /// </summary>
        public DateTime Date { get; set; }
    }
}
