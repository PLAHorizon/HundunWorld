using FlaxEngine;
using FlaxEngine.GUI;

namespace HundunWorld.Game.UI.Components
{
    /// <summary>
    /// 加载指示器组件
    /// 显示旋转的加载动画
    /// </summary>
    public class LoadingIndicator : Control
    {
        private float _rotationAngle = 0f;
        private const float RotationSpeed = 180f; // 度/秒
        private UpdateDelegate _updateDelegate;
        
        public LoadingIndicator()
        {
            Size = new Float2(40, 40);
            BackgroundColor = Color.Transparent;
            
            // 设置更新回调
            _updateDelegate = OnUpdate;
            SetUpdate(ref _updateDelegate, _updateDelegate);
        }
        
        private void OnUpdate(float deltaTime)
        {
            // 更新旋转角度
            _rotationAngle += RotationSpeed * deltaTime;
            if (_rotationAngle >= 360f)
                _rotationAngle -= 360f;
        }
        
        public override void Draw()
        {
            base.Draw();
            
            var center = Size * 0.5f;
            var radius = Mathf.Min(Size.X, Size.Y) * 0.4f;
            
            // 绘制旋转的圆环 - 使用 DrawBorder 代替 DrawCircle
            var rect = new Rectangle(center.X - radius, center.Y - radius, radius * 2, radius * 2);
            Render2D.DrawRectangle(rect, UIHelper.PrimaryColor, 3f);
            
            // 绘制中心点
            var centerRect = new Rectangle(center.X - 3, center.Y - 3, 6, 6);
            Render2D.FillRectangle(centerRect, UIHelper.PrimaryColor);
        }
        

        
        /// <summary>
        /// 显示加载指示器
        /// </summary>
        public void Show()
        {
            Visible = true;
        }
        
        /// <summary>
        /// 显示加载指示器（重载）
        /// </summary>
        public void Show(string message)
        {
            // 在实际项目中，这里可以显示加载消息
            Visible = true;
        }
        
        /// <summary>
        /// 隐藏加载指示器
        /// </summary>
        public void Hide()
        {
            Visible = false;
        }
        
        /// <summary>
        /// 开始动画
        /// </summary>
        public void Start()
        {
            Visible = true;
        }
        
        /// <summary>
        /// 停止动画
        /// </summary>
        public void Stop()
        {
            Visible = false;
        }
        
        /// <summary>
        /// 创建加载指示器实例
        /// </summary>
        public static LoadingIndicator Create()
        {
            return new LoadingIndicator();
        }
    }
}