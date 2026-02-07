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
    /// 社交偏好
    /// </summary>
    [Table("IM_Preference")]
    public class SocailPreference : BaseIdentityModel<long>
    {
        private long _id;
        [Key]
        [Required]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity), Column(Order = 1)]

        public new long Id
        {
            get { return _id; }
            set { _id = value; base.Id = value; }
        }
        /// <summary>
        /// 社交偏好类型
        /// </summary>
        public SocialPreferenceType Type { get; set; }
        /// <summary>
        /// 偏好
        /// </summary>
        public string Preference { get; set; }
    }
}
