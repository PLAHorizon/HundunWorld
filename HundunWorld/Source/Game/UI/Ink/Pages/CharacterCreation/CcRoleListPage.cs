using FlaxEngine;
using FlaxEngine.GUI;
using Game.UI.Character;
using HundunWorld.Game.Services;
using HundunWorld.Game.UI.StyleSystem;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using System;
using System.Collections.Generic;

namespace HundunWorld.Game.UI.Ink.Pages.CharacterCreation
{
    /// <summary>
    /// 门派显示名。
    /// 创角阶段**不再选择门派**，因此新建角色的 <see cref="Profession"/> 恒为
    /// <see cref="Profession.None"/>，此时选角行不显示门派一行；
    /// 待角色进入游戏、创建或加入门派后，这里提供名字用于展示。
    /// </summary>
    public static class CcSectNames
    {
        /// <summary>取门派名；无门派返回空串（调用方据此决定是否显示）。</summary>
        public static string Get(Profession profession)
        {
            return profession switch
            {
                Profession.None => string.Empty,
                Profession.Shaolin => "少林寺",
                Profession.Wudang => "武当派",
                Profession.Emei => "峨眉派",
                Profession.Huashan => "华山派",
                Profession.Xiaoyao => "逍遥派",
                Profession.Gaibang => "丐帮",
                Profession.Mingjiao => "明教",
                Profession.Tangmen => "唐门",
                Profession.Wudu => "五毒教",
                Profession.Tianshan => "天山派",
                Profession.Murong => "慕容世家",
                Profession.Duan => "段氏皇族",
                Profession.Xingxiu => "星宿派",
                Profession.Lingjiu => "灵鹫宫",
                Profession.Riyue => "日月神教",
                _ => string.Empty,
            };
        }
    }

    /// <summary>
    /// 选角页的一行角色：首字圆章 + 名字 + 等级，已入门派者再补一行门派。
    /// </summary>
    internal class CcRoleRow : ContainerControl
    {
        private const float NameFontSize = 22f;
        private const float SubFontSize = 13f;
        private bool _pressed;

        /// <summary>本行对应的角色数据。</summary>
        public CharacterInfo Role;

        /// <summary>角色名。</summary>
        public string RoleName = string.Empty;

        /// <summary>等级。</summary>
        public int Level;

        /// <summary>门派名；空串表示尚未入门派，不显示该行。</summary>
        public string SectName = string.Empty;

        /// <summary>是否选中。</summary>
        public bool Selected;

        /// <summary>点击本行。</summary>
        public event Action Activated;

        public CcRoleRow()
        {
            BackgroundColor = Color.Transparent;
            ClipChildren = false;
            Size = new Float2(420f, 84f);
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

            if (location.X >= 0f && location.Y >= 0f && location.X <= Width && location.Y <= Height)
                Activated?.Invoke();
            return true;
        }

