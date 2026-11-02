#if FLAX_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using FlaxEditor;
using FlaxEngine;

namespace NarrativePro.EditorTools
{
    /// <summary>
    /// NarrativePro 叙事资产创作编辑器插件：UE5 那四个 <c>EditorModules</c>（NarrativeQuestEditor /
    /// NarrativeDialogueEditor / NarrativeArsenalEditor / NarrativeArsenalPreEditor）在本端口的真实落点。
    /// <para>
    /// 原先它们是 <c>public static class</c>：静态类永远构造不出 <c>EditorPlugin</c>，也没有任何调用方，
    /// 只把 <c>GameAssetCategory</c> 赋成 1/2 然后打一行日志——等于没有编辑器入口。
    /// 现在按 <see cref="NarrativePro.Editor.Navigation.NarrativeProNavigationEditorPlugin"/> 的既证通路
    /// （<c>: FlaxEditor.EditorPlugin</c> + <c>InitializeEditor</c> + <c>Editor.UI.AddMenuButton(...)</c>）实现。
    /// </para>
    /// <para>菜单（Tools → NarrativePro: ...）：</para>
    /// <list type="bullet">
    /// <item>打开叙事资产编辑器：打开可停靠窗口，新建/打开/编辑/校验/保存 Quest 与 Dialogue；</item>
    /// <item>新建任务模板 / 新建对话模板：直接落盘到运行时读取的目录并通过运行时加载器自检；</item>
    /// <item>校验全部叙事资产：逐个用 <c>QuestFactory.LoadQuest</c> / <c>DialogueFactory.LoadDialogue</c>
    /// 真装一遍，并把未知事件/条件/任务类型按注册表报错；</item>
    /// <item>打印逻辑类型注册表：导出运行时 <c>NarrativeLogicFactory</c> 当前可用的类型名。</item>
    /// </list>
    /// <para>被丢弃的 UE5 模块职责（Flax 无对应 API，无法在编辑器脚本层表达）：</para>
    /// <list type="bullet">
    /// <item>资产类型操作/资产编辑器工具包（FAssetTypeActions_*、AssetEditorToolkit、蓝图工厂）：
    /// FlaxEditor 没有按类型注册资产编辑器的公开 API（<c>FlaxEngine.CSharp.xml</c> 里 <c>FlaxEditor</c> 侧只有
    /// <c>EditorPlugin</c> / <c>UIModule.AddMenuButton</c> / <c>WindowsModule.Open</c> 这类入口），
    /// 本端口用编辑器窗口承担同一职责；</item>
    /// <item>组件可视化器 / 详情面板定制 / 属性定制（FComponentVisualizer、IDetailCustomization）：同上，无对应扩展点；</item>
    /// <item>存档槽菜单"play using this save"：<c>UIModule</c> 只能加菜单项与状态栏文字，
    /// 没有任何把存档槽注入播放流程的 API，存档槽选择留在游戏内暂停菜单做；</item>
    /// <item>ProjectSetup 向导与资产分类常量（GameAssetCategory）：Flax 的 Content 树由引擎自身管理，
    /// 分类 ID 是给 UE5 AssetRegistry 用的整数，写在这里没有任何消费者，已随静态类一并删除。</item>
    /// </list>
    /// </summary>
    public class NarrativeProAuthoringEditorPlugin : EditorPlugin
    {
        private NarrativeProAuthoringWindow _window;

        /// <summary>当前插件实例：给跨插件反射入口（任务编辑器 → 叙事编辑器）用。</summary>
        private static NarrativeProAuthoringEditorPlugin _instance;

        /// <summary>本次会话里是否有人真的要求打开过窗口（菜单/标记/跨插件跳转）。</summary>
        private static bool _openRequested;

        /// <summary>启动后等布局恢复完成，再决定要不要关掉"被自动恢复出来"的窗口。</summary>
        private bool _restoreChecked;

        private double _firstUpdateTime;

