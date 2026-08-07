using System;
using System.Collections.Generic;
using FlaxEditor;

namespace HundunAgent.Core
{
    /// <summary>
    /// 基于 Lambda 的自定义撤销动作。
    /// </summary>
    public sealed class LambdaUndoAction : IUndoAction
    {
        private readonly Action _do;
        private readonly Action _undo;

        public LambdaUndoAction(Action doAction, Action undoAction)
        {
            _do = doAction;
            _undo = undoAction;
        }

        /// <inheritdoc />
        public string ActionString => "HundunAgent";

        /// <inheritdoc />
        public void Dispose()
        {
        }

        /// <inheritdoc />
        public void Do()
        {
            _do?.Invoke();
        }

        /// <inheritdoc />
        public void Undo()
        {
            _undo?.Invoke();
        }
    }

    /// <summary>
    /// AI 操作的 Undo 事务管理：
    /// 每次变更工具调用后通过 <see cref="Record"/> 记录一条撤销项；
    /// 任务级回滚通过连续调用 Editor.PerformUndo 实现（见 undo_rollback 工具）。
    /// </summary>
    public static class AgentUndo
    {
        /// <summary>
        /// 记录一条撤销动作（必须在主线程调用）。
        /// </summary>
        /// <param name="actionName">撤销项名称，如 "AI: 设置 Transform"。</param>
        /// <param name="undoCallback">撤销时执行的恢复逻辑。</param>
        /// <param name="redoCallback">重做时执行的逻辑（可为 null）。</param>
        /// <param name="markSceneEdited">是否标记场景已修改。</param>
        public static void Record(string actionName, Action undoCallback, Action redoCallback = null, bool markSceneEdited = true)
        {
            var editor = FlaxEditor.Editor.Instance;
            if (editor?.Undo == null || !editor.Undo.Enabled)
                return;

            editor.Undo.AddAction(new MultiUndoAction(
                new IUndoAction[] { new LambdaUndoAction(redoCallback, undoCallback) },
                actionName));

            if (markSceneEdited && editor.Scene != null)
                editor.Scene.MarkAllScenesEdited();

            ToolRegistry.OnUndoableAction();
        }
    }
}
