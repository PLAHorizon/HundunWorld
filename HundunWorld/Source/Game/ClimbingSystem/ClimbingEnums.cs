using FlaxEngine;

namespace HundunWorld.Game.ClimbingSystem
{
    /// <summary>
    /// 攀爬状态枚举
    /// </summary>
    public enum ClimbingState
    {
        /// <summary>
        /// 无攀爬状态
        /// </summary>
        None,
        
        /// <summary>
        /// 接近可攀爬边缘
        /// </summary>
        ApproachingEdge,
        
        /// <summary>
        /// 抓住边缘
        /// </summary>
        GrabbingEdge,
        
        /// <summary>
        /// 悬挂在边缘
        /// </summary>
        Hanging,
        
        /// <summary>
        /// 攀爬到顶部
        /// </summary>
        Mantling,
        
        /// <summary>
        /// 垂直攀爬
        /// </summary>
        Climbing,
        
        /// <summary>
        /// 攀爬结束
        /// </summary>
        Finished
    }
    
    /// <summary>
    /// 攀爬类型枚举
    /// </summary>
    public enum ClimbType
    {
        /// <summary>
        /// 低边缘攀爬（如窗台）
        /// </summary>
        LowEdge,
        
        /// <summary>
        /// 高边缘攀爬（如墙壁顶部）
        /// </summary>
        HighEdge,
        
        /// <summary>
        /// 垂直墙面攀爬
        /// </summary>
        VerticalWall,
        
        /// <summary>
        /// 水平横杆攀爬
        /// </summary>
        HorizontalBar
    }
}