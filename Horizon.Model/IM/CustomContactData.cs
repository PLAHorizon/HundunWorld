using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Horizon.Core.Abstract;
using Horizon.Core.Abstract.Enums;

namespace Horizon.Model
{
    /// <summary>
    /// 会员自定义社交资料
    /// </summary>
    [Table("IM_CustomContactData")]
    public class CustomContactData : BaseNoneModel<Guid>
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
        public string PassportId { get; set; }
        public ContactDataType Type { get; set; }
        /// <summary>
        /// 资料名称
        /// </summary>
        public string DataName { get; set; }
    }

}
