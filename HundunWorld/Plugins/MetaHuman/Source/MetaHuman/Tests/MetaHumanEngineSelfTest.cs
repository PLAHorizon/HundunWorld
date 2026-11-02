using System;
using System.Collections.Generic;
using FlaxEngine;
using MetaHuman;
using MetaHuman.Core;

namespace MetaHuman.Runtime
{
    /// <summary>
    /// 引擎内自检：离屏工装编不到、也跑不到的那一层（模板真从工程目录读得到吗、
    /// Flax 脚本环境里 System.Text.Json 可用吗、骨骼偏移真的写进姿势了吗）。
    /// 每组各自计数并如实报告 skipped 及原因——「没跑」与「跑过且绿」必须能区分。
    /// </summary>
    public static class MetaHumanEngineSelfTest
    {
        public class Group
        {
            public string Name;
            public int Passed;
            public int Failed;
            public int Skipped;
            public readonly List<string> Notes = new List<string>();

            public void Check(bool condition, string what)
            {
                if (condition)
                {
                    Passed++;
                }
                else
                {
                    Failed++;
                    Notes.Add("FAIL: " + what);
                }
            }

            public void Skip(string why)
            {
                Skipped++;
                Notes.Add("SKIP: " + why);
            }
        }

        /// <summary>跑全部组，返回可贴进日志/面板的报告。</summary>
        public static string Run(Scene scene)
        {
            List<Group> groups = new List<Group>();
            groups.Add(TestTemplateLoading());
            groups.Add(TestGuiToRawInEngine());
            groups.Add(TestSessionAndSolver());
            groups.Add(TestLookStoreAndPending());
            groups.Add(TestBoneWriteBack(scene));
            groups.Add(TestBoneWriteBackSemantics(scene));
            groups.Add(TestMorphWriteBack(scene));
            groups.Add(TestHeadShapeCoverage(scene));
            groups.Add(TestBoneNameUniqueness(scene));
            groups.Add(TestWarpShapeTable(scene));
            groups.Add(TestExpressionDeferredShapes(scene));
            groups.Add(TestExpressionRigLoaded());
            groups.Add(TestMaterialParameterTypeGuard(scene));
            groups.Add(TestHeadSkinning());
            groups.Add(TestExpressionSafeHeadCandidate());
            groups.Add(TestHistoryAndSealInEngine());

            int passed = 0;
            int failed = 0;
            int skipped = 0;
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine("[MetaHuman 引擎自检]");

            foreach (Group group in groups)
            {
                passed += group.Passed;
                failed += group.Failed;
                skipped += group.Skipped;
                sb.Append("  ").Append(group.Name).Append(": 通过 ").Append(group.Passed)
                  .Append(" 失败 ").Append(group.Failed).Append(" 跳过 ").Append(group.Skipped);
                sb.AppendLine();
                foreach (string note in group.Notes)
                {
                    sb.Append("      ").Append(note).AppendLine();
                }
            }

            sb.Append("  合计: 通过 ").Append(passed).Append(" 失败 ").Append(failed)
              .Append(" 跳过 ").Append(skipped);

            string report = sb.ToString();
            if (failed > 0)
            {
                Debug.LogError(report);
            }
            else
            {
                Debug.Log(report);
            }

            return report;
        }

        private static Group TestTemplateLoading()
        {
            Group group = new Group();
            group.Name = "模板装载（工程根解析 + JSON 反序列化）";

            List<string> ids = MetaHumanTemplateStore.AvailableTemplateIds();
            group.Check(ids.Count > 0, "模板目录里应至少有一个模板，实得 " + ids.Count);
            group.Check(ids.Contains("metahuman.face_archetype"), "脸部模板在清单里：" + string.Join(" , ", ids.ToArray()));

            MetaHumanTemplate face = MetaHumanTemplateStore.Get("metahuman.face_archetype");
            group.Check(face != null, "脸部模板可装载");
            if (face == null)
            {
                return group;
            }

            group.Check(face.Parameters.Count >= 400, "脸部参数成规模，实得 " + face.Parameters.Count);
            group.Check(face.MorphCatalog.Count == 37, "morph 名录 37 项，实得 " + face.MorphCatalog.Count);
            group.Check(face.Validate().Count == 0, "模板自检零错误");

            MetaHumanTemplate body = MetaHumanTemplateStore.Get("metahuman.body_base");
            group.Check(body != null && body.Find("body.height") != null, "身体模板带身高参数");

            MetaHumanFaceControlTable table = MetaHumanTemplateStore.FaceControls;
            group.Check(table != null && table.Count == 174, "表情控制表 174 条，实得 " + (table == null ? -1 : table.Count));
            if (table != null)
            {
                group.Check(table.ResolvedCount() > 0, "控制表至少解析出 " + table.ResolvedCount() + " 条落点");
                group.Check(table.UnresolvedControls().Count > 0, "未解析条目被如实登记，实得 " + table.UnresolvedControls().Count);
            }

            // 内嵌载体：编辑器里应该走散文件（回退次数为 0），但发布版没有那个目录，
            // 所以这里要同时证明「内嵌那份在真 Flax 脚本环境里也读得进来」。
            group.Check(MetaHumanTemplateStore.BakedFallbackCount == 0,
                        "编辑器里不该用到内嵌回退，实得 " + MetaHumanTemplateStore.BakedFallbackCount);
            MetaHumanTemplate bakedFace;
            bool bakedOk = MetaHumanTemplateStore.TryGetBaked("metahuman.face_archetype", out bakedFace);
            group.Check(bakedOk, "内嵌脸模板可在引擎里解析");
            if (bakedOk && face != null)
            {
                group.Check(bakedFace.Parameters.Count == face.Parameters.Count,
                            "内嵌脸模板参数数与散文件一致（" + face.Parameters.Count + "），实得 " + bakedFace.Parameters.Count);
                group.Check(bakedFace.MorphCatalog.Count == face.MorphCatalog.Count,
                            "内嵌脸模板 morph 名录条数与散文件一致，实得 " + bakedFace.MorphCatalog.Count);
            }

            // morph→GUI 表：真头到位后 blendshape 那一侧全靠它，所以引擎里必须
            // ①读得进来、②装载期复校过、③反查（GUI→morph）给得出条数——运行期用的就是反查方向。
            MetaHumanMorphGuiMap morphGui = MetaHumanTemplateStore.MorphGui;
            group.Check(morphGui != null,
                        "morph→GUI 表在引擎里装载成功（失败时 Store 会把校验问题逐条 Fail 出来，看上面的报错）");
            if (morphGui != null)
            {
                group.Check(morphGui.Entries.Count == 33,
                            "morph→GUI 成表 33 条，实得 " + morphGui.Entries.Count);
                group.Check(morphGui.Declined.Count == 4,
                            "declined 4 条（唇部区域形状键）如实带出来，实得 " + morphGui.Declined.Count);
                group.Check(morphGui.MorphsForGui("CTRL_L_eye_cheekRaise.ty").Count == 3,
                            "反查一条共用控制给出 3 个变体，实得 "
                            + morphGui.MorphsForGui("CTRL_L_eye_cheekRaise.ty").Count);
                string blinkGui;
                group.Check(morphGui.TryGetGuiControl("blink_L", out blinkGui)
                            && blinkGui == "CTRL_L_eye_blink.ty",
                            "正查 blink_L 得到 CTRL_L_eye_blink.ty，实得 " + blinkGui);
            }

            return group;
        }

        private static bool Approx(float a, float b)
        {
            return System.Math.Abs(a - b) < 1e-4f;
        }

        private static Group TestGuiToRawInEngine()
        {
            Group group = new Group();
            group.Name = "GUI→raw 换算在引擎侧装载与生效（散文件反序列化 + 双极量程）";

            MetaHumanGuiToRawMap map = MetaHumanTemplateStore.GuiToRaw;
            if (map == null)
            {
                group.Skip("Gui_To_Raw_Map.json 没能装载，换算通路整组没跑");
                return group;
            }

            group.Check(map.GuiCount == 174, "GUI 控制 174 个，实得 " + map.GuiCount);
            group.Check(map.RawCount == 251, "raw 控制 251 个，实得 " + map.RawCount);
            group.Check(map.SegmentCount == 259, "换算段 259 条，实得 " + map.SegmentCount);
            group.Check(map.Validate().Count == 0, "换算表结构自检零错误");

            // 这条同时是「结构体字段真被读进来了」的证据：全是公开字段的结构体最容易被序列化静默丢空
            Dictionary<string, float> blinkInput = new Dictionary<string, float>();
            blinkInput["CTRL_L_eye_blink.ty"] = -0.5f;
            Dictionary<string, float> raw = map.Convert(blinkInput);
            group.Check(raw.ContainsKey("CTRL_expressions_eyeWidenL") && Approx(raw["CTRL_expressions_eyeWidenL"], 0.5f),
                        "眨眼 -0.5 经换算应开大眼睛（eyeWidenL≈0.5），实得 " + (raw.ContainsKey("CTRL_expressions_eyeWidenL") ? raw["CTRL_expressions_eyeWidenL"].ToString() : "缺键"));
            group.Check(Approx(raw["CTRL_expressions_eyeBlinkL"], 0f),
                        "同一时刻 eyeBlinkL 应为 0（半开区间互斥），实得 " + raw["CTRL_expressions_eyeBlinkL"]);

            MetaHumanFaceControlTable table = MetaHumanTemplateStore.FaceControls;
            if (table == null || table.Count != 174)
            {
                group.Skip("控制表不可用，换算段那部分没跑");
                return group;
            }

            int withRange = 0;
            int withRawTargets = 0;
            int bipolar = 0;
            int segmentTotal = 0;
            for (int i = 0; i < table.Controls.Count; i++)
            {
                MetaHumanFaceControl control = table.Controls[i];
                if (control.GuiRange != null && control.GuiRange.Count == 2)
                {
                    withRange++;
                    if (control.GuiRange[0] < 0f)
                    {
                        bipolar++;
                    }
                }

                if (control.RawTargets != null && control.RawTargets.Count > 0)
                {
                    withRawTargets++;
                    for (int t = 0; t < control.RawTargets.Count; t++)
                    {
                        segmentTotal += control.RawTargets[t].Segments == null ? 0 : control.RawTargets[t].Segments.Count;
                    }
                }
            }

            group.Check(withRange == 174, "每条控制都带量程，实得 " + withRange);
            group.Check(withRawTargets == 174, "每条控制都带 raw 落点，实得 " + withRawTargets);
            group.Check(bipolar == 70, "双极（有负半轴）控制 70 个，实得 " + bipolar);
            group.Check(segmentTotal == 259, "控制表里的换算段总数与映射表一致，实得 " + segmentTotal);

            MetaHumanArkitMap arkit = MetaHumanTemplateStore.ArkitMap;
            if (arkit == null)
            {
                group.Skip("ARKit 对照表没装载上，外部追踪入口这半组没跑");
                return group;
            }

            group.Check(arkit.Validate().Count == 0, "ARKit 对照表结构自检零错误");
            group.Check(arkit.ChannelCount == 47 && arkit.EntryCount == 60,
                        "ARKit 通道 47 条、对应 60 条，实得 " + arkit.ChannelCount + "/" + arkit.EntryCount);
            Dictionary<string, float> wide = new Dictionary<string, float>();
            wide["eyeWideLeft"] = 1f;
            int unmatched;
            int declined;
            int applied;
            Dictionary<string, float> gui = arkit.ToGuiControls(wide, out unmatched, out declined, out applied);
            float blinkValue;
            gui.TryGetValue("CTRL_L_eye_blink.ty", out blinkValue);
            group.Check(applied > 0 && blinkValue < 0f,
                        "eyeWideLeft 经散文件那份表反解成负半轴 GUI 值，实得 " + blinkValue + "（applied=" + applied + "）");
            return group;
        }

