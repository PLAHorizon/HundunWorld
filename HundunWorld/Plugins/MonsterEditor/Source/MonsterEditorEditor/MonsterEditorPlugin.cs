using System;
using FlaxEngine;
using FlaxEditor;

namespace MonsterEditor
{
    /// <summary>
    /// 怪物编辑器插件入口：在 Flax 编辑器的 Tools 菜单里挂一个"怪物编辑器"窗口。
    /// <para>
    /// 插件本身不含数据库依赖——所有读写经 <see cref="MonsterCli"/> 交给 Horizon.MonsterCli 子进程，
    /// 校验规则因此只存在于服务端那一份。
    /// </para>
    /// </summary>
    public class MonsterEditorPlugin : EditorPlugin
    {
        private bool _menuAdded;

        public MonsterEditorPlugin()
        {
            _description = new PluginDescription
            {
                Name = "MonsterEditor",
                Category = "Editor",
                Author = "HundunWorld",
                Description = "MMORPG 怪物编辑器：为怪物编号并配置属性公式、AI 档位与行为、技能挂载、巡逻路线与寻路、掉落与逐件概率、击杀经验（随玩家等级反比例衰减）。数据权威在 SQL Server，本插件经 Horizon.MonsterCli 子进程读写。",
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

                Editor.UI.AddMenuButton("Tools", "怪物编辑器", () => MonsterEditorWindow.ShowWindow());
                _menuAdded = true;
                Debug.Log("[QuestEditor] 菜单按钮已添加: Tools → 怪物编辑器");
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
