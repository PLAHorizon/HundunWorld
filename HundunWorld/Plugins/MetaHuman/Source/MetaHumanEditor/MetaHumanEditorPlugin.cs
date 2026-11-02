using System;
using FlaxEditor;
using FlaxEngine;

namespace MetaHumanEditor
{
    /// <summary>
    /// MetaHuman 编辑器模块入口：在 Tools 菜单挂「数字人模板编辑器」。
    /// 菜单按钮延后重试添加（编辑器 UI 在 InitializeEditor 时可能还没建好，
    /// 一次性添加会在部分启动路径上静默消失 —— PCG 插件踩过同一坑）。
    /// </summary>
    public sealed class MetaHumanEditorPlugin : EditorPlugin
    {
        private bool _menuAdded;

        public MetaHumanEditorPlugin()
        {
            _description = new PluginDescription
            {
                Name = "MetaHuman Editor",
                Category = "Characters",
                Author = "HundunWorld",
                Description = "数字人模板编辑器：模板创作、自动绑骨与 morph 对照、骨骼动画绑定、UE5 MetaHuman 资源导入",
                Version = new Version(1, 0),
                IsAlpha = true,
            };
        }

        public override void InitializeEditor()
        {
            base.InitializeEditor();

            TryAddMenuButton();

            if (!_menuAdded && Editor != null)
            {
                Editor.EditorUpdate += OnEditorUpdateForMenu;
            }
        }

        private void OnEditorUpdateForMenu()
        {
            TryAddMenuButton();

            if (_menuAdded && Editor != null)
            {
                Editor.EditorUpdate -= OnEditorUpdateForMenu;
            }
        }

        private void TryAddMenuButton()
        {
            if (_menuAdded)
            {
                return;
            }

            try
            {
                if (Editor == null || Editor.UI == null)
                {
                    return;
                }

                Editor.UI.AddMenuButton("Tools", "数字人模板编辑器", OpenTool);
                _menuAdded = true;
            }
            catch (Exception exception)
            {
                Debug.Log("[MetaHuman] 菜单按钮添加失败，稍后重试：" + exception.Message);
            }
        }

        private void OpenTool()
        {
            if (Editor == null)
            {
                Debug.Log("[MetaHuman] 编辑器尚未就绪，请稍后再试。");
                return;
            }

            MetaHumanTemplateEditorWindow.ShowFor(Editor);
        }

        public override void DeinitializeEditor()
        {
            try
            {
                if (Editor != null)
                {
                    Editor.EditorUpdate -= OnEditorUpdateForMenu;
                }
            }
            catch
            {
                // 编辑器可能已经在拆事件源
            }

            _menuAdded = false;
            base.DeinitializeEditor();
        }
    }
}