        public override void Draw()
        {
            if (Width <= 0f || Height <= 0f)
            {
                base.Draw();
                return;
            }

            bool hasSect = !string.IsNullOrEmpty(SectName);
            CcDraw.VerticalColorGradient(Float2.Zero, Width, Height, CcPalette.CardTop, CcPalette.CardBottom, 8);

            var avatarCenter = new Float2(42f, Height * 0.5f);
            InkRenderHelper.FillCircle(avatarCenter, 24f, new Color(0.16f, 0.17f, 0.20f, 0.88f));
            string initial = string.IsNullOrEmpty(RoleName) ? "侠" : RoleName.Substring(0, 1);
            CcDraw.Text(initial, new Rectangle(avatarCenter.X - 24f, avatarCenter.Y - 24f, 48f, 48f),
                InkWashTheme.FontRole.Display, 24f, CcPalette.BrushGold);

            float textX = 80f;
            float nameY = hasSect ? 14f : (Height - NameFontSize) * 0.5f;
            CcDraw.Text(RoleName, new Rectangle(textX, nameY, Width - textX - 20f, 30f),
                InkWashTheme.FontRole.Display, NameFontSize, Selected ? CcPalette.TextGold : CcPalette.InkText, TextAlignment.Near);

            if (hasSect)
                CcDraw.Text(SectName, new Rectangle(textX, 50f, 200f, 20f),
                    InkWashTheme.FontRole.Body, SubFontSize, CcPalette.InkIdle, TextAlignment.Near);

            CcDraw.Text($"Lv.{Level}", new Rectangle(Width - 90f, hasSect ? 50f : Height * 0.5f - 10f, 70f, 20f),
                InkWashTheme.FontRole.Number, SubFontSize, CcPalette.InkIdle, TextAlignment.Far);

            var border = new Rectangle(0f, 0f, Width - 1f, Height - 1f);
            InkRenderHelper.DrawRoundedRectangle(border, InkWashTheme.RadiusMd,
                Selected ? CcPalette.BrushGold : new Color(CcPalette.InkText.R, CcPalette.InkText.G, CcPalette.InkText.B, 0.18f),
                Selected ? 1.8f : 1f);

            if (Selected)
            {
                const float seal = 20f;
                var rect = new Rectangle(Width - seal - 10f, 10f, seal, seal);
                Render2D.FillRectangle(rect, new Color(CcPalette.SealRed.R, CcPalette.SealRed.G, CcPalette.SealRed.B, 0.88f));
            }

            base.Draw();
        }
    }

    /// <summary>
    /// 角色选择页（登录后首屏）：与创角流程同一套雾白舞台语言。
    /// <para>
    /// 只负责呈现与交互，网络动作（拉列表 / 进入游戏 / 删除）由流程宿主订阅本页面事件后代做，
    /// 页面本身只从 <see cref="CharacterManager"/> 读取快照并订阅列表刷新。
    /// </para>
    /// </summary>
    public class CcRoleListPage : CcStagePage
    {
        /// <summary>角色数量上限，与服务端 <c>UserCharacterManagerGrain.MAX_CHARACTER_COUNT</c> 保持一致。</summary>
        public const int MaxRoles = 5;

        private const float RowW = 420f;
        private const float RowH = 84f;
        private const float RowGap = 14f;
        private const float RowsTop = 210f;

        private readonly List<CcRoleRow> _rows = new List<CcRoleRow>();

        private CcHint _createHint;
        private CcHint _deleteHint;
        private CcHint _logoutHint;
        private Label _titleLabel;
        private Label _emptyLabel;
        private CharacterInfo _selected;

        /// <summary>点击「创建角色」。</summary>
        public event Action CreateRequested;

        /// <summary>确认进入某个角色。</summary>
        public event Action<CharacterInfo> EnterRequested;

        /// <summary>删除当前选中角色。</summary>
        public event Action<CharacterInfo> DeleteRequested;

        /// <summary>退出登录回到登录页（旧选角界面的「返回登录」，缺了就没有回头路）。</summary>
        public event Action LogoutRequested;

        public CcRoleListPage()
        {
            _titleLabel = new Label
            {
                Text = "行 囊",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Display, 30f),
                TextColor = CcPalette.InkText,
                Parent = this,
            };

            _emptyLabel = new Label
            {
                Text = "尚无侠客，创建一位以入江湖",
                Font = InkRenderHelper.GetFontRef(InkWashTheme.FontRole.Heading, 16f),
                TextColor = CcPalette.InkIdle,
                Parent = this,
            };

            _createHint = new CcHint
            {
                Key = "N",
                Text = "创建角色",
                TextColor = CcPalette.TextGold,
                Parent = this,
            };
            _createHint.Activated += () => CreateRequested?.Invoke();

            _deleteHint = new CcHint
            {
                Text = "删除",
                TextColor = CcPalette.InkIdle,
                Parent = this,
            };
            _deleteHint.Activated += OnDeleteClicked;

            _logoutHint = new CcHint
            {
                Text = "退出登录",
                TextColor = CcPalette.InkIdle,
                Parent = this,
            };
            _logoutHint.Activated += () => LogoutRequested?.Invoke();

