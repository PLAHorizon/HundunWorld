using FlaxEngine;
using FlaxEngine.GUI;

namespace HundunWorld.Game.UI.Components
{
    /// <summary>
    /// 按钮波纹动画效果
    /// 在点击位置发出圆形波纹，向外扩散同时淡出
    /// </summary>
    public class RippleEffect : Control
    {
        private Float2 _center;
        private float _elapsed;
        private float _duration = 0.3f;
        private bool _isActive;
        private float _maxRadius;

        // 金色波纹颜色 (RGB 212,175,55)
        private static readonly Color RippleColor = new Color(212f / 255f, 175f / 255f, 55f / 255f, 1f);

        public RippleEffect()
        {
            BackgroundColor = Color.Transparent;
            IsScrollable = false;
        }

        /// <summary>
        /// 启动一次波纹动画
        /// </summary>
        /// <param name="center">波纹中心点（控件本地坐标）</param>
        /// <param name="maxRadius">波纹最大半径</param>
        public void StartRipple(Float2 center, float maxRadius)
        {
            _center = center;
            _maxRadius = maxRadius;
            _elapsed = 0f;
            _isActive = true;
        }

        public override void Update(float deltaTime)
        {
            base.Update(deltaTime);

            if (!_isActive) return;

            _elapsed += deltaTime;
            if (_elapsed >= _duration)
            {
                _isActive = false;
            }
        }

        public override void Draw()
        {
            base.Draw();

            if (!_isActive || _maxRadius <= 0f) return;

            float t = _elapsed / _duration;
            if (t > 1f) t = 1f;

            float radius = _maxRadius * t;
            float alpha = 0.5f * (1f - t);

            var color = new Color(RippleColor.R, RippleColor.G, RippleColor.B, alpha);
            Render2D.FillRectangle(new Rectangle(_center.X - radius, _center.Y - radius, radius * 2, radius * 2), color);
        }
    }
}
