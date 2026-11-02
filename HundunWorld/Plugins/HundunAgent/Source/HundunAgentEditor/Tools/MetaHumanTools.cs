using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using FlaxEngine;
using HundunAgent.Core;

namespace HundunAgent.Tools
{
    /// <summary>
    /// MetaHuman 工具集：把插件的引擎侧自检、角色组件诊断与「改数值→回溯」这条通路暴露出来。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 为什么需要它：MetaHuman 插件的行为分成两层——纯数据核能被离线 dotnet 工装真跑断言，
    /// 但「模板真的从工程目录读得到吗」「骨骼偏移真的写进姿势了吗」「玩家改数值后真的能回溯」
    /// 这三件事只在真脚本环境里成立。此前它们只有"编译过"这一条证据，
    /// 而自检本体是静态方法+Script，编辑器里没有入口调它。
    /// </para>
    /// <para>
    /// 与 <see cref="PCGTools"/> 同一个理由按类型名反射，而不是加一条编译期跨插件依赖：
    /// 编辑器会话缓存的构建图不会重读 Build.cs，加了引用也要等一次完整 Flax.Build 才生效。
    /// 代价是这层没有编译期检查，所以每个入口都先取类型、取方法，取不到就明确报错，
    /// 不拿 null 往下走。
    /// </para>
    /// </remarks>
    public static class MetaHumanTools
    {
        private const BindingFlags Statics = BindingFlags.Public | BindingFlags.Static;
        private const BindingFlags Members = BindingFlags.Public | BindingFlags.Instance;

        private const string SelfTestTypeName = "MetaHuman.Runtime.MetaHumanEngineSelfTest";
        private const string CharacterTypeName = "MetaHuman.Runtime.MetaHumanCharacter";