        /// <summary>
        /// Flax 会按布局把上次开着的窗口恢复出来；任务编辑器已经内嵌了叙事图，
        /// 独立窗口在启动时再冒出来只会挡住主界面。等 2 秒（布局稳定）后：没人请求过就关掉它。
        /// </summary>
        private void CloseUnrequestedRestoredWindow()
        {
            if (_restoreChecked) return;

            double now = DateTime.UtcNow.Ticks / (double)TimeSpan.TicksPerSecond;
            if (_firstUpdateTime <= 0)
            {
                _firstUpdateTime = now;
                return;
            }
            if (now - _firstUpdateTime < 2.0) return;

            _restoreChecked = true;
            if (_window == null || _openRequested) return;

            try
            {
                Debug.Log("[NarrativePro] 关闭随布局恢复出来的独立窗口（本次会话没有请求打开；任务编辑器里已内嵌叙事图）");
                _window.Close();
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[NarrativePro] 关闭恢复窗口失败：" + ex.Message);
            }
            _window = null;
        }

        /// <summary>
        /// 供其它编辑器（本工程的「任务编辑器」）调用：打开叙事资产编辑器并载入指定资产。
        /// 找不到资产时窗口会自己报"找不到名为 … 的叙事资产"。
        /// </summary>
        public static bool OpenAssetByName(string assetName)
        {
            var plugin = _instance;
            if (plugin == null || string.IsNullOrEmpty(assetName)) return false;

            plugin.OpenWindow();
            if (plugin._window == null) return false;
            plugin._window.TryOpenAsset(assetName);

            // TryOpenAsset 在"有未保存修改"时会拒绝打开；这里核对真的换过去了，别对调用方谎报成功。
            return string.Equals(plugin._window.CurrentAssetName, assetName, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>给跨插件反射入口用：最近一次报告的状态行（便于对方回显）。</summary>
        public static string LastStatus => _instance?._window?.CurrentStatus ?? "";
        private bool _menuAdded;

        public NarrativeProAuthoringEditorPlugin()
        {
            _description = new PluginDescription
            {
                Name = "NarrativePro Authoring Tools",
                Category = "Tools",
                Author = "成阳",
                Description = "叙事资产创作工具：任务/对话 JSON 的新建、编辑、校验与保存",
                Version = new Version(1, 0),
                IsAlpha = false,
                IsBeta = false,
            };
        }

        public override void InitializeEditor()
        {
            base.InitializeEditor();
            _instance = this;

            // 预热注册表并保证数据目录存在：菜单项点第一次就能列出正确名单、模板能直接落盘。
            NarrativeAssetStore.WarmLogicRegistry();
            try
            {
                NarrativeAssetStore.EnsureDirectories();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[NarrativePro] 叙事数据目录创建失败：{ex.Message}");
            }

            TryAddMenuButtons();
            if (Editor != null)
            {
                Editor.EditorUpdate += OnEditorUpdate;
            }
        }

        public override void DeinitializeEditor()
        {
            try
            {
                if (Editor != null)
                {
                    Editor.EditorUpdate -= OnEditorUpdate;
                }
                if (_window != null)
                {
                    _window.Dispose();
                    _window = null;
                }
            }
            catch { }

            base.DeinitializeEditor();
        }

        private void OnEditorUpdate()
        {
            if (!_menuAdded) TryAddMenuButtons();
            if (_window != null && _window.NeedsInitialPlacement) _window.EnsureFloatingRect();
            CloseUnrequestedRestoredWindow();
            TryAutoOpenFromMarker();
        }

        // 自动化验收钩子：工程根 Tools/_np_editor_open.on 存在时打开本窗口，
        // 文件内容可写一个资产名（任务或对话），窗口会顺带打开它。没有该文件时完全不影响正常使用。
        // 采用轮询（0.5s 节流）而不是一次性检查：编辑器启动后会重载插件脚本，窗口会被销毁重建。
        private double _nextMarkerCheck;

        private void TryAutoOpenFromMarker()
        {
            double now = DateTime.UtcNow.Ticks / (double)TimeSpan.TicksPerSecond;
            if (now < _nextMarkerCheck) return;
            _nextMarkerCheck = now + 0.5;

            string marker;
            try
            {
                marker = Path.Combine(Globals.ProjectFolder, "Tools", "_np_editor_open.on");
            }
            catch
            {
                return;
            }

            if (!File.Exists(marker) || Editor?.UI == null || Editor.Windows == null) return;

            string asset = "";
            try
            {
                asset = File.ReadAllText(marker).Trim();
                File.Delete(marker);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[NarrativePro] NP_AUTOOPEN marker cleanup failed: " + ex.Message);
                return;
            }

            OpenWindow();
            if (string.IsNullOrEmpty(asset)) return;

            // 标记文件支持 "资产名|命令1;命令2"（命令：validate / layout / act:* / fileop:* …），供自动化验收直接跑真实通路。
            string[] parts = asset.Split('|');
            string wanted = parts[0].Trim();

            // 已经是当前资产就别再打开：重开会把选中项清空，选择+动作这种两步命令就断在半路。
            bool alreadyOpen = !string.IsNullOrEmpty(wanted) &&
                               string.Equals(_window?.CurrentAssetName, wanted, StringComparison.OrdinalIgnoreCase);
            if (!alreadyOpen)
            {
                _window?.TryOpenAsset(wanted);

                // 资产没打开成功就往下跑命令是危险的：fileop:delete 这类命令作用在"当前文档"上，
                // 写错资产名就会删掉另一个资产（实测发生过一次）。宁可跳过，也不要在错的文档上执行。
                string current = _window?.CurrentAssetName ?? "";
                if (!string.IsNullOrEmpty(wanted) && !string.Equals(current, wanted, StringComparison.OrdinalIgnoreCase))
                {
                    Debug.LogWarning($"[NarrativePro] NP_MARKER_SKIP 资产 {wanted} 没打开（当前 {current}），命令一律不执行。");
                    return;
                }
            }

            if (parts.Length > 1)
            {
                foreach (var piece in parts[1].Split(';'))
                {
                    string cmd = piece.Trim();
                    if (cmd.Length > 0) _window?.RunAutomationCommand(cmd);
                }
            }
        }

        private void TryAddMenuButtons()
        {
            if (_menuAdded) return;

            try
            {
                if (Editor?.UI == null) return;

                Editor.UI.AddMenuButton("Tools", "NarrativePro: 打开叙事资产编辑器（任务/对话）", OpenWindow);
                Editor.UI.AddMenuButton("Tools", "NarrativePro: 新建任务模板（Quest）", CreateQuestTemplate);
                Editor.UI.AddMenuButton("Tools", "NarrativePro: 新建对话模板（Dialogue）", CreateDialogueTemplate);
                Editor.UI.AddMenuButton("Tools", "NarrativePro: 校验全部叙事资产", ValidateAllAssets);
                Editor.UI.AddMenuButton("Tools", "NarrativePro: 打印叙事逻辑类型注册表", PrintRegistry);

                _menuAdded = true;
                Debug.Log("[NarrativePro] 叙事创作工具菜单已添加：Tools → NarrativePro: ...");
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[NarrativePro] 叙事创作工具菜单添加失败，稍后重试: " + ex.Message);
            }
        }

        // ===================================================================
        // 菜单动作
        // ===================================================================

        private void OpenWindow()
        {
            _openRequested = true;
            if (Editor == null)
            {
                Debug.LogWarning("[NarrativePro] 编辑器尚未就绪，稍后再试一次该菜单命令");
                return;
            }

            try
            {
                if (_window == null)
                {
                    _window = new NarrativeProAuthoringWindow(Editor);
                    Editor.Windows.Open(_window);
                    Say("叙事资产编辑器已打开（Tools 菜单再次点击会回到该窗口）");
                    return;
                }

                // hideOnClose=true 的窗口关标签只是隐藏，重新 Open/Focus 即可回到它。
                try
                {
                    _window.FocusOrShow();
                }
                catch
                {
                    Editor.Windows.Open(_window);
                }
                _window.RefreshAssets();
            }
            catch (Exception ex)
            {
                Debug.LogError("[NarrativePro] NP_OPEN failed: " + ex);
            }
        }

        private void CreateQuestTemplate()
        {
            string id = "Q_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string path;
            try
            {
                NarrativeAssetStore.CreateQuestTemplate(id, out path);
            }
            catch (Exception ex)
            {
                Fail($"新建任务模板失败：{ex.Message}");
                return;
            }

            if (!Report(path, NarrativeAssetStore.ValidateQuestFile(path), $"已新建任务 {Path.GetFileName(path)}")) return;

            OpenWindow();
            _window?.ShowQuest(path);
        }

        private void CreateDialogueTemplate()
        {
            string id = "D_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string path;
            try
            {
                NarrativeAssetStore.CreateDialogueTemplate(id, out path);
            }
            catch (Exception ex)
            {
                Fail($"新建对话模板失败：{ex.Message}");
                return;
            }

            if (!Report(path, NarrativeAssetStore.ValidateDialogueFile(path), $"已新建对话 {Path.GetFileName(path)}")) return;

            OpenWindow();
            _window?.ShowDialogue(path);
        }

        private void ValidateAllAssets()
        {
            var quests = NarrativeAssetStore.ListJsonFiles(NarrativeAssetStore.QuestsDirectory);
            var dialogues = NarrativeAssetStore.ListJsonFiles(NarrativeAssetStore.DialoguesDirectory);
            int failed = 0;

            foreach (var path in quests)
            {
                failed += PrintProblems(path, NarrativeAssetStore.ValidateQuestFile(path));
            }
            foreach (var path in dialogues)
            {
                failed += PrintProblems(path, NarrativeAssetStore.ValidateDialogueFile(path));
            }

            int total = quests.Count + dialogues.Count;
            if (failed == 0)
            {
                Say($"叙事资产全量校验通过：{total} 个（{quests.Count} 任务 / {dialogues.Count} 对话），运行时装载器全部读得懂");
                Debug.Log($"[NarrativePro] 叙事资产全量校验通过：{total} 个");
            }
            else
            {
                Say($"叙事资产校验发现 {failed} 个问题条目，详见日志");
                Debug.LogWarning($"[NarrativePro] 叙事资产校验发现 {failed} 个问题条目（任务目录 {NarrativeAssetStore.QuestsDirectory}；对话目录 {NarrativeAssetStore.DialoguesDirectory}）");
            }
        }

        private void PrintRegistry()
        {
            NarrativeAssetStore.WarmLogicRegistry();
            string text = NarrativeAssetStore.DescribeRegistry();
            Debug.Log("[NarrativePro] 叙事逻辑类型注册表（运行时可用名）\n" + text);
            Say("注册表名单已写入日志");
        }

        // ===================================================================
        // 反馈
        // ===================================================================

        /// <summary>把校验结果讲清楚：通过则回执路径，失败则逐条报错并写状态栏。</summary>
        private bool Report(string path, List<string> problems, string okMessage)
        {
            if (problems != null && problems.Count > 0)
            {
                var text = new System.Text.StringBuilder(okMessage + "，但校验未通过：");
                foreach (var problem in problems)
                {
                    text.Append(" ").Append(problem);
                    Debug.LogError($"[NarrativePro][校验] {Path.GetFileName(path)}: {problem}");
                }
                Fail(text.ToString());
                return false;
            }

            Say(okMessage + "（已通过运行时装载器校验）");
            Debug.Log($"[NarrativePro] {okMessage} -> {path}");
            return true;
        }

        private int PrintProblems(string path, List<string> problems)
        {
            if (problems.Count == 0)
            {
                Debug.Log($"[NarrativePro][校验] {Path.GetFileName(path)} OK");
                return 0;
            }

            foreach (var problem in problems)
            {
                Debug.LogError($"[NarrativePro][校验] {Path.GetFileName(path)}: {problem}");
            }
            return problems.Count;
        }

        private void Say(string message)
        {
            try
            {
                Editor?.UI?.AddStatusMessage(message);
            }
            catch { }
        }

        private void Fail(string message)
        {
            Say(message);
            Debug.LogError("[NarrativePro] " + message);
        }
    }
}
#endif
