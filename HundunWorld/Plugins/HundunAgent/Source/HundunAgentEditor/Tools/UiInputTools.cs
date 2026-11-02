using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Reflection;
using System.IO;
using System.Threading.Tasks;
using FlaxEngine;
using FlaxEngine.GUI;
using HundunAgent.Core;

namespace HundunAgent.Tools
{
    /// <summary>
    /// 运行态 UI 输入注入：把鼠标事件直接打进 PIE 里那块屏幕空间画布的控件树。
    /// <para>
    /// 存在的理由：桥接以前只能读（ui_tree）不能点，于是"拖滑杆到底有没有效"这类验证只能靠人。
    /// 这里走引擎自己的派发路径（CanvasRootControl → 子控件递归 → 未处理的冒泡），
    /// 与真实鼠标唯一的差别是鼠标捕获不经过 OS：因此 down 之后的 move/up 由本工具按
    /// "按下时命中的那个控件"补发（<see cref="_pressed"/>），这正是引擎捕获时的路由目标。
    /// </para>
    /// <para>
    /// 坐标用**画布像素**（与 ui_tree 里的 runtimeTree bounds 同一套，屏幕空间画布等于窗口像素）。
    /// </para>
    /// </summary>
    public static class UiInputTools
    {
        /// <summary>按下时命中的控件（捕获期间 move/up 都发给它）。</summary>
        private static Control _pressed;

        /// <summary>预览头骨骼的直接写入基线（按骨骼名记第一次读到的矩阵，reset 时还原）。</summary>
        private static readonly Dictionary<string, Matrix> _boneBaseline = new Dictionary<string, Matrix>();

        /// <summary>上一次用到的画布；Flax 的 == 比原生指针，PIE 重开后它会自己判死重找。</summary>
        private static UICanvas _canvas;