        private static Group TestSessionAndSolver()
        {
            Group group = new Group();
            group.Name = "会话与求解（Flax 脚本环境内）";

            MetaHumanTemplate face = MetaHumanTemplateStore.Get("metahuman.face_archetype");
            MetaHumanTemplate body = MetaHumanTemplateStore.Get("metahuman.body_base");
            if (face == null || body == null)
            {
                group.Skip("模板装载失败，无法建会话");
                return group;
            }

            MetaHumanTemplate merged = MetaHumanTemplate.Merge(new[] { face, body });
            MetaHumanSession session;
            try
            {
                session = new MetaHumanSession(merged);
            }
            catch (Exception exception)
            {
                group.Check(false, "会话建立抛错：" + exception.Message);
                return group;
            }

            group.Check(session.Template.Parameters.Count == face.Parameters.Count + body.Parameters.Count,
                        "合成模板参数数相加不乱");

            MetaHumanParameter morphParam = null;
            foreach (MetaHumanParameter p in session.Template.All())
            {
                if (p.Id.StartsWith("morph.", StringComparison.Ordinal))
                {
                    morphParam = p;
                    break;
                }
            }

            if (morphParam == null)
            {
                group.Skip("模板里没有 morph 参数");
                return group;
            }

            session.Adjust(morphParam.Id, 0.6f);
            float weight;
            session.Solution.MorphWeights.TryGetValue(morphParam.Effects[0].Target, out weight);
            group.Check(Math.Abs(weight - 0.6f) < 1e-4f, "改一个值应把 morph 解到 0.6，实得 " + weight);
            group.Check(session.Solution.Bones.Count > 0, "形状参数解出了骨骼偏移，实得 " + session.Solution.Bones.Count);

            List<string> audit = session.AuditIncremental();
            group.Check(audit.Count == 0, "增量解与全量解一致：" + (audit.Count > 0 ? audit[0] : ""));

            return group;
        }

        private static Group TestLookStoreAndPending()
        {
            Group group = new Group();
            group.Name = "本地外观仓库与待认领交接";

            MetaHumanTemplate face = MetaHumanTemplateStore.Get("metahuman.face_archetype");
            if (face == null)
            {
                group.Skip("模板装载失败");
                return group;
            }

            string root = MetaHumanLookStore.RootDirectory;
            if (string.IsNullOrEmpty(root))
            {
                group.Skip("仓库根目录没解析出来（Globals 不可用）");
                return group;
            }

            // 用一次性 Id，测完删文件，不污染真实角色的记录
            string characterId = "selftest_" + DateTime.UtcNow.Ticks;
            try
            {
                MetaHumanSession session = new MetaHumanSession(face);
                List<MetaHumanParameter> parameters = new List<MetaHumanParameter>(face.All());
                for (int i = 0; i < 5 && i < parameters.Count; i++)
                {
                    session.Adjust(parameters[i].Id, parameters[i].Max);
                    session.Commit("改动 " + i);
                }

                MetaHumanLook published = session.Publish();
                group.Check(published.Published, "发布快照带发布位");

                // 待认领交接：发布时没有角色 Id → 挂起；取走一次就清空
                MetaHumanLookStore.SetPendingLook(published);
                group.Check(MetaHumanLookStore.HasPendingLook, "挂起后能被看见");
                MetaHumanLook taken;
                group.Check(MetaHumanLookStore.TryTakePendingLook(out taken), "待认领能被取走");
                group.Check(taken != null && taken.Values.Count == published.Values.Count, "取到的就是发布的那一份");
                group.Check(!MetaHumanLookStore.TryTakePendingLook(out taken), "第二次取为空（防下一次创建误领上一张脸）");

                string error;
                string path = MetaHumanLookStore.Save(characterId, published, out error);
                group.Check(path != null, "落盘应成功，实得 " + (error ?? "ok"));

                MetaHumanLook loaded = MetaHumanLookStore.LoadLatest(characterId);
                group.Check(loaded != null, "读回最近一版");
                if (loaded != null)
                {
                    group.Check(published.Values.Count == loaded.Values.Count, "读回条目数一致" + "（期望 " + published.Values.Count + " 实得 " + loaded.Values.Count + "）");
                    group.Check(loaded.VerifyChecksum(), "读回的快照校验和自证");
                    group.Check(loaded.Published, "发布位过存储不丢");
                    int differences = 0;
                    foreach (KeyValuePair<string, float> kv in published.Values)
                    {
                        float other;
                        loaded.Values.TryGetValue(kv.Key, out other);
                        if (Math.Abs(other - kv.Value) > 1e-4f)
                        {
                            differences++;
                        }
                    }

                    group.Check(0 == differences, "逐值一致" + "（期望 " + 0 + " 实得 " + differences + "）");
                }

                List<MetaHumanLook> all = MetaHumanLookStore.ReadAll(characterId);
                group.Check(1 == all.Count, "仓库里就一条记录" + "（期望 " + 1 + " 实得 " + all.Count + "）");
                group.Check(0 == MetaHumanLookStore.CorruptCount, "没有损坏条目被丢弃（正向对照：下面会故意写坏）" + "（期望 " + 0 + " 实得 " + MetaHumanLookStore.CorruptCount + "）");
                group.Check(MetaHumanLookStore.HasPublished(characterId), "该角色已有发布版");

                // 登录采纳那条通路（服务端 blob → 本地仓库）以前只测到 Save(对象)，没测 SaveRawBytes(blob)：
                // 而 AdoptFromServer 用的正是后者。blob 里带着发布位，落成仓库记录后就必须仍算发布版，
                // 否则重登读到的是"未发布的草稿"，回溯入口会被点亮、而每次保存都被服务端拒。
                string adoptedId = characterId + "_adopt";
                MetaHumanLook toAdopt = new MetaHumanLook();
                toAdopt.Published = true;
                toAdopt.Values = new Dictionary<string, float>();
                toAdopt.Values["face.adopt.probe"] = 0.5f;
                // 校验和得自己算：走 Capture 的会顺手算，手搓这份不会。
                // 漏了这一句，ReadAll 会把记录当损坏条目丢弃，于是两条断言红的是夹具而不是产品
                // ——2026-10-15 这次重启窗口就是这么撞红的，产品那条路另有其账（见下面拒收那组）。
                toAdopt.Checksum = MetaHumanLook.ComputeChecksum(toAdopt.Values);
                string adoptError;
                string adoptedPath = MetaHumanLookStore.SaveRawBytes(adoptedId, toAdopt.ToBytes(), out adoptError);
                group.Check(adoptedPath != null, "SaveRawBytes 要把下发的 blob 落成功（实得 " + (adoptError ?? "ok") + "）");
                group.Check(MetaHumanLookStore.HasPublished(adoptedId),
                            "采纳下来的那份必须立刻算发布版，否则重登解锁回溯");
                MetaHumanLook backAdopted = MetaHumanLookStore.LoadLatest(adoptedId);
                group.Check(backAdopted != null && backAdopted.Published,
                            "读档取回的要有发布位（RestoreFromStore 就靠它顺带定格会话）");

                // 反向对照：自证不过的 blob 过去会被"收下"，然后记录在读取时静默消失——
                // 写入点不报错、HasPublished 变假、重登点亮回溯入口。现在必须当场拒收并给原因。
                MetaHumanLook tampered = new MetaHumanLook();
                tampered.Published = true;
                tampered.Values = new Dictionary<string, float>();
                tampered.Values["face.adopt.probe"] = 0.5f;
                tampered.Checksum = MetaHumanLook.ComputeChecksum(tampered.Values) ^ 0xFFu;
                string tamperError;
                string tamperPath = MetaHumanLookStore.SaveRawBytes(adoptedId, tampered.ToBytes(), out tamperError);
                group.Check(tamperPath == null,
                            "校验和不符的下发外观必须拒收（实得 " + (tamperPath == null ? "已拒收" : "居然落盘了") + "）");
                group.Check(!string.IsNullOrEmpty(tamperError), "拒收要交出原因，不许静默回 null");
                group.Check(MetaHumanLookStore.HasPublished(adoptedId),
                            "拒收这条拒绝分支不许顺手把仓库里已有的发布版一起弄坏");

                // 故意写坏文件尾部的一个字节，确认损坏被点名而不是被当成好数据返回
                byte[] bytes = System.IO.File.ReadAllBytes(path);
                bytes[bytes.Length - 1] ^= 0xFF;
                System.IO.File.WriteAllBytes(path, bytes);
                List<MetaHumanLook> damaged = MetaHumanLookStore.ReadAll(characterId);
                group.Check(damaged.Count == 0 || MetaHumanLookStore.CorruptCount > 0,
                            "损坏记录必须要么读不出、要么计入 CorruptCount");
            }
            finally
            {
                try
                {
                    string path = System.IO.Path.Combine(root, characterId + ".mhl");
                    if (System.IO.File.Exists(path))
                    {
                        System.IO.File.Delete(path);
                    }

                    // 采纳那条测的是第二个角色号，不清就等于在用户机器上留一份假存档
                    string adoptedPath = System.IO.Path.Combine(root, characterId + "_adopt.mhl");
                    if (System.IO.File.Exists(adoptedPath))
                    {
                        System.IO.File.Delete(adoptedPath);
                    }
                }
                catch
                {
                    // 清理失败不影响判定，只是留个测试文件
                }
            }

            return group;
        }