            ConfirmRequested += OnConfirm;

            var manager = CharacterManager.Instance;
            if (manager != null)
            {
                // 新建 / 上次进入的角色作为初始高亮；列表里没有该行时回落到第一行。
                _keepSelected = manager.SelectedCharacter;
                manager.CharacterListUpdated += OnListUpdated;
                Rebuild(manager.CharacterList);
            }
        }

        protected override string ConfirmText => "进入游戏";

        /// <summary>选角是这条链的首屏，没有可回退的上一屏，故不画「返回」与「«」。</summary>
        protected override bool NeedsBackHint => false;

        /// <summary>N 创建角色（与「创建角色」提示上的键帽一致）。</summary>
        protected override void HandleShortcuts()
        {
            if (Input.GetKeyDown(KeyboardKeys.N))
                CreateRequested?.Invoke();
        }

        private void OnConfirm()
        {
            if (_selected != null)
                EnterRequested?.Invoke(_selected);
            else
                CreateRequested?.Invoke();
        }

        private void OnListUpdated(List<CharacterInfo> roles)
        {
            Rebuild(roles);
        }

        /// <summary>按角色列表重建行控件，并尽量保留原选中项。</summary>
        private void Rebuild(List<CharacterInfo> roles)
        {
            foreach (var row in _rows)
                row.Dispose();
            _rows.Clear();

            int count = roles?.Count ?? 0;
            for (int i = 0; i < count; i++)
            {
                var info = roles[i];
                var row = new CcRoleRow
                {
                    Role = info,
                    RoleName = info.CharacterName,
                    Level = info.Level,
                    SectName = CcSectNames.Get(info.Profession),
                    Parent = this,
                };

                CharacterInfo captured = info;
                row.Activated += () => SelectRow(captured, row);
                _rows.Add(row);
            }

            // 列表刷新后原选中项可能已被删除；回落到第一行。
            _selected = null;
            foreach (var row in _rows)
            {
                row.Selected = _keepSelected != null && ReferenceEquals(row.Role, _keepSelected);
                if (row.Selected)
                    _selected = _keepSelected;
            }

            if (_selected == null && _rows.Count > 0)
            {
                _rows[0].Selected = true;
                _selected = _rows[0].Role;
            }

            _keepSelected = _selected;

            // 首次进页 / 删除后回列表：立绘要对上"当前选中的那一个"，而不是默认身体
            if (_selected != null)
            {
                ApplyPreviewGender(_selected.Gender);
                ApplyPreviewLook(_selected);
            }

            UpdateIdWatermark();

            if (_emptyLabel != null)
                _emptyLabel.Visible = _rows.Count == 0;

            // 列表刷新（含删除成功回来）后一律解除删除确认态
            DisarmDelete();

            // 满员时把「创建角色」提示改成灰化的已满状态，避免玩家走到命名页才被服务端拒。
            bool canCreate = _rows.Count < MaxRoles;
            _createHint.Text = canCreate ? "创建角色" : $"角色已满 {_rows.Count}/{MaxRoles}";
            _createHint.TextColor = canCreate ? CcPalette.TextGold : CcPalette.InkIdle;
            _createHint.ApplyWidth();

            RefreshLayout();
        }

        private CharacterInfo _keepSelected;

        /// <summary>删除需二次确认：一次点击只是"上膛"，4 秒内再点一次才真发删除请求。</summary>
        private const float DeleteArmSeconds = 4f;

        private bool _deleteArmed;
        private float _deleteArmTimer;

        private void OnDeleteClicked()
        {
            if (_selected == null)
                return;

            if (!_deleteArmed)
            {
                _deleteArmed = true;
                _deleteArmTimer = DeleteArmSeconds;
                _deleteHint.Text = $"再点一次删除「{_selected.CharacterName}」";
                _deleteHint.TextColor = CcPalette.SealRed;
                _deleteHint.ApplyWidth();
                return;
            }

            DisarmDelete();
            DeleteRequested?.Invoke(_selected);
        }

