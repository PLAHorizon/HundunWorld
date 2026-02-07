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
    /// 聊天消息基类
    /// </summary>

    public class BaseChatMessage : BaseNoneModel<Guid>
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
        /// 应用Id，预留字段，默认为0
        /// </summary>
        public long ApplictionId { get; set; }
        /// <summary>
        /// 阅后即焚类型
        /// </summary>
        public BurnAfterReading BurnAfterReading { get; set; }
        /// <summary>
        /// 消息持续时间，对于消息传递两者的可阅读时间
        /// </summary>
        public long OtherTime { get; set; }
        /// <summary>
        /// 文本类型的消息
        /// </summary>
        public string Text { get; set; }
        /// <summary>
        /// 图片类型的消息，存储图片地址路径（相对路径）
        /// </summary>
        public string ImagePath { get; set; }
        /// <summary>
        /// 音频类型的消息，存储音频地址路径（相对路径）
        /// </summary>
        public string SoundPath { get; set; }
        /// <summary>
        /// 视频类型的消息，存储视频地址路径（相对路径）
        /// </summary>
        public string VideoPath { get; set; }
        /// <summary>
        /// 语音类型的消息，存储语音地址路径（相对路径）
        /// </summary>
        public string VoicePath { get; set; }
        /// <summary>
        /// 消息发送日期
        /// </summary>
        public DateTime Date { get; set; }
    }
}