        private static Group TestBoneWriteBack(Scene scene)
        {
            Group group = new Group();
            group.Name = "骨骼偏移写回（SetNodeTransformation 回读）";

            AnimatedModel model = FindAnimatedModel(scene);
            if (model == null || model.SkinnedModel == null)
            {
                group.Skip("场景里没有带骨架的 AnimatedModel，这组要放到有角色的场景里跑");
                return group;
            }

            int nodeCount = model.SkinnedModel.Nodes.Length;
            if (nodeCount == 0)
            {
                group.Skip("骨架没有任何节点");
                return group;
            }

            int index = nodeCount > 1 ? 1 : 0;
            Matrix before;
            model.GetNodeTransformation(index, out before, false);

            MetaHumanVec3 offset = new MetaHumanVec3(0f, 2.5f, 0f);
            // 右乘：Flax 的 Matrix 是行向量约定，"在结果上叠加平移"是 current * Translation。
            // 生产代码（MetaHumanAppearanceApplier）写的就是这个顺序；左乘的对照留在
            // TestBoneWriteBackSemantics 的 A 档里，那条断言把"左乘读不回来"钉住。
            Matrix moved = before * Matrix.Translation(offset.X, offset.Y, offset.Z);
            model.SetNodeTransformation(index, moved, false);

            Matrix after;
            model.GetNodeTransformation(index, out after, false);

            // 只回读平移分量：四元素本身受浮点与轴序影响，比较关系比比较数值稳
            float deltaY = after.TranslationVector.Y - before.TranslationVector.Y;
            group.Check(Math.Abs(deltaY - 2.5f) < 0.25f, "平移 2.5 应被读回，实得 ΔY=" + deltaY);

            Matrix restored = after * Matrix.Translation(-offset.X, -offset.Y, -offset.Z);
            model.SetNodeTransformation(index, restored, false);
            Matrix final;
            model.GetNodeTransformation(index, out final, false);
            group.Check(Math.Abs(final.TranslationVector.Y - before.TranslationVector.Y) < 0.25f, "撤偏移能回到原位");

            return group;
        }

        /// <summary>
        /// 骨骼写回的语义判别：同一个节点上跑五种写法，把每种实测到的 ΔY 全写进报告。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 存在的理由：<see cref="TestBoneWriteBack"/> 只走"平移左乘 + 写后立刻读"这一条通路，
        /// 它一旦红了就分不清三件事——矩阵合成顺序写反了、<c>NodesPose</c> 还没初始化、
        /// 还是引擎把写入延到下一次动画更新才生效。而 G3 的身体各项参数全靠这条通路，
        /// 分不清就等于没做。
        /// </para>
        /// <para>
        /// 这组自带正向对照：五种写法里至少要有一种真能把 2.5 写进去。
        /// 没有这条对照，"五种全读回 0"也会被记成"这组跑过了"，那是空转不是结论。
        /// 最后一条断言把已知的 A 形态钉住——哪天 A 不红了，说明引擎行为或夹具变了，
        /// 要回来重读这一段结论，而不是默默沿用。
        /// </para>
        /// </remarks>
        private static Group TestBoneWriteBackSemantics(Scene scene)
        {
            Group group = new Group();
            group.Name = "骨骼写回语义判别（五种写法对照）";

            AnimatedModel model = FindAnimatedModel(scene);
            if (model == null || model.SkinnedModel == null || model.SkinnedModel.Nodes.Length == 0)
            {
                group.Skip("场景里没有带骨架的 AnimatedModel：先用 actor_create(AnimatedModel) "
                           + "配 actor_set_property(SkinnedModel, 资产路径) 搭一个夹具再跑这组");
                return group;
            }

            int nodeCount = model.SkinnedModel.Nodes.Length;

            // 选节点不能固定挑 index 1：只有**局部带旋转/缩放**的节点才让"左乘 vs 右乘"产生差别。
            // 真头 Ada_FaceMesh 的 1 号节点局部近似单位阵，两种写法读回来一模一样
            // （2026-10-14 实测 A/B/C/D/E 全读到 ΔY=2.5），那时长期望"A 必须不等于 2.5"就是拿夹具口径当结论。
            // 所以先找一根**能分辨**的节点；找不到就如实把 A 臂记为跳过，而不是硬判它红。
            int index = FirstDiscriminableNode(model, nodeCount);
            bool discriminable = index >= 0;
            if (!discriminable)
            {
                index = nodeCount > 1 ? 1 : 0;
            }

            Matrix origin;
            model.GetNodeTransformation(index, out origin, false);

            List<KeyValuePair<string, float>> results = new List<KeyValuePair<string, float>>();

            // A 就是 TestBoneWriteBack 现在用的写法，留在这里当被对照的基线。
            results.Add(ProbePair("A 平移左乘 + 立刻读",
                ProbeNode(model, index, Matrix.Translation(0f, 2.5f, 0f) * origin, false, origin)));
            results.Add(ProbePair("B 平移右乘 + 立刻读",
                ProbeNode(model, index, origin * Matrix.Translation(0f, 2.5f, 0f), false, origin)));

            model.PreInitSkinningData();
            results.Add(ProbePair("C 先 PreInitSkinningData + 左乘",
                ProbeNode(model, index, Matrix.Translation(0f, 2.5f, 0f) * origin, false, origin)));

            results.Add(ProbePair("D 左乘后 UpdateAnimation 再读",
                ProbeNode(model, index, Matrix.Translation(0f, 2.5f, 0f) * origin, true, origin)));

            results.Add(ProbePair("E 整副 SetCurrentPose",
                ProbeWholePose(model, index, origin, nodeCount)));

            for (int i = 0; i < results.Count; i++)
            {
                group.Notes.Add("    " + results[i].Key + "：实测 ΔY=" + results[i].Value);
            }

            int reached = 0;
            for (int i = 0; i < results.Count; i++)
            {
                if (Math.Abs(results[i].Value - 2.5f) < 0.25f)
                {
                    reached++;
                }
            }

            group.Check(reached > 0,
                "五种写法没有一个能把 2.5 平移写进骨骼（ΔY 全在噪声内）：SetNodeTransformation 这条通路"
                + "在编辑器里不可用，MetaHumanFaceAnimator 的骨骼偏移必须换写法");

            // 判据问的是「关系」而不是「A 的绝对值」。A（左乘）在旧夹具上读到 ~0（什么都没写进去），
            // 在 Character_1 这具 1109 节点的资产上读到 223.85（写进去一个被节点局部缩放放大过的错位移），
            // 在真头的近似单位阵节点上又正好读到 2.5（左右乘此刻根本等价）——
            // 三种读数都说明"钉绝对值"必错，所以 A 臂只在**该节点能分辨顺序**时才判对错。
            group.Notes.Add("    节点口径：探针用第 " + index + " 号节点，可分辨左/右乘 = " + discriminable);
            if (discriminable)
            {
                group.Check(Math.Abs(results[0].Value - results[1].Value) >= 0.25f,
                            "在这根能分辨顺序的节点上，A（左乘）与 B（右乘）应给出不同位移，"
                            + "实得 A=" + results[0].Value + " B=" + results[1].Value + " ⇒ 该节点其实也分辨不了，判据该退回跳过");
                group.Check(Math.Abs(results[0].Value - 2.5f) >= 0.25f,
                            "A（左乘 + 立刻读）在这根可分辨节点上读到了正好 2.5 ⇒ 左乘变对了，"
                            + "§10 那条「必须右乘」的结论要重新判（实得 ΔY=" + results[0].Value + "）");
            }
            else
            {
                group.Skip("这具资产的节点局部变换都是近似单位阵（前 " + SampleNodesForOrderProbe
                           + " 个里找不到可分辨顺序的节点）"
                           + "⇒ 左乘/右乘结果等价，A 臂无从判对错；本组只判「写回通路可用」（B/E）");
            }

            // 生产写法必须真的能把平移写进去。
            group.Check(Math.Abs(results[1].Value - 2.5f) < 0.25f,
                "B（右乘 + 立刻读）是生产代码在用的写法，应把 2.5 读回来（实得 ΔY=" + results[1].Value + "）");

            // 独立参照：整副姿态写法与 B 应当给出同一个位移，
            // 否则"B 对"只是同一套写法的自证（E 走的是 SetCurrentPose，与 B 不同通路）。
            group.Check(Math.Abs(results[4].Value - results[1].Value) < 0.25f,
                "E（整副 SetCurrentPose）与 B（右乘）读到的位移应一致，"
                + "否则两者里有一个不是真写法（B=" + results[1].Value + "，E=" + results[4].Value + "）");

            if (Math.Abs(results[0].Value) >= 0.25f)
            {
                group.Notes.Add("    夹具口径：A（左乘）读到 ΔY=" + results[0].Value
                    + "，不是旧夹具上的 ~0 —— 在带局部缩放的节点上左乘更危险："
                    + "它写进去一个大偏移，而不是什么都不写");
            }

            return group;
        }

        private static KeyValuePair<string, float> ProbePair(string name, float deltaY)
        {
            return new KeyValuePair<string, float>(name, deltaY);
        }

        /// <summary>写一次节点偏移，（可选）催一次动画更新，读回 ΔY，再把节点复位。</summary>
        private static float ProbeNode(AnimatedModel model, int index, Matrix moved, bool tick, Matrix origin)
        {
            model.SetNodeTransformation(index, moved, false);
            if (tick)
            {
                model.UpdateAnimation();
            }

            Matrix after;
            model.GetNodeTransformation(index, out after, false);
            float deltaY = after.TranslationVector.Y - origin.TranslationVector.Y;

            model.SetNodeTransformation(index, origin, false);
            if (tick)
            {
                model.UpdateAnimation();
            }

            return deltaY;
        }

        /// <summary>引擎记忆里推荐的整体写法：整副姿态取出来、改一个节点、再整体写回去。</summary>
        private static float ProbeWholePose(AnimatedModel model, int index, Matrix origin, int nodeCount)
        {
            Matrix[] pose = new Matrix[nodeCount];
            for (int i = 0; i < nodeCount; i++)
            {
                Matrix nodeTransform;
                model.GetNodeTransformation(i, out nodeTransform, false);
                pose[i] = nodeTransform;
            }

            float baseY = pose[index].TranslationVector.Y;
            pose[index] = pose[index] * Matrix.Translation(0f, 2.5f, 0f);
            model.SetCurrentPose(pose, false);
            model.UpdateAnimation();

            Matrix after;
            model.GetNodeTransformation(index, out after, false);
            float deltaY = after.TranslationVector.Y - baseY;

            pose[index] = origin;
            model.SetCurrentPose(pose, false);
            model.UpdateAnimation();

            return deltaY;
        }

        private static Group TestMorphWriteBack(Scene scene)
        {
            Group group = new Group();
            group.Name = "blendshape 写回（SetBlendShapeWeight 回读）";

            AnimatedModel model = FindAnimatedModel(scene);
            if (model == null || model.SkinnedModel == null)
            {
                group.Skip("场景里没有 AnimatedModel");
                return group;
            }

            string[] blendShapes = model.SkinnedModel.BlendShapes;
            if (blendShapes == null || blendShapes.Length == 0)
            {
                group.Skip(model.SkinnedModel.Path + " 没有任何 blendshape：该资产导入时未开 ImportBlendShapes，"
                          + "这一层要在编辑器里重新导入（MetaHuman 导入器会自动开）后才测得到");
                return group;
            }

            string name = blendShapes[0];
            model.SetBlendShapeWeight(name, 0.7f);
            float read = model.GetBlendShapeWeight(name);
            group.Check(Math.Abs(read - 0.7f) < 0.02f, "写入 0.7 后应回读到 0.7，实得 " + read);

            model.SetBlendShapeWeight(name, 4f);
            float clamped = model.GetBlendShapeWeight(name);
            group.Check(clamped <= 1.0001f, "引擎应把权重钳到 1，实得 " + clamped);

            model.SetBlendShapeWeight(name, 0f);
            return group;
        }

