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
        /// 按名称获取数据任务。对应 UE5 <c>GetTaskByName</c>（NarrativeFunctionLibrary.cpp:84-95）：
        /// UE5 经 GameInstance 取 <c>UNarrativeTaskManager</c> 子系统再 GetTask；
        /// Flax 中对应静态注册表 <see cref="NarrativeTaskManager"/>，资产只扫描一次后走缓存，不再逐次读盘。
        /// </summary>
        public static NarrativeDataTask GetTaskByName(object worldContextObject, string eventName)
        {
            if (string.IsNullOrEmpty(eventName)) return null;
            return NarrativeTaskManager.GetTask(eventName);
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

        /// <summary>
        /// 替换输入变量占位。对应 UE5 <c>UArsenalStatics::ReplaceInputVariables</c>（ArsenalStatics.cpp:357-400）：
        /// <c>{Input.Attack}</c> 这类占位会被替换成按当前输入设备定制的富文本标签
        /// <c>&lt;img id="Input.Xbox.Attack"/&gt;</c>，其它占位原样保留交给后续变量替换。
        /// </summary>
        /// <param name="controller">玩家控制器（NarrativePlayerController 或承载它的 Actor）。</param>
        /// <param name="textToReplace">待替换文本。</param>
        public static string ReplaceInputVariables(object controller, string textToReplace)
        {
            if (string.IsNullOrEmpty(textToReplace)) return textToReplace;

            string inputDeviceName = GetNarrativeInputDeviceName(controller);
            if (string.IsNullOrEmpty(inputDeviceName)) return textToReplace;

            string lineString = textToReplace;

            int iterations = 0;
            while (iterations < 50) // 超过 50 个通配替换多半是数据写错了，做保护
            {
                int openBrace = lineString.IndexOf('{');
                int closeBrace = lineString.IndexOf('}');

                if (openBrace < 0 || closeBrace < 0 || openBrace >= closeBrace) break;

                string variableName = lineString.Substring(openBrace + 1, closeBrace - openBrace - 1);

                int dotIndex = variableName.IndexOf('.');
                if (dotIndex <= 0)
                {
                    // 非 Input.xxx 占位：留给 Dialogue 自己的变量替换处理，避免死循环
                    break;
                }

                string left = variableName.Substring(0, dotIndex);
                string right = variableName.Substring(dotIndex + 1);

                if (!string.Equals(left, "Input", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                string variableValue = string.Format("<img id=\"Input.{0}.{1}\"/>", inputDeviceName, right);

                lineString = lineString.Remove(openBrace, closeBrace - openBrace + 1);
                lineString = lineString.Insert(openBrace, variableValue);

                iterations++;
            }

            return lineString;
        }

        /// <summary>
        /// 取当前输入设备名（用于输入图标富文本）。对应 UE5
        /// <c>ANarrativePlayerController::GetNarrativeInputDeviceName</c>。
        /// </summary>
        public static string GetNarrativeInputDeviceName(object controller)
        {
            if (controller is UnrealFramework.NarrativePlayerController playerController)
            {
                string name = playerController.GetNarrativeInputDeviceName();
                if (!string.IsNullOrEmpty(name)) return name;
            }

            if (controller is Actor controllerActor)
            {
                var pc = controllerActor.GetScript<UnrealFramework.NarrativePlayerController>();
                if (pc != null)
                {
                    string name = pc.GetNarrativeInputDeviceName();
                    if (!string.IsNullOrEmpty(name)) return name;
                }
            }

            // Flax 没有 EnrichedInput 的设备快照，按是否有手柄在线判断
            return Input.GamepadsCount > 0 ? "Gamepad" : "Keyboard";
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
