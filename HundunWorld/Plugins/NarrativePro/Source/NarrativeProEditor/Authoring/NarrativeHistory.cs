#if FLAX_EDITOR
using System.Collections.Generic;

namespace NarrativePro.EditorTools
{
    /// <summary>
    /// 叙事编辑器的撤销/重做栈。存的是内存快照串（与写盘同一套序列化口径），只在内存里，绝不落盘。
    /// <para>
    /// 用"基线 + 提交"而不是"改动前插桩"：调用方改完 DTO 后把新快照交进来，栈里存的是上一份基线，
    /// 那正好是本次改动之前的状态。这样检查器与拖动都不用知道历史的存在。
    /// </para>
    /// </summary>
    internal sealed class NarrativeHistory
    {
        private readonly List<string> _undo = new List<string>();
        private readonly List<string> _redo = new List<string>();
        private readonly int _limit;
        private string _baseline = "";

        public NarrativeHistory(int limit = 40)
        {
            _limit = limit < 1 ? 1 : limit;
        }

        /// <summary>可撤销层数。</summary>
        public int UndoCount => _undo.Count;

        /// <summary>可重做层数。</summary>
        public int RedoCount => _redo.Count;

        /// <summary>当前基线快照（= 最近一次提交后的内容）。</summary>
        public string Baseline => _baseline;

        /// <summary>换文档：两侧栈都归零，基线设为磁盘版本。</summary>
        public void OpenDocument(string snapshot)
        {
            _undo.Clear();
            _redo.Clear();
            _baseline = snapshot ?? "";
        }

        /// <summary>
        /// 提交一份改动后的快照。内容与基线相同就不入栈（空点一次按钮不该占一层历史），返回是否真的入栈。
        /// </summary>
        public bool Commit(string currentSnapshot)
        {
            string current = currentSnapshot ?? "";
            if (current == _baseline) return false;

            _undo.Add(_baseline);
            while (_undo.Count > _limit) _undo.RemoveAt(0);
            _redo.Clear();
            _baseline = current;
            return true;
        }

        /// <summary>回退一层；没有可撤销的返回 null。</summary>
        public string Undo(string currentSnapshot)
        {
            if (_undo.Count == 0) return null;

            string target = _undo[_undo.Count - 1];
            _undo.RemoveAt(_undo.Count - 1);
            _redo.Add(currentSnapshot ?? "");
            _baseline = target;
            return target;
        }

        /// <summary>前进一步；没有可重做的返回 null。</summary>
        public string Redo(string currentSnapshot)
        {
            if (_redo.Count == 0) return null;

            string target = _redo[_redo.Count - 1];
            _redo.RemoveAt(_redo.Count - 1);
            _undo.Add(currentSnapshot ?? "");
            _baseline = target;
            return target;
        }
    }
}
#endif