        public static void Register()
        {
            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "ui_pick",
                Description = "只读探针：返回画布上某点命中的控件类型（与 ui_input 同一套命中判定，不发事件）。用来确认坐标到底落在谁身上。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"x\":{\"type\":\"number\"},\"y\":{\"type\":\"number\"},\"canvas\":{\"type\":\"string\",\"description\":\"画布名可选，缺省取第一个 ReceivesEvents 的屏幕空间画布\"}}}",
                Execute = UiPickAsync
            });

            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "ui_input",
                Description = "向运行态 UI 注入鼠标：action=down|up|move|click|drag|wheel。click/drag 会自己补 down/up；drag 在 (x,y) 按下、线性移动到 (toX,toY) 再抬起。返回每次按下/拖动命中的控件类型。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"action\":{\"type\":\"string\"},\"x\":{\"type\":\"number\"},\"y\":{\"type\":\"number\"},\"toX\":{\"type\":\"number\"},\"toY\":{\"type\":\"number\"},\"steps\":{\"type\":\"integer\",\"description\":\"drag 的中间移动步数，默认 8\"},\"button\":{\"type\":\"string\",\"description\":\"left|right|middle，默认 left\"},\"delta\":{\"type\":\"number\",\"description\":\"wheel 的滚动量，默认 1\"}}}",
                Execute = UiInputAsync
            });

            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "ui_click_control",
                Description = "按控件**类型名**点一下（在画布树里前序查找第一个匹配，可加 index 取第 N 个）。用于目标在画布外/被裁掉的情况：命中测试够不到，但控件本身仍在树里。返回匹配到的类型与它自己的 bounds。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"type\":{\"type\":\"string\",\"description\":\"控件类型名，如 CcConfirmBlock / Button / SculptSlider\"},\"index\":{\"type\":\"integer\",\"description\":\"第几个匹配，默认 0\"},\"canvas\":{\"type\":\"string\"},\"offsetX\":{\"type\":\"number\",\"description\":\"相对控件左上角的点击点，缺省用中心\"},\"offsetY\":{\"type\":\"number\"}}}",
                Execute = UiClickControlAsync
            });

            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "ui_text_input",
                Description = "向运行态 TextBox 注入文本（按控件类型名查找，默认 TextBox；clear=true 先清空）。"
                    + "OS 键盘注入在 PIE 里不稳时用这条：直接走控件的 OnCharInput，返回控件类型、发送字符数、最终 Text 与长度。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{"
                    + "\"type\":{\"type\":\"string\",\"description\":\"控件类型名，默认 TextBox\"},"
                    + "\"text\":{\"type\":\"string\"},"
                    + "\"clear\":{\"type\":\"boolean\",\"description\":\"默认 true\"},"
                    + "\"canvas\":{\"type\":\"string\"}},"
                    + "\"required\":[\"text\"]}",
                Execute = UiTextInputAsync
            });

            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "ui_invoke",
                Description = "按**类型名**触发控件上的一个 public event（默认 Activated）。有些交互控件（如 CcHint）只靠页面里的按键触发事件、自身不处理鼠标；点不动的就走这个入口，效果等价于按下了它绑定的键。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"type\":{\"type\":\"string\"},\"index\":{\"type\":\"integer\"},\"event\":{\"type\":\"string\",\"description\":\"默认 Activated\"},\"canvas\":{\"type\":\"string\"}}}",
                Execute = UiInvokeAsync
            });

            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "ui_drag_control",
                Description = "按控件**类型名**在它自己的坐标里拖一段（按下 fromX/fromY → 分 steps 步线性移到 toX/toY → 抬起）。滑杆这类控件用它最省事：不用换算画布坐标、也不受内容层偏移/裁切影响。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"type\":{\"type\":\"string\"},\"index\":{\"type\":\"integer\"},\"canvas\":{\"type\":\"string\"},\"fromX\":{\"type\":\"number\"},\"fromY\":{\"type\":\"number\"},\"toX\":{\"type\":\"number\"},\"toY\":{\"type\":\"number\"},\"steps\":{\"type\":\"integer\",\"description\":\"默认 10\"}}}",
                Execute = UiDragControlAsync
            });

            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "ui_preview_screenshot",
                Description = "把画布里的 3D 预览（CharacterPreview3D 的离屏 RenderTexture）存成 PNG。game_screenshot 只截游戏视口、不含 UI 与离屏预览，这个才是「捏脸画面」的像素证据。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"path\":{\"type\":\"string\",\"description\":\"输出 PNG 路径，缺省写到 Logs/HundunAgent/screenshots/preview_*.png\"},\"canvas\":{\"type\":\"string\"}}}",
                Execute = UiPreviewScreenshotAsync
            });

            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "ui_dump",
                Description = "从画布根逐层列出运行态控件（类型/bounds/可见/文本），用来找按钮坐标。与 ui_tree 的区别：直接走 canvas.GUI，不依赖场景 Actor 枚举（PIE 里那条路会漏掉运行时画布）。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"canvas\":{\"type\":\"string\"},\"maxDepth\":{\"type\":\"integer\",\"description\":\"默认 8\"},\"maxRows\":{\"type\":\"integer\",\"description\":\"默认 300\"}}}",
                Execute = UiDumpAsync
            });

            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "preview_bone_drive",
                Description = "直接驱动创角预览那颗 MetaHuman 头的骨骼：读当前节点矩阵、右乘一段平移、写回并刷新动画。" +
                              "用来把『皮肤够不够蒙皮』与『参数→骨骼的写回端有没有接到这颗头』分开判断。" +
                              "reset=true 时把该骨骼还原到本会话第一次读到的矩阵。只在 PIE 的捏脸页有意义。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{" +
                                  "\"bone\":{\"type\":\"string\",\"description\":\"骨骼节点名，如 head 或 FACIAL_L_CheekInner\"}," +
                                  "\"x\":{\"type\":\"number\",\"description\":\"本地空间平移 X，默认 0\"}," +
                                  "\"y\":{\"type\":\"number\",\"description\":\"本地空间平移 Y，默认 0\"}," +
                                  "\"z\":{\"type\":\"number\",\"description\":\"本地空间平移 Z，默认 0\"}," +
                                  "\"reset\":{\"type\":\"boolean\",\"description\":\"true 时还原基线（忽略 x/y/z）\"}," +
                                  "\"canvas\":{\"type\":\"string\"}}," +
                                  "\"required\":[\"bone\"]}",
                Dangerous = true,
                Execute = PreviewBoneDriveAsync
            });

            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "preview_state",
                Description = "读创角 3D 预览的运行态：头/模板身体/蒙皮身体/默认身体各自挂了什么模型、是否激活、包围盒，以及当前的可见性开关。" +
                              "用来回答『身体页签到底有没有把蒙皮身体露出来』这类只能看内部字段的问题。只读。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{\"canvas\":{\"type\":\"string\"}}}",
                Execute = PreviewStateAsync
            });

            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "sculpt_seal_preview",
                Description = "仅用于自测：在 PIE 捏脸页直接调用 MetaHumanSculptPanel.OnServerSealedByService()，" +
                              "把本机会话封存并让滑杆/撤销/步骤条切到封存态（**不写服务端**）。用来目视验收封存 UI。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{}}",
                Dangerous = true,
                Execute = SculptSealPreviewAsync
            });
        }

        private static Task<object> SculptSealPreviewAsync(JsonElement args)
        {
            return MainThread.InvokeAsync<object>(() =>
            {
                UICanvas canvas = ResolveCanvas(null);
                Control panel = FindByTypeName(canvas.GUI, "MetaHumanSculptPanel");
                if (panel == null)
                {
                    throw new InvalidOperationException("画布树里没有 MetaHumanSculptPanel（当前不在捏脸页？）");
                }

                MethodInfo method = panel.GetType().GetMethod("OnServerSealedByService",
                    BindingFlags.Instance | BindingFlags.Public);
                if (method == null)
                {
                    throw new InvalidOperationException("MetaHumanSculptPanel 上找不到 OnServerSealedByService()");
                }

                method.Invoke(panel, null);
                return new
                {
                    invoked = "OnServerSealedByService",
                    panel = panel.GetType().Name
                };
            });
        }

        private static Task<object> PreviewStateAsync(JsonElement args)
        {
            return MainThread.InvokeAsync<object>(() =>
            {
                UICanvas canvas = ResolveCanvas(EditorUtils.GetString(args, "canvas"));
                Control preview = FindByTypeName(canvas.GUI, "CharacterPreview3D");
                if (preview == null)
                {
                    throw new InvalidOperationException("画布树里没有 CharacterPreview3D");
                }

                Type type = preview.GetType();
                object Field(string name) => type.GetField(name,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(preview);

                object Describe(AnimatedModel model)
                {
                    if (model == null || !model)
                        return new { alive = false };
                    var box = model.Box;
                    return new
                    {
                        alive = true,
                        active = model.IsActive,
                        modelPath = model.SkinnedModel != null ? model.SkinnedModel.Path : null,
                        boxSize = new { x = box.Size.X, y = box.Size.Y, z = box.Size.Z },
                        boxCenter = new { x = box.Center.X, y = box.Center.Y, z = box.Center.Z }
                    };
                }

                object DescribeStatic(StaticModel model)
                {
                    if (model == null || !model)
                        return new { alive = false };
                    var box = model.Box;
                    return new
                    {
                        alive = true,
                        active = model.IsActive,
                        modelPath = model.Model != null ? model.Model.Path : null,
                        boxSize = new { x = box.Size.X, y = box.Size.Y, z = box.Size.Z }
                    };
                }

                return new
                {
                    head = Describe(Field("_headModel") as AnimatedModel),
                    templateHead = DescribeStatic(Field("_templateHead") as StaticModel),
                    templateBody = DescribeStatic(Field("_templateBody") as StaticModel),
                    skinnedBody = Describe(Field("_skinnedBody") as AnimatedModel),
                    animatedBody = Describe(Field("_animatedModel") as AnimatedModel),
                    skinnedBodyVisible = Field("_skinnedBodyVisible"),
                    templateBodyVisible = Field("_templateBodyVisible"),
                    templateHeadVisible = Field("_templateHeadVisible"),
                    bodyGender = Field("_bodyGender")
                };
            });
        }

        private static Task<object> PreviewBoneDriveAsync(JsonElement args)
        {
            return MainThread.InvokeAsync<object>(() =>
            {
                UICanvas canvas = ResolveCanvas(EditorUtils.GetString(args, "canvas"));
                Control preview = FindByTypeName(canvas.GUI, "CharacterPreview3D");
                if (preview == null)
                {
                    throw new InvalidOperationException("画布树里没有 CharacterPreview3D（当前不在 3D 预览页？）");
                }

                FieldInfo headField = preview.GetType().GetField("_headModel",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                AnimatedModel head = headField?.GetValue(preview) as AnimatedModel;
                if (head == null || !head)
                {
                    throw new InvalidOperationException("预览的 _headModel 为空或已销毁");
                }

                string bone = EditorUtils.GetString(args, "bone");
                if (string.IsNullOrEmpty(bone))
                {
                    throw new ArgumentException("缺少 bone");
                }

                bool reset = EditorUtils.GetBool(args, "reset", false);
                Matrix before;
                head.GetNodeTransformation(bone, out before, false);
                if (before.M11 == 0 && before.M44 == 0)
                {
                    throw new InvalidOperationException("骨架上没有节点 " + bone);
                }

                if (!_boneBaseline.ContainsKey(bone))
                {
                    _boneBaseline[bone] = before;
                }

                Matrix target;
                if (reset)
                {
                    target = _boneBaseline[bone];
                }
                else
                {
                    float dx = EditorUtils.GetNum(args, "x", 0f);
                    float dy = EditorUtils.GetNum(args, "y", 0f);
                    float dz = EditorUtils.GetNum(args, "z", 0f);
                    target = before * Matrix.Translation(dx, dy, dz);
                }

                head.SetNodeTransformation(bone, target, false);
                head.UpdateAnimation();

                Matrix after;
                head.GetNodeTransformation(bone, out after, false);

                return new
                {
                    bone,
                    reset,
                    nodeCount = head.SkinnedModel != null ? head.SkinnedModel.Nodes.Length : 0,
                    modelPath = head.SkinnedModel != null ? head.SkinnedModel.Path : null,
                    before = new { x = before.TranslationVector.X, y = before.TranslationVector.Y, z = before.TranslationVector.Z },
                    after = new { x = after.TranslationVector.X, y = after.TranslationVector.Y, z = after.TranslationVector.Z },
                    box = new
                    {
                        center = new { x = head.Box.Center.X, y = head.Box.Center.Y, z = head.Box.Center.Z },
                        size = new { x = head.Box.Size.X, y = head.Box.Size.Y, z = head.Box.Size.Z }
                    }
                };
            });
        }

        private static Task<object> UiClickControlAsync(JsonElement args)
        {
            return MainThread.InvokeAsync<object>(() =>
            {
                UICanvas canvas = ResolveCanvas(EditorUtils.GetString(args, "canvas"));
                CanvasRootControl root = canvas.GUI;
                string wanted = EditorUtils.GetString(args, "type");
                int index = Mathf.Max(0, EditorUtils.GetInt(args, "index", 0));

                Control target = FindByType(root, wanted, ref index);
                if (target == null)
                {
                    throw new InvalidOperationException("画布树里没有类型为 " + wanted + " 的控件");
                }

                float ox = EditorUtils.GetNum(args, "offsetX", target.Width * 0.5f);
                float oy = EditorUtils.GetNum(args, "offsetY", target.Height * 0.5f);
                Float2 local = new Float2(ox, oy);

                target.OnMouseDown(local, MouseButton.Left);
                target.OnMouseUp(local, MouseButton.Left);

                return new
                {
                    clicked = target.GetType().Name,
                    bounds = new { x = target.Bounds.X, y = target.Bounds.Y, width = target.Width, height = target.Height },
                    point = new { x = ox, y = oy }
                };
            });
        }

        private static Task<object> UiInvokeAsync(JsonElement args)
        {
            return MainThread.InvokeAsync<object>(() =>
            {
                UICanvas canvas = ResolveCanvas(EditorUtils.GetString(args, "canvas"));
                string wanted = EditorUtils.GetString(args, "type");
                string eventName = EditorUtils.GetString(args, "event", "Activated");
                int index = Mathf.Max(0, EditorUtils.GetInt(args, "index", 0));

                Control target = FindByType(canvas.GUI, wanted, ref index);
                if (target == null)
                {
                    throw new InvalidOperationException("画布树里没有类型为 " + wanted + " 的控件");
                }

                // event 编译成私有 backing field：直接按字段取（EventInfo 在 .NET 10 上没有 GetValue）。
                object handler = null;
                FieldInfo field = target.GetType().GetField(eventName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null)
                {
                    handler = field.GetValue(target);
                }

                if (handler == null)
                {
                    throw new InvalidOperationException(
                        wanted + " 上没有可触发的 " + eventName + "（字段/事件都没取到处理器）");
                }

                ((Delegate)handler).DynamicInvoke();

                return new
                {
                    invoked = target.GetType().Name,
                    eventName,
                    subscribers = ((Delegate)handler).GetInvocationList().Length
                };
            });
        }

        private static Task<object> UiDragControlAsync(JsonElement args)
        {
            return MainThread.InvokeAsync<object>(() =>
            {
                UICanvas canvas = ResolveCanvas(EditorUtils.GetString(args, "canvas"));
                CanvasRootControl root = canvas.GUI;
                string wanted = EditorUtils.GetString(args, "type");
                int index = Mathf.Max(0, EditorUtils.GetInt(args, "index", 0));

                Control target = FindByType(root, wanted, ref index);
                if (target == null)
                {
                    throw new InvalidOperationException("画布树里没有类型为 " + wanted + " 的控件");
                }

                int steps = Mathf.Max(1, EditorUtils.GetInt(args, "steps", 10));
                float fromX = EditorUtils.GetNum(args, "fromX", target.Width * 0.5f);
                float fromY = EditorUtils.GetNum(args, "fromY", target.Height * 0.5f);
                float toX = EditorUtils.GetNum(args, "toX", fromX);
                float toY = EditorUtils.GetNum(args, "toY", fromY);

                target.OnMouseDown(new Float2(fromX, fromY), MouseButton.Left);
                for (int i = 1; i <= steps; i++)
                {
                    float t = (float)i / steps;
                    target.OnMouseMove(new Float2(Mathf.Lerp(fromX, toX, t), Mathf.Lerp(fromY, toY, t)));
                }

                target.OnMouseUp(new Float2(toX, toY), MouseButton.Left);

                return new
                {
                    dragged = target.GetType().Name,
                    from = new { x = fromX, y = fromY },
                    to = new { x = toX, y = toY },
                    bounds = new { width = target.Width, height = target.Height }
                };
            });
        }

        private static Control FindByType(Control control, string typeName, ref int index)
        {
            if (control == null || string.IsNullOrEmpty(typeName))
            {
                return null;
            }

            if (control.GetType().Name == typeName)
            {
                if (index == 0)
                {
                    return control;
                }

                index--;
            }

            ContainerControl container = control as ContainerControl;
            if (container == null)
            {
                return null;
            }

            for (int i = 0; i < container.ChildrenCount; i++)
            {
                Control found = FindByType(container.GetChild(i), typeName, ref index);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static async Task<object> UiPreviewScreenshotAsync(JsonElement args)
        {
            string customPath = EditorUtils.GetString(args, "path");
            string canvasName = EditorUtils.GetString(args, "canvas");

            string captureSource = "task";
            string targetPath = await MainThread.InvokeAsync(() =>
            {
                UICanvas canvas = ResolveCanvas(canvasName);
                Control preview = FindByTypeName(canvas.GUI, "CharacterPreview3D");
                if (preview == null)
                {
                    throw new InvalidOperationException("画布树里没有 CharacterPreview3D（当前不在 3D 预览页？）");
                }

                PropertyInfo taskProperty = preview.GetType().GetProperty("PreviewRenderTask");
                var task = taskProperty != null ? taskProperty.GetValue(preview) as SceneRenderTask : null;
                if (task == null)
                {
                    throw new InvalidOperationException("CharacterPreview3D.PreviewRenderTask 为空（离屏 RenderTexture 未初始化）");
                }

                string path;
                if (!string.IsNullOrEmpty(customPath))
                {
                    path = Path.IsPathRooted(customPath) ? customPath : Path.Combine(Globals.ProjectFolder, customPath);
                }
                else
                {
                    string dir = Path.Combine(Globals.ProjectFolder, "Logs", "HundunAgent", "screenshots");
                    Directory.CreateDirectory(dir);
                    path = Path.Combine(dir, "preview_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff") + ".png");
                }

                string dirName = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dirName) && !Directory.Exists(dirName))
                {
                    Directory.CreateDirectory(dirName);
                }

                // Flax 1.13 实测：Screenshot.Capture(task, path) 会对预览任务截出全黑图
                // （1060×1080 全 0），而任务自身的 Output 纹理有内容。优先抓 Output，
                // 拿不到才回退旧的 task 捕获路径。
                var output = task.Output;
                if (output != null)
                {
                    Screenshot.Capture(output, path);
                    captureSource = "output";
                }
                else
                {
                    Screenshot.Capture(task, path);
                }

                return path;
            });

            // 截图在下一渲染帧写入，等文件出现
            bool ok = await MainThread.WaitUntilAsync(() => File.Exists(targetPath), 8000);
            if (!ok)
            {
                throw new TimeoutException("预览截图未生成: " + targetPath);
            }

            return new
            {
                status = "captured",
                path = targetPath,
                size = new FileInfo(targetPath).Length,
                source = captureSource
            };
        }

        /// <summary>按**类型名**做前序查找（不要求类型在桥接程序集里可见：预览组件属于 Game 模块）。</summary>
        private static Control FindByTypeName(Control control, string typeName)
        {
            if (control == null)
            {
                return null;
            }

            if (control.GetType().Name == typeName)
            {
                return control;
            }

            ContainerControl container = control as ContainerControl;
            if (container == null)
            {
                return null;
            }

            for (int i = 0; i < container.ChildrenCount; i++)
            {
                Control found = FindByTypeName(container.GetChild(i), typeName);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static Task<object> UiDumpAsync(JsonElement args)
        {
            return MainThread.InvokeAsync<object>(() =>
            {
                UICanvas canvas = ResolveCanvas(EditorUtils.GetString(args, "canvas"));
                CanvasRootControl root = canvas.GUI;
                int maxDepth = Math.Clamp(EditorUtils.GetInt(args, "maxDepth", 8), 1, 16);
                int maxRows = Math.Clamp(EditorUtils.GetInt(args, "maxRows", 300), 1, 2000);

                var rows = new List<string>();
                Walk(root, 0, maxDepth, maxRows, rows);

                return new
                {
                    canvas = canvas != null && canvas.Name != null ? canvas.Name : "<null>",
                    size = new { width = root.Width, height = root.Height },
                    rows = rows.Count,
                    tree = rows
                };
            });
        }

        private static void Walk(Control control, int depth, int maxDepth, int maxRows, List<string> rows)
        {
            if (control == null || depth > maxDepth || rows.Count >= maxRows)
            {
                return;
            }

            string text = null;
            Label label = control as Label;
            if (label != null)
            {
                text = label.Text;
            }
            else
            {
                Button button = control as Button;
                if (button != null)
                {
                    text = button.Text;
                }
            }

            rows.Add(string.Format("{0}{1} [{2:F0},{3:F0} {4:F0}x{5:F0}]{6}{7}",
                new string(' ', depth * 2),
                control.GetType().Name,
                control.Bounds.X,
                control.Bounds.Y,
                control.Bounds.Width,
                control.Bounds.Height,
                control.Visible ? "" : " hidden",
                string.IsNullOrEmpty(text) ? "" : " \"" + text + "\""));

            ContainerControl container = control as ContainerControl;
            if (container == null)
            {
                return;
            }

            for (int i = 0; i < container.ChildrenCount && rows.Count < maxRows; i++)
            {
                Walk(container.GetChild(i), depth + 1, maxDepth, maxRows, rows);
            }
        }

        private static Task<object> UiPickAsync(JsonElement args)
        {
            return MainThread.InvokeAsync<object>(() =>
            {
                UICanvas canvas = ResolveCanvas(EditorUtils.GetString(args, "canvas"));
                CanvasRootControl root = canvas.GUI;
                Float2 point = new Float2(EditorUtils.GetNum(args, "x", -1f), EditorUtils.GetNum(args, "y", -1f));
                Control hit;
                root.RayCast(ref point, out hit);

                return new
                {
                    canvas = canvas != null && canvas.Name != null ? canvas.Name : "<null>",
                    canvasSize = new { width = root.Width, height = root.Height },
                    x = point.X,
                    y = point.Y,
                    hit = hit == null ? "<none>" : hit.GetType().Name,
                    hitsBounds = hit == null ? null : new
                    {
                        x = hit.Bounds.X,
                        y = hit.Bounds.Y,
                        width = hit.Bounds.Width,
                        height = hit.Bounds.Height
                    }
                };
            });
        }

        private static Task<object> UiInputAsync(JsonElement args)
        {
            return MainThread.InvokeAsync<object>(() =>
            {
                string action = EditorUtils.GetString(args, "action", "click");
                string buttonName = EditorUtils.GetString(args, "button", "left");
                MouseButton button = buttonName == "right"
                    ? MouseButton.Right
                    : (buttonName == "middle" ? MouseButton.Middle : MouseButton.Left);

                float x = EditorUtils.GetNum(args, "x", 0f);
                float y = EditorUtils.GetNum(args, "y", 0f);
                float toX = EditorUtils.GetNum(args, "toX", x);
                float toY = EditorUtils.GetNum(args, "toY", y);
                int steps = Mathf.Max(1, EditorUtils.GetInt(args, "steps", 8));
                float delta = EditorUtils.GetNum(args, "delta", 1f);

                UICanvas canvas = ResolveCanvas(EditorUtils.GetString(args, "canvas"));
                CanvasRootControl root = canvas.GUI;

                string hitType = "-";
                Float2 point = new Float2(x, y);

                switch (action)
                {
                    case "down":
                        hitType = Press(root, point, button);
                        break;

                    case "move":
                        MoveTo(point);
                        break;

                    case "up":
                        Release(point, button);
                        break;

                    case "wheel":
                        root.OnMouseWheel(point, delta);
                        break;

                    case "click":
                        hitType = Press(root, point, button);
                        Release(point, button);
                        break;

                    case "drag":
                    default:
                        hitType = Press(root, point, button);
                        for (int i = 1; i <= steps; i++)
                        {
                            float t = (float)i / steps;
                            MoveTo(new Float2(Mathf.Lerp(x, toX, t), Mathf.Lerp(y, toY, t)));
                        }

                        Release(new Float2(toX, toY), button);
                        break;
                }

                return new
                {
                    action,
                    canvas = canvas != null && canvas.Name != null ? canvas.Name : "<null>",
                    from = new { x, y },
                    to = new { x = toX, y = toY },
                    hit = hitType,
                    pressedLeft = _pressed != null
                };
            });
        }

        /// <summary>按下：先按引擎同一套命中判定找出真实目标（回报用），再交给根控件按正常路径派发。</summary>
        /// <summary>
        /// 向 TextBox 注入文本：OS 键盘（SendKeys）在 PIE 里依赖窗口焦点，实测常丢键；
        /// 这里直接调用控件的 OnCharInput，与真实输入同一入口，且回执能读到最终 Text 作为判据。
        /// </summary>
        private static Task<object> UiTextInputAsync(JsonElement args)
        {
            string typeName = EditorUtils.GetString(args, "type", "TextBox");
            string text = EditorUtils.GetString(args, "text", "");
            bool clear = !(args.TryGetProperty("clear", out var clearEl) && clearEl.ValueKind == JsonValueKind.False);

            return MainThread.InvokeAsync<object>(() =>
            {
                UICanvas canvas = ResolveCanvas(EditorUtils.GetString(args, "canvas"));
                Control control = FindByTypeName(canvas.GUI, typeName);
                if (control == null)
                {
                    throw new InvalidOperationException("画布树里没有控件类型 " + typeName);
                }
                if (!(control is TextBoxBase box))
                {
                    throw new InvalidOperationException(typeName + " 不是文本框（TextBoxBase）");
                }

                if (clear)
                {
                    box.Text = string.Empty;
                }

                int sent = 0;
                foreach (char ch in text)
                {
                    control.OnCharInput(ch);
                    sent++;
                }

                return new
                {
                    controlType = control.GetType().Name,
                    sent,
                    length = box.Text?.Length ?? 0,
                    text = box.Text
                };
            });
        }

        private static string Press(CanvasRootControl root, Float2 point, MouseButton button)
        {
            Control hit;
            root.RayCast(ref point, out hit);
            _pressed = hit;
            root.OnMouseDown(point, button);
            return hit == null ? "<none>" : hit.GetType().Name;
        }

        /// <summary>移动：捕获期间只发给按下时命中的那个控件（等价于引擎的 _trackingControl 路由）。</summary>
        private static void MoveTo(Float2 point)
        {
            if (_pressed == null)
            {
                return;
            }

            _pressed.OnMouseMove(_pressed.PointFromWindow(point));
        }

        private static void Release(Float2 point, MouseButton button)
        {
            if (_pressed != null)
            {
                _pressed.OnMouseUp(_pressed.PointFromWindow(point), button);
                _pressed = null;
            }
        }

        /// <summary>取一块屏幕空间画布（优先名字匹配）；PIE 里通常只有一块承接事件。</summary>
        private static UICanvas ResolveCanvas(string wantedName)
        {
            if (_canvas != null && string.IsNullOrEmpty(wantedName))
            {
                return _canvas;
            }

            UICanvas fallback = null;
            for (int i = 0; i < Level.ScenesCount; i++)
            {
                Scene scene = Level.GetScene(i);
                if (scene == null)
                {
                    continue;
                }

                UICanvas found = FindCanvas(scene, wantedName, ref fallback);
                if (found != null)
                {
                    _canvas = found;
                    return found;
                }
            }

            if (fallback != null)
            {
                _canvas = fallback;
                return fallback;
            }

            throw new InvalidOperationException("场景里没有可接收事件的 UICanvas（是否已进入 PIE？）");
        }

        private static UICanvas FindCanvas(Actor actor, string wantedName, ref UICanvas fallback)
        {
            if (actor == null)
            {
                return null;
            }

            // UICanvas 在 Flax 1.12 里是 **Actor 本体**，不是挂在 Actor 上的脚本：
            // 用 GetScript<UICanvas>() 永远返回 null（2026-10-19 实测 ui_pick 明明有画布却报"没有"）。
            UICanvas canvas = actor as UICanvas;
            if (canvas == null)
            {
                canvas = actor.GetScript<UICanvas>();
            }
            if (canvas != null && canvas.ReceivesEvents)
            {
                bool nameOk = string.IsNullOrEmpty(wantedName)
                              || (actor.Name != null && actor.Name.Contains(wantedName));
                if (nameOk)
                {
                    return canvas;
                }

                if (fallback == null)
                {
                    fallback = canvas;
                }
            }

            Actor[] children = actor.Children;
            if (children == null)
            {
                return null;
            }

            for (int i = 0; i < children.Length; i++)
            {
                UICanvas found = FindCanvas(children[i], wantedName, ref fallback);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }
    }
}
