using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Horizon.Core.Abstract;

namespace Horizon.Model
{
    /// <summary>
    /// 社交会员自定义偏好
    /// </summary>
    [Table("IM_MemberPreference")]
    public class MemberPreference : BaseNoneModel<Guid>
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
        /// 通行证Id
        /// </summary>
        public string PassportId { get; set; }
        /// <summary>
        /// 偏好Id
        /// </summary>
        public long PreferenceId { get; set; }
    }
}
