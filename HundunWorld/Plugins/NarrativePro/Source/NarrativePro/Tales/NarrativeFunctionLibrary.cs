using System;
using System.Collections.Generic;
using FlaxEngine;
using NarrativePro.Core;
using NarrativePro.Tales.Data;
using DialogueClass = NarrativePro.Tales.Dialogue.Dialogue;
using QuestClass = NarrativePro.Tales.Quest.Quest;

namespace NarrativePro.Tales
{
    /// <summary>
    /// 叙事系统静态工具函数库。对应 UE5 UNarrativeFunctionLibrary。
    /// </summary>
    public static class NarrativeFunctionLibrary
    {
        /// <summary>
        /// 从 WorldContextObject 查找本地 TalesComponent。
        /// Flax 中简化实现：遍历当前场景中所有 TalesComponent，返回第一个激活的。
        /// </summary>
        public static TalesComponent GetTalesComponent(object worldContextObject)
        {
            var list = FindAllTalesComponents();
            return list.Count > 0 ? list[0] : null;
        }

        /// <summary>
        /// 从目标 Actor 上查找 TalesComponent。
        /// </summary>
        public static TalesComponent GetTalesComponentFromTarget(Actor target)
        {
            if (target == null) return null;
            return target.GetScript<TalesComponent>();
        }

        /// <summary>
        /// 调用 TalesComponent.CompleteNarrativeDataTask 完成数据任务。
        /// </summary>
        public static bool CompleteNarrativeDataTask(TalesComponent target, NarrativeDataTask task, string argument, int quantity = 1)
        {
            if (target == null || task == null) return false;
            return target.CompleteNarrativeDataTask(task.TaskName, argument, quantity);
        }

        /// <summary>
        /// 完成松散数据任务（无需 NarrativeDataTask 资产）。
        /// </summary>
        public static bool CompleteLooseNarrativeDataTask(TalesComponent target, string argument, int quantity = 1)
        {
            if (target == null) return false;
            return target.CompleteNarrativeDataTask("LooseTask", argument, quantity);
        }

        /// <summary>
        /// 按名称获取数据任务。Flax 中通过 NarrativeDataLoader 从 JSON 加载 NarrativeDataTask。
        /// </summary>
        public static NarrativeDataTask GetTaskByName(object worldContextObject, string eventName)
        {
            if (string.IsNullOrEmpty(eventName)) return null;
            var settings = NarrativeProPlugin.Instance?.NarrativeSettings;
            string dir = settings?.DefaultDataTaskDirectory ?? "Content/NarrativePro/DataTasks";
            string path = System.IO.Path.Combine(dir, eventName + ".json");
            return NarrativePro.Data.NarrativeDataLoader.LoadDataTask(path);
        }

        /// <summary>
        /// 生成显示字符串（FName::NameToDisplayString 的简化版）。
        /// 将下划线/驼峰边界转为空格并首字母大写。
        /// </summary>
        public static string MakeDisplayString(string str)
        {
            if (string.IsNullOrEmpty(str)) return str;
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < str.Length; i++)
            {
                char c = str[i];
                if (c == '_')
                {
                    sb.Append(' ');
                }
                else if (char.IsUpper(c) && i > 0 && (char.IsLower(str[i - 1]) || char.IsDigit(str[i - 1])))
                {
                    sb.Append(' ');
                    sb.Append(c);
                }
                else
                {
                    sb.Append(i == 0 ? char.ToUpper(c) : c);
                }
            }
            return sb.ToString();
        }

        // ===== 对话节点选择器工厂 =====

        public static DialogueNodeSelector MakeDialogueNodeSelector(DialogueNodeSelector selector)
        {
            return selector ?? new DialogueNodeSelector();
        }

        public static DialogueNodeSelector MakeDialogueNodeSelectorFromID(string nodeID)
        {
            return new DialogueNodeSelector { NodeID = nodeID ?? "" };
        }

        public static void BreakDialogueNodeSelector(DialogueNodeSelector selector, out string nodeID)
        {
            nodeID = selector?.NodeID ?? "";
        }

        public static string Conv_DialogueNodeSelectorToName(DialogueNodeSelector selector)
        {
            return selector?.NodeID ?? "";
        }

        public static DialogueNodeSelector Conv_NameToDialogueNodeSelector(string nodeID)
        {
            return new DialogueNodeSelector { NodeID = nodeID ?? "" };
        }

        // ===== 任务状态选择器工厂 =====

        public static QuestStateSelector MakeQuestStateSelector(QuestStateSelector selector)
        {
            return selector ?? new QuestStateSelector();
        }

        public static QuestStateSelector MakeQuestStateSelectorFromID(string nodeID)
        {
            return new QuestStateSelector { NodeID = nodeID ?? "" };
        }

        public static void BreakQuestStateSelector(QuestStateSelector selector, out string nodeID)
        {
            nodeID = selector?.NodeID ?? "";
        }

        public static string Conv_QuestStateSelectorToName(QuestStateSelector selector)
        {
            return selector?.NodeID ?? "";
        }

        public static QuestStateSelector Conv_NameToQuestStateSelector(string nodeID)
        {
            return new QuestStateSelector { NodeID = nodeID ?? "" };
        }

        // ===== 任务分支选择器工厂 =====

        public static QuestBranchSelector MakeQuestBranchSelector(QuestBranchSelector selector)
        {
            return selector ?? new QuestBranchSelector();
        }

        public static QuestBranchSelector MakeQuestBranchSelectorFromID(string nodeID)
        {
            return new QuestBranchSelector { NodeID = nodeID ?? "" };
        }

        public static void BreakQuestBranchSelector(QuestBranchSelector selector, out string nodeID)
        {
            nodeID = selector?.NodeID ?? "";
        }

        public static string Conv_QuestBranchSelectorToName(QuestBranchSelector selector)
        {
            return selector?.NodeID ?? "";
        }

        public static QuestBranchSelector Conv_NameToQuestBranchSelector(string nodeID)
        {
            return new QuestBranchSelector { NodeID = nodeID ?? "" };
        }

        // ===== 辅助 =====

        private static List<TalesComponent> FindAllTalesComponents()
        {
            var result = new List<TalesComponent>();
            var all = Level.GetScripts<TalesComponent>();
            if (all != null)
            {
                foreach (var c in all)
                {
                    if (c != null && c.Enabled)
                    {
                        result.Add(c);
                    }
                }
            }
            return result;
        }
    }
}