        /// <summary>
        /// 这具资产到底能承载多少条头部参数 —— G1 的判据（文档 §34/§35）。
        ///
        /// 名字清单**只能**在引擎侧取：字节扫描 .flax 看不见 SkinnedModel 的节点名
        /// （判别实验：`skm_uefn_mannequin.flax` 的节点名早已由引擎 API 实测为 pelvis/spine_01/…，
        /// 而字节扫描对这些名字全部 0 命中）。这里用 `FindNode` 逐个探模板声明的骨骼目标，
        /// morph 直接读 `BlendShapes`（就是 string[]，生产代码里也这么用）。
        ///
        /// 判据写成"恒等式 + 防空转"，承载量本身进 Notes：现成的 UEFN Mannequin 没有脸骨，
        /// 那条数应当**可见地为 0**（那是 G1 的资产缺口，不是自检该红的地方）。
        /// </summary>
        private static Group TestHeadShapeCoverage(Scene scene)
        {
            Group group = new Group();
            group.Name = "头部承载量（按参数计的 G1 判据）";

            MetaHumanTemplate face = null;
            foreach (string id in MetaHumanTemplateStore.AvailableTemplateIds())
            {
                MetaHumanTemplate candidate = MetaHumanTemplateStore.Get(id);
                if (candidate != null && candidate.MorphCatalog.Count > 0)
                {
                    face = candidate;
                    break;
                }
            }

            if (face == null)
            {
                group.Skip("没找到带 morph 名录的脸模板（TemplateStore 装载失败，看上面的报错）");
                return group;
            }

            AnimatedModel model = FindAnimatedModel(scene);
            if (model == null || model.SkinnedModel == null)
            {
                group.Skip("场景里没有 AnimatedModel：配 metahuman_fixture(带 SkinnedModel 的 Actor) 后再跑这组");
                return group;
            }

            SkinnedModel asset = model.SkinnedModel;
            HashSet<string> bones;
            HashSet<string> morphs;
            MetaHumanBindingReport report = MetaHuman.MetaHumanAssetProbe.Measure(asset, face, out bones, out morphs);

            group.Check(report.HeadParams > 0, "头部参数条数不该为 0（实得 " + report.HeadParams + "）");
            group.Check(report.HeadParams
                        == report.ServableParams + report.PartiallyServableParams
                        + report.UnservableParams.Count + report.NoTargetParams.Count,
                        "四档之和必须等于头部参数总数（防重计与漏计），实得 " + report.HeadParams);
            group.Check(report.HeadParams <= face.Parameters.Count,
                        "只统计非 Body 区域，所以头部条数不该超过模板参数总数");

            group.Notes.Add("HEAD COVERAGE " + asset.Path + "：全可承载 " + report.ServableParams
                            + " / 部分 " + report.PartiallyServableParams
                            + " / 写不出来 " + report.UnservableParams.Count
                            + " / 只挂材质 " + report.NoTargetParams.Count
                            + "（共 " + report.HeadParams + " 条；骨架里命中的模板骨骼目标 " + bones.Count
                            + " 个、资产 blendshape " + morphs.Count + " 个）");

            if (report.ServableParams == 0 && report.HeadParams > report.NoTargetParams.Count)
            {
                group.Notes.Add("G1 缺口：这具资产一条头部参数都写不出来"
                                + "（头部形状的主载体是 FACIAL_ 那套脸骨，1488 条骨骼效果 vs 37 条 morph）"
                                + " ⇒ 需要带脸骨的 MetaHuman 头资产，不是代码问题");
            }

            return group;
        }

        /// <summary>
        /// 按名寻址有没有歧义。
        /// <para>
        /// 生产代码用 <c>SkinnedModel.FindNode(name)</c> 解析骨骼目标，而引擎侧的恒等绑定回执量到
        /// 这具骨架是「1109 个节点只塌到 558 个唯一名字」（文档 §118）—— 名字重复时命中哪一个由引擎
        /// 内部顺序决定，写偏一格的表现是"某根手指的偏移跑到了另一根上"，不报错也不为 0。
        /// 所以这里把两件事分开量：整副骨架的重名面（只是事实，不是缺陷）与
        /// **模板声明的骨骼目标**是否重名（那才是会让写回落错节点的开口）。
        /// </para>
        /// </summary>
        private static Group TestBoneNameUniqueness(Scene scene)
        {
            Group group = new Group();
            group.Name = "骨架名唯一性（按名寻址的歧义面）";

            AnimatedModel model = FindAnimatedModel(scene);
            if (model == null || model.SkinnedModel == null || model.SkinnedModel.Nodes.Length == 0)
            {
                group.Skip("场景里没有带骨架的 AnimatedModel：配 metahuman_fixture 后再跑这组");
                return group;
            }

            SkinnedModel asset = model.SkinnedModel;
            SkeletonNode[] nodes = asset.Nodes;

            Dictionary<string, int> counts = new Dictionary<string, int>();
            for (int i = 0; i < nodes.Length; i++)
            {
                string name = nodes[i].Name;
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                int seen;
                counts.TryGetValue(name, out seen);
                counts[name] = seen + 1;
            }

            // 量尺防空转：读到 0 个名字不是"没有重名"，是 Nodes[].Name 没被填（口径错了要大声说）。
            group.Check(counts.Count > 0,
                        "一个节点名都没读到 ⇒ 这把尺没在量东西，重名数 0 不可信（节点数=" + nodes.Length + "）");

            int collisions = 0;
            List<string> collisionNames = new List<string>();
            foreach (KeyValuePair<string, int> pair in counts)
            {
                if (pair.Value > 1)
                {
                    collisions++;
                    if (collisionNames.Count < 12)
                    {
                        collisionNames.Add(pair.Key + "×" + pair.Value);
                    }
                }
            }

            // roster 口径：把所有模板声明的骨骼目标并起来，只看"直接命中这具骨架"的那批名字。
            HashSet<string> roster = new HashSet<string>();
            foreach (string id in MetaHumanTemplateStore.AvailableTemplateIds())
            {
                MetaHumanTemplate template = MetaHumanTemplateStore.Get(id);
                if (template == null)
                {
                    continue;
                }

                foreach (string target in template.BoneTargets())
                {
                    if (!string.IsNullOrEmpty(target))
                    {
                        roster.Add(target);
                    }
                }
            }

            group.Check(roster.Count > 0, "模板骨骼目标为空 ⇒ roster 检查会在空集上自证通过（实得 0）");

            // 生产解析优先取"蒙皮关节"；只有"同名里存在多个蒙皮节点"才是真歧义。
            HashSet<int> skinnedNodes = new HashSet<int>();
            if (asset.Bones != null)
            {
                for (int i = 0; i < asset.Bones.Length; i++)
                {
                    skinnedNodes.Add(asset.Bones[i].NodeIndex);
                }
            }

            int directHits = 0;
            int ambiguousHits = 0;
            List<string> ambiguousNames = new List<string>();
            foreach (string name in roster)
            {
                int seen;
                if (!counts.TryGetValue(name, out seen))
                {
                    continue;   // 这具骨架没有这个名字，走的是别名档或写不出来，归承载量那组管
                }

                directHits++;
                if (seen > 1)
                {
                    int skinnedCount = 0;
                    for (int i = 0; i < nodes.Length; i++)
                    {
                        if (nodes[i].Name == name && skinnedNodes.Contains(i))
                        {
                            skinnedCount++;
                        }
                    }

                    // 蒙皮 0 表示这个名字在本骨架里根本不可写（不是“写到错骨”），归承载量那组；
                    // 只有“多个蒙皮节点同名”才是真正的歧义。
                    if (skinnedCount == 0 && ambiguousNames.Count < 12)
                    {
                        ambiguousNames.Add(name + "×" + seen + "（蒙皮 0，不可写）");
                    }

                    if (skinnedCount > 1)
                    {
                        ambiguousHits++;
                        if (ambiguousNames.Count < 12)
                        {
                            ambiguousNames.Add(name + "×" + seen + "（蒙皮 " + skinnedCount + "）");
                        }
                    }
                }
            }

            group.Notes.Add("SKELETON NAMES " + asset.Path + "：节点 " + nodes.Length
                            + " / 唯一名 " + counts.Count + " / 重名名 " + collisions
                            + "（样例 " + string.Join(", ", collisionNames.ToArray()) + "）");
            group.Notes.Add("    roster " + roster.Count + " 条骨骼目标里直接命中 " + directHits
                            + " 条，其中重名 " + ambiguousHits + " 条"
                            + (ambiguousNames.Count > 0 ? "（" + string.Join(", ", ambiguousNames.ToArray()) + "）" : ""));

            // 判据问的是关系而不是绝对值：整副骨架重名是这具资产的事实（两套链各带一份），
            // 会写错节点的只有"模板真要用的名字重名"这一格。
            group.Check(ambiguousHits == 0,
                        ambiguousHits + " 个模板骨骼目标在这具骨架里重名（" + string.Join(", ", ambiguousNames.ToArray()) + "）"
                        + " ⇒ FindNode 命中哪一个由引擎顺序决定，写回可能落到同名另一根上而不报错");

            // 反向对照：如果这条检查从没真正命中过任何 roster 名，它红了也没意义 —— 说明口径没对上。
            group.Check(directHits > 0,
                        "roster 里一个名字都不在这具骨架上（直接命中 0）⇒ 本组只是在空窗口上空转");

            return group;
        }

