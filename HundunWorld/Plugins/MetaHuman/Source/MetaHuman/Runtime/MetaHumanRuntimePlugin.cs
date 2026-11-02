using System;
using FlaxEngine;

namespace MetaHuman.Runtime
{
    /// <summary>
    /// MetaHuman 运行期模块的插件入口。
    ///
    /// 存在的理由（引擎源码里量到的机制，不是猜的）：
    /// `Source/Engine/Scripting/Plugins/PluginManager.cpp` 的 `OnAssemblyLoaded` 会遍历
    /// **本次装载的装配里的每一个类**，凡是非抽象的 `GamePlugin` 子类就 `Scripting::NewObject` 实例化。
    /// 换句话说，"装配被带进当前脚本域"这件事在引擎里是有主动触点的，
    /// 但那个触点只认 `GamePlugin` 子类——而 `MetaHuman` 模块此前一个都没有
    /// （编辑器侧有 `MetaHumanEditorPlugin : EditorPlugin`，运行期侧是空的）。
    /// 于是每次换域（进/退播放模式、热重载）后，本装配里那些脚本类型没人替它们"报到"，
    /// 表现为 `MetaHuman.Runtime.MetaHumanCharacter` 不可实例化、
    /// `Actor.AddScript` 在 `Actor.cs:201` 拿到 null 后抛 NRE。
    ///
    /// 本文件是**一次可判真伪的实验**，不是已证疗效的修复：
    /// `Initialize` 里那行日志就是判据——重启后日志里应当出现一行
    /// `Loading plugin MetaHuman`，且随后撞 `metahuman_fixture` 不再 NRE。
    /// 若两件事都发生了，机制假设成立；若插件装载了但 NRE 依旧，假设被否证，
    /// 要按否证记回方案文档，别把它当"再重启一次就好"继续赌。
    /// </summary>
    public sealed class MetaHumanRuntimePlugin : GamePlugin
    {
        public MetaHumanRuntimePlugin()
        {
            _description = new PluginDescription
            {
                Name = "MetaHuman",
                Category = "Characters",
                Author = "HundunWorld",
                Description = "数字人运行期模块：模板参数求解、骨骼与 morph 写回、外观仓库与任意步骤回溯",
                Version = new Version(1, 0),
                IsAlpha = true,
            };
        }

        public override void Initialize()
        {
            base.Initialize();

            // 这行文本是本版独有的，故意当装载判据用：
            // 回执/日志里没有它，就说明引擎侧程序集还是加这个类之前的那份。
            Debug.Log("[MetaHuman] 运行期插件已初始化：本装配已在当前脚本域登记（装载判据 2026-10-15）");
        }
    }
}
