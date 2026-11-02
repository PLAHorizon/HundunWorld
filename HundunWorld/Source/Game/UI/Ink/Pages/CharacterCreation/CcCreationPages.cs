using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.StyleSystem;
using MetaHuman;
using MetaHuman.Runtime;
using System;
using System.Collections.Generic;

namespace HundunWorld.Game.UI.Ink.Pages.CharacterCreation
{

    /// <summary>
    /// 分类标题（参考图 图片/2.png「写实 ◅」/ 图片/3.png「◆ 风雅」）：
    /// 一次只显示当前分类，点标题或箭头翻到另一类，标题下方一条细横线。
    /// </summary>
    internal class CcCategoryTitle : ContainerControl
    {
        private const float FontSize = 22f;
        private bool _pressed;

        /// <summary>当前分类名。</summary>
        public string Title = string.Empty;

        /// <summary>箭头在标题右侧（◅）还是左侧（◆）。</summary>
        public bool ArrowOnRight = true;

        /// <summary>点击切换分类。</summary>
        public event Action Activated;

        public CcCategoryTitle()
        {
            BackgroundColor = Color.Transparent;
            ClipChildren = false;
            Size = new Float2(200f, 40f);
        }

        public override bool OnMouseDown(Float2 location, MouseButton button)
        {
            if (button != MouseButton.Left)
                return false;
            _pressed = true;
            return true;
        }

        public override bool OnMouseUp(Float2 location, MouseButton button)
        {
            if (!_pressed)
                return false;
            _pressed = false;
            Activated?.Invoke();
            return true;
        }

        public override void Draw()
        {
            if (Width > 0f)
            {
                float titleW = CcDraw.MeasureText(Title, FontSize);
                float titleX = ArrowOnRight ? 0f : 26f;

                CcDraw.Text(Title, new Rectangle(titleX, 0f, titleW + 10f, Height - 8f), InkWashTheme.FontRole.Display, FontSize, CcPalette.InkText);
                CcDraw.Text(ArrowOnRight ? "◅" : "◆", new Rectangle(ArrowOnRight ? titleX + titleW + 6f : 0f, 0f, 22f, Height - 8f),
                    InkWashTheme.FontRole.Body, 14f, CcPalette.InkIdle);

                Render2D.DrawLine(new Float2(0f, Height - 6f), new Float2(Width, Height - 6f),
                    new Color(CcPalette.InkText.R, CcPalette.InkText.G, CcPalette.InkText.B, 0.25f), 1f);
            }

            base.Draw();
        }
    }

    /// <summary>
    /// 创角第一步：性别（参考图 图片/6.png、7.png）。
    /// 左侧一条水墨笔触双档开关选「男 / 女」，右侧全身立绘，右下角「Space 下一步」。
    /// </summary>
    public class CcGenderPage : CcStagePage
    {
        private CcBrushToggle _toggle;

        public CcGenderPage()
        {
            _toggle = new CcBrushToggle
            {
                Parent = this,
            };
            _toggle.Selected = CcSession.Current.Gender;
            _toggle.SelectedChanged += index =>
            {
                CcSession.Current.Gender = index;

                // 男/女是两具独立身体资产（Character_1 / Character_2），切档就换预览模型；
                // 后续步骤的页面在构造时也会按当前会话性别重新绑定。
                ApplyPreviewGender(index);

                FlaxEngine.Debug.Log($"[CcGenderPage] 性别 -> {(index == 1 ? "女" : "男")}");
            };
        }

        protected override string ConfirmText => "下一步";

        public override void RefreshLayout()
        {
            base.RefreshLayout();

            if (_toggle == null || Width <= 0f)
                return;

            // 实测参考图（图片/6、7.png）：笔触整体从 x≈75 延伸到 x≈846（两段各约 371，中缝 28），
            // 腹部中心在屏幕高约 43% 处——按此定位，而不是凭眼估。
            _toggle.Size = new Float2(770f, 120f);
            _toggle.Location = new Float2(75f, Height * 0.368f);
        }
    }

