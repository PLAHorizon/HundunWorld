using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Horizon.Core.Abstract;

namespace Horizon.Core
{
    /// <summary>
    /// 分布式服务应用实例对象信息
    /// Distributed Application Service
    /// </summary>
    public class DASObject
    {
        /// <summary>
        /// 分布式服务应用实例对象信息
        /// </summary>
        public DASObject()
        {
            IsOpen = true; Radius = 1000 * 1000;
        }
        public Type ServiceType { get; set; }
        /// <summary>
        /// 服务物理主机的中心纬度
        /// </summary>       
        public decimal Lat { get; set; }
        /// <summary>
        /// 服务物理主机的中心经度
        /// </summary>       
        public decimal Lng { get; set; }
        /// <summary>
        /// 服务半径，单位:米;依据服务能力提供的服务半径
        /// </summary>       
        public int Radius { get; set; }
        /// <summary>
        /// 是否提供向全区域的兼容服务，即客户端应用无法确定由谁来提供服务时为其提供可靠服务
        /// </summary>       
        public bool IsOpen { get; set; }

        /// <summary>
        ///数据应用类型
        /// </summary>
        public AppType AppType { get; set; }
        /// <summary>
        /// 应用Id
        /// </summary>
        public long APPId { get; set; }
        /// <summary>
        /// 区域Id
        /// </summary>
        public long AreaId { get; set; }
        /// <summary>
        /// 服务Id
        /// </summary>
        public long ServerId { get; set; }
    }
}