        private static Group TestHistoryAndSealInEngine()
        {
            Group group = new Group();
            group.Name = "回溯与发布封存（含 JSON 往返）";

            MetaHumanTemplate face = MetaHumanTemplateStore.Get("metahuman.face_archetype");
            if (face == null)
            {
                group.Skip("模板装载失败");
                return group;
            }

            MetaHumanSession session = new MetaHumanSession(face);
            List<MetaHumanParameter> parameters = new List<MetaHumanParameter>(face.All());
            for (int i = 0; i < 6 && i < parameters.Count; i++)
            {
                session.Adjust(parameters[i].Id, parameters[i].Min + (parameters[i].Max - parameters[i].Min) * 0.25f * (i + 1));
                session.Commit("步骤 " + i);
            }

            group.Check(session.StepCount == 7, "6 次提交 + 基线 = 7 步，实得 " + session.StepCount);
            group.Check(session.JumpTo(2) != null, "可跳到第 2 步");
            group.Check(session.Cursor == 2, "跳步后游标在 2");
            group.Check(session.Undo() != null, "还能继续回退");

            MetaHumanLook look = session.Save();
            MetaHumanLook parsed = MetaHumanLook.FromJson(look.ToJson());
            group.Check(parsed.Values.Count == look.Values.Count, "JSON 往返在引擎里也成立");
            group.Check(parsed.VerifyChecksum(), "校验和自证");

            MetaHumanLook bytesRound = MetaHumanLook.FromBytes(look.ToBytes());
            group.Check(bytesRound.Values.Count == look.Values.Count, "字节往返条目数不变");

            session.Publish();
            group.Check(session.IsSealed, "发布后为封存态");
            group.Check(!session.Commit("封存后再提交"), "封存后提交被拒");
            group.Check(session.Undo() == null, "封存后撤销不可用");

            // §31/§31.1 的两条要在引擎里再钉一遍：离线门跑的是同一份纯核源码，
            // 但发布版加载的是内嵌表 + cooked 程序集，只有在脚本域里跑过才算"发布后不可回溯"在真游戏里成立。
            MetaHumanLook publishedLook = session.Save();
            group.Check(publishedLook.Published, "已封存会话的 Save 仍带发布位（不能保存出未发布快照）");

            MetaHumanSession reload = new MetaHumanSession(face);
            reload.LoadLook(MetaHumanLook.FromJson(publishedLook.ToJson()));
            group.Check(reload.IsSealed, "发布过的快照读回来仍是封存态（重登不该解锁回溯）");
            group.Check(!reload.Commit("读档后再提交"), "读档回来的封存态照样拒绝记步");

            // 闸门要挡在"改状态"之前，不是只挡记账。发布版加载的是 cooked 程序集 + 内嵌表，
            // 只有在脚本域里再量一遍"值到没到"，才算这条契约在真游戏里成立。
            // 判据一律问实值，不问返回值：真机量到的缺陷形状正是"历史不记步、值却照改"。
            MetaHumanParameter knob = parameters[0];
            float dragTo = knob.Min + (knob.Max - knob.Min) * 0.9f;
            const float Epsilon = 1e-4f;

            // 正向对照（同一夹具）：未封存时这根滑杆与"重置"确实会改值。
            // 没有这一条，下面四条"不许改"可能只是空转。
            MetaHumanSession twin = new MetaHumanSession(face);
            twin.Adjust(knob.Id, dragTo);
            float twinMoved = twin.State.Get(knob.Id);
            group.Check(Mathf.Abs(twinMoved - dragTo) < Epsilon,
                        "正向对照：未封存时 Adjust 应把实值写成拖到的位置（期望 " + dragTo + "，实得 " + twinMoved + "）");
            group.Check(twin.ResetToBaseline("对照重置"), "正向对照：未封存时重置应当被接受");
            group.Check(Mathf.Abs(twin.State.Get(knob.Id) - knob.Default) < Epsilon
                        || Mathf.Abs(twin.State.Get(knob.Id) - twinMoved) > Epsilon,
                        "正向对照：未封存的重置确实把值动了（否则它是个空操作，下面的\"不许动\"就没东西可挡）");

            MetaHumanSession sealedSession = new MetaHumanSession(face);
            sealedSession.Adjust(knob.Id, dragTo);
            sealedSession.Commit("封存前的定格值");
            sealedSession.Publish();
            float frozenValue = sealedSession.State.Get(knob.Id);
            group.Check(Mathf.Abs(frozenValue - knob.Default) > Epsilon,
                        "夹具必须是「封存值不等于默认值」，否则下面「重置不许抹回默认」会在退化输入上恒过，实得 "
                        + frozenValue + "，默认 " + knob.Default);

            float echoed = sealedSession.Adjust(knob.Id, knob.Min);
            group.Check(Mathf.Abs(sealedSession.State.Get(knob.Id) - frozenValue) < Epsilon,
                        "封存后 Adjust 不得改变实值（引擎内），实得 " + sealedSession.State.Get(knob.Id));
            group.Check(Mathf.Abs(echoed - frozenValue) < Epsilon,
                        "封存后 Adjust 要回原值，不能把被拒的新值报给滑杆（实得 " + echoed + "）");
            group.Check(!sealedSession.Randomize(4242, "封存后随机"), "封存后随机必须被拒");
            group.Check(Mathf.Abs(sealedSession.State.Get(knob.Id) - frozenValue) < Epsilon,
                        "封存后随机不得改动实值（引擎内）");
            group.Check(!sealedSession.ResetToBaseline("封存后重置"), "封存后重置必须被拒");
            group.Check(Mathf.Abs(sealedSession.State.Get(knob.Id) - frozenValue) < Epsilon,
                        "封存后重置不得把实值抹回默认（引擎内）——这条挡的是第 6 个改状态入口");
            List<MetaHumanPreset> presetsForSeal = face.Presets;
            if (presetsForSeal != null && presetsForSeal.Count >= 2)
            {
                group.Check(!sealedSession.ApplyPreset(presetsForSeal[0].Id, "封存后套预设"), "封存后套预设必须被拒（引擎内）");
                group.Check(Mathf.Abs(sealedSession.State.Get(knob.Id) - frozenValue) < Epsilon,
                            "封存后套预设不得改动实值（引擎内）");
                group.Check(!sealedSession.BlendPresets(presetsForSeal[0].Id, presetsForSeal[1].Id, 0.5f, "封存后混合"),
                            "封存后预设混合必须被拒（引擎内）");
                group.Check(Mathf.Abs(sealedSession.State.Get(knob.Id) - frozenValue) < Epsilon,
                            "封存后混合不得改动实值（引擎内）");
            }
            else
            {
                group.Skip("脸模板里不足两个预设，套预设与混合这两条没量（其余各条已覆盖同一闸门）");
            }

            bool symmetricBefore = sealedSession.State.LinkSymmetric;
            group.Check(!sealedSession.SetLinkSymmetric(!symmetricBefore, "封存后翻镜像"), "封存后镜像开关必须被拒（引擎内）");
            group.Check(sealedSession.State.LinkSymmetric == symmetricBefore, "封存后镜像开关不该变（引擎内）");

            // 反向对照：未发布的会话不该被这套逻辑误锁
            MetaHumanSession fresh = new MetaHumanSession(face);
            fresh.Adjust(parameters[0].Id, parameters[0].Min + (parameters[0].Max - parameters[0].Min) * 0.5f);
            MetaHumanLook draft = fresh.Save();
            group.Check(!draft.Published, "未封存会话的 Save 不该带发布位");
            MetaHumanSession freshReload = new MetaHumanSession(face);
            freshReload.LoadLook(draft);
            group.Check(!freshReload.IsSealed, "未发布快照读回来仍可继续编辑");
            group.Check(freshReload.Commit("未封存时可以记步"), "未封存的会话 Commit 该成功");

            return group;
        }

        /// <summary>
        /// 表情通道的落点判据（方案文档 §112 的断言形状 + §113 的对照表）。
        ///
        /// 判据不是"没报错"、也不是计数 &gt;0，而是**资产上那根 blendshape 的权重真的被这次输入改动**：
        /// 挑一条"控制表只给 warp 名（走 §111 那条送出分支）"且"warp→形状对照表给了这具资产真有的名字"的组，
        /// 驱动 → 立即 Rebuild → 回读权重；随后归零再 Rebuild，权重必须回落（防残留让回溯看起来没生效）。
        ///
        /// 每一层缺件都如实 Skip 并说明缺哪一层，不混进"通过"：
        /// 没挂动画器 / 资产没 blendshape / 对照表覆盖不到这具资产 / 控制表没有对应控制，是四种不同的现场。
        /// </summary>
        private static Group TestExpressionDeferredShapes(Scene scene)
        {
            Group group = new Group();
            group.Name = "表情落点（warp→形状对照表在真资产上承重）";

            // 先找宿主角色、再向它要动画器：夹具上的 FaceAnimator 是运行期 AddScript 挂上去的，
            // 编辑器态不跑 OnEnable ⇒ 在场景树里按类型搜 MetaHumanFaceAnimator 会搜不到
            // （2026-10-14 首次真跑就是这条把自己跳过了，而宿主角色明明在）。
            // EnsureFaceAnimator() 是 OnEnable 走的同一条公开入口，比搜索可靠。
            MetaHumanCharacter character = FindCharacter(scene);
            if (character == null)
            {
                group.Skip("场景里没有挂 MetaHumanCharacter 的 Actor：配 metahuman_fixture 后再跑这组");
                return group;
            }

            MetaHumanFaceAnimator animator = character.FaceAnimator != null
                ? character.FaceAnimator
                : character.EnsureFaceAnimator();
            if (animator == null)
            {
                group.Skip("拿不到 MetaHumanFaceAnimator（EnableFaceAnimator 被关，或 AddScript 没成功）");
                return group;
            }

            if (animator.Owner == null)
            {
                animator.Owner = character;
            }

            List<AnimatedModel> models = new List<AnimatedModel>();
            CollectAnimatedModels(scene, models);
            if (models.Count == 0)
            {
                group.Skip("场景里没有带 SkinnedModel 的 AnimatedModel");
                return group;
            }

            MetaHumanWarpShapeMap warp = MetaHumanTemplateStore.WarpShapes;
            MetaHumanFaceControlTable table = MetaHumanTemplateStore.FaceControls;
            if (warp == null || table == null || warp.Entries.Count == 0 || table.Controls == null)
            {
                group.Skip("warp→形状对照表或脸部控制表没装起来（看上面的装载报错）");
                return group;
            }

            // 锚点：控制表里"只给形状名（走送出分支）且没有换算段"的控制，
            // 它引用的 warp 名在对照表里有候选，且该候选真在这具资产的 blendshape 名录里
            //
            // 这三段筛选各自都可能把候选清零，而只有第三段才是"资产的锅"。
            // 上一版直接跳过时把原因写成"这具资产里没有覆盖的形状名"，是在没量过的情况下替资产定罪
            // —— 同夹具的 WARP TABLE 组当时报"这具资产覆盖 25 条"，两个读数就打架了。
            // 所以这里逐段计数，跳过语只说量到的那一段。
            int shapeOnlyControls = 0;
            int controlsWithCandidates = 0;
            int shapesPresentInAsset = 0;
            string pickedWarp = null;
            string pickedShape = null;
            string pickedControlId = null;
            AnimatedModel pickedModel = null;
            for (int c = 0; c < table.Controls.Count && pickedWarp == null; c++)
            {
                MetaHumanFaceControl control = table.Controls[c];
                if (control == null || control.MorphTargets == null || control.MorphTargets.Count == 0)
                {
                    continue;
                }
                if (control.RawTargets != null && control.RawTargets.Count > 0)
                {
                    // 有换算段的那条走的是另一分支，不是本组要测的通路
                    continue;
                }

                shapeOnlyControls++;
                bool thisControlHasCandidate = false;

                for (int m = 0; m < control.MorphTargets.Count; m++)
                {
                    List<string> shapes = warp.ShapesForWarp(control.MorphTargets[m]);
                    if (shapes.Count == 0)
                    {
                        continue;
                    }
                    thisControlHasCandidate = true;
                    for (int s = 0; s < shapes.Count; s++)
                    {
                        for (int i = 0; i < models.Count; i++)
                        {
                            if (HasBlendShape(models[i], shapes[s]))
                            {
                                shapesPresentInAsset++;
                                pickedWarp = control.MorphTargets[m];
                                pickedShape = shapes[s];
                                pickedControlId = control.Id;
                                pickedModel = models[i];
                                break;
                            }
                        }
                        if (pickedWarp != null)
                        {
                            break;
                        }
                    }
                    if (pickedWarp != null)
                    {
                        break;
                    }
                }

                if (thisControlHasCandidate)
                {
                    controlsWithCandidates++;
                }
            }

            if (pickedWarp == null)
            {
                // 三段筛选分别归零的成因不同：第一段是控制表的通路缺口（与资产无关），
                // 第二段是对照表没覆盖这些 warp 名，只有第三段才真的在说资产。
                string blame = shapeOnlyControls == 0
                    ? "控制表里没有「只给形状名且无换算段」的控制（" + table.Controls.Count
                        + " 条控制全带换算段或没有形状目标）⇒ 这是通路缺口，不是资产的锅"
                    : controlsWithCandidates == 0
                        ? "有 " + shapeOnlyControls + " 条形状名控制，但它们引用的 warp 名在对照表里都没有候选"
                        : "shape-only 控制 " + shapeOnlyControls + " 条、有候选 " + controlsWithCandidates
                            + " 条，但候选形状名一个都不在这 " + models.Count + " 具 AnimatedModel 的 blendshape 名录里 ⇒ 才需要带真 MetaHuman 形状的资产";
                group.Skip("没挑到锚点：" + blame + "（形状名命中资产的条数=" + shapesPresentInAsset + "）");
                return group;
            }

            float before = pickedModel.GetBlendShapeWeight(pickedShape);
            animator.SetControl(pickedControlId, 1f);
            animator.Rebuild();
            float after = pickedModel.GetBlendShapeWeight(pickedShape);

            group.Check(after > before + 0.01f,
                        "驱动 " + pickedControlId + " 后 " + pickedShape + " 的权重应上升：" + before + " -> " + after);
            group.Check(animator.MorphsDeferredToApplier >= 1,
                        "送出计数应记下交写回端定名的落点，实得 " + animator.MorphsDeferredToApplier);

            int applied;
            int bridged;
            int skipped;
            character.CollectMorphReceipt(out applied, out bridged, out skipped);
            group.Check(applied >= 1, "写回端回执应有落地条数，实得 写=" + applied + " 译=" + bridged + " 缺=" + skipped);

            // 反向对照：归零必须回落。残留权重会让"回溯到早期步骤"看起来没生效，
            // 而这一条不成立时，上面那次上升可能是别的通道写的。
            animator.SetControl(pickedControlId, 0f);
            animator.Rebuild();
            float zeroed = pickedModel.GetBlendShapeWeight(pickedShape);
            group.Check(zeroed < after, "把控制归零后权重应回落：" + after + " -> " + zeroed);

            group.Notes.Add("EXPR ANCHOR " + pickedControlId + " warp=" + pickedWarp + " shape=" + pickedShape
                             + " model=" + pickedModel.Name + " 权重 " + before + "/" + after + "/" + zeroed
                             + " 回执 写=" + applied + " 译=" + bridged + " 缺=" + skipped);

            pickedModel.SetBlendShapeWeight(pickedShape, 0f);
            return group;
        }