    /// <summary>
    /// 创角第二步：面容风格预设（参考图 图片/2.png、3.png）。
    /// 左上「风雅 / 写实」分类标题，下方一排竖版水墨卡，选中卡打朱红印章。
    /// </summary>
    public class CcFacePresetPage : CcStagePage
    {
        private const float CardW = 165f;
        private const float CardH = 500f;   // 实测参考图 图片/2、3.png 竖卡约 195~700
        private const float CardGap = 20f;   // 4 张卡右边缘 855，留出预览区（860 起）的余量
        private const float CardsTop = 200f;

        private readonly List<CcPresetCard> _cards = new List<CcPresetCard>();

        private CcCategoryTitle _title;

        public CcFacePresetPage()
        {
            _title = new CcCategoryTitle
            {
                Parent = this,
            };
            _title.Activated += FlipCategory;

            RebuildCards();
        }

        protected override string ConfirmText => "下一步";

        /// <summary>这一步看的是脸：相机推近并对准头肩（参考图 图片/2、3.png）。</summary>
        protected override float PreviewFramingScale => 0.55f;
        protected override float PreviewFramingHeight => 0.78f;

        /// <summary>看脸的页必须用**脖子以下**的模板身体：按性别的身体自带脑袋，会把脸罩住。</summary>
        protected override bool PreviewUsesTemplateBody => true;

        /// <summary>Q/E 或 Z/C 翻分类，与点标题箭头等效。</summary>
        protected override void HandleShortcuts()
        {
            if (Input.GetKeyDown(KeyboardKeys.Q) || Input.GetKeyDown(KeyboardKeys.E) ||
                Input.GetKeyDown(KeyboardKeys.Z) || Input.GetKeyDown(KeyboardKeys.C))
                FlipCategory();
        }

        private void FlipCategory()
        {
            CcSession.Current.StyleCategory = CcSession.Current.StyleCategory == 0 ? 1 : 0;
            RebuildCards();
        }

        /// <summary>按当前分类重建卡面（分类切换 / 首次进入）。</summary>
        private void RebuildCards()
        {
            int category = CcSession.Current.StyleCategory;

            _title.Title = CcFacePresets.Categories[category];
            // 参考图：选「写实」时箭头在右（◅），选「风雅」时箭头在左（◆）
            _title.ArrowOnRight = category == 1;

            foreach (var card in _cards)
                card.Dispose();
            _cards.Clear();

            foreach (var preset in CcFacePresets.GetByCategory(CcSession.Current.Gender, category))
            {
                int index = preset.Index;
                var card = new CcPresetCard
                {
                    Name = preset.Name,
                    PresetIndex = preset.Index,
                    Selected = CcSession.Current.FacePresetIndex == preset.Index,
                    Size = new Float2(CardW, CardH),
                    Parent = this,
                };
                card.Activated += () => SelectPreset(index);
                _cards.Add(card);
            }
        }

        private void SelectPreset(int index)
        {
            CcSession.Current.FacePresetIndex = index;
            foreach (var card in _cards)
                card.Selected = card.PresetIndex == index;
        }

        public override void RefreshLayout()
        {
            base.RefreshLayout();

            if (Width <= 0f)
                return;

            _title.Size = new Float2(220f, 40f);
            _title.Location = new Float2(RailX + 55f, 130f);

            for (int i = 0; i < _cards.Count; i++)
            {
                _cards[i].Size = new Float2(CardW, CardH);
                _cards[i].Location = new Float2(RailX + 55f + i * (CardW + CardGap), CardsTop);
            }
        }
    }

    /// <summary>
    /// 创角第三步：捏脸（参考图 图片/4.png、5.png）。
    /// <para>
    /// 旧版把「万相集 / 分享 / 发型 / 姿势 / 导入」画成圆钮却只记日志，是假入口；
    /// 现在整块左栏换成自成一体的 <see cref="MetaHumanSculptPanel"/>，真实驱动 MetaHumanCharacter：
    /// 区域页签、数值滑杆、左右联动、步骤条与撤销/重做、保存/发布、随机/重置/预设。
    /// </para>
    /// </summary>
    public class CcFaceSculptPage : CcStagePage
    {
        private MetaHumanSculptPanel _sculpt;

        /// <summary>本页要驱动的那一具捏脸角色（归 <see cref="CcSession"/>，本页只是取用）。</summary>
        private MetaHumanCharacter _character;

