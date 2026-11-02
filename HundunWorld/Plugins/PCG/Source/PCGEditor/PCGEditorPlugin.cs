// PCG Plugin - Editor plugin entry
// Part of the FlaxEngine PCG plugin. See docs/PCG_PLUGIN_DESIGN.md §8.

using System;
using FlaxEditor;
using FlaxEngine;
using PCG.Execution;
using PCG.Runtime;

namespace PCGEditor
{
    /// <summary>
    /// Editor-side entry point: the graph tool command and the reload hygiene the canvas needs.
    /// </summary>
    /// <remarks>
    /// The menu button is added with a retry because the editor UI may not exist yet when
    /// <see cref="InitializeEditor"/> runs (docs/PCG_PLUGIN_DESIGN.md §8.1 note 16); without the retry the
    /// command silently disappears on some startup paths.
    /// </remarks>
    public sealed class PCGEditorPlugin : EditorPlugin
    {
        private bool _menuAdded;

        /// <summary>Creates the plugin and its description.</summary>
        public PCGEditorPlugin()
        {
            _description = new PluginDescription
            {
                Name = "PCG Editor",
                Category = "Procedural",
                Author = "HundunWorld",
                Description = "PCG 图表编辑器：节点画布、参数表与图表文件读写",
                Version = new Version(1, 0),
                IsAlpha = true,
            };
        }

        /// <inheritdoc />
        public override void InitializeEditor()
        {
            base.InitializeEditor();

            // The canvas builds its node table from the runtime registry, which is normally populated by the
            // runtime plugin's own start; register here as well so the tool still works when only the editor
            // side has come up.
            if (!PCGNodeRegistration.IsRegistered)
                PCGNodeRegistration.RegisterAll();

            TryAddMenuButton();

            if (!_menuAdded && Editor != null)
                Editor.EditorUpdate += OnEditorUpdateForMenu;
        }

        private void OnEditorUpdateForMenu()
        {
            TryAddMenuButton();

            if (_menuAdded && Editor != null)
                Editor.EditorUpdate -= OnEditorUpdateForMenu;
        }

        private void TryAddMenuButton()
        {
            if (_menuAdded)
                return;

            try
            {
                if (Editor?.UI == null)
                    return;

                Editor.UI.AddMenuButton("Tools", "PCG 图表编辑器", OpenTool);
                _menuAdded = true;
            }
            catch (Exception ex)
            {
                Debug.Log($"[PCG] 菜单按钮添加失败，稍后重试：{ex.Message}");
            }
        }

        /// <summary>
        /// Opens the graph tool. The window asks for a graph file name rather than picking up a Content
        /// Browser selection, because the editor exposes no public getter for the current selection to script
        /// plugins - only <c>Select(...)</c> to set one.
        /// </summary>
        private void OpenTool()
        {
            if (Editor == null)
            {
                Debug.Log("[PCG] 编辑器尚未就绪，请稍后再试。");
                return;
            }

            PCGGraphEditorWindow.ShowFor(Editor, null);
            Debug.Log($"[PCG] PCG 图表编辑器已打开。图表文件目录：{PCGGraphFile.DefaultFolder()}");
        }

        /// <inheritdoc />
        public override void DeinitializeEditor()
        {
            try
            {
                if (Editor != null)
                    Editor.EditorUpdate -= OnEditorUpdateForMenu;
            }
            catch (Exception)
            {
                // The editor may already be tearing down its event sources.
            }

            _menuAdded = false;
            base.DeinitializeEditor();
        }

        /// <inheritdoc />
        public override void Deinitialize()
        {
            // The archetype delegates and their node factories point into this assembly, so the table must not
            // survive a scripts reload (docs/PCG_PLUGIN_DESIGN.md §8.2).
            PCGNodeArchetypes.Clear();
            base.Deinitialize();
        }
    }
}