        /// <summary>按类型从整棵场景树里取脚本实例（`Actor.GetScripts&lt;T&gt;()`，引擎 Actor.cs:317）。</summary>
        private static MetaHumanCharacter FindCharacter(Scene scene)
        {
            if (scene == null)
            {
                return null;
            }

            Actor[] roots = scene.Children;
            for (int i = 0; i < roots.Length; i++)
            {
                MetaHumanCharacter found = SearchCharacter(roots[i]);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static MetaHumanCharacter SearchCharacter(Actor actor)
        {
            MetaHumanCharacter[] scripts = actor.GetScripts<MetaHumanCharacter>();
            if (scripts != null && scripts.Length > 0)
            {
                return scripts[0];
            }

            Actor[] children = actor.Children;
            for (int i = 0; i < children.Length; i++)
            {
                MetaHumanCharacter found = SearchCharacter(children[i]);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static void CollectAnimatedModels(Scene scene, List<AnimatedModel> into)
        {
            if (scene == null)
            {
                return;
            }

            Actor[] roots = scene.Children;
            for (int i = 0; i < roots.Length; i++)
            {
                SearchAnimatedModels(roots[i], into);
            }
        }

        private static void SearchAnimatedModels(Actor actor, List<AnimatedModel> into)
        {
            AnimatedModel model = actor as AnimatedModel;
            if (model != null && model.SkinnedModel != null)
            {
                into.Add(model);
            }

            Actor[] children = actor.Children;
            for (int i = 0; i < children.Length; i++)
            {
                SearchAnimatedModels(children[i], into);
            }
        }

        private static bool HasBlendShape(AnimatedModel model, string shapeName)
        {
            if (model == null || model.SkinnedModel == null || string.IsNullOrEmpty(shapeName))
            {
                return false;
            }

            string[] blendShapes = model.SkinnedModel.BlendShapes;
            if (blendShapes == null)
            {
                return false;
            }

            for (int i = 0; i < blendShapes.Length; i++)
            {
                if (shapeName == blendShapes[i])
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// warp→形状对照表在引擎里的装载与对账（离线门管内容，这一组管"引擎真读到了什么"）。
        ///
        /// 三段判据各有归属：
        /// ① 装载即校验（表被人手改坏 / 只重烘了一半，都在装载点就红）；
        /// ② 左岸覆盖：控制表里每个自带形状名的 warp，必须在对照表或 declined 里有一档去处——
        ///    这条是防"静默丢弃"，也是 declined 与 unmapped 的唯一可见方式；
        /// ③ 候选真实性：拿场景这具资产的 `SkinnedModel.BlendShapes` 回问，
        ///    命中数进 Notes 当现场读数；一条都不中才红，并且红话要说清是资产缺口还是表坏了。
        /// </summary>
        private static Group TestWarpShapeTable(Scene scene)
        {
            Group group = new Group();
            group.Name = "warp→形状对照表（引擎装载、左岸覆盖、真资产命中）";

            MetaHumanWarpShapeMap map = MetaHumanTemplateStore.WarpShapes;
            if (map == null)
            {
                group.Skip("对照表没装起来（装载点已报错，看上面的装载问题）");
                return group;
            }

            List<string> problems = map.Validate();
            group.Check(problems.Count == 0, "对照表校验应零问题，实得 " + problems.Count
                        + (problems.Count > 0 ? "：" + problems[0] : string.Empty));

            MetaHumanFaceControlTable table = MetaHumanTemplateStore.FaceControls;
            if (table == null || table.Controls == null)
            {
                group.Skip("脸部控制表没装起来，左岸覆盖这层没法对账");
                return group;
            }

            HashSet<string> warps = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < table.Controls.Count; i++)
            {
                MetaHumanFaceControl control = table.Controls[i];
                if (control == null || control.MorphTargets == null)
                {
                    continue;
                }
                for (int k = 0; k < control.MorphTargets.Count; k++)
                {
                    if (!string.IsNullOrEmpty(control.MorphTargets[k]))
                    {
                        warps.Add(control.MorphTargets[k]);
                    }
                }
            }

            group.Check(warps.Count > 0, "控制表里自带形状名的条数应非零（为 0 说明这份控制表没带 MorphTargets）");
            int unlisted = 0;
            foreach (string warp in warps)
            {
                if (map.ShapesForWarp(warp).Count == 0 && !map.IsDeclined(warp))
                {
                    unlisted++;
                }
            }
            group.Check(unlisted == 0, "有 " + unlisted + " 个 warp 名既不在对照表也不在 declined 里（会被静默丢弃）");

            AnimatedModel model = FindAnimatedModel(scene);
            if (model == null || model.SkinnedModel == null || model.SkinnedModel.BlendShapes == null
                || model.SkinnedModel.BlendShapes.Length == 0)
            {
                group.Skip("场景里没有带 blendshape 的资产：候选命中率这层要在带真 MetaHuman 形状的头资产上测（配 metahuman_fixture）");
                return group;
            }

            HashSet<string> available = new HashSet<string>(StringComparer.Ordinal);
            string[] blendShapes = model.SkinnedModel.BlendShapes;
            for (int i = 0; i < blendShapes.Length; i++)
            {
                available.Add(blendShapes[i]);
            }

            int covered = 0;
            for (int i = 0; i < map.Entries.Count; i++)
            {
                List<string> shapes = map.Entries[i].Shapes;
                for (int k = 0; k < shapes.Count; k++)
                {
                    if (available.Contains(shapes[k]))
                    {
                        covered++;
                        break;
                    }
                }
            }

            group.Notes.Add("WARP TABLE " + model.SkinnedModel.Path + "：条目 " + map.Entries.Count
                             + "、declined " + map.Declined.Count + "、控制表 warp " + warps.Count
                             + "、这具资产覆盖 " + covered + " 条");
            group.Check(covered > 0, "这具资产的形状名录至少应覆盖对照表的一条候选；实得 " + covered
                        + "（0 且资产确有 blendshape ⇒ 要么资产不是 MetaHuman 头、要么对照表与名录分家）");
            return group;
        }

        /// <summary>矩阵顺序判别探针最多扫多少个节点来找"能分辨左右乘"的那一根。</summary>
        private const int SampleNodesForOrderProbe = 64;

        /// <summary>
        /// 找一根**能分辨左乘/右乘**的节点：只有局部带旋转（或非均匀缩放）的节点，
        /// `T*origin` 与 `origin*T` 的平移分量才会不同；单位阵节点上两者恒等，
        /// 此时"A 读到的数应当不等于 B"这类判据根本无从成立。找不到返回 -1。
        /// </summary>
        private static int FirstDiscriminableNode(AnimatedModel model, int nodeCount)
        {
            int limit = nodeCount < SampleNodesForOrderProbe ? nodeCount : SampleNodesForOrderProbe;
            for (int i = 0; i < limit; i++)
            {
                Matrix origin;
                try
                {
                    model.GetNodeTransformation(i, out origin, false);
                }
                catch
                {
                    continue;
                }

                Vector3 shift = new Vector3(0f, 2.5f, 0f);
                Vector3 left = (Matrix.Translation(shift.X, shift.Y, shift.Z) * origin).TranslationVector;
                Vector3 right = (origin * Matrix.Translation(shift.X, shift.Y, shift.Z)).TranslationVector;
                float dx = left.X - right.X;
                float dy = left.Y - right.Y;
                float dz = left.Z - right.Z;
                if (dx * dx + dy * dy + dz * dz > 0.01f)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// 表情骨骼表（控制 → 眼睑骨旋转）。这条表是"眨眼看不见"的修复载体：
        /// 表缺失或通道空掉时，表情只剩 0.1mm 级 morph，画面几乎不动但所有写回计数仍是绿的。
        /// 这里把"表里每条通道都可用"钉成常驻判据，并检查支点/骨名都是 FACIAL_ 族。
        /// </summary>
        private static Group TestExpressionRigLoaded()
        {
            Group group = new Group();
            group.Name = "表情骨骼表装载与可用性（眼睑旋转通道）";

            MetaHumanExpressionRig rig = MetaHumanTemplateStore.ExpressionRig;
            group.Check(rig != null, "表情骨骼表在引擎里装载成功（缺失时表情只剩 0.1mm 级 morph）");
            if (rig == null)
            {
                return group;
            }

            group.Check(rig.ChannelCount == 7, "通道数 7（左右各 blink/eyelidU/eyelidD + 张口），实得 " + rig.ChannelCount);

            int usable = 0;
            int rotationChannels = 0;
            int translateChannels = 0;
            int drivenBones = 0;
            int translatedBones = 0;
            for (int i = 0; i < rig.Channels.Count; i++)
            {
                MetaHumanExpressionChannel channel = rig.Channels[i];

                // 平移通道（张口）：没有支点/轴，逐段要求骨清单与位移齐全
                bool translateOk = channel != null && channel.Translates != null && channel.Translates.Count > 0;
                if (translateOk)
                {
                    for (int t = 0; t < channel.Translates.Count; t++)
                    {
                        MetaHumanExpressionTranslate tr = channel.Translates[t];
                        if (tr == null || tr.Bones == null || tr.Bones.Count == 0
                            || tr.DeltaAtOneCm == null || tr.DeltaAtOneCm.Count != 3
                            || System.Math.Abs(tr.DeltaAtOneCm[0]) + System.Math.Abs(tr.DeltaAtOneCm[1])
                               + System.Math.Abs(tr.DeltaAtOneCm[2]) < 1e-3f)
                        {
                            translateOk = false;
                            break;
                        }

                        for (int b = 0; b < tr.Bones.Count; b++)
                        {
                            if (string.IsNullOrEmpty(tr.Bones[b])
                                || !tr.Bones[b].StartsWith("FACIAL_", StringComparison.Ordinal))
                            {
                                translateOk = false;
                                break;
                            }

                            translatedBones++;
                        }

                        if (!translateOk)
                        {
                            break;
                        }
                    }
                }

                bool ok = translateOk;
                if (translateOk)
                {
                    translateChannels++;
                }

                bool rotateOk = channel != null && !string.IsNullOrEmpty(channel.PivotBone)
                    && channel.PivotBone.StartsWith("FACIAL_", StringComparison.Ordinal)
                    && channel.Axis != null && channel.Axis.Count == 3
                    && channel.Groups != null && channel.Groups.Count > 0;
                if (rotateOk)
                {
                    rotationChannels++;
                }

                ok = ok || rotateOk;
                if (rotateOk)
                {
                    for (int g = 0; g < channel.Groups.Count; g++)
                    {
                        MetaHumanExpressionGroup boneGroup = channel.Groups[g];
                        if (boneGroup == null || boneGroup.Bones == null || boneGroup.Bones.Count == 0
                            || System.Math.Abs(boneGroup.AngleAtOne) < 1e-3f)
                        {
                            ok = false;
                            break;
                        }

                        for (int b = 0; b < boneGroup.Bones.Count; b++)
                        {
                            if (string.IsNullOrEmpty(boneGroup.Bones[b])
                                || !boneGroup.Bones[b].StartsWith("FACIAL_", StringComparison.Ordinal))
                            {
                                ok = false;
                                break;
                            }

                            drivenBones++;
                        }

                        if (!ok)
                        {
                            break;
                        }
                    }
                }

                if (ok)
                {
                    usable++;
                }
                else
                {
                    group.Check(false, "通道不可用：" + (channel == null ? "<null>" : channel.Control));
                }
            }

            group.Check(usable == 7, "7 条通道全部可用（旋转或平移落点齐全），实得 " + usable);
            group.Check(rotationChannels == 6, "旋转通道 6（左右各 blink/eyelidU/eyelidD），实得 " + rotationChannels);
            group.Check(translateChannels == 1, "平移通道 1（张口），实得 " + translateChannels);
            // 实测：6 条旋转通道合计 60 条骨条目（上睑 9 根 / 下睑 6 根，每侧再乘三通道）。
            // 下界取 50 只用来挡"表被清空/被裁掉一半"，不把实测值钉死成唯一合法值。
            group.Check(drivenBones >= 50, "驱动骨条目成规模（左右眼睑合计，实测 60），实得 " + drivenBones);
            // 实测：张口组 57 条（12IPV_LipLower 56 + 下齿 1）
            group.Check(translatedBones >= 40, "张口平移骨条目成规模（实测 57），实得 " + translatedBones);

            // 支点必须是眼球骨：绕骨自己的头转，实测 60° 只有 0.25mm
            MetaHumanExpressionChannel blink = rig.Find("CTRL_L_eye_blink.ty");
            group.Check(blink != null && blink.PivotBone == "FACIAL_L_Eye",
                        "眨眼支点是眼球骨，实得 " + (blink == null ? "<无通道>" : blink.PivotBone));
            MetaHumanExpressionChannel blinkR = rig.Find("CTRL_R_eye_blink.ty");
            group.Check(blinkR != null && blinkR.PivotBone == "FACIAL_R_Eye",
                        "右眼眨眼支点是眼球骨，实得 " + (blinkR == null ? "<无通道>" : blinkR.PivotBone));

            // 张口：下唇组一段平移（下齿骨平移会拉裂牙齿网格，实测后剔除）
            MetaHumanExpressionChannel jaw = rig.Find("CTRL_C_jaw.ty");
            group.Check(jaw != null && jaw.Translates != null && jaw.Translates.Count == 1,
                        "张口通道有 1 段平移（下唇组），实得 "
                        + (jaw == null || jaw.Translates == null ? -1 : jaw.Translates.Count));
            if (jaw != null && jaw.Translates != null && jaw.Translates.Count >= 1)
            {
                float lipDy = jaw.Translates[0].DeltaAtOneCm[1];
                group.Check(lipDy < -0.5f && lipDy > -4.0f,
                            "下唇位移向下且量级合理（-0.5~-4cm，实测 -3cm），实得 " + lipDy.ToString("F2") + "cm");
            }

            return group;
        }

        /// <summary>
        /// 材质参数类型闸门。浮点通道落到一个贴图参数上时，引擎把写入丢掉，
        /// 但每个实例各刷一条 Error —— 2026-10-22 的胡茬通道就是这样刷出 1596 条日志，而画面没有任何变化。
        /// 这组同时带正对照：同一条循环里另一个落点是浮点的通道必须真的写进去，
        /// 否则"没报错"与"整条通路没跑"是同一个形状。
        /// </summary>
        private static Group TestMaterialParameterTypeGuard(Scene scene)
        {
            Group group = new Group();
            group.Name = "材质参数类型闸门（浮点不许写贴图参数）";

            const string TextureParameter = "BakedGroomRootTipGradientRegionMasks";
            const string HeadModelPath = "Content/Character/Models/head_mh2/Ada_FaceMesh_conformhead4.flax";

            // 不依赖"场景里恰好有角色"：这层判据只关乎材质实例上的类型，直接按资产取材质槽。
            // 必须走 ResolveProjectPath：内容库按路径字符串精确比对，直接拿相对路径 Content.Load
            // 在编辑器里查不到（会静默变成"头部模型载不进来"的 skip）。12MB/821 形状的头冷加载要 >10s。
            string headFullPath = MetaHumanAssetUtil.ResolveProjectPath(HeadModelPath);
            // 类型必须是 SkinnedModel：Model 与 SkinnedModel 是兄弟类型（都继承 ModelBase），
            // 用 Content.Load<Model> 取 SkinnedModel 会返回 null —— 这组因此长期以 "载不进来" 跳过。
            SkinnedModel headAsset = Content.Load<SkinnedModel>(headFullPath, 60000.0);
            if (headAsset == null || headAsset.MaterialSlots == null)
            {
                group.Skip("头部模型载不进来（" + headFullPath + "），没有材质槽可取");
                return group;
            }

            MaterialBase headMaterial = null;
            for (int i = 0; i < headAsset.MaterialSlots.Length; i++)
            {
                MaterialBase candidate = headAsset.MaterialSlots[i].Material;
                if (candidate != null && candidate.GetParameter(TextureParameter) != null)
                {
                    headMaterial = candidate;
                    break;
                }
            }

            // 槽上的 Material 引用在资产刚载入时可能还没把命名参数准备好（实测 9 个槽 GetParameter 全空，
            // 而图里明明有这个名字）。按路径把头部材质本身载进来再查一次，避免这组永远 skip。
            if (headMaterial == null)
            {
                const string HeadMaterialPath = "Content/Character/Materials/MH_Head_New.flax";
                MaterialBase direct = Content.Load<MaterialBase>(
                    MetaHumanAssetUtil.ResolveProjectPath(HeadMaterialPath), 30000.0);
                if (direct != null && direct.GetParameter(TextureParameter) != null)
                {
                    headMaterial = direct;
                }
            }

            if (headMaterial == null)
            {
                group.Skip("头模 " + headAsset.MaterialSlots.Length
                           + " 个材质槽里没有带 " + TextureParameter + " 参数的材质：闸门这组没跑");
                return group;
            }

            MaterialInstance instance = headMaterial.CreateVirtualInstance() as MaterialInstance;
            if (instance == null)
            {
                group.Skip("拿不到虚拟材质实例（写浮点会污染共享资产，不允许退化去改基础材质）");
                return group;
            }

            // 前提本身要先量：这条闸门防的就是"落点是贴图"，前提不成立时后面每条判据都是空转
            MaterialParameter maskParameter = instance.GetParameter(TextureParameter);
            MaterialParameterType maskType = maskParameter.ParameterType;
            bool textureKind = maskType == MaterialParameterType.Texture
                               || maskType == MaterialParameterType.NormalMap
                               || maskType == MaterialParameterType.CubeTexture
                               || maskType == MaterialParameterType.GPUTexture;
            group.Check(textureKind, "前提：" + TextureParameter + " 在实例上是贴图类参数，实得 " + maskType);
            if (!textureKind)
            {
                return group;
            }

            object maskBefore = maskParameter.Value;

            MetaHumanSolution solution = new MetaHumanSolution();
            solution.MaterialFloats["Skin_Stubble"] = 0.7f;
            solution.MaterialFloats["Skin_Melanin"] = 0.5f;

            // 写回端需要一个活的 AnimatedModel 才不提前 return。材质写全在虚拟实例上，
            // 不落基础材质、也不碰这具模型（解算用的 morph/bone 两张表都是空的）
            AnimatedModel host = FindAnimatedModel(scene);
            if (host == null)
            {
                group.Skip("场景里没有 AnimatedModel 可当宿主：先用 actor_create(AnimatedModel) 搭一个再跑这组");
                return group;
            }

            MetaHumanAppearanceApplier applier = new MetaHumanAppearanceApplier(host);
            applier.SetMaterials(new MaterialBase[] { instance });
            applier.Apply(solution);

            float tintReadback = float.NaN;
            object tintValue = instance.GetParameterValue("SkinTint");
            if (tintValue is float tintFloat)
            {
                tintReadback = tintFloat;
            }

            group.Check(Mathf.Abs(tintReadback - 0.8f) < 0.001f,
                "正对照：同一条循环把 Skin_Melanin=0.5 落成 SkinTint=0.8，实得 " + tintReadback);

            bool warned = false;
            for (int i = 0; i < applier.Warnings.Count; i++)
            {
                if (applier.Warnings[i].Contains("Skin_Stubble"))
                {
                    warned = true;
                    break;
                }
            }

            group.Check(warned, "闸门触发：跳过原因登记进 Warnings（共 " + applier.Warnings.Count + " 条）");
            group.Check(ReferenceEquals(maskBefore, instance.GetParameter(TextureParameter).Value),
                "贴图参数的原值没被浮点覆盖掉");

            return group;
        }

        private static AnimatedModel FindAnimatedModel(Scene scene)
        {
            if (scene == null)
            {
                return null;
            }

            Actor[] roots = scene.Children;
            for (int i = 0; i < roots.Length; i++)
            {
                AnimatedModel found = Search(roots[i]);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        /// <summary>
        /// 头部蒙皮落点：源 GLB 的权重是否真绑在面部骨上、导入后是否保留了 BlendIndices/BlendWeights。
        /// 为什么要有这条：2026-10-24 曾按"权重全绑 root"下结论并撤掉了 Eye Aim；2026-10-25 离线解析源 GLB
        /// 发现权重是**正常分布**的（眼球 92.4% 在 FACIAL_L/R_EyeParallel、牙齿在 TeethUpper/Lower、脸皮 633 根骨），
        /// 导入产物的 9 个网格也都带蒙皮流。结论要靠盘上数据复核，不能靠一次没动的像素。
        /// </summary>
        private static Group TestHeadSkinning()
        {
            Group group = new Group();
            group.Name = "头部蒙皮落点（权重流 + 面部骨名）";

            const string HeadPath = "Content/Character/Models/head_mh2/Ada_FaceMesh_conformhead4.flax";
            SkinnedModel model = Content.Load<SkinnedModel>(MetaHumanAssetUtil.ResolveProjectPath(HeadPath), 60000.0);
            if (model == null || !model.IsLoaded)
            {
                group.Skip("头部 SkinnedModel 载不进来：" + HeadPath);
                return group;
            }

            // 先摸一下 LODs：model_skin_probe 在 GetMeshes 之前读过 model.LODs，那里读到的 9/9 带蒙皮流；
            // 只调 GetMeshes 时 VertexLayout 可能还没初始化（实测同一资产在这里读到 0/9）。
            var lods = model.LODs;
            group.Check(lods != null && lods.Length > 0, "资产至少 1 级 LOD（实得 " + (lods?.Length ?? 0) + "）");

            model.GetMeshes(out MeshBase[] meshes, 0);
            int meshCount = meshes == null ? 0 : meshes.Length;
            group.Check(meshCount == 9, "LOD0 网格数 = 9（实得 " + meshCount + "）");

            int skinned = 0;
            for (int i = 0; i < meshCount; i++)
            {
                GPUVertexLayout layout = meshes[i].VertexLayout;
                if (layout == null)
                {
                    continue;
                }
                bool hasIndices = layout.FindElement(VertexElement.Types.BlendIndices).Type == VertexElement.Types.BlendIndices;
                bool hasWeights = layout.FindElement(VertexElement.Types.BlendWeights).Type == VertexElement.Types.BlendWeights;
                if (hasIndices && hasWeights)
                {
                    skinned++;
                }
            }
            if (skinned == meshCount && meshCount > 0)
            {
                group.Check(true, "9/9 网格带 BlendIndices+BlendWeights（实得 " + skinned + "/" + meshCount + "）");
            }
            else if (skinned == 0)
            {
                // 顶点布局要等渲染侧初始化后才填：同一资产在 model_skin_probe 里读到 9/9，在本组同步执行时读到 0/9。
                // "布局没填"与"流缺失"同形，不能拿它判缺失，也不能拿资产文件里的元素名当替身
                // （实测 .flax 里既没有 UTF-16 也没有 ASCII 的 "BlendIndices" —— 布局是按枚举序列化的）。
                group.Skip("顶点布局未初始化（渲染侧未触碰），本轮不判蒙皮流；用 model_skin_probe 单独取证");
            }
            else
            {
                group.Check(false, "9/9 网格带 BlendIndices+BlendWeights（实得 " + skinned + "/" + meshCount + "）");
            }

            // 写回是"按名寻址"：权重引用的骨名必须在节点表里能按名找到，否则通道等于断的
            HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < model.Nodes.Length; i++)
            {
                string n = model.Nodes[i].Name;
                if (!string.IsNullOrEmpty(n))
                {
                    names.Add(n);
                }
            }
            string[] required =
            {
                "head", "FACIAL_C_Jaw", "FACIAL_L_EyeParallel", "FACIAL_R_EyeParallel",
                "FACIAL_L_Pupil", "FACIAL_C_TeethUpper", "FACIAL_C_TeethLower", "FACIAL_L_EyelidUpperA1"
            };
            for (int i = 0; i < required.Length; i++)
            {
                group.Check(names.Contains(required[i]), "节点表含 " + required[i]);
            }

            // 形状表：数量与关键通道名（表情/眨眼/张口）必须还在 —— 恢复资产后这三条最容易被"换个文件"打穿。
            // `SkinnedModel.BlendShapes` 托管侧是 string[]（名字），不是结构体数组。
            string[] shapes = model.BlendShapes;
            int shapeCount = shapes == null ? 0 : shapes.Length;
            group.Check(shapeCount == 821, "BlendShape 数量 = 821（实得 " + shapeCount + "）");
            HashSet<string> shapeNames = new HashSet<string>(StringComparer.Ordinal);
            if (shapes != null)
            {
                for (int i = 0; i < shapes.Length; i++)
                {
                    if (!string.IsNullOrEmpty(shapes[i]))
                    {
                        shapeNames.Add(shapes[i]);
                    }
                }
            }
            string[] keyShapes =
            {
                "head_lod0_mesh__eye_blink_L", "head_lod0_mesh__eye_blink_R",
                "head_lod0_mesh__jaw_open", "head_lod0_mesh__brow_raiseIn_L"
            };
            for (int i = 0; i < keyShapes.Length; i++)
            {
                group.Check(shapeNames.Contains(keyShapes[i]), "形状表含 " + keyShapes[i]);
            }

            return group;
        }

        /// <summary>
        /// "表情安全头"候选（morph NORMAL 全零 ⇒ UseNormals=false ⇒ 绕过引擎切线 bug）与生产头的骨名差集。
        /// 用途：判断它能不能直接替换生产头。实测它 821 形状 / 9 槽，但 boneCount=492、生产头 495 ——
        /// 缺哪 3 根必须先点名，再决定是否可用。
        /// </summary>
        private static Group TestExpressionSafeHeadCandidate()
        {
            Group group = new Group();
            group.Name = "表情安全头候选（与生产头骨名差集）";

            const string ProdPath = "Content/Character/Models/head_mh2/Ada_FaceMesh_conformhead4.flax";
            const string SafePath = "Content/Character/Models/head_expression_safe/head_expression_safe.flax";

            SkinnedModel prod = Content.Load<SkinnedModel>(MetaHumanAssetUtil.ResolveProjectPath(ProdPath), 60000.0);
            SkinnedModel safe = Content.Load<SkinnedModel>(MetaHumanAssetUtil.ResolveProjectPath(SafePath), 60000.0);
            if (prod == null || !prod.IsLoaded || safe == null || !safe.IsLoaded)
            {
                group.Skip("候选或生产头载不进来（prod=" + (prod != null) + ", safe=" + (safe != null) + "）");
                return group;
            }

            group.Check(safe.BlendShapes != null && safe.BlendShapes.Length == 821,
                "候选 BlendShape = 821（实得 " + (safe.BlendShapes?.Length ?? 0) + "）");
            group.Check(safe.MaterialSlots != null && safe.MaterialSlots.Length == 9,
                "候选材质槽 = 9（实得 " + (safe.MaterialSlots?.Length ?? 0) + "）");

            HashSet<string> prodNames = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < prod.Nodes.Length; i++)
            {
                if (!string.IsNullOrEmpty(prod.Nodes[i].Name))
                {
                    prodNames.Add(prod.Nodes[i].Name);
                }
            }
            HashSet<string> safeNames = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < safe.Nodes.Length; i++)
            {
                if (!string.IsNullOrEmpty(safe.Nodes[i].Name))
                {
                    safeNames.Add(safe.Nodes[i].Name);
                }
            }
            List<string> missing = new List<string>();
            foreach (string n in prodNames)
            {
                if (!safeNames.Contains(n))
                {
                    missing.Add(n);
                }
            }
            missing.Sort(StringComparer.Ordinal);
            group.Notes.Add("生产头节点 " + prodNames.Count + "，候选 " + safeNames.Count
                            + "，候选缺失 " + missing.Count + "：" + string.Join(",", missing));
            group.Check(safeNames.Count > 0, "候选节点表非空（实得 " + safeNames.Count + "）");
            group.Check(missing.Count <= 8, "候选缺失节点 ≤8（实得 " + missing.Count
                                             + "：" + string.Join(",", missing) + "）");

            string[] critical =
            {
                "head", "FACIAL_C_Jaw", "FACIAL_L_EyeParallel", "FACIAL_R_EyeParallel",
                "FACIAL_L_Pupil", "FACIAL_R_Pupil", "FACIAL_C_TeethUpper", "FACIAL_C_TeethLower"
            };
            for (int i = 0; i < critical.Length; i++)
            {
                group.Check(safeNames.Contains(critical[i]), "候选含关键骨 " + critical[i]);
            }

            return group;
        }

        private static AnimatedModel Search(Actor actor)
        {
            AnimatedModel model = actor as AnimatedModel;
            if (model != null && model.SkinnedModel != null)
            {
                return model;
            }

            Actor[] children = actor.Children;
            for (int i = 0; i < children.Length; i++)
            {
                AnimatedModel found = Search(children[i]);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }
    }
}
