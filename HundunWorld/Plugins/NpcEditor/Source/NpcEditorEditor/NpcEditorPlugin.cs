using System;
using FlaxEngine;
using FlaxEditor;

namespace NpcEditor
{
    /// <summary>
    /// NPC 编辑器插件入口：在 Flax 编辑器的 Tools 菜单里挂一个"NPC 编辑器"窗口。
    /// <para>
    /// 插件本身不含数据库依赖——所有读写经 <see cref="NpcCli"/> 交给 Horizon.NpcCli 子进程，
    /// 校验规则因此只存在于服务端那一份（与 QuestEditor / MonsterEditor 同一条纪律）。
    /// </para>
    /// </summary>
    public class NpcEditorPlugin : EditorPlugin
    {
        private bool _menuAdded;

        public NpcEditorPlugin()
        {
            _description = new PluginDescription
            {
                Name = "NpcEditor",
                Category = "Editor",
                Author = "HundunWorld",
                Description = "MMORPG NPC 编辑器：为 NPC 编号并配置技能教学、行为与 AI 档位、外观、任务发布与头顶指示器、"
                    + "售卖货架、技能学习、制作配方与设施、巡逻寻路与作息、掉落与击杀贡献经验（PVP/敌对，随玩家等级反比例衰减）。"
                    + "数据权威在 SQL Server，本插件经 Horizon.NpcCli 子进程读写。",
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
                // InitializeEditor 期 Editor.UI 还不一定就绪：挂了更新回调重试，不要在这里抛。
                if (Editor?.UI == null)
                    return;

                Editor.UI.AddMenuButton("Tools", "NPC 编辑器", NpcEditorWindow.ShowWindow);
                _menuAdded = true;
                Debug.Log("[NpcEditor] 菜单按钮已添加: Tools → NPC 编辑器");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[NpcEditor] 菜单按钮添加失败: " + ex.Message);
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
