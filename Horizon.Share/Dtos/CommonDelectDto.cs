using Horizon.Core.Abstract.Enums;
using System;
using System.Collections.Generic;
using System.Text;

namespace Horizon.Share.Dtos
{
    /// <summary>
    /// 通用数据删除Dto
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class CommonDelectDto<T>
    {
        public string Passport { get; set; }
        public PassportType PassportType { get; set; }
        /// <summary>
        /// 数据键
        /// </summary>
        public IList<T> Ids { get; set; }
    }
}
