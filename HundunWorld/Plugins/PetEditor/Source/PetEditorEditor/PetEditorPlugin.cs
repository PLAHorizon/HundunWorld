using System;
using FlaxEngine;
using FlaxEditor;

namespace PetEditor
{
    /// <summary>
    /// 宠物编辑器插件入口：在 Flax 编辑器的 Tools 菜单里挂一个"宠物编辑器"窗口。
    /// <para>
    /// 插件本身不含数据库依赖——所有读写经 <see cref="PetCli"/> 交给 Horizon.PetCli 子进程，
    /// 校验规则因此只存在于服务端那一份。
    /// </para>
    /// </summary>
    public class PetEditorPlugin : EditorPlugin
    {
        private bool _menuAdded;

        public PetEditorPlugin()
        {
            _description = new PluginDescription
            {
                Name = "PetEditor",
                Category = "Editor",
                Author = "HundunWorld",
                Description = "MMORPG 宠物编辑器：为宠物编号并配置属性公式、AI 档位与行为、技能挂载、巡逻路线与寻路、掉落与逐件概率、击杀经验（随玩家等级反比例衰减）。数据权威在 SQL Server，本插件经 Horizon.PetCli 子进程读写。",
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

                Editor.UI.AddMenuButton("Tools", "宠物编辑器", () => PetEditorWindow.ShowWindow());
                _menuAdded = true;
                Debug.Log("[PetEditor] 菜单按钮已添加: Tools → 宠物编辑器");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[PetEditor] 菜单按钮添加失败: " + ex.Message);
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