        /// <summary>已经交给面板的那一具；与 <see cref="_character"/> 不同才需要重新 Bind。</summary>
        private MetaHumanCharacter _boundCharacter;

        public CcFaceSculptPage()
        {
            _sculpt = new MetaHumanSculptPanel
            {
                Parent = this,
            };

            // 会话归 CcSession，页面只负责把"往哪写"指过来。
            AttachSculpt();
            // 写之前让面板叫得动我们：预览那颗头会随页面重建变成死目标，而会话是跨页留着的。
            _sculpt.EnsureWriteTarget = AttachSculpt;

            // 发色：面板只管下标，预览实时换色（与世界侧 MH_Hair 的 HairColor 同一调色板）。
            _sculpt.HairColorChanged = idx => Preview?.SetHairColorIndex(idx);
            Preview?.SetHairColorIndex(CcSession.Current.HairColorIndex);

            // 身体页签切到**蒙皮**身体：模板身体是静态网格，对骨骼/形状参数没有任何反应。
            _sculpt.ShowBodyMode = body =>
            {
                if (Preview != null)
                {
                    // 2026-10-19：模板身体（body.flax）在 12:30 的重导后不可用，替换件也只有两只手；
                    // 身体页签优先用 MetaHuman 蒙皮身体（Ada_BodyPreview4，躯干齐全、可被身体参数驱动），
                    // 它没就绪时退回按性别取的完整角色 Character_1/2。头页签只留 Ada 脸（头资产自带肩颈）。
                    bool useSkinnedBody = body
                                          && HundunWorld.Game.Equipment.EquipmentDatabase.UseSkinnedBodyForPreview;
                    // 头页签只留 Ada 脸（按头自己的包围盒取景）；身体页签用蒙皮身体或按性别取的完整角色。
                    Preview.SetTemplateBodyVisible(false);
                    Preview.SetSkinnedBodyVisible(useSkinnedBody);
                    Preview.SetAnimatedBodyVisible(body && !useSkinnedBody);
                    Preview.SetMetaHumanHeadVisible(!body);
                    if (body)
                    {
                        Preview.FocusOn("whole");
                    }
                    else
                    {
                        Preview.Reframe();
                    }
                }

                // 刚露出来的身体要立刻带上当前解算结果，否则要等玩家再动一次滑杆才看得到。
                // 目标必须**现收**：页签切换会换可见身体（头 / 蒙皮身体 / 按性别身体），
                // 拿进入页面时缓存的 _writtenTargets 重指会把新身体排除在外（实测身体滑杆零像素）。
                if (!ReferenceEquals(_character, null) && _character && Preview != null)
                {
                    List<AnimatedModel> models = new List<AnimatedModel>();
                    Preview.CollectSkinnedModels(models);
                    if (models.Count > 0)
                    {
                        _character.RetargetModels(models.ToArray());
                        RememberTargets(models);
                    }
                }
            };

            // 分区按钮 → 镜头平滑过渡（头型/眼睛/脸颊/下巴/脖子、躯干/四肢/三围）。
            _sculpt.ShowSection = sectionId =>
            {
                if (Preview != null)
                {
                    Preview.FocusOn(sectionId);
                }
            };

            // 分区按钮 → 镜头平滑过渡（头型/眼睛/脸颊/下巴/脖子、躯干/四肢/三围）。
            _sculpt.ShowSection = sectionId =>
            {
                if (Preview != null)
                {
                    Preview.FocusOn(sectionId);
                }
            };

            // 上一步选的面容预设：id/名字由宿主注入（面板不认识 CcFacePresets，它要能被单文件编）。
            int presetIndex = CcSession.Current.FacePresetIndex;
            _sculpt.IncomingPresetId = CcFacePresets.PresetIdOf(presetIndex);
            _sculpt.IncomingPresetName = CcFacePresets.PresetNameOf(presetIndex);

            _boundCharacter = _character;
            _sculpt.Bind(_character);
            WireServerSeal(_sculpt);
        }

        /// <summary>
        /// 下一步/建号前把当前捏脸快照交给创建流程（不封存）。
        /// 否则未点「发布」时，捏脸值在建号那一步会被丢弃（2026-10-22 实机复现）。
        /// </summary>
        public void CommitPendingLook()
        {
            _sculpt?.CommitPendingLook();
        }

