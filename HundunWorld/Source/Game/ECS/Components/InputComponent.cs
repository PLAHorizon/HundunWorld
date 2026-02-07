using Arch.Core.Utils;
using FlaxEngine;

namespace HundunWorld.Game.ECS.Components
{
    /// <summary>
    /// 输入组件，用于存储玩家输入状态
    /// </summary>
    public struct InputComponent
    {
        /// <summary>
        /// 水平轴输入
        /// </summary>
        public float Horizontal;

        /// <summary>
        /// 垂直轴输入
        /// </summary>
        public float Vertical;

        /// <summary>
        /// 鼠标X轴移动
        /// </summary>
        public float MouseX;

        /// <summary>
        /// 鼠标Y轴移动
        /// </summary>
        public float MouseY;

        /// <summary>
        /// 鼠标滚轮
        /// </summary>
        public float MouseWheel;

        /// <summary>
        /// 鼠标左键是否按下
        /// </summary>
        public bool Fire1;

        /// <summary>
        /// 鼠标右键是否按下
        /// </summary>
        public bool Fire2;

        /// <summary>
        /// 跳跃键是否按下
        /// </summary>
        public bool Jump;

        /// <summary>
        /// 是否正在控制相机
        /// </summary>
        public bool IsCameraControlling;

        /// <summary>
        /// 鼠标屏幕位置
        /// </summary>
        public Float2 MouseScreenPosition;

        /// <summary>
        /// 鼠标世界位置
        /// </summary>
        public Vector3 MouseWorldPosition;

        /// <summary>
        /// 是否点击了地面
        /// </summary>
        public bool GroundClicked;

        public override string ToString()
        {
            return $"Input(H:{Horizontal}, V:{Vertical}, MX:{MouseX}, MY:{MouseY})";
        }
    }
}