#if FLAX_EDITOR
namespace NarrativePro.EditorTools
{
    /// <summary>
    /// 叙事编辑面板（图 / 检查器 / 校验）所依赖的宿主能力。
    /// 独立窗口与"任务编辑器里的内嵌面板"各实现一份，面板因此不关心自己挂在谁的窗口里——
    /// 这是把叙事编辑能力并进任务编辑器时不必复制窗口逻辑的关键一层。
    /// </summary>
    public interface INarrativeEditorHost
    {
        /// <summary>当前资产的绝对路径（空串=没打开）。</summary>
        string CurrentPath { get; }

        /// <summary>当前资产名（不含扩展名）；没打开时给占位文案。</summary>
        string CurrentAssetName { get; }

        /// <summary>只更新状态行（不改变文档）。</summary>
        void Report(string message);

        /// <summary>结构/字段改动之后调用：入撤销栈、标脏、刷新图与检查器、重新校验。</summary>
        void Mutated(string message);

        /// <summary>选中图上的节点（会联动检查器）。</summary>
        void SelectNode(NarrativeSelectionKind kind, string id);

        void ClearSelection();

        /// <summary>打开一条对话资产（任务头里的关联对话）。</summary>
        void ShowDialogue(string path);

        /// <summary>资产文件操作；返回错误串，成功返回 null。</summary>
        string RenameCurrentAsset(string newName);

        string DuplicateCurrentAsset(string newName);

        string DeleteCurrentAsset();
    }
}
#endif