        /// <summary>
        /// 服务端"已发布"事件 ⇒ 通知当前活着的那块捏脸面板。
        /// <para>
        /// 只订阅一次、并按静态引用转给当前面板：页面每次导航都会新建，
        /// 若每个面板都往服务事件上挂一份，旧面板会被事件持有而永不释放，还会各自重复封存日志。
        /// </para>
        /// </summary>
        private static MetaHumanSculptPanel _sealTarget;

        private static bool _sealWired;

        private static void WireServerSeal(MetaHumanSculptPanel panel)
        {
            _sealTarget = panel;
            if (_sealWired)
            {
                return;
            }

            HundunWorld.Game.Services.MetaHumanAppearanceService.Instance.OnServerSealed += () =>
            {
                MetaHumanSculptPanel live = _sealTarget;
                if (live != null)
                {
                    live.OnServerSealedByService();
                }
            };
            _sealWired = true;
        }

        protected override string ConfirmText => "下一步";

        /// <summary>捏脸同样是头肩特写（参考图 图片/4、5.png）。</summary>
        protected override float PreviewFramingScale => 0.55f;
        protected override float PreviewFramingHeight => 0.78f;

        /// <summary>
        /// 捏脸页看的就是 MetaHuman 的脸 ⇒ 不用按性别取的完整角色身体（它自带的头会遮挡）。
        /// 也不用静态模板身体：模板身体已损坏、替换件只剩手；头资产自带肩颈，足够撑起头肩特写。
        /// </summary>
        protected override bool PreviewUsesTemplateBody => false;

