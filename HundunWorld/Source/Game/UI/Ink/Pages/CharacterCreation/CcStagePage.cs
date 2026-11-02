using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.Ink.Components;
using HundunWorld.Game.UI.StyleSystem;
using System;

namespace HundunWorld.Game.UI.Ink.Pages.CharacterCreation
{

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

        /// <summary>左侧操作栏与立绘的分界（立绘现在全屏铺满，这只是内容栏的排版基准）。</summary>
        protected const float PreviewX = 860f;

        /// <summary>
        /// 立绘在画面里的水平偏移（相对页宽）：正的把角色让到中心右侧。
        /// 全屏铺满后角色默认居中，会和左侧文字抢戏，所以按参考图版式右移。
        /// </summary>
        private const float PreviewOffsetXFrac = 0.20f;

        /// <summary>
        /// 取景俯仰偏移（度）：负值把相机压到腰部以下仰拍，地平线落进画面下三分之一（工作台截图构图）。
        /// 只给全身页用（取景距离系数 ≥1）；头肩特写仍用基线仰角，否则脸会被台面顶到画面外。
        /// <para>
        /// 标定：-18° 时地平线在 78%、脚在 92%（雾光只剩十来个百分点）；-15.5° 抬到 70%/84%，
        /// 与参考图（地平线 69%、脚 82%）对齐，雾带才有纵向空间铺开。
        /// </para>
        /// </summary>
        protected virtual float PreviewPitchDeg => PreviewFramingScale >= 1f ? -15.5f : 0f;

        private CcStageScrim _backdrop;
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
        /// 取景距离系数：1 = 自动取景（全身完整入画、留 18% 边距）；
        /// 小于 1 推近做头肩特写（参考图 图片/2-5.png 的面容与捏脸步骤）。
        /// 全身页放到 1.9：工作台截图的构图是"小立绘 + 大片暗天 + 台面在前"，
        /// 贴着画幅的全身会把舞台压没（看清脸/捏脸的两页仍用各自的小系数）。
        /// </summary>
        protected virtual float PreviewFramingScale => 2.0f;

        /// <summary>
        /// 取景瞄准高度占身高的比例（0.5 = 躯干中央，约 0.78 = 头肩）。
        /// 全身页放到 0.72：工作台截图的立绘是"人物压在画面下半、头顶留一大片暗天、
        /// 台面铺在前景"，焦点抬到胸口以上才能把人整体压下去；看脸的两页仍用 0.78。
        /// </summary>
        protected virtual float PreviewFramingHeight => 1.0f;

        /// <summary>
        /// 本页的预览身体是否用**基础模板身体**（conform topology，脖子以下、灰模）取代
        /// 按性别取的那具完整角色（后者自带脑袋，会挡住 MetaHuman 的脸）。
        /// <para>
        /// 只有"看脸"的页该开：面容风格预设、捏脸。看身材的页仍用按性别的完整角色。
        /// 默认 false ⇒ 既有页面行为不变。
        /// </para>
        /// </summary>
        protected virtual bool PreviewUsesTemplateBody => false;

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
            if (NeedsPreview)
            {
                // 3D 舞台铺满整页（工作台场景同源的暗天+地台），先建它，
                // 后面的左侧压暗层与所有控件都叠在它上面。
                Preview = new CharacterPreview3D
                {
                    AnchorPreset = AnchorPresets.TopLeft,
                    TransparentBackground = true,
                    FramingScale = PreviewFramingScale,
                    FramingHeightFrac = PreviewFramingHeight,
                    FramingOffsetX = PreviewOffsetXFrac,
                    FramingPitchDeg = PreviewPitchDeg,
                    Parent = this,
                };

                // 预览里是"哪一具身体"完全由性别决定：本流程还没有运行时角色 Actor，
                // 不走这一步 SetCharacter 取不到模型，画面就只剩雾背景。
                ApplyPreviewGender();

                // 看脸的页改用基础模板身体（脖子以下）——按性别取的那具自带脑袋，会把脸罩住。
                Preview.SetTemplateBodyVisible(PreviewUsesTemplateBody);
            }

            _backdrop = new CcStageScrim
            {
                AnchorPreset = AnchorPresets.StretchAll,
                Offsets = Margin.Zero,
                Parent = this,
            };

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
        /// 把预览绑到指定性别（默认取当前创角会话）。
        /// 页面是每次导航新建的，所以进入任一步骤都会按当下性别重新取身体模型。
        /// </summary>
        protected void ApplyPreviewGender(int? gender = null)
        {
            Preview?.SetBodyGender(gender ?? CcSession.Current.Gender);
        }

        /// <summary>
        /// 舞台公共部分的设计空间布局；子类覆写后先调 base 再摆自己的控件。
        /// </summary>
        public virtual void RefreshLayout()
        {
            if (Width <= 0f || Height <= 0f)
                return;

            if (Preview != null)
            {
                // 全屏铺满：3D 舞台就是整页背景，角色由 FramingOffsetX 让到右半。
                Preview.Size = new Float2(Mathf.Max(Width, 1f), Mathf.Max(Height, 1f));
                Preview.Location = Float2.Zero;
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
