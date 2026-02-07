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
    /// 社交资料
    /// </summary>
    [Table("IM_ContactData")]
    public class ContactData : BaseIdentityModel<long>
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
        public ContactDataType Type { get; set; }
        /// <summary>
        /// 资料名称
        /// </summary>
        public string DataName { get; set; }
    }
}