        /// <summary>
        /// 取（或建）整段创角共用的那一具 <see cref="MetaHumanCharacter"/>，并把它的写回目标
        /// 指到**这一页当前真正被渲染的**那颗头（预览控件没公开它的 Actor，按名字在场景里找回来）。
        /// <para>
        /// 为什么要拆成"复用对象 + 重指目标"：页面每次导航都会新建，预览的 AnimatedModel 是新一具，
        /// 而数值会话与可回溯历史必须留着。2026-10-18 实测旧做法（每次在预览的叶子 Actor 上 AddScript）
        /// 造成两件事：① 面板 #1/#2/#3 各绑一具全新对象 ⇒ 来回导航后"数据无保留"；
        /// ② 同一页内预览一旦重建 Actor，组件就成了孤儿（Flax 的 <c>Object.==</c> 比原生指针，
        /// 面板里 <c>_character == null</c> 随即为真）⇒ 拖动改值、写回一次不跑、界面一句不响。
        /// </para>
        /// <para>
        /// 反过来也不能用 <c>Load()</c> 换目标：它会连 <c>Session</c> 一起重建，等于把玩家调好的脸清空。
        /// </para>
        /// </summary>
        private void AttachSculpt()
        {
            try
            {
                CcSession session = CcSession.Current;
                AnimatedModel head = FindPreviewModelActor();

                // 写回目标由预览组件发布（它自己建的头/身体），不在场景里按名字搜 —— 搜出来的可能已销毁、
                // 属于别的页面、甚至没有骨架（2026-10-19 实测四具里两具死的、一具骨架对不上）。
                List<AnimatedModel> models = new List<AnimatedModel>();
                if (Preview != null)
                {
                    Preview.CollectSkinnedModels(models);

                    // 进页默认是头页签：只留 Ada 脸（头资产自带肩颈），取景按头自己的包围盒算。
                    Preview.SetTemplateBodyVisible(false);
                    Preview.SetSkinnedBodyVisible(false);
                    Preview.SetAnimatedBodyVisible(false);
                    Preview.SetMetaHumanHeadVisible(true);
                    Preview.Reframe();
                }

                if (models.Count == 0 && head != null)
                {
                    models.Add(head); // 兜底：预览还没建好时，至少别让写回目标为空
                }

                MetaHumanCharacter reuse = session.Sculpt;
                // 判活要用 Flax 的语义：托管引用还在、原生对象已销毁时按"没有"算（隐式 bool 比原生指针）。
                bool alive = !ReferenceEquals(reuse, null) && reuse;
                if (alive)
                {
                    if (models.Count > 0 && !SameTargets(models))
                    {
                        reuse.RetargetModels(models.ToArray());
                        RememberTargets(models);
                    }

                    _character = reuse;
                    HookHairStyle();
                    FlaxEngine.Debug.Log("[CcFaceSculptPage] 重绑 alive: targets=" + models.Count
                        + " head=" + (head != null ? head.Name : "<无>")
                        + " headModel=" + (head != null && head.SkinnedModel != null ? head.SkinnedModel.Path : "<无>")
                        + " servable=" + reuse.ServableHeadParameterCount + "/" + reuse.HeadParameterCount);
                    return;
                }

                Actor host = session.SculptHost;
                if (ReferenceEquals(host, null) || !host)
                {
                    host = CreateHostActor();
                    session.SculptHost = host;
                }

                if (host == null)
                {
                    FlaxEngine.Debug.LogWarning("[CcFaceSculptPage] 无可用 Actor 承载 MetaHumanCharacter");
                    _character = null;
                    return;
                }

                MetaHumanCharacter character = host.GetScript<MetaHumanCharacter>();
                if (character == null)
                {
                    character = host.AddScript<MetaHumanCharacter>();
                }

                if (character == null)
                {
                    _character = null;
                    return;
                }

                character.TemplateIds = new[] { "metahuman.face_archetype", "metahuman.body_base" };
                // 身份必须是"编辑/预览"档：LOD 政策里 `AlwaysShapeRoles = {local, editing, preview}`
                // 才会**无条件保形状**。默认值是 "remote"，而预览那具头挂在舞台上
                // （世界 y=-100000，离主相机约 1000m），距离分档直接判"超出形状档 ⇒ 整体不写形状"
                // ⇒ 滑杆就算落地也会被压掉（2026-10-17 与"Adjust 不落地"叠在一起，症状是零反应）。
                character.CharacterRole = "editing";
                if (models.Count > 0)
                {
                    // 组件在常驻宿主上，所以写回目标必须点名 —— 宿主自己那棵树里没有头也没有身体。
                    character.TargetModels = models.ToArray();
                    RememberTargets(models);
                }

                character.Load();

                session.Sculpt = character;
                _character = character;
                HookHairStyle();

                // #57 定罪用：绑到谁身上、写回目标有几具、头里有多少个形状通道。
                // 只打"成功"没用的——历史上这一路径静默绑到了身体（Character_1），捏脸照样"全绿但无效果"。
                var asset = head != null ? head.SkinnedModel : null;
                FlaxEngine.Debug.Log(
                    "[CcFaceSculptPage] 宿主=" + (host != null ? host.Name : "<null>")
                    + " | 写回目标数=" + models.Count
                    + " | 头=" + (head != null ? head.Name : "<无>")
                    + " | 资产=" + (asset != null ? asset.Path : "<无>")
                    + " | 形状通道=" + (asset != null && asset.BlendShapes != null ? asset.BlendShapes.Length.ToString() : "0")
                    + $" | 承载参数 {character.ServableHeadParameterCount}/{character.HeadParameterCount}");
            }
            catch (Exception ex)
            {
                FlaxEngine.Debug.LogError($"[CcFaceSculptPage] 绑定 MetaHumanCharacter 失败: {ex}");
                _character = null;
            }
        }

        /// <summary>
        /// 发型（hair.style）→ 预览换模。角色挂在常驻宿主上、不在预览父级链里，所以这里主动推；
        /// 订阅 OnApplied 覆盖滑杆/回溯/载入 look 三条来路。
        /// </summary>
        private void HookHairStyle()
        {
            if (_character == null)
            {
                return;
            }

            _character.OnApplied -= OnCharacterApplied;
            _character.OnApplied += OnCharacterApplied;
            Preview?.SetHairStyle(_character.HairStyle);
        }

        private void OnCharacterApplied(MetaHuman.MetaHumanSolution solution, bool full)
        {
            Preview?.SetHairStyle(_character != null ? _character.HairStyle : 0);
        }

