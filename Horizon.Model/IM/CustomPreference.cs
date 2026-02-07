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
    /// 社交会员自定义偏好
    /// </summary>
    [Table("IM_CustomPreference")]
    public class CustomPreference : BaseNoneModel<Guid>
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
        /// <summary>
        /// 偏好类型
        /// </summary>
        public SocialPreferenceType Type { get; set; }
        /// <summary>
        /// 偏好
        /// </summary>
        public string Preference { get; set; }
    }
}
