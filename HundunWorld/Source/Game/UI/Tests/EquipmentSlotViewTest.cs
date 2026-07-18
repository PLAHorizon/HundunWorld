using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.Equipment;
using HundunWorld.Game.UI;
using HundunWorld.Game.UI.Components;

namespace HundunWorld.Game.UI.Tests
{
    /// <summary>
    /// EquipmentSlotView 临时测试脚本
    /// 创建一个面板，包含空槽、已装备武器的槽位，验证点击事件与刷新显示
    /// </summary>
    public class EquipmentSlotViewTest : Script
    {
        private UICanvas _canvas;

        public override void OnStart()
        {
            base.OnStart();
            CreateTestUI();
        }

        private void CreateTestUI()
        {
            _canvas = UIHelper.CreateUICanvas("EquipmentSlotViewTest");
            _canvas.RenderMode = CanvasRenderMode.ScreenSpace;
            _canvas.Order = 100;

            _canvas.GUI.Size = Screen.Size;
            _canvas.GUI.AnchorPreset = AnchorPresets.StretchAll;
            _canvas.GUI.Offsets = Margin.Zero;
            _canvas.GUI.BackgroundColor = new Color(0.1f, 0.1f, 0.1f, 1.0f);

            var titleLabel = new Label
            {
                Parent = _canvas.GUI,
                AnchorPreset = AnchorPresets.TopCenter,
                Offsets = new Margin(0, 20, 0, 0),
                Size = new Float2(400, 40),
                Text = "装备插槽视图测试",
                TextColor = Color.White,
                Font = UIHelper.SetFont(size: 24),
                HorizontalAlignment = TextAlignment.Center
            };

            var container = new Panel
            {
                Parent = _canvas.GUI,
                AnchorPreset = AnchorPresets.MiddleCenter,
                Size = new Float2(400, 120),
                BackgroundColor = new Color(0.15f, 0.15f, 0.15f, 0.9f)
            };

            // 空槽
            var emptySlot = new EquipmentSlotView(EquipmentSlot.Head, new Float2(80, 80))
            {
                Parent = container,
                Location = new Float2(40, 20)
            };
            emptySlot.Clicked += OnSlotClicked;

            // 已装备武器
            var weaponSlot = new EquipmentSlotView(EquipmentSlot.RightHand, new Float2(80, 80))
            {
                Parent = container,
                Location = new Float2(160, 20)
            };
            weaponSlot.Refresh(EquipmentDatabase.DefaultLongsword);
            weaponSlot.Clicked += OnSlotClicked;

            // 已装备身体
            var bodySlot = new EquipmentSlotView(EquipmentSlot.Body, new Float2(80, 80))
            {
                Parent = container,
                Location = new Float2(280, 20)
            };
            bodySlot.Refresh(EquipmentDatabase.DefaultBody);
            bodySlot.Clicked += OnSlotClicked;

            var infoLabel = new Label
            {
                Parent = _canvas.GUI,
                AnchorPreset = AnchorPresets.BottomCenter,
                Offsets = new Margin(0, 0, 0, 40),
                Size = new Float2(500, 30),
                Text = "点击插槽会在控制台输出事件；悬停有装备时显示名称提示",
                TextColor = Color.Gray,
                Font = UIHelper.SetFont(size: 14),
                HorizontalAlignment = TextAlignment.Center
            };
        }

        private void OnSlotClicked(EquipmentSlotView slotView)
        {
            string equipName = slotView.CurrentEquipment != null ? slotView.CurrentEquipment.Name : "空槽";
            Debug.Log($"[EquipmentSlotViewTest] 点击插槽: {slotView.Slot}, 装备: {equipName}");
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            if (_canvas != null)
            {
                UICanvas.Destroy(_canvas);
                _canvas = null;
            }
        }
    }
}