        private void DisarmDelete()
        {
            _deleteArmed = false;
            _deleteArmTimer = 0f;

            if (_deleteHint == null)
                return;

            _deleteHint.Text = "删除";
            _deleteHint.TextColor = CcPalette.InkIdle;
            _deleteHint.ApplyWidth();
        }

        public override void Update(float dt)
        {
            base.Update(dt);

            // 选角预览的外观是异步回的：拿到缓存就套一次。
            if (_pendingLookId != 0UL
                && MetaHumanAppearanceService.Instance.TryGetCachedLook(_pendingLookId, out MetaHuman.MetaHumanLook look))
            {
                _pendingLookId = 0UL;
                ApplyPreviewLook(look);
            }

            // 头/身模型是异步加载的：就绪前每 0.5s 重试绑定，超时后明确告警（不静默停在半个预览上）。
            if (_pendingApplyLook != null)
            {
                _applyRetryTimer -= dt;
                if (_applyRetryTimer <= 0f)
                {
                    _applyAttempts++;
                    if (TryBindPreviewMetaHuman(_pendingApplyLook))
                    {
                        _pendingApplyLook = null;
                    }
                    else if (_applyAttempts >= 20)
                    {
                        FlaxEngine.Debug.LogWarning("[CcRoleListPage] 选角预览绑定外观超时（10s）：头或身体模型未就绪，保留当前显示");
                        _pendingApplyLook = null;
                    }
                    else
                    {
                        _applyRetryTimer = 0.5f;
                    }
                }
            }

            if (!_deleteArmed)
                return;

            _deleteArmTimer -= dt;
            if (_deleteArmTimer <= 0f)
                DisarmDelete();
        }

        private void SelectRow(CharacterInfo info, CcRoleRow row)
        {
            _selected = info;
            _keepSelected = info;
            // 选角页的立绘跟着选中角色走：男女是两具身体，不重绑就一直是默认那一具
            ApplyPreviewGender(info.Gender);
            // 有已保存外观时换成该角色的 MetaHuman 脸+身（缺 look 时保持占位人形，行为与旧版一致）。
            ApplyPreviewLook(info);
            UpdateIdWatermark();
            // 换了删除目标就把确认清掉，避免"对着 A 上膛、删掉 B"。
            DisarmDelete();
            foreach (var other in _rows)
                other.Selected = ReferenceEquals(other, row);
        }

        private ulong _pendingLookId;

        /// <summary>
        /// 选角页预览用该角色已保存的 MetaHuman look 渲染（6a14）。
        /// 列表回执不一定带外观 blob：没有就先向网关要一份，Update 里轮询缓存后再套。
        /// </summary>
        private void ApplyPreviewLook(CharacterInfo info)
        {
            if (Preview == null || info == null)
                return;

            _pendingLookId = 0UL;
            byte[] blob = info.MetaHumanLook != null ? info.MetaHumanLook.Blob : null;
            if (blob != null && blob.Length > 0)
            {
                try
                {
                    ApplyPreviewLook(MetaHuman.MetaHumanLook.FromBytes(blob));
                }
                catch (Exception ex)
                {
                    FlaxEngine.Debug.LogWarning("[CcRoleListPage] 角色外观解码失败，保留占位人形: " + ex.Message);
                }
                return;
            }

            if (info.CharacterId <= 0)
                return;

            _pendingLookId = (ulong)info.CharacterId;
            MetaHumanAppearanceService.Instance.RequestRemoteAppearance(_pendingLookId);
        }

        /// <summary>把一份外观套到预览的头/身体上；头/身模型还没就绪时转成"待重试"，不静默放弃。</summary>
        private void ApplyPreviewLook(MetaHuman.MetaHumanLook look)
        {
            if (look == null || Preview == null)
                return;

            // 先切显示（幂等）：蒙皮身体 + Ada 头同屏（头用 master pose 跟随身体姿态），藏掉占位人形/模板身体。
            Preview.SetTemplateBodyVisible(false);
            Preview.SetAnimatedBodyVisible(false);
            Preview.SetMetaHumanLookMode(true);

            _pendingApplyLook = null;
            if (TryBindPreviewMetaHuman(look))
                return;

            _pendingApplyLook = look;
            _applyRetryTimer = 0.4f;
            _applyAttempts = 0;
        }

