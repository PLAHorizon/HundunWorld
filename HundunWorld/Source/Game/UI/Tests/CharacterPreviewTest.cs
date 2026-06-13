using System;
using FlaxEngine;
using FlaxEngine.GUI;
using HundunWorld.Game.UI.Components;
using HundunWorld.Game.UI;

namespace HundunWorld.Game.UI.Tests
{
    public class CharacterPreviewTest : Script
    {
        private UICanvas _canvas;
        private CharacterPreviewPanel _previewPanel;
        
        public string CharacterPrefabPath = "Characters/player";
        
        public override void OnStart()
        {
            base.OnStart();
            
            CreateTestUI();
        }
        
        private void CreateTestUI()
        {
            _canvas = UIHelper.CreateUICanvas("CharacterPreviewTest");
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
                Size = new Float2(300, 40),
                Text = "3D角色预览测试",
                TextColor = Color.White,
                Font = UIHelper.SetFont(size: 24),
                HorizontalAlignment = TextAlignment.Center
            };
            
            _previewPanel = new CharacterPreviewPanel
            {
                Parent = _canvas.GUI,
                AnchorPreset = AnchorPresets.StretchAll,
                Offsets = new Margin(100, 100, 100, 100),
                CharacterPrefabPath = CharacterPrefabPath
            };
            
            _previewPanel.OnCharacterLoaded += OnCharacterLoaded;
            
            var infoLabel = new Label
            {
                Parent = _canvas.GUI,
                AnchorPreset = AnchorPresets.BottomCenter,
                Offsets = new Margin(0, 0, 0, 50),
                Size = new Float2(400, 60),
                Text = "操作说明:\n- 鼠标拖拽: 旋转视角\n- 鼠标滚轮: 缩放视角\n- R按钮: 重置视角\n- 左右按钮: 快速旋转",
                TextColor = Color.Gray,
                Font = UIHelper.SetFont(size: 14),
                HorizontalAlignment = TextAlignment.Center,
                AutoHeight = true
            };
        }
        
        private void OnCharacterLoaded()
        {
            Debug.Log("Character loaded successfully!");
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