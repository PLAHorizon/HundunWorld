using System;
using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.Equipment;
using HundunWorld.Game.UI;
using HundunWorld.Game.UI.GameMain;

namespace HundunWorld.Game.UI.Tests
{
    /// <summary>
    /// EquipmentComparisonUI 内嵌预览临时测试脚本
    /// 创建三组面板分别验证：空槽+选中装备、已穿戴+选中其他装备、已穿戴+null 三种组合
    /// </summary>
    public class EquipmentComparisonUITest : Script
    {
        private UICanvas _canvas;
        private Label _logLabel;

        public override void OnStart()
        {
            base.OnStart();
            CreateTestUI();
        }

        private void CreateTestUI()
        {
            _canvas = UIHelper.CreateUICanvas("EquipmentComparisonUITest");
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
                Size = new Float2(600, 40),
                Text = "装备对比内嵌预览测试",
                TextColor = Color.White,
                Font = UIHelper.SetFont(size: 24),
                HorizontalAlignment = TextAlignment.Center
            };

            var equipUI = new EquipmentComparisonUI();

            // 测试用例 1：空槽 + 选中装备（应显示选中装备属性与“穿戴”按钮）
            CreateCasePanel(
                equipUI,
                "用例1：空槽 + 选中长剑",
                null,
                EquipmentDatabase.DefaultLongsword,
                new Float2(40, 80));

            // 测试用例 2：已穿戴 + 选中其他装备（应显示两列对比与“穿戴”按钮）
            CreateCasePanel(
                equipUI,
                "用例2：已穿戴长剑 + 选中衣服",
                EquipmentDatabase.DefaultLongsword,
                EquipmentDatabase.DefaultBody,
                new Float2(340, 80));

            // 测试用例 3：已穿戴 + null（应显示当前装备属性与“卸下”按钮）
            CreateCasePanel(
                equipUI,
                "用例3：已穿戴头巾 + 未选中",
                EquipmentDatabase.DefaultHeadScarf,
                null,
                new Float2(640, 80));

            _logLabel = new Label
            {
                Parent = _canvas.GUI,
                AnchorPreset = AnchorPresets.BottomCenter,
                Offsets = new Margin(0, 0, 0, 40),
                Size = new Float2(800, 30),
                Text = "点击按钮会在此处显示回调日志",
                TextColor = Color.Gray,
                Font = UIHelper.SetFont(size: 14),
                HorizontalAlignment = TextAlignment.Center
            };
        }

        private void CreateCasePanel(
            EquipmentComparisonUI equipUI,
            string title,
            EquipmentData current,
            EquipmentData selected,
            Float2 position)
        {
            var titleLabel = new Label
            {
                Parent = _canvas.GUI,
                Location = new Float2(position.X, position.Y - 30),
                Size = new Float2(260, 26),
                Text = title,
                TextColor = new Color(0.8f, 0.8f, 0.8f),
                Font = UIHelper.SetFont(size: 12),
                HorizontalAlignment = TextAlignment.Center
            };

            var container = new Panel
            {
                Parent = _canvas.GUI,
                Location = position,
                Size = new Float2(280, 360),
                BackgroundColor = new Color(0.15f, 0.15f, 0.15f, 0.9f)
            };

            equipUI.PopulateEmbeddedPreview(
                container,
                current,
                selected,
                () => LogAction($"[{title}] 点击穿戴"),
                () => LogAction($"[{title}] 点击卸下"));

            UIHelper.ApplyChineseFontRecursive(container);
        }

        private void LogAction(string message)
        {
            Debug.Log($"[EquipmentComparisonUITest] {message}");
            if (_logLabel != null)
                _logLabel.Text = message;
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
