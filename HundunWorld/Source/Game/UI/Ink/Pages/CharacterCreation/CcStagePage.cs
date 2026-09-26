using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.Ink.Components;
using HundunWorld.Game.UI.StyleSystem;
using System;

namespace HundunWorld.Game.UI.Ink.Pages.CharacterCreation
{
    /// <summary>
    /// 一次创角流程的跨页状态。
    /// 页面由 <see cref="InkPageRouter"/> 的工厂每次导航时新建（旧页会被销毁），
    /// 所以性别 / 面容预设 / 姓名这类跨步骤数据必须放在页面之外。
    /// </summary>
    public sealed class CcSession
    {
        /// <summary>当前创角会话。</summary>
        public static readonly CcSession Current = new CcSession();

        /// <summary>0 = 男，1 = 女。</summary>
        public int Gender;

        /// <summary>面容风格分类：0 = 风雅，1 = 写实。</summary>
        public int StyleCategory;

        /// <summary>选中的面容预设下标，-1 表示未选。</summary>
        public int FacePresetIndex = -1;

        /// <summary>选中的衣装 / 体型下标，写入 <c>AppearanceInfo.Clothing</c>。</summary>
        public int BodyIndex;

        /// <summary>发色 / 瞳色 / 肤色下标，分别写入 <c>AppearanceInfo.HairColor / EyeColor / SkinColor</c>。</summary>
        public int HairColorIndex;
        public int EyeColorIndex;
        public int SkinColorIndex;

        /// <summary>角色名。</summary>
        public string CharacterName = string.Empty;

        /// <summary>
        /// 重置为新一次创角（从选角页点「创建角色」进入时调用）。
        /// </summary>
        public void Reset()
        {
            Gender = 0;
            StyleCategory = 0;
            FacePresetIndex = -1;
            BodyIndex = 0;
            HairColorIndex = 0;
            EyeColorIndex = 0;
            SkinColorIndex = 0;
            CharacterName = string.Empty;
        }
    }

    /// <summary>
    /// 创角 / 选角流程页面的公共舞台。
    /// <para>
    /// 对齐参考图（图片/1-7.png）的版式：雾白背景铺满、角色立绘偏右、
    /// 左侧留白放操作控件、右下角鎏金「Space 下一步」确认块、左下角 ID 水印。
    /// 页面在 <see cref="InkPageShell"/> 提供的 1920×1080 设计空间中布局，
    /// 因此下面的坐标都是设计像素（按 2560×1440 参考图 ×0.75 换算）。
    /// </para>
    /// </summary>
    public abstract class CcStagePage : Panel, IInkPage
    {
        /// <summary>
        /// 确认块尺寸与位置：按参考图实测为 263x56、**紧贴窗口右边缘**、底边距约 45
        /// （图片/1、4、6、7 的金色块右边缘都到画面最右）。
        /// </summary>
        protected const float ConfirmW = 263f;
        protected const float ConfirmH = 56f;
        protected const float ConfirmBottom = 45f;

        /// <summary>左侧操作栏起始 x（参考图内容区左边界）。</summary>
        protected const float RailX = 80f;

        /// <summary>角色立绘预览区（屏幕右半，全高）。</summary>
        protected const float PreviewX = 860f;
        protected const float PreviewW = 1060f;

        private CcMistBackdrop _backdrop;
        private CcWatermark _watermark;
        private CcHint _backHint;
        private CcChevronBack _backChevron;

        /// <summary>角色 3D 预览（离屏渲染，不依赖当前场景的相机）。</summary>
        protected CharacterPreview3D Preview { get; private set; }

        /// <summary>右下角鎏金确认块。</summary>
        protected CcConfirmBlock Confirm { get; private set; }

        /// <summary>本页是否需要角色 3D 预览（选角页不需要）。</summary>
        protected virtual bool NeedsPreview => true;

        /// <summary>
        /// 取景距离系数：1 = 全身入画（参考图 图片/6、7.png 的性别步骤）；
        /// 小于 1 推近做头肩特写（参考图 图片/2-5.png 的面容与捏脸步骤）。
        /// </summary>
        protected virtual float PreviewFramingScale => 1f;

        /// <summary>取景瞄准高度占身高的比例（0.5 = 全身居中，约 0.78 = 头肩）。</summary>
        protected virtual float PreviewFramingHeight => 0.5f;

        /// <summary>本页是否显示左下角「返回」提示。</summary>
        protected virtual bool NeedsBackHint => true;

        /// <summary>
        /// 是否响应空格推进。命名页要关掉：输入框里的空格属于名字，不该当成「下一步」。
        /// </summary>
        protected virtual bool ConfirmOnSpace => true;