        /// <summary>在已加载场景里找预览那具 AnimatedModel（父级名含 CharacterPreview3D 者优先）。</summary>
        private static AnimatedModel FindPreviewModelActor()
        {
            AnimatedModel fallback = null;
            for (int i = 0; i < Level.ScenesCount; i++)
            {
                // 必须写全限定名：本文件在 HundunWorld.Game.UI.* 命名空间下，
                // 简单名 Scene 会先撞上兄弟命名空间 HundunWorld.Game.Scene（CS0118）。
                FlaxEngine.Scene scene = Level.GetScene(i);
                if (scene == null || scene.Name == "TransitionScene")
                    continue;

                // **优先找"预览里的头"**：MetaHumanCharacter 的 morph 只能写在 MetaHuman 头上，
                // 而身体（Character_1/2）是另一套骨架、写了也看不出变化。
                // 这具头由 CharacterPreview3D 创建，名字固定含 CharacterPreview3D_Head（见 §170）。
                AnimatedModel head = FindModelIn(scene, false, "CharacterPreview3D_Head");
                if (head != null)
                    return head;

                AnimatedModel preview = FindModelIn(scene, true, null);
                if (preview != null)
                    return preview;

                if (fallback == null)
                    fallback = FindModelIn(scene, false, null);
            }
            return fallback;
        }


        /// <summary>上一次点名给组件的那批写回目标（判"要不要重指点"用，按引用比）。</summary>
        private readonly List<AnimatedModel> _writtenTargets = new List<AnimatedModel>();