        private MetaHuman.MetaHumanLook _pendingApplyLook;
        private float _applyRetryTimer;
        private int _applyAttempts;

        /// <summary>绑定成功返回 true；头/身模型尚未加载完返回 false（由 Update 重试）。</summary>
        private bool TryBindPreviewMetaHuman(MetaHuman.MetaHumanLook look)
        {
            AnimatedModel head = Preview.HeadModel;
            if (head == null || head.SkinnedModel == null || !head.SkinnedModel.IsLoaded)
                return false;

            var models = new List<AnimatedModel>();
            Preview.CollectSkinnedModels(models);
            if (models.Count < 2)
                return false;

            var character = head.GetScript<MetaHuman.Runtime.MetaHumanCharacter>();
            if (character == null)
                character = head.AddScript<MetaHuman.Runtime.MetaHumanCharacter>();
            if (character == null)
                return false;

            character.TemplateIds = new[] { "metahuman.face_archetype", "metahuman.body_base" };
            // "preview" 在引擎 LOD 政策的 AlwaysShapeRoles 里：舞台离主相机很远，别的身份档会被判"超出形状档"。
            character.CharacterRole = "preview";
            character.ApplyOnStart = false;
            character.TargetModels = models.ToArray();
            if (!character.IsLoaded)
                character.Load();

            if (!character.LoadLook(look))
                return false;

            Preview.SetHairStyle(character.HairStyle);
            // 完整外观是全身像：按可见模型重新取景（外观模式下取景高度用到头冠，见 TryAutoFrame）。
            Preview.Reframe();
            FlaxEngine.Debug.Log("[CcRoleListPage] 选角预览已应用外观: targets=" + models.Count
                                 + " head=" + (head.Name ?? "<null>"));
            return true;
        }

        /// <summary>
        /// 左下角 ID 水印显示当前选中角色的 ID：共 11 位，不足左侧补 0。
        /// 无选中（空列表）时不动，保留工厂注入的账号 ID 兜底。
        /// </summary>
        private void UpdateIdWatermark()
        {
            if (_selected != null)
                WatermarkText = $"ID: {_selected.CharacterId:D11}";
        }

        public override void RefreshLayout()
        {
            base.RefreshLayout();

            if (Width <= 0f)
                return;

            float x = RailX + 55f;

            _titleLabel.Size = new Float2(200f, 40f);
            _titleLabel.Location = new Float2(x, 130f);

            for (int i = 0; i < _rows.Count; i++)
            {
                _rows[i].Size = new Float2(RowW, RowH);
                _rows[i].Location = new Float2(x, RowsTop + i * (RowH + RowGap));
            }

            _emptyLabel.Size = new Float2(RowW, 26f);
            _emptyLabel.Location = new Float2(x, RowsTop + 10f);

            // 本页没有「返回」提示，底部两个入口直接对齐其它页面返回提示所在的左边界
            _createHint.Location = new Float2(RailX, Height - 72f);
            _createHint.ApplyWidth();

            _deleteHint.Location = new Float2(RailX + _createHint.Width + 40f, Height - 72f);
            _deleteHint.ApplyWidth();

            _logoutHint.Location = new Float2(
                RailX + _createHint.Width + 40f + _deleteHint.Width + 32f, Height - 72f);
            _logoutHint.ApplyWidth();
        }

        /// <summary>页面销毁时解除对单例的事件订阅，避免跨 Play/Stop 周期累积。</summary>
        public override void OnDestroy()
        {
            var manager = CharacterManager.Instance;
            if (manager != null)
                manager.CharacterListUpdated -= OnListUpdated;

            base.OnDestroy();
        }
    }
}