        /// <summary>确认块的动作词（默认「下一步」）。</summary>
        protected virtual string ConfirmText => "下一步";

        /// <summary>点击返回 / 按 Esc。</summary>
        public event Action BackRequested;

        /// <summary>点击确认 / 按 Space。</summary>
        public event Action ConfirmRequested;

        protected CcStagePage()
        {
            BackgroundColor = Color.Transparent;
            ClipChildren = false;

            try
            {
                BuildStage();
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[{GetType().Name}] 舞台初始化失败: {ex.Message}");
            }
        }

        /// <summary>搭建背景、预览、确认块、返回提示与水印。</summary>
        private void BuildStage()
        {
            _backdrop = new CcMistBackdrop
            {
                AnchorPreset = AnchorPresets.StretchAll,
                Offsets = Margin.Zero,
                Parent = this,
            };

            if (NeedsPreview)
            {
                Preview = new CharacterPreview3D
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    TransparentBackground = true,
                    UseSkyBackdrop = false,
                    FramingScale = PreviewFramingScale,
                    FramingHeightFrac = PreviewFramingHeight,
                    Parent = this,
                };
            }

            _watermark = new CcWatermark
            {
                Parent = this,
            };

            if (NeedsBackHint)
            {
                _backHint = new CcHint
                {
                    MouseButtonGlyph = true,
                    Text = "返回",
                    TextColor = CcPalette.InkText,
                    Parent = this,
                };
                _backHint.Activated += () => BackRequested?.Invoke();

                // 参考图 图片/2-5.png 右上角都有「«」返回箭头；与左下「返回」指向同一动作。
                // 没有回退目标的页面（如选角首屏）两个都不画，避免出现点了没反应的入口。
                _backChevron = new CcChevronBack { Parent = this };
                _backChevron.Activated += () => BackRequested?.Invoke();
            }

            Confirm = new CcConfirmBlock
            {
                Text = ConfirmText,
                Parent = this,
            };
            Confirm.Activated += () => ConfirmRequested?.Invoke();

            // 参考图 图片/2-5.png 右上角都有「«」返回箭头；与左下「返回」指向同一动作。
            _backChevron = new CcChevronBack { Parent = this };
            _backChevron.Activated += () => BackRequested?.Invoke();
        }

        /// <summary>
        /// 左下角 ID 水印文本（参考图「ID: 0126998214」）。
        /// 由流程宿主在账号就绪后赋值；页面本身不去摸登录态，避免和认证层耦合。
        /// </summary>
        public string WatermarkText
        {
            get => _watermarkText;
            set
            {
                _watermarkText = value ?? string.Empty;
                if (_watermark != null)
                    _watermark.Text = _watermarkText;
            }
        }

        private string _watermarkText = string.Empty;

        /// <summary>
        /// 舞台公共部分的设计空间布局；子类覆写后先调 base 再摆自己的控件。
        /// </summary>
        public virtual void RefreshLayout()
        {
            if (Width <= 0f || Height <= 0f)
                return;

            if (Preview != null)
            {
                Preview.Size = new Float2(PreviewW, Height);
                Preview.Location = new Float2(PreviewX, 0f);
                Preview.RefreshLayout();
            }

            Confirm.Size = new Float2(ConfirmW, ConfirmH);
            Confirm.Location = new Float2(Width - ConfirmW, Height - ConfirmH - ConfirmBottom);

            if (_backHint != null)
            {
                _backHint.Location = new Float2(RailX, Height - 72f);
                _backHint.ApplyWidth();
            }

            if (_watermark != null)
                _watermark.Location = new Float2(24f, Height - 38f);

            if (_backChevron != null)
            {
                _backChevron.Size = new Float2(56f, 28f);
                _backChevron.Location = new Float2(Width - 96f, 40f);
            }
        }

        public override void Update(float dt)
        {
            base.Update(dt);

            if (ConfirmOnSpace && Input.GetKeyDown(KeyboardKeys.Spacebar))
                ConfirmRequested?.Invoke();

            // Esc 与右上角「«」、左下「返回」等价；没订阅者的页面自然无操作。
            if (Input.GetKeyDown(KeyboardKeys.Escape))
                BackRequested?.Invoke();

            HandleShortcuts();
        }

        /// <summary>
        /// 页面专属快捷键钩子（参考图里 Q/E、Z/C、N 这类键帽提示都是可按的，不能只画不响应）。
        /// 空格由本类统一处理，子类只需在这里响应自己的键。
        /// </summary>
        protected virtual void HandleShortcuts()
        {
        }

    }
}
