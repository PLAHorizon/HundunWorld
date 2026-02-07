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
    /// 人脸检测
    /// 第三方提供的返回值
    /// </summary>
    [Table("Ai_FaceDetect")]
    public class FaceDetect : BaseNoneModel<Guid>
    {
        public FaceDetect()
        {
            Id = Guid.NewGuid();
        }
        private Guid _id;
        [Key, Required, DatabaseGenerated(DatabaseGeneratedOption.None), Column(Order = 1)]
        public new Guid Id
        {
            get { return _id; }
            set { _id = value; base.Id = value; }
        }
        /// <summary>
        /// 用户Id
        /// </summary>
        public string PassportId { get; set; }

        /// <summary>
        /// 日志Id
        /// </summary>
        public string LogId { get; set; }
        /// <summary>
        /// 人脸数目
        /// </summary>
        public int ResultNum { get; set; }
        /// <summary>
        /// 返回的结果集(json)
        /// </summary>
        public string Result { get; set; }
        /// <summary>
        /// Face++ 返回结果集 (Json)
        /// </summary>
        public string Faces { get; set; }
        /// <summary>
        /// 被检测的图片在Face++ 系统中的标识
        /// </summary>
        public string ImageId { get; set; }
        /// <summary>
        /// 用于区分对Face++ 每一次请求的唯一的字符串
        /// </summary>
        public string RequestId { get; set; }
        /// <summary>
        /// Face++整个请求所花费的时间，单位为毫秒
        /// </summary>
        public int TimeUsed { get; set; }
        /// <summary>
        /// 扫脸时间
        /// </summary>
        public DateTime Date { get; set; }
    }
}
