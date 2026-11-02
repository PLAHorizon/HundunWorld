using System;
using FlaxEngine;
using FlaxEditor;

namespace ItemEditor
{
    /// <summary>
    /// 物品编辑器插件入口：在 Flax 编辑器的 Tools 菜单里挂一个"物品编辑器"窗口。
    /// <para>
    /// 插件本身不含数据库依赖——所有读写经 <see cref="ItemCli"/> 交给 Horizon.ItemCli 子进程，
    /// 校验规则因此只存在于服务端那一份。
    /// </para>
    /// </summary>
    public class ItemEditorPlugin : EditorPlugin
    {
        private bool _menuAdded;

        public ItemEditorPlugin()
        {
            _description = new PluginDescription
            {
                Name = "ItemEditor",
                Category = "Editor",
                Author = "HundunWorld",
                Description = "MMORPG 物品编辑器（对标 WoW item_template）：编号/品质/类型、需求与限制、属性词条、武器伤害与护甲、插槽与宝石、随机词缀、套装、使用效果、经济与出处、tooltip 预览。数据权威在 SQL Server，本插件经 Horizon.ItemCli 子进程读写。",
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

                Editor.UI.AddMenuButton("Tools", "物品编辑器", () => ItemEditorWindow.ShowWindow());
                _menuAdded = true;
                Debug.Log("[ItemEditor] 菜单按钮已添加: Tools → 物品编辑器");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[ItemEditor] 菜单按钮添加失败: " + ex.Message);
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
