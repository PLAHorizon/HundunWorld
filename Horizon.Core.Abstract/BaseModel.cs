using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Runtime.Serialization;
using System.Text;

namespace Horizon.Core.Abstract
{
    public interface IBaseModel<T>
    {

        /// <summary>
        /// 主键
        /// </summary> 
        public T Id { get; set; }
        /// <summary>
        /// 数据在业务中是否有效
        /// </summary>
        [Required]
        [Column(Order = 39), TableDescription(Name = "IsValid", Order = "39", Description = "数据在业务中是否有效")]
        bool IsValid { get; set; }

    }
    /// <summary>
    /// 数据实体类基类
    /// </summary>
    /// <typeparam name="T">主键数据类型类型参数</typeparam>
    public class BaseModel<T> : IBaseModel<T>
    {
        private T _id;
        /// <summary>
        /// 主键
        /// </summary> 
        public T Id { get { return _id; } set { _id = value; } }
        /// <summary>
        /// 数据在业务中是否有效
        /// </summary>
        [Required]
        [Column(Order = 59), TableDescription(Name = "IsValid", Order = "59", Description = "数据在业务中是否有效")]
        public bool IsValid { get; set; } = true;

    }
    /// <summary>
    /// 数据实体类基类
    /// </summary>
    /// <typeparam name="T">主键数据类型类型参数</typeparam>
    public class BaseAggregateRootModel<T> : BaseModel<T>, IAggregateRoot, IPassport
    {
        /// <summary>
        /// 创建人通行证
        /// </summary>
        [Required]
        [StringLength(32), Column(TypeName = "varchar(32)", Order = 55), TableDescription(TypeName = "varchar(32)", Name = "Passport", Order = "55", Description = "创建通行证")]
        public string Passport { get; set; }
        /// <summary>
        /// 创建时间
        /// </summary>
        [Required]
        [Column(TypeName = "datetimeoffset(7)", Order = 56), TableDescription(TypeName = "datetimeoffset(7)", Name = "Passport", Order = "56", Description = "创建时间")]
        public DateTime CreateTime { get; set; }
        /// <summary>
        /// 修改人通行证
        /// </summary>       
        [StringLength(32), Column(TypeName = "varchar(32)", Order = 57), TableDescription(TypeName = "varchar(32)", Name = "ModifyPassport", Order = "57", Description = "修改通行证")]
        public string ModifyPassport { get; set; }
        /// <summary>
        /// 修改时间
        /// </summary>        
        [Column(TypeName = "datetimeoffset(7)", Order = 58), TableDescription(TypeName = "datetimeoffset(7)", Name = "ModifyTime", Order = "58", Description = "修改时间")]
        public DateTime? ModifyTime { get; set; }

        /// <summary>
        /// 刷新通行证令牌
        /// </summary>
        public string Refresh()
        {
            ModifyTime = DateTime.Now;
            return Passport;
        }

        /// <summary>
        /// 撤销通行证
        /// </summary>
        public void Revoke()
        {
            IsValid = false;
            ModifyTime = DateTime.Now;
        }

        /// <summary>
        /// 验证通行证是否有效
        /// </summary>
        public bool Validate()
        {
            return IsValid && !string.IsNullOrWhiteSpace(Passport);
        }
    }
    /// <summary>
    /// 数据实体类基类,主键不自增
    /// </summary>        
    [Serializable]
    public class BaseNoneModel<T> : BaseModel<T>
    {
        private T _id;
        /// <summary>
        /// 主键
        /// </summary>       
        [Key]
        [Required]
        [DatabaseGenerated(DatabaseGeneratedOption.None), DataMember, Column(Order = 1)]
        public new T Id { get { return _id; } set { _id = value; } }

    }
    /// <summary>
    /// 数据实体类基类,主键不自增
    /// </summary>        
    [Serializable]
    public class BaseNoneAggregateRootModel<T> : BaseAggregateRootModel<T>
    {
        private T _id;
        /// <summary>
        /// 主键
        /// </summary>       
        [Key]
        [Required]
        [DatabaseGenerated(DatabaseGeneratedOption.None), DataMember, Column(Order = 1)]
        public new T Id { get { return _id; } set { _id = value; } }


    }
    /// <summary>
    /// 数据实体类基类，主键自增
    /// </summary>
    /// <typeparam name="T">主键数据类型类型参数</typeparam>
    [Serializable]
    public class BaseIdentityModel<T> : BaseModel<T>
    {
        private T _id;
        /// <summary>
        /// 主键
        /// </summary>       
        [Key]
        [Required]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity), DataMember, Column(Order = 1)]
        public new T Id { get { return _id; } set { _id = value; } }

    }
    /// <summary>
    /// 数据实体类基类，主键自增
    /// </summary>
    /// <typeparam name="T">主键数据类型类型参数</typeparam>
    [Serializable]
    public class BaseIdentityAggregateRootModel<T> : BaseAggregateRootModel<T>
    {
        private T _id;
        /// <summary>
        /// 主键
        /// </summary>       
        [Key]
        [Required]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity), DataMember, Column(Order = 1)]
        public new T Id { get { return _id; } set { _id = value; } }
    }

}