        public static void Register()
        {
            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "metahuman_selftest",
                Description = "跑一遍 MetaHuman 插件的引擎内自检（模板装载 / 会话与求解 / 本地存档与交接 / " +
                              "骨骼写回 / blendshape 写回 / 历史与封存 / GUI→raw 换算与 ARKit 入口）。" +
                              "它是静态入口，不需要挂 Actor 也不需要播放模式。报告里的 skipped 与 failed 分开统计：" +
                              "「没跑」与「跑绿」是两件事。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{}}",
                Execute = SelfTestAsync
            });

            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "metahuman_inspect",
                Description = "列出场景里挂了 MetaHumanCharacter 的 Actor 及其诊断：装载诊断、参数规模、" +
                              "写回端数量、命中/跳过的 blendshape、历史步数与游标。只读，用来回答" +
                              "「这个角色的数字人组件到底接上了没有」。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{" +
                                  "\"paramSample\":{\"type\":\"integer\",\"description\":\"每个角色样列出前几根滑杆，默认 6\"}}}",
                Execute = InspectAsync
            });

            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "metahuman_drive",
                Description = "在真脚本环境里走一遍「改数值 → 提交 → 回溯」：按参数名调整并提交，" +
                              "可选撤销若干步或跳到指定步，最后回读该参数的实值与诊断报告。" +
                              "这一步验证的是玩家自定义的可回溯性（发布封存之后就回不去了，所以 publish 默认关）。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{" +
                                  "\"actor\":{\"type\":\"string\",\"description\":\"挂了 MetaHumanCharacter 的 Actor 名\"}," +
                                  "\"param\":{\"type\":\"string\",\"description\":\"参数 Id\"}," +
                                  "\"value\":{\"type\":\"number\"}," +
                                  "\"undoTimes\":{\"type\":\"integer\",\"description\":\"提交后撤销几次\"}," +
                                  "\"jumpTo\":{\"type\":\"integer\",\"description\":\"提交后跳到第几步（优先于 undoTimes）\"}," +
                                  "\"publish\":{\"type\":\"boolean\",\"description\":\"true 才封存（不可逆，默认 false）\"}}," +
                                  "\"required\":[\"actor\",\"param\",\"value\"]}",
                Dangerous = true,
                Execute = DriveAsync
            });

            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "metahuman_fixture",
                Description = "在场景里搭一个数字人夹具：建 AnimatedModel、把指定 SkinnedModel 资产挂上去、" +
                              "再给同一个 Actor 加 MetaHumanCharacter 并 Load() 模板。" +
                              "存在的理由：metahuman_selftest 的「骨骼偏移写回 / blendshape 写回」两组只认场景里" +
                              "有没有带骨架的 AnimatedModel，而 metahuman_drive 需要一个挂着 MetaHumanCharacter 的 Actor；" +
                              "现成的 actor_create / actor_set_property 能建能赋值，却没有给 Actor 加脚本的入口，" +
                              "所以这两层实机证据一直没被跑过。remove=true 按名字拆掉夹具。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{" +
                                  "\"model\":{\"type\":\"string\",\"description\":\"SkinnedModel 资产的绝对路径\"}," +
                                  "\"name\":{\"type\":\"string\",\"description\":\"夹具 Actor 名，默认 MH_Fixture\"}," +
                                  "\"templateIds\":{\"type\":\"array\",\"items\":{\"type\":\"string\"}," +
                                  "\"description\":\"要装载的模板 Id，缺省用 face_archetype + body_base 这一对\"}," +
                                  "\"remove\":{\"type\":\"boolean\",\"description\":\"true 就只按名字删夹具，不再新建\"}," +
                                  "\"remote\":{\"type\":\"boolean\",\"description\":\"true：再挂 RemotePlayerActor 并套一份合成外观（走远端 ApplyMetaHumanLook 同一条链）\"}," +
                                  "\"hairStyle\":{\"type\":\"integer\",\"description\":\"remote 用：发型下标 0..5\"}," +
                                  "\"hairColor\":{\"type\":\"integer\",\"description\":\"remote 用：发色下标（调色板）\"}," +
                                  "\"eyeColor\":{\"type\":\"number\",\"description\":\"remote 用：eye.iris_color 0..1（会映射到创角色板）\"}," +
                                  "\"characterId\":{\"type\":\"integer\",\"description\":\"remote 用：合成远端的角色 Id，默认 900001\"}}," +
                                  "\"required\":[\"model\"]}",
                Dangerous = true,
                Execute = FixtureAsync
            });

            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "metahuman_bind",
                Description = "G2「自动绑定骨骼动画」的引擎内入口：调 MetaHumanEditor.MetaHumanAnimationBinder，" +
                              "按纯核 MetaHumanRetarget 的四档匹配算出「目标骨架节点名 → 来源骨架节点名」映射，" +
                              "写进引擎原生通道 SkinnedModel.SkeletonRetargets。" +
                              "默认 apply=false 只算不写（不动资产）；apply=true 才落盘并 Save。" +
                              "回执里的 retargetCountAfter 与 mappingCount 都是从资产/条目上读回来的实际值，不是请求里想要的值。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{" +
                                  "\"target\":{\"type\":\"string\",\"description\":\"目标 SkinnedModel 路径，默认 Content/Character/Models/Character_1.flax\"}," +
                                  "\"source\":{\"type\":\"string\",\"description\":\"来源骨架的 SkinnedModel 路径（动画原始骨架），必填\"}," +
                                  "\"templateId\":{\"type\":\"string\",\"description\":\"用于槽位档匹配的模板 Id，默认 metahuman.face_archetype\"}," +
                                  "\"apply\":{\"type\":\"boolean\",\"description\":\"true 才写回资产并 Save，默认 false\"}," +
                                  "\"animationsDir\":{\"type\":\"string\",\"description\":\"可选：源骨架的动画目录（如 Content/Animations/als）。给了就在 apply 时给目录内每份动画补「动画 id → 来源骨架」条目，播放才会命中重定向\"}}, " +
                                  "\"required\":[\"source\"]}",
                Execute = BindAnimationAsync
            });

            ToolRegistry.Register(new AgentToolDescriptor
            {
                Name = "metahuman_template",
                Description = "数字人模板编辑器四条动作的脚本入口，与窗口按钮一一对应、共用同一批实现：" +
                              "scan=扫 UE 导出目录（只读）、import=导入并据资产生成模板、" +
                              "check=拿真实 blendshape/骨骼名录核对模板（只读）、" +
                              "backfill=把资产里多出来的 morph 回填成滑杆、save=校验后另存为 JSON。" +
                              "回填改的是缓存里那份模板，只有 save 才落盘；落盘后运行期还要重烘内嵌表才生效。" +
                              "回执里的计数全部从对象上读回来（含每份资产的骨骼数与 blendshape 数）。",
                InputSchemaJson = "{\"type\":\"object\",\"properties\":{" +
                                  "\"action\":{\"type\":\"string\",\"enum\":[\"scan\",\"import\",\"check\",\"backfill\",\"save\"],\"description\":\"缺省 scan\"}," +
                                  "\"directory\":{\"type\":\"string\",\"description\":\"scan/import 用：UE 导出目录（绝对或工程相对）\"}," +
                                  "\"destinationFolder\":{\"type\":\"string\",\"description\":\"import 用：资产输出目录，缺省 Content/MetaHuman\"}," +
                                  "\"templateId\":{\"type\":\"string\",\"description\":\"check/backfill/save 的源模板 Id，缺省 metahuman.face_archetype\"}," +
                                  "\"models\":{\"type\":\"array\",\"items\":{\"type\":\"string\"},\"description\":\"check/backfill 用的 SkinnedModel 资产路径，可多条（头/身/眼各一份）\"}," +
                                  "\"outputTemplateId\":{\"type\":\"string\",\"description\":\"import/save 写出的模板 Id\"}}," +
                                  "\"required\":[\"action\"]}",
                Dangerous = true,
                Execute = TemplateAsync
            });
        }

        private static Task<object> FixtureAsync(JsonElement args)
        {
            string modelPath = EditorUtils.GetString(args, "model", null);
            if (string.IsNullOrWhiteSpace(modelPath))
                throw new InvalidOperationException("model 不能为空（要指向一份 SkinnedModel 资产）");
            string name = EditorUtils.GetString(args, "name", "MH_Fixture");
            bool remove = EditorUtils.GetBool(args, "remove", false);
            var templateIds = EditorUtils.GetStringArray(args, "templateIds");
            if (templateIds == null)
                templateIds = new List<string>();
            if (templateIds.Count == 0)
            {
                templateIds.Add("metahuman.face_archetype");
                templateIds.Add("metahuman.body_base");
            }

            bool remote = EditorUtils.GetBool(args, "remote", false);
            int remoteHairStyle = EditorUtils.GetInt(args, "hairStyle", 0);
            int remoteHairColor = EditorUtils.GetInt(args, "hairColor", 0);
            float remoteEyeColor = EditorUtils.GetNum(args, "eyeColor", 0.5f);
            int remoteCharacterId = EditorUtils.GetInt(args, "characterId", 900001);

            return MainThread.InvokeAsync<object>(() =>
            {
                var existing = FindActorByName(name);

                if (remove)
                {
                    if (existing == null)
                        return new { status = "absent", name };
                    Actor.Destroy(existing);
                    return new { status = "removed", name };
                }

                if (existing != null)
                    Actor.Destroy(existing);

                // 合成远端走 Game 程序集自己的入口：不必再建一份“夹具 + MetaHumanCharacter”，
                // 否则会出现两个脚本实例、Session 为空的假失败。
                if (remote)
                {
                    return ApplySyntheticRemote((ulong)remoteCharacterId, remoteHairStyle, remoteHairColor, remoteEyeColor);
                }

                var modelType = EditorUtils.FindType("FlaxEngine.SkinnedModel");
                if (modelType == null)
                    throw new InvalidOperationException("找不到 FlaxEngine.SkinnedModel");

                // 与 model_inspect 共用同一条加载通路：先按内容数据库的资产 ID 加载，
                // 不把调用方的路径拼写送进注册表（否则会重复登记同一 GUID，见 6a2z）。
                // 12MB/821 形状的大头冷加载实测 >10s，超时给足 60s。
                var model = EditorUtils.LoadAsset(modelPath, 60000.0) as SkinnedModel;
                if ((model == null || !model.IsLoaded) && Guid.TryParse(modelPath, out var assetId))
                {
                    model = Content.Load<SkinnedModel>(assetId, 60000.0);
                }

                if (model == null || !model.IsLoaded)
                    throw new InvalidOperationException("资产加载为空（路径对吗、编辑器扫过这份资产吗）[带 60 秒超时]: " + modelPath);

                var animatedType = EditorUtils.FindType("FlaxEngine.AnimatedModel");
                var actor = (Actor)Activator.CreateInstance(animatedType);
                actor.Name = name;
                // 与运行期口径一致地关掉逐骨运动模糊（495 骨 ×48×2≈47KB 会撞 32KB 冷缓冲）。
                // 注意：这只减半，编辑器的骨骼缓冲是**所有** AnimatedModel 共用的，
                // 若缓冲此前没被别的模型撑到 32KB，首次分配仍会命中 AnimatedModel.cpp:82 断言并崩溃
                // —— 因此编辑器内夹具只适合小骨架；大骨架的目视验证走 PIE（#76）。
                animatedType.GetProperty("PerBoneMotionBlur")?.SetValue(actor, false);
                animatedType.GetProperty("SkinnedModel").SetValue(actor, model);

                FlaxEditor.Editor.Instance.SceneEditing.Spawn(actor, EditorUtils.RequireScene(), -1, false);

                var characterType = RequireCharacterType();
                // Flax 的 Actor.AddScript(Type) 在 `New(type)` 返回空时自己不判空，直接在
                // `script.Parent = this`（Source/Engine/Level/Actor.cs:201）抛 NullReferenceException，
                // 于是"脚本类型没被登记成可实例化脚本"这种会话级问题只剩一句"未将对象引用设置到实例"。
                // 实测：Character_1 / Ada 身 / Ada 头三种模型、带不带 templateIds 全都同一条 NRE，
                // 而类型名与运行时类的 namespace 逐字对得上 ⇒ 不是资产、不是参数、不是名字写错，
                // 是本次编辑器会话里 MetaHuman 脚本模块不可实例化（PIE 换域后的典型形状）。
                // 这里把它兜成有指向的错误，并把当时能观测到的现场一起带出来。
                var script = (FlaxEngine.Script)null;
                try
                {
                    script = actor.AddScript(characterType);
                }
                catch (NullReferenceException nre)
                {
                    throw new InvalidOperationException(
                        "AddScript 内部 NRE（Flax Actor.cs:201 的 New() 返回了空）⇒ 脚本类型在本次会话里不可实例化，"
                        + "通常是 MetaHuman 脚本模块未激活或刚被播放模式换域；重启编辑器再试。"
                        + " 类型=" + characterType.FullName
                        + " 程序集=" + characterType.AssemblyQualifiedName
                        + " 场景=" + EditorUtils.RequireScene().Name, nre);
                }
                if (script == null)
                    throw new InvalidOperationException("AddScript 返回空（MetaHumanCharacter 挂不上）");

                // TemplateIds 是 MetaHumanCharacter 上的 public **字段**（不是属性），
                // 原来只走 GetProperty ⇒ 拿到 null 就静默跳过，Load() 用空数组跑完，
                // 而回执照旧回显请求里的 id 列表 —— 于是夹具永远 loaded=false、drive 永远没参数可跑，
                // 工具却报成功。改法：属性→字段都试，并且回执只写**从对象上读回来的条数**。
                string templateApplyError = null;
                string[] idsValue = templateIds.ToArray();
                var idsProp = characterType.GetProperty("TemplateIds");
                if (idsProp != null && idsProp.CanWrite)
                {
                    idsProp.SetValue(script, idsValue);
                }
                else
                {
                    var idsField = characterType.GetField("TemplateIds");
                    if (idsField == null)
                        templateApplyError = "MetaHumanCharacter 上既没有可写的 TemplateIds 属性也没有字段";
                    else
                        idsField.SetValue(script, idsValue);
                }

                string[] appliedIds = characterType.GetProperty("TemplateIds")?.GetValue(script) as string[]
                                      ?? characterType.GetField("TemplateIds")?.GetValue(script) as string[];
                int templateIdsApplied = appliedIds?.Length ?? 0;
                if (templateApplyError == null && templateIdsApplied != templateIds.Count)
                    templateApplyError = "TemplateIds 写不进去：请求 " + templateIds.Count
                                         + " 条、对象上读回 " + templateIdsApplied + " 条";

                string loadError = templateApplyError;
                try
                {
                    var loadMethod = characterType.GetMethod("Load", Type.EmptyTypes);
                    if (loadMethod == null)
                        loadError = "MetaHumanCharacter.Load() 不存在";
                    else
                        loadMethod.Invoke(script, null);
                }
                catch (Exception ex)
                {
                    loadError = ex.InnerException?.Message ?? ex.Message;
                }

                var skinned = modelType.GetProperty("Nodes")?.GetValue(model) as Array;
                var blends = modelType.GetProperty("BlendShapes")?.GetValue(model) as Array;

                var loadDiags = characterType.GetField("LoadDiagnostics")?.GetValue(script)
                                as System.Collections.Generic.List<string>;

                var result = new
                {
                    actor = name,
                    id = actor.ID.ToString(),
                    model = modelPath,
                    boneNodes = skinned?.Length ?? 0,
                    blendShapes = blends?.Length ?? 0,
                    templateIds,
                    // 对账字段：从对象上读回来的条数与脚本自己的装载诊断，而不是请求里想要什么。
                    templateIdsApplied,
                    loadDiagnostics = loadDiags,
                    scriptAttached = true,
                    loadError
                };

                if (loadError != null)
                    Debug.LogWarning("夹具的 MetaHumanCharacter 配置失败：" + loadError);
                else if (loadDiags != null && loadDiags.Count > 0)
                    Debug.LogWarning("夹具 Load() 后仍有装载问题：" + string.Join(" ; ", loadDiags.ToArray()));

                return result;
            });
        }

        /// <summary>
        /// 合成远端：在夹具 Actor 上挂 RemotePlayerActor，并调它自己的 ApplyMetaHumanLook
        /// （与网络远端渲染链完全同一条），外观用一份内存里的 MetaHumanLook 构造，不碰服务端/本地存档。
        /// 回执只写**从场景对象上读回来的**东西（发模路径、HairStyle、头/发是否挂上）。
        /// </summary>
        private static object ApplySyntheticRemote(ulong characterId, int hairStyle, int hairColor, float eyeColor01)
        {
            try
            {
                // 必须在 Game 程序集里执行：工具侧反射拿到的 MetaHumanCharacter 与运行时脚本
                // 不是同一个装配，GetScript<> 会返回 null（实测），Session 因此为空、LoadLook 必失败。
                var probeType = EditorUtils.FindType("HundunWorld.Game.Character.MetaHumanRemoteProbe");
                if (probeType == null)
                    return new { ok = false, error = "找不到 MetaHumanRemoteProbe 类型" };

                var synth = probeType.GetMethod("Synthesize");
                if (synth == null)
                    return new { ok = false, error = "MetaHumanRemoteProbe.Synthesize 不存在" };

                // 合成位置放在玩法场景原点前方 2.5m（Flax +Z），便于截图时进入玩家相机。
                object[] call = { 0f, 0f, 250f, hairStyle, hairColor, eyeColor01, characterId, null };
                string actorName = synth.Invoke(null, call) as string;
                string diag = call[7] as string;
                return new { ok = actorName != null, actor = actorName, diagnostics = diag };
            }
            catch (Exception ex)
            {
                return new { ok = false, error = (ex.InnerException ?? ex).Message };
            }
        }

        /// <summary>按名字在已加载场景里找 Actor（编辑器脚本域里没有 Actor.Find(string) 这种入口）。</summary>
        private static Actor FindActorByName(string name)
        {
            foreach (var scene in Level.Scenes)
            {
                if (scene == null)
                    continue;

                var found = FindActorIn(scene, name);
                if (found != null)
                    return found;
            }

            return null;
        }

        private static Actor FindActorIn(Actor root, string name)
        {
            if (root == null)
                return null;

            if (string.Equals(root.Name, name, StringComparison.Ordinal))
                return root;

            foreach (var child in root.Children)
            {
                var found = FindActorIn(child, name);
                if (found != null)
                    return found;
            }

            return null;
        }

        /// <summary>
        /// G2 的引擎内入口：按纯核四档匹配算出 retarget 映射，默认只算不写。
        /// 回执里的 mappingCount / retargetCountBefore / retargetCountAfter 全是**从对象上读回来的值**，
        /// 不是请求里想要的值 —— 夹具那边刚犯过这个错（文档 §38）。
        /// </summary>
        private static Task<object> BindAnimationAsync(JsonElement args)
        {
            string targetPath = EditorUtils.GetString(args, "target", "Content/Character/Models/Character_1.flax");
            string sourcePath = EditorUtils.GetString(args, "source", null);
            if (string.IsNullOrWhiteSpace(sourcePath))
                throw new InvalidOperationException("source 不能为空（动画原始骨架的 SkinnedModel 路径）");
            string templateId = EditorUtils.GetString(args, "templateId", "metahuman.face_archetype");
            bool apply = EditorUtils.GetBool(args, "apply", false);
            string animationsDir = EditorUtils.GetString(args, "animationsDir", null);

            return MainThread.InvokeAsync<object>(() =>
            {
                var binderType = EditorUtils.FindType("MetaHumanEditor.MetaHumanAnimationBinder");
                if (binderType == null)
                    throw new InvalidOperationException("找不到 MetaHumanEditor.MetaHumanAnimationBinder"
                                                       + "（MetaHumanEditor 程序集没加载？编辑器重启后再试）");

                var resultType = binderType.GetNestedType("Result");
                var build = binderType.GetMethod("BuildEntry", Statics);
                var applyMethod = binderType.GetMethod("Apply", Statics);
                var formatMethod = binderType.GetMethod("Format", Statics);
                if (resultType == null || build == null || applyMethod == null)
                    throw new InvalidOperationException("MetaHumanAnimationBinder 的签名对不上（Result/BuildEntry/Apply）");

                object result = Activator.CreateInstance(resultType);
                object template = LoadTemplateById(templateId);
                SkinnedModel target = LoadSkinnedModelByName(targetPath);
                SkinnedModel source = LoadSkinnedModelByName(sourcePath);

                int retargetCountBefore = target.SkeletonRetargets == null ? 0 : target.SkeletonRetargets.Length;

                object entry = build.Invoke(null, new object[] { target, source, template, result });
                // Flax 的 SkinnedModel.SkeletonRetarget.NodesMapping 是**字段**不是属性（实测回执里
                // mapped=1105 而 mappingCount=0 的矛盾就是这么来的）：属性、字段都试；
                // 两处都没有就记 -1（=我读不到），绝不把"读不到"报成"表是空的"。
                object mappingMember = entry == null ? null
                    : (entry.GetType().GetProperty("NodesMapping")?.GetValue(entry)
                       ?? entry.GetType().GetField("NodesMapping")?.GetValue(entry));
                int mappingCount = mappingMember == null ? -1 : CountOf(mappingMember);

                // 把映射表的键（目标骨架节点名）直接摊出来：探针选了不在这 55 条里的节点就会误读成"没动"。
                var mappingKeys = new List<string>();
                if (mappingMember is System.Collections.IDictionary dict)
                {
                    foreach (object key in dict.Keys)
                    {
                        if (mappingKeys.Count >= 80)
                        {
                            break;
                        }
                        mappingKeys.Add(key as string);
                    }
                }

                bool saved = false;
                if (apply)
                    saved = (bool)applyMethod.Invoke(null, new object[] { target, source, template, result });

                // 引擎按动画资产 Id 查重定向条目，所以只写「来源骨架模型」的条目在播放时不会命中。
                // 这里给目录里的每份动画补一条 {动画 Id, 来源骨架}，播放器才走 Retargeting。
                int animationFilesFound = 0;
                int animationEntries = 0;
                bool animationSaved = false;
                if (apply && !string.IsNullOrWhiteSpace(animationsDir))
                {
                    string dirFull = Path.Combine(Globals.ProjectFolder,
                        animationsDir.Replace('/', Path.DirectorySeparatorChar).TrimEnd(Path.DirectorySeparatorChar));
                    string[] files = Directory.Exists(dirFull)
                        ? Directory.GetFiles(dirFull, "*.flax", SearchOption.AllDirectories)
                        : new string[0];
                    animationFilesFound = files.Length;

                    var animations = new List<Animation>();
                    var notesList = resultType.GetField("Notes")?.GetValue(result) as List<string>
                                    ?? resultType.GetProperty("Notes")?.GetValue(result) as List<string>;
                    foreach (string file in files)
                    {
                        string rel = Path.GetRelativePath(Globals.ProjectFolder, file).Replace('\\', '/');
                        Animation anim = Content.Load<Animation>(rel, 10000.0f);
                        if (anim != null && anim.IsLoaded)
                        {
                            animations.Add(anim);
                        }
                        else if (notesList != null)
                        {
                            notesList.Add("动画加载失败：" + rel);
                        }
                    }

                    // 直接写引擎类型，不反射 MetaHumanEditor：编辑器热重载只换本程序集时，
                    // 那边可能还是旧版，反射会报「缺 ApplyAnimations」。
                    Guid skeletonId = source.ID;
                    var merged = new List<SkinnedModel.SkeletonRetarget>(target.SkeletonRetargets
                        ?? new SkinnedModel.SkeletonRetarget[0]);
                    foreach (Animation anim in animations)
                    {
                        Guid animId = anim.ID;
                        merged.RemoveAll(e => e.SourceAsset == animId);
                        merged.Add(new SkinnedModel.SkeletonRetarget
                        {
                            SourceAsset = animId,
                            SkeletonAsset = skeletonId,
                            NodesMapping = new Dictionary<string, string>()
                        });
                        animationEntries++;
                    }

                    target.SkeletonRetargets = merged.ToArray();
                    bool animSaved = target.Save(false, string.Empty);
                    animationSaved = !animSaved;
                    if (animSaved && notesList != null)
                    {
                        notesList.Add("动画条目已算出但资产落盘失败（Save 返回 true=存不了）");
                    }
                    else if (notesList != null)
                    {
                        notesList.Add("已为 " + animationEntries + " 份动画写入动画源条目并保存");
                    }
                }

                int retargetCountAfter = target.SkeletonRetargets == null ? 0 : target.SkeletonRetargets.Length;

                // `mapped` 是**逐个节点**数的，`NodesMapping` 是**按目标名去重**的字典：
                // 重名多的骨架（Character_1 的 1109 个节点里有大量同名）会让前者远大于后者，
                // 上一轮就出现过 mapped=8 而 mappingCount=2 —— 看着像 bug，其实是两套计数。
                // 与其让人猜，把两套都报出来，并顺手量出重名到底吃掉多少。
                var mappedList = ReadStringList(result, "Mapped");
                var unmappedList = ReadStringList(result, "Unmapped");
                var mappedTargets = new HashSet<string>();
                foreach (string line in mappedList)
                {
                    int arrow = line.IndexOf('\u2190');
                    string name = arrow > 0 ? line.Substring(0, arrow).Trim() : line.Trim();
                    if (!string.IsNullOrEmpty(name))
                    {
                        mappedTargets.Add(name);
                    }
                }

                var unmappedTargets = new HashSet<string>();
                foreach (string name in unmappedList)
                {
                    if (!string.IsNullOrEmpty(name))
                    {
                        unmappedTargets.Add(name);
                    }
                }

                return new
                {
                    target = targetPath,
                    source = sourcePath,
                    templateId,
                    apply,
                    targetNodes = target.Nodes == null ? 0 : target.Nodes.Length,
                    sourceNodes = source.Nodes == null ? 0 : source.Nodes.Length,
                    mapped = CountOf(ReadMember(result, "Mapped")),
                    unmapped = CountOf(ReadMember(result, "Unmapped")),
                    // 两套计数各自摊开：occurrences 是逐节点，unique 是按目标名去重后
                    // （`NodesMapping` 是字典，只能按名字存一条，所以 mappingCount 应当等于 unique 那一套）。
                    mappedOccurrences = mappedList.Count,
                    mappedUniqueTargets = mappedTargets.Count,
                    unmappedOccurrences = unmappedList.Count,
                    unmappedUniqueTargets = unmappedTargets.Count,
                    // 每档命中数让引擎自己报。之前"预测 12 / 实测 11"这种差只能靠我手里的部分名单去推，
                    // 而名单覆盖不全就会造出假矛盾（离线夹具只带 Character_1 的 52/1109 个节点名）。
                    tierExact = IntMember(result, "ExactMatches"),
                    tierCase = IntMember(result, "CaseInsensitiveMatches"),
                    tierMirror = IntMember(result, "MirrorMatches"),
                    tierAlias = IntMember(result, "AliasMatches"),
                    tierSlot = IntMember(result, "SlotMatches"),
                    mappingCount,
                    mappingKeys,
                    retargetCountBefore,
                    retargetCountAfter,
                    animationFilesFound,
                    animationEntries,
                    animationSaved,
                    saved,
                    notes = ReadStringList(result, "Notes"),
                    report = formatMethod == null ? null
                             : formatMethod.Invoke(null, new object[] { targetPath, sourcePath, result }) as string
                };
            });
        }

        /// <summary>读一个整数成员；读不到返回 -1（=我读不到），绝不把"读不到"报成 0。</summary>
        private static int IntMember(object instance, string name)
        {
            object value = ReadMember(instance, name);
            if (value is int i)
                return i;
            return -1;
        }

        private static object ReadMember(object instance, string name)
        {
            if (instance == null)
                return null;
            var type = instance.GetType();
            var field = type.GetField(name);
            if (field != null)
                return field.GetValue(instance);
            return type.GetProperty(name)?.GetValue(instance);
        }

        private static int CountOf(object collection)
        {
            var list = collection as System.Collections.ICollection;
            return list?.Count ?? 0;
        }

        private static List<string> ReadStringList(object instance, string name)
        {
            var found = new List<string>();
            var raw = ReadMember(instance, name) as System.Collections.IEnumerable;
            if (raw == null)
                return found;
            foreach (var item in raw)
                found.Add(item == null ? string.Empty : item.ToString());
            return found;
        }

        private static object LoadTemplateById(string templateId)
        {
            Type store = EditorUtils.FindType("MetaHuman.Core.MetaHumanTemplateStore")
                         ?? EditorUtils.FindType("MetaHuman.Runtime.MetaHumanTemplateStore")
                         ?? EditorUtils.FindType("MetaHuman.MetaHumanTemplateStore");
            if (store == null)
                throw new InvalidOperationException("找不到 MetaHumanTemplateStore（程序集没加载？）");
            var get = store.GetMethod("Get", Statics);
            if (get == null)
                throw new InvalidOperationException("MetaHumanTemplateStore.Get(string) 不存在");
            var template = get.Invoke(null, new object[] { templateId });
            if (template == null)
                throw new InvalidOperationException("模板装载为空：" + templateId);
            return template;
        }

        /// <summary>
        /// 与夹具里那份加载是同一段逻辑的第二次落地（编译期泛型 + 10 秒超时 + Guid 兜底）。
        /// 暂时没把夹具那边改成调这里：那份已经被真机验过，而新代码要等重启才跑得动，
        /// 此时动它等于把"已证"换成"未证"。下一次能跑真机时一并合。
        /// </summary>
        private static SkinnedModel LoadSkinnedModelByName(string path)
        {
            string normalized = path.Replace('\\', '/');
            var model = Content.Load<SkinnedModel>(normalized, 10000.0f);
            if (model == null || !model.IsLoaded)
            {
                AssetInfo info;
                Content.GetAssetInfo(normalized, out info);
                if (info.ID != Guid.Empty)
                {
                    model = Content.Load<SkinnedModel>(info.ID, 10000.0f);
                }
                else
                {
                    Guid assetId;
                    if (Guid.TryParse(normalized, out assetId))
                        model = Content.Load<SkinnedModel>(assetId, 10000.0f);
                }
            }

            if (model == null || !model.IsLoaded)
                throw new InvalidOperationException("资产加载为空（路径对吗、编辑器扫过这份资产吗）: " + path);

            return model;
        }

        /// <summary>
        /// 模板工作流的脚本入口。与窗口共用 MetaHumanUEImporter / MetaHumanRigBinder / MetaHumanTemplateStore，
        /// 所以「窗口里点出来的结论」和「这里跑出来的回执」是同一套判定，不存在两份口径。
        /// 反射而非直接引用：与 metahuman_bind 同一个理由（脚本域热重载只换本程序集时，那边可能是旧版）。
        /// </summary>
        private static Task<object> TemplateAsync(JsonElement args)
        {
            string action = (EditorUtils.GetString(args, "action", "scan") ?? "scan").Trim().ToLowerInvariant();
            string directory = EditorUtils.GetString(args, "directory", null);
            string destination = EditorUtils.GetString(args, "destinationFolder", "Content/MetaHuman");
            string templateId = EditorUtils.GetString(args, "templateId", "metahuman.face_archetype");
            string outputId = EditorUtils.GetString(args, "outputTemplateId", null);
            List<string> modelPaths = EditorUtils.GetStringArray(args, "models") ?? new List<string>();

            return MainThread.InvokeAsync<object>(() =>
            {
                var importer = EditorUtils.FindType("MetaHumanEditor.MetaHumanUEImporter");
                var binder = EditorUtils.FindType("MetaHuman.Runtime.MetaHumanRigBinder");
                var store = EditorUtils.FindType("MetaHuman.Core.MetaHumanTemplateStore");
                if (importer == null || binder == null || store == null)
                    throw new InvalidOperationException(
                        "类型没加载齐（importer=" + (importer != null) + " binder=" + (binder != null)
                        + " store=" + (store != null) + "）；编辑器重启过吗");

                string root = store.GetProperty("RootDirectory")?.GetValue(null) as string;

                switch (action)
                {
                    case "scan":
                        return new { action, rootDirectory = root, plan = DescribePlan(InvokeScan(importer, directory, destination)) };
                    case "import":
                        return DoImport(importer, binder, directory, destination, outputId, root);
                    case "check":
                        return new { action, rootDirectory = root, check = DoCheck(binder, templateId, modelPaths) };
                    case "backfill":
                        return DoBackfill(binder, templateId, modelPaths, root);
                    case "save":
                        return DoSave(templateId, outputId, root);
                    default:
                        throw new InvalidOperationException("action 只认 scan/import/check/backfill/save，收到：" + action);
                }
            });
        }

        private static object InvokeScan(Type importer, string directory, string destination)
        {
            if (string.IsNullOrWhiteSpace(directory))
                throw new InvalidOperationException("directory 不能为空（要指向 UE 导出物文件夹本身）");
            var scan = importer.GetMethod("Scan", Statics);
            if (scan == null)
                throw new InvalidOperationException("MetaHumanUEImporter.Scan(string, string) 不存在");
            return scan.Invoke(null, new object[] { directory, destination });
        }

        /// <summary>把扫描计划摊成可回执的字段：模型/贴图槽位/描述 JSON/告警，一条都不藏。</summary>
        private static object DescribePlan(object plan)
        {
            var modelFiles = ReadStringList(plan, "ModelFiles");
            var names = new List<string>();
            foreach (string file in modelFiles)
                names.Add(Path.GetFileName(file));

            var slots = new List<string>();
            if (ReadMember(plan, "TexturesBySlot") is System.Collections.IDictionary dict)
            {
                foreach (object key in dict.Keys)
                {
                    string texture = dict[key] as string ?? string.Empty;
                    slots.Add(key + " ← " + Path.GetFileName(texture));
                }
            }

            return new
            {
                sourceDirectory = Get(plan, "SourceDirectory") as string,
                isEmpty = ToBool(Get(plan, "IsEmpty")),
                modelCount = modelFiles.Count,
                modelFiles = names,
                textureSlots = slots,
                descriptionFiles = ReadStringList(plan, "DescriptionFiles").Count,
                warnings = ReadStringList(plan, "Warnings"),
            };
        }

        private static SkinnedModel[] LoadModelSet(List<string> paths)
        {
            if (paths == null || paths.Count == 0)
                throw new InvalidOperationException("models 不能为空（check/backfill 要知道拿哪些资产对照）");

            var models = new List<SkinnedModel>();
            foreach (string path in paths)
                models.Add(LoadSkinnedModelByName(path));
            return models.ToArray();
        }

        /// <summary>核对：模板引用的 morph/骨骼在不在资产里，以及多少参数真的能被这份资产驱动。</summary>
        private static object DoCheck(Type binder, string templateId, List<string> modelPaths)
        {
            object template = LoadTemplateById(templateId);
            SkinnedModel[] models = LoadModelSet(modelPaths);

            var bind = binder.GetMethod("Bind", Statics);
            var format = binder.GetMethod("Format", Statics);
            if (bind == null || format == null)
                throw new InvalidOperationException("MetaHumanRigBinder 的 Bind/Format 签名对不上");

            object report = bind.Invoke(null, new object[] { template, models });
            string text = format.Invoke(null, new object[] { templateId, report }) as string;

            var perAsset = new List<object>();
            foreach (SkinnedModel model in models)
            {
                perAsset.Add(new
                {
                    // Flax 的托管 Asset 只暴露 Path，展示名自己从路径切
                    asset = Path.GetFileNameWithoutExtension(model.Path ?? string.Empty),
                    path = model.Path,
                    bones = model.Nodes == null ? 0 : model.Nodes.Length,
                    blendShapes = model.BlendShapes == null ? 0 : model.BlendShapes.Length,
                });
            }

            return new
            {
                templateId,
                parameters = CountOf(Get(template, "Parameters")),
                morphCatalog = CountOf(Get(template, "MorphCatalog")),
                missingSlots = CountOf(Get(report, "MissingSlots")),
                missingMorphs = CountOf(Get(report, "MissingMorphs")),
                missingBoneTargets = CountOf(Get(report, "MissingBoneTargets")),
                unservableParams = CountOf(Get(report, "UnservableParams")),
                noTargetParams = CountOf(Get(report, "NoTargetParams")),
                headParams = IntMember(report, "HeadParams"),
                servableParams = IntMember(report, "ServableParams"),
                partiallyServableParams = IntMember(report, "PartiallyServableParams"),
                notes = ReadStringList(report, "Notes"),
                assets = perAsset,
                reportText = text,
            };
        }

        private static object DoImport(Type importer, Type binder, string directory,
            string destination, string outputId, string root)
        {
            if (string.IsNullOrWhiteSpace(outputId))
                throw new InvalidOperationException("outputTemplateId 不能为空（导入的产物是一份新模板）");

            object plan = InvokeScan(importer, directory, destination);
            if (ToBool(Get(plan, "IsEmpty")))
                return new { action = "import", imported = 0, reason = "目录里没有可导入的模型", plan = DescribePlan(plan) };

            var importModels = importer.GetMethod("ImportModels", Statics);
            var compose = importer.GetMethod("ComposeTemplateFromAssets", Statics);
            if (importModels == null || compose == null)
                throw new InvalidOperationException("MetaHumanUEImporter 的 ImportModels/ComposeTemplateFromAssets 签名对不上");

            object[] importArgs = { plan, null };
            var imported = importModels.Invoke(null, importArgs) as System.Collections.IEnumerable;
            int failed = importArgs[1] is int f ? f : -1;

            var importedList = new List<string>();
            if (imported != null)
            {
                foreach (object item in imported)
                    importedList.Add(item == null ? string.Empty : item.ToString());
            }

            if (importedList.Count == 0)
                return new { action = "import", imported = 0, failed, reason = "没有一个资产导入成功，未生成模板", plan = DescribePlan(plan) };

            object[] composeArgs = { outputId, "导入自 " + directory, importedList, null };
            string templatePath = compose.Invoke(null, composeArgs) as string;
            object report = composeArgs[3];

            var format = binder.GetMethod("Format", Statics);
            string text = format == null || report == null
                ? null
                : format.Invoke(null, new object[] { outputId, report }) as string;

            // 模板文件是否真的在盘上，以文件为准，不以返回值为准
            bool onDisk = !string.IsNullOrEmpty(templatePath) && File.Exists(templatePath);
            return new
            {
                action = "import",
                plan = DescribePlan(plan),
                imported = importedList.Count,
                importedAssets = importedList,
                failed,
                templatePath,
                templateFileExists = onDisk,
                templateFileBytes = onDisk ? new FileInfo(templatePath).Length : -1,
                rootDirectory = root,
                reportText = text,
            };
        }

        private static object DoBackfill(Type binder, string templateId, List<string> modelPaths, string root)
        {
            // 与窗口同一条口径：回填改的是缓存里那份模板，save 才落盘。
            // 所以这里不能 ClearCache——清了就把上一次回填的成果冲掉，而回执照样是绿的。
            object template = LoadTemplateById(templateId);
            SkinnedModel[] models = LoadModelSet(modelPaths);

            var importMissing = binder.GetMethod("ImportMissingMorphs", Statics);
            if (importMissing == null)
                throw new InvalidOperationException("MetaHumanRigBinder.ImportMissingMorphs 不存在");

            int before = CountOf(Get(template, "Parameters"));
            int added = (int)importMissing.Invoke(null, new object[] { template, models });
            var check = DoCheck(binder, templateId, modelPaths);

            return new
            {
                action = "backfill",
                templateId,
                parametersBefore = before,
                added,
                parametersAfter = CountOf(Get(template, "Parameters")),
                morphCatalog = CountOf(Get(template, "MorphCatalog")),
                persisted = false,
                hint = "回填只在内存里（模板根目录 " + (string.IsNullOrEmpty(root) ? "未解析" : root)
                       + "）。要落盘接着调 action=save。",
                check,
            };
        }

        private static object DoSave(string templateId, string outputId, string root)
        {
            if (string.IsNullOrWhiteSpace(outputId))
                throw new InvalidOperationException("outputTemplateId 不能为空");
            if (string.IsNullOrEmpty(root))
                throw new InvalidOperationException("模板根目录没解析出来，无处可写");

            object template = LoadTemplateById(templateId);
            var templateType = template.GetType();

            var validate = templateType.GetMethod("Validate", Members);
            if (validate == null)
                throw new InvalidOperationException("MetaHumanTemplate.Validate() 不存在");
            if (validate.Invoke(template, null) is System.Collections.IEnumerable errorsEnum)
            {
                var errors = new List<string>();
                foreach (object error in errorsEnum)
                    errors.Add(error == null ? string.Empty : error.ToString());
                if (errors.Count > 0)
                    return new { action = "save", saved = false, reason = "校验未通过", errors };
            }

            string sourceId = Get(template, "TemplateId") as string;
            SetMember(template, "TemplateId", outputId);
            SetMember(template, "DisplayName", "派生自 " + sourceId);

            string path = Path.Combine(root, "Template_" + outputId + ".json");
            bool overwrite = File.Exists(path);
            var save = templateType.GetMethod("SaveToFile", Members);
            if (save == null)
                throw new InvalidOperationException("MetaHumanTemplate.SaveToFile(string) 不存在");
            save.Invoke(template, new object[] { path });

            bool onDisk = File.Exists(path);
            return new
            {
                action = "save",
                saved = onDisk,
                path,
                overwrite,
                bytes = onDisk ? new FileInfo(path).Length : -1,
                parameters = CountOf(Get(template, "Parameters")),
                morphCatalog = CountOf(Get(template, "MorphCatalog")),
                hint = "盘上 JSON 只影响编辑器窗口；游戏/PIE 读的是内嵌表，" +
                       "要生效跑 python Tools/MetaHumanBakeTables.py 再重编 MetaHumanTarget。",
            };
        }

        private static void SetMember(object instance, string name, object value)
        {
            var type = instance.GetType();
            var property = type.GetProperty(name);
            if (property != null && property.CanWrite)
            {
                property.SetValue(instance, value);
                return;
            }

            var field = type.GetField(name);
            if (field != null)
            {
                field.SetValue(instance, value);
                return;
            }

            throw new InvalidOperationException(type.Name + " 上写不了成员 " + name);
        }

        private static Type RequireCharacterType()
        {
            // 先看场景里活着的组件属于哪份程序集：脚本域重载后按名字找很可能拿到**旧**那份，
            // 旧类型映不回原生对象，报出来的是一句没有信息的 NullReference（PCG 那边实测栽过）。
            foreach (var scene in Level.Scenes)
            {
                if (scene == null)
                    continue;
                var live = FindLiveScriptType(scene);
                if (live != null)
                    return live;
            }

            var type = EditorUtils.FindType(CharacterTypeName);
            if (type == null)
                throw new InvalidOperationException("找不到 " + CharacterTypeName + "（MetaHuman 插件未加载，或脚本域正在重载）");
            return type;
        }

        private static Type FindLiveScriptType(Actor actor)
        {
            foreach (var script in actor.GetScripts(typeof(FlaxEngine.Script)))
            {
                if (string.Equals(script.GetType().FullName, CharacterTypeName, StringComparison.Ordinal))
                    return script.GetType();
            }

            foreach (var child in actor.Children)
            {
                var found = FindLiveScriptType(child);
                if (found != null)
                    return found;
            }

            return null;
        }

        private static List<Actor> CharactersWithScript(out int actorsWithScript)
        {
            var type = RequireCharacterType();
            var found = new List<Actor>();
            actorsWithScript = 0;

            // 与 PCGTools 同一个入口：编辑器里挂着的场景都从 Level.Scenes 拿，
            // 每个 Scene 本身就是根 Actor，Children 递归下去。
            foreach (var scene in Level.Scenes)
            {
                if (scene != null)
                    Collect(scene, type, found);
            }

            actorsWithScript = found.Count;
            return found;
        }

        private static void Collect(Actor actor, Type scriptType, List<Actor> found)
        {
            if (actor == null)
                return;

            foreach (var script in actor.GetScripts(typeof(FlaxEngine.Script)))
            {
                if (scriptType.IsInstanceOfType(script))
                {
                    found.Add(actor);
                    break;
                }
            }

            foreach (var child in actor.Children)
                Collect(child, scriptType, found);
        }

        private static object Get(object target, string member)
        {
            if (target == null)
                return null;

            var type = target.GetType();
            var prop = type.GetProperty(member, Members);
            if (prop != null)
                return prop.GetValue(target);

            var field = type.GetField(member, Members);
            return field?.GetValue(target);
        }

        private static object Call(object target, string method, params object[] args)
        {
            var type = target.GetType();
            var mi = type.GetMethod(method, Members);
            if (mi == null)
                throw new InvalidOperationException(type.Name + " 上没有方法 " + method);
            try
            {
                return mi.Invoke(target, args);
            }
            catch (TargetInvocationException ex)
            {
                throw new InvalidOperationException(method + " 抛出：" + ex.InnerException?.Message, ex.InnerException);
            }
        }

        private static Task<object> SelfTestAsync(JsonElement args)
        {
            return MainThread.InvokeAsync<object>(() =>
            {
                var selfTest = EditorUtils.FindType(SelfTestTypeName);
                if (selfTest == null)
                    throw new InvalidOperationException("找不到 " + SelfTestTypeName + "（MetaHuman 插件未加载？）");

                var run = selfTest.GetMethod("Run", Statics);
                if (run == null)
                    throw new InvalidOperationException(SelfTestTypeName + " 上没有静态 Run(Scene)");

                var scene = EditorUtils.RequireScene();
                var report = run.Invoke(null, new object[] { scene }) as string;

                // 报告文本里已经带了每组的通过/失败/跳过，这里再抽一次汇总方便直接判断
                int passed = 0, failed = 0, skipped = 0;
                foreach (var line in (report ?? string.Empty).Split('\n'))
                {
                    if (!line.Contains("合计"))
                        continue;
                    passed = PickNumber(line, "通过");
                    failed = PickNumber(line, "失败");
                    skipped = PickNumber(line, "跳过");
                }

                return new
                {
                    scene = scene.Name,
                    passed,
                    failed,
                    skipped,
                    report
                };
            });
        }

        private static int PickNumber(string line, string label)
        {
            int at = line.IndexOf(label, StringComparison.Ordinal);
            if (at < 0)
                return -1;

            var sb = new System.Text.StringBuilder();
            for (int i = at + label.Length; i < line.Length; i++)
            {
                char c = line[i];
                if (char.IsDigit(c) || c == '-')
                    sb.Append(c);
                else if (sb.Length > 0)
                    break;
            }

            return int.TryParse(sb.ToString(), out int value) ? value : -1;
        }

        private static Task<object> InspectAsync(JsonElement args)
        {
            int sample = EditorUtils.GetInt(args, "paramSample", 6);

            return MainThread.InvokeAsync<object>(() =>
            {
                var actors = CharactersWithScript(out int count);
                var items = new List<object>();

                foreach (var actor in actors)
                {
                    var component = FindScript(actor, RequireCharacterType());
                    if (component == null)
                        continue;

                    var session = Get(component, "Session");
                    var template = Get(session, "Template");
                    var parameters = Get(template, "Parameters") as System.Collections.IList;

                    var sampleList = new List<object>();
                    if (parameters != null)
                    {
                        for (int i = 0; i < parameters.Count && i < sample; i++)
                        {
                            var p = parameters[i];
                            sampleList.Add(new
                            {
                                id = Get(p, "Id") as string,
                                region = Get(p, "Region")?.ToString(),
                                min = ToFloat(Get(p, "Min")),
                                max = ToFloat(Get(p, "Max")),
                                value = ToFloat(Get(p, "Default"))
                            });
                        }
                    }

                    var appliers = Get(component, "Appliers") as System.Collections.IList;

                    // 写回端的材质告警必须出现在回执里：闸门"跳过了哪条通道"如果只落在 applier 对象上，
                    // 与"这条通路压根没跑"是同一种形状，无法分辨。
                    var materialWarnings = new List<string>();
                    if (appliers != null)
                    {
                        for (int i = 0; i < appliers.Count && materialWarnings.Count < 8; i++)
                        {
                            var warnings = Get(appliers[i], "Warnings") as System.Collections.IEnumerable;
                            if (warnings == null)
                                continue;
                            foreach (var w in warnings)
                            {
                                if (materialWarnings.Count >= 8)
                                    break;
                                materialWarnings.Add("applier" + i + ": " + w);
                            }
                        }
                    }

                    items.Add(new
                    {
                        actor = actor.Name,
                        loaded = ToBool(Get(component, "IsLoaded")),
                        sealedLook = ToBool(Get(component, "IsSealed")),
                        parameterCount = parameters?.Count ?? 0,
                        applierCount = appliers?.Count ?? 0,
                        materialWarnings = materialWarnings,
                        historySteps = ToInt(Get(component, "HistoryStepCount")),
                        historyCursor = ToInt(Get(component, "HistoryCursor")),
                        lodDecision = Get(component, "LastLodDecision")?.ToString(),
                        diagnostics = Call(component, "BuildDiagnosticReport") as string,
                        sampleParameters = sampleList
                    });
                }

                return new
                {
                    characters = count,
                    hint = count == 0
                        ? "场景里没有挂 MetaHumanCharacter 的 Actor：这一层还没有真机证据，"
                          + "先在场景里放一个带 SkinnedModel 的 Actor 并挂上该脚本（或用 actor_create + ui_script_add）"
                        : null,
                    items
                };
            });
        }

        private static Task<object> DriveAsync(JsonElement args)
        {
            string actorName = EditorUtils.GetString(args, "actor");
            string param = EditorUtils.GetString(args, "param");
            double value = EditorUtils.GetDouble(args, "value", 0.0);
            int undoTimes = EditorUtils.GetInt(args, "undoTimes", 0);
            int jumpTo = EditorUtils.GetInt(args, "jumpTo", int.MinValue);
            bool publish = EditorUtils.GetBool(args, "publish", false);

            return MainThread.InvokeAsync<object>(() =>
            {
                var type = RequireCharacterType();
                var actors = CharactersWithScript(out _);
                var actor = actors.Find(a => string.Equals(a.Name, actorName, StringComparison.OrdinalIgnoreCase));
                if (actor == null)
                    throw new InvalidOperationException("找不到名为 " + actorName + " 且挂着 MetaHumanCharacter 的 Actor");

                var component = FindScript(actor, type);
                var session = Get(component, "Session");
                if (session == null)
                {
                    Call(component, "Load");
                    session = Get(component, "Session");
                    if (session == null)
                        throw new InvalidOperationException("Load() 之后仍然没有会话（模板装载失败，看 metahuman_inspect 的诊断）");
                }

                var state = Get(session, "State");
                float Before()
                {
                    var raw = Call(state, "Get", param);
                    return raw is float f ? f : float.NaN;
                }

                float before = Before();
                Call(component, "Adjust", param, (float)value);
                Call(component, "Commit", "agent-drive");
                float afterCommit = Before();

                int steps = ToInt(Get(component, "HistoryStepCount"));

                object jumped = null;
                if (jumpTo != int.MinValue)
                {
                    jumped = Call(component, "JumpToStep", jumpTo);
                }
                else
                {
                    for (int i = 0; i < undoTimes; i++)
                        Call(component, "Undo");
                }

                float afterBack = Before();
                int cursor = ToInt(Get(component, "HistoryCursor"));
                bool sealedNow = ToBool(Get(component, "IsSealed"));

                if (publish)
                    Call(component, "Publish");

                return new
                {
                    actor = actor.Name,
                    param,
                    valueBefore = before,
                    valueAfterCommit = afterCommit,
                    valueAfterRollback = afterBack,
                    historySteps = steps,
                    historyCursor = cursor,
                    rollbackWorked = Math.Abs(afterBack - before) < 1e-4f,
                    sealedBeforePublish = sealedNow,
                    sealedAfter = ToBool(Get(component, "IsSealed")),
                    jumpReturned = jumped != null,
                    // 骨骼写回/跳过计数：参数"拖了不动"时用来区分是名字没命中还是权重太小。
                    diagnostics = Call(component, "BuildDiagnosticReport") as string
                };
            });
        }

        private static Script FindScript(Actor actor, Type type)
        {
            foreach (var script in actor.GetScripts(typeof(FlaxEngine.Script)))
            {
                if (type.IsInstanceOfType(script))
                    return script;
            }

            return null;
        }

        private static float ToFloat(object value) => value is float f ? f : float.NaN;

        private static int ToInt(object value) => value is int i ? i : -1;

        private static bool ToBool(object value) => value is bool b && b;
    }
}
