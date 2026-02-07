using Horizon.Core.Abstract;
using Horizon.Core.Abstract.Enums;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Runtime.Serialization;
using System.Text;

namespace Horizon.Model
{
    /// <summary>
    /// 行政区域模型
    /// </summary>
    [Table("Basic_Sys_Region"), DataContract]
    [EntityStorage("Basic")]
    public class Region : BaseModel<int>
    {
        /// <summary>
        /// 区域编号
        /// </summary>
        public new int Id { get; set; }
        /// <summary>
        /// 区域名称
        /// </summary>
        [Comment("区域名称")]
        public string Name { get; set; }
        /// <summary>
        /// 区域简称
        /// </summary>
        [Comment("区域简称")]
        public string ShortName { get; set; }

        /// <summary>
        /// 状态(0:正常,9删除)
        /// </summary>
        [Comment("状态")]
        public RegionStatus Status { get; set; }


        /// <summary>
        /// 下属区域
        /// </summary>

        //[ScriptIgnore]//使用JavaScriptSerializer序列化时不序列化此字段
        //[IgnoreDataMember]//使用DataContractJsonSerializer序列化时不序列化此字段
        //[Newtonsoft.Json.JsonIgnore]//使用JsonConvert序列化时不序列化此字段
        [NotMapped]
        public List<Region> Sub { get; set; }

        /// <summary>
        /// 上属区域
        /// </summary>
        //[ScriptIgnore]//使用JavaScriptSerializer序列化时不序列化此字段
        //[IgnoreDataMember]//使用DataContractJsonSerializer序列化时不序列化此字段
        [Newtonsoft.Json.JsonIgnore]//使用JsonConvert序列化时不序列化此字段
        [ForeignKey("ParentId")]
        public virtual Region Parent { get; set; }

        /// <summary>
        /// 上级区域编号
        /// </summary>
        //[ScriptIgnore]//使用JavaScriptSerializer序列化时不序列化此字段
        //[IgnoreDataMember]//使用DataContractJsonSerializer序列化时不序列化此字段
        [Newtonsoft.Json.JsonIgnore]//使用JsonConvert序列化时不序列化此字段
        [Comment("上级区域编号")]
        public int ParentId { get; set; }

        /// <summary>
        /// 区域行政等级
        /// </summary>
        //[ScriptIgnore]//使用JavaScriptSerializer序列化时不序列化此字段
        //[IgnoreDataMember]//使用DataContractJsonSerializer序列化时不序列化此字段
        [Newtonsoft.Json.JsonIgnore]//使用JsonConvert序列化时不序列化此字段
        [Comment("区域行政等级")]
        public RegionLevel Level { get; set; }



        /// <summary>
        /// 获取当前区域到最上级的名称串
        /// </summary>
        /// <param name="split">名称之前的分隔符，默认为空格</param>
        /// <returns></returns>
        public string GetNamePath(string split = " ")
        {
            if (Parent != null)
                return string.Format("{0}{1}{2}", Parent.GetNamePath(), split, Name);
            return Name;
        }

        /// <summary>
        /// 获取当前区域到最上级的id串
        /// </summary>
        /// <param name="split">名称之前的分隔符，默认为逗号</param>
        /// <returns></returns>
        public string GetIdPath(string split = ",")
        {
            if (Parent != null)
                return string.Format("{0}{1}{2}", Parent.GetIdPath(), split, Id);
            return Id.ToString();
        }
    }
}
