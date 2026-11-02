using System;
using FlaxEngine;
using FlaxEditor;

namespace QuestEditor
{
    /// <summary>
    /// 任务编辑器插件入口：在 Flax 编辑器的 Tools 菜单里挂一个"任务编辑器"窗口。
    /// <para>
    /// 插件本身不含数据库依赖——所有读写经 <see cref="QuestCli"/> 交给 Horizon.QuestCli 子进程，
    /// 校验规则因此只存在于服务端那一份。
    /// </para>
    /// </summary>
    public class QuestEditorPlugin : EditorPlugin
    {
        private bool _menuAdded;

        public QuestEditorPlugin()
        {
            _description = new PluginDescription
            {
                Name = "QuestEditor",
                Category = "Editor",
                Author = "HundunWorld",
                Description = "MMORPG 任务编辑器：可视化编辑任务内容/完成条件/奖励，关联 NPC·怪物·物品，实时发布与下线",
                Version = new Version(1, 0),
                IsAlpha = false,
                IsBeta = false,
            };
        }

        /// <inheritdoc />
        public override void InitializeEditor()
        {
            base.InitializeEditor();

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

                Editor.UI.AddMenuButton("Tools", "任务编辑器", () => QuestEditorWindow.ShowWindow());
                _menuAdded = true;
                Debug.Log("[QuestEditor] 菜单按钮已添加: Tools → 任务编辑器");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[QuestEditor] 菜单按钮添加失败: " + ex.Message);
            }
        }

        /// <inheritdoc />
        public override void DeinitializeEditor()
        {
            try
            {
                if (Editor != null)
                    Editor.EditorUpdate -= OnEditorUpdateForMenu;
            }
            catch
            {
                // 卸载阶段的菜单解绑失败没有可操作意义。
            }

            base.DeinitializeEditor();
        }
    }
}