        private bool SameTargets(List<AnimatedModel> models)
        {
            if (_writtenTargets.Count != models.Count)
            {
                return false;
            }

            for (int i = 0; i < models.Count; i++)
            {
                if (!ReferenceEquals(_writtenTargets[i], models[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private void RememberTargets(List<AnimatedModel> models)
        {
            _writtenTargets.Clear();
            for (int i = 0; i < models.Count; i++)
            {
                _writtenTargets.Add(models[i]);
            }
        }

        private static AnimatedModel FindModelIn(Actor root, bool onlyPreview, string nameContains = null)
        {
            if (root == null)
                return null;

            AnimatedModel model = root as AnimatedModel;
            if (model != null)
            {
                bool nameOk = string.IsNullOrEmpty(nameContains)
                              || (model.Name != null && model.Name.Contains(nameContains));
                if (nameOk)
                {
                    if (!onlyPreview)
                        return model;

                    Actor parent = model.Parent;
                    if (parent != null && parent.Name != null && parent.Name.Contains("CharacterPreview3D"))
                        return model;
                }
            }

            Actor[] children = root.Children;
            if (children != null)
            {
                for (int i = 0; i < children.Length; i++)
                {
                    AnimatedModel found = FindModelIn(children[i], onlyPreview, nameContains);
                    if (found != null)
                        return found;
                }
            }

            return null;
        }

        private static Actor CreateHostActor()
        {
            FlaxEngine.Scene target = null;
            for (int i = 0; i < Level.ScenesCount; i++)
            {
                FlaxEngine.Scene scene = Level.GetScene(i);
                if (scene != null && scene.Name != "TransitionScene")
                {
                    target = scene;
                    break;
                }
            }

            EmptyActor host = new EmptyActor { Name = "MetaHumanSculptHost" };
            if (target != null)
                Level.SpawnActor(host, target);
            return host;
        }

        public override void RefreshLayout()
        {
            base.RefreshLayout();

            if (Width <= 0f || _sculpt == null)
                return;

            // 预览那颗头可能是晚一步才随页面建出来的，所以每次布局都再确认一次写回目标；
            // 只有换成了另一具角色才重绑面板（Bind 会整表重建 120 行，不能每帧做）。
            AttachSculpt();
            if (!ReferenceEquals(_character, _boundCharacter))
            {
                _boundCharacter = _character;
                _sculpt.Bind(_character);
            }

            // 面板占左侧留白（预览从 PreviewX 起），底部留出确认/返回的高度。
            float x = RailX;
            float top = 110f;
            float w = PreviewX - RailX - 40f;
            float h = Height - top - 40f;

            _sculpt.Bounds = new Rectangle(x, top, w, h);
            _sculpt.RefreshLayout();
        }
    }

    /// <summary>
    /// 创角最后一步：命名（沿用同一舞台版式）。
    /// <para>
    /// 参考图未给出命名页，故只沿用其舞台语言（雾白底 / 左侧留白 / 右下确认块），
    /// 并且**不再提供门派选择**：出身门派由角色进入游戏后自行创建或加入时才显示。
    /// </para>
    /// </summary>
    public class CcNamePage : CcStagePage
    {
        private const float FieldW = 420f;

        private Label _titleLabel;
        private Panel _fieldWrap;
        private TextBox _nameInput;
        private Label _countLabel;
        private Label _hintLabel;
        private CcHint _randomHint;

        /// <summary>当前输入的角色名。</summary>
        public string CharacterName => _nameInput != null ? _nameInput.Text : CcSession.Current.CharacterName;

        public CcNamePage()
        {
            _titleLabel = NewLabel("姓 名", InkWashTheme.FontRole.Heading, 20f, CcPalette.InkText);

            _fieldWrap = new Panel
            {
                BackgroundColor = new Color(1f, 1f, 1f, 0.55f),
                Parent = this,
            };

            _nameInput = new TextBox
            {
                Text = CcSession.Current.CharacterName,
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 22f),
                TextColor = CcPalette.InkText,
                BackgroundColor = Color.Transparent,
                MaxLength = 6,
                Parent = _fieldWrap,
            };
            _nameInput.TextChanged += () =>
            {
                CcSession.Current.CharacterName = _nameInput.Text;
                _countLabel.Text = $"{_nameInput.Text.Length}/6";
            };

            _countLabel = new Label
            {
                Text = $"{_nameInput.Text.Length}/6",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Number, 13f),
                TextColor = CcPalette.InkIdle,
                Parent = _fieldWrap,
            };

            _hintLabel = NewLabel("汉字二至六字；门派不入江湖户籍，入门由你日后自行抉择", InkWashTheme.FontRole.Body, 13f, CcPalette.InkIdle);

            _randomHint = new CcHint
            {
                Text = "天机赐名",
                TextColor = CcPalette.TextGold,
                Parent = this,
            };
            _randomHint.Activated += GenerateRandomName;
        }

        /// <summary>命名页里空格属于名字本身，不能当作「下一步」。</summary>
        protected override bool ConfirmOnSpace => false;

        protected override string ConfirmText => "踏入江湖";

        private Label NewLabel(string text, InkWashTheme.FontRole role, float fontSize, Color color)
        {
            return new Label
            {
                Text = text,
                Font = InkRenderHelper.GetFontRef(role, fontSize),
                TextColor = color,
                Parent = this,
            };
        }

        private static readonly string[] Surnames = { "李", "王", "张", "赵", "萧", "楚", "林", "陈", "叶", "苏" };
        private static readonly string[] GivenNames = { "忘生", "长风", "无极", "青衫", "惊鸿", "孤云", "听雨", "凌霄", "落尘", "问剑" };

        private void GenerateRandomName()
        {
            var rng = new Random();
            _nameInput.Text = Surnames[rng.Next(Surnames.Length)] + GivenNames[rng.Next(GivenNames.Length)];
            CcSession.Current.CharacterName = _nameInput.Text;
            _countLabel.Text = $"{_nameInput.Text.Length}/6";
        }

        public override void RefreshLayout()
        {
            base.RefreshLayout();

            if (Width <= 0f)
                return;

            float x = RailX + 55f;
            float y = Height * 0.34f;

            _titleLabel.Location = new Float2(x, y);
            _titleLabel.Size = new Float2(FieldW, 22f);

            _fieldWrap.Location = new Float2(x, y + 32f);
            _fieldWrap.Size = new Float2(FieldW, 52f);

            _nameInput.Location = new Float2(14f, 12f);
            _nameInput.Size = new Float2(FieldW - 80f, 28f);
            _countLabel.Location = new Float2(FieldW - 60f, 18f);
            _countLabel.Size = new Float2(50f, 16f);

            _hintLabel.Location = new Float2(x, y + 96f);
            _hintLabel.Size = new Float2(FieldW + 120f, 20f);

            _randomHint.Location = new Float2(x, y + 136f);
            _randomHint.ApplyWidth();
        }
    }
}
