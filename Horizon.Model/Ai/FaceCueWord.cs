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
    /// 扫脸后的提示词句
    /// </summary>
    [Table("Ai_FaceCueWord")]
    public class FaceCueWord : BaseNoneModel<Guid>
    {
        public FaceCueWord()
        {
            Id = Guid.NewGuid();
        }
        private Guid _id;
        [Key, DatabaseGenerated(DatabaseGeneratedOption.None), Column(Order = 1)]
        public new Guid Id
        {
            get { return _id; }
            set { _id = value; base.Id = value; }
        }
        /// <summary>
        /// 次数
        /// </summary>
        public long ShowNumber { get; set; }
        /// <summary>
        /// 词句
        /// </summary>
        public string Word { get; set; }
        /// <summary>
        /// 使用性别
        /// </summary>
        public Gender Gender { get; set; }
    }
}
