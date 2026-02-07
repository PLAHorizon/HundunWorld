using System;
using System.Collections.Generic;
using System.Text;

namespace Horizon.Share.VMs
{
    /// <summary>
    /// 数据返回类
    /// </summary>
    public class ResultVM<T>
    {
        public static ResultVM<C> Clone<C, T>(ResultVM<T> vM, C data)
        {
            return new ResultVM<C> { Code = vM.Code, Data = data, ErrorMessage = vM.ErrorMessage, IsSuccess = vM.IsSuccess };
        }
        /// <summary>
        /// 状态码
        /// </summary>
        public int Code { get; set; } = 200;
        /// <summary>
        /// 错误消息
        /// </summary>
        public string ErrorMessage { get; set; }
        /// <summary>
        /// 执行是否成功
        /// </summary>
        public bool IsSuccess { get; set; } = false;
        /// <summary>
        /// 返回的数据
        /// </summary>
        public T Data { get; set; }
    }
}
