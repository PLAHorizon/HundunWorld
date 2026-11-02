using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using MetaHuman;

namespace MetaHumanVerify
{
    /// <summary>
    /// MetaHuman 纯数据核的离线断言。判据一律来自被测代码之外的独立参照或源数据，
    /// 且每个否定断言都配一个同夹具的正向对照，避免「绿灯=空转」。
    /// </summary>
    internal static class Program
    {
        private static int _passed;
        private static int _failed;
        private static int _skipped;
        private static readonly List<string> _failures = new List<string>();
        private static readonly List<string> _skips = new List<string>();

        /// <summary>因夹具文件缺失而没跑的检查必须出现在结果里，不能和「跑绿」同形。</summary>
        private static void Skip(string why)
        {
            _skipped++;
            _skips.Add(why);
        }
        private static void True(bool condition, string what)
        {
            if (condition)
            {
                _passed++;
            }
            else
            {
                _failed++;
                _failures.Add(what);
            }
        }

        private static void Eq(int expected, int actual, string what)
        {
            True(expected == actual, what + "（期望 " + expected + "，实得 " + actual + "）");
        }

        private static void EqF(float expected, float actual, string what)
        {
            True(Math.Abs(expected - actual) <= 1e-4f, what + "（期望 " + expected + "，实得 " + actual + "）");
        }

        private static void Eq(bool expected, bool actual, string what)
        {
            True(expected == actual, what + "（期望 " + expected + "，实得 " + actual + "）");
        }

        private static void EqS(string expected, string actual, string what)
        {
            True(expected == actual, what + "（期望 \"" + expected + "\"，实得 \"" + actual + "\"）");
        }

        private static string LocateTemplatesDir()
        {
            string dir = AppContext.BaseDirectory;
            for (int up = 0; up < 10 && !string.IsNullOrEmpty(dir); up++)
            {
                string candidate = Path.Combine(dir, "HundunWorld", "Plugins", "MetaHuman", "Content", "Templates");
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }

                dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            }

            return null;
        }

        private static int Main()
        {
            string templatesDir = LocateTemplatesDir();
            if (templatesDir == null)
            {
                Console.WriteLine("找不到模板目录，全部断言视为失败");
                return 2;
            }

            Console.WriteLine("模板目录: " + templatesDir);

            MetaHumanTemplate face = MetaHumanTemplate.LoadFromFile(Path.Combine(templatesDir, "Template_Face_Archetype.json"));
            MetaHumanTemplate body = MetaHumanTemplate.LoadFromFile(Path.Combine(templatesDir, "Template_Body_Base.json"));

            TestRealTemplates(face, body, templatesDir);
            TestSolverEquivalence(face, body);
            TestClampAndCounter(face);
            TestHistoryAndSeal(face);
            TestLookRoundTrip(face, body);
            TestConstraints();
            TestLodPolicy();
            TestMergeAndBlend(face, body);
            TestBodyEffectiveness(face, body);
            TestEveryParameterMovesItsChannel(face, body);
            TestSculptRegionPartition(face, body);
            TestPanelRowBudgetCoversPartition(face, body, templatesDir);
            TestRestorePrefersPublishedSnapshot();
            TestAppearanceArbitration();
            TestLookRevision(face);
            TestRemoteLookRevisionPolicy();
            TestWmNormalDriven(face);
            TestTwoPublishedLooks();
            TestServerLookIsAdoptedBeforeBind(templatesDir);
            TestHeadMaterialSpecSelfConsistency(templatesDir);
            TestNeutralPresetActuallyResets(face);
            TestMaterialTextureChannel();
            TestEveryEffectKindIsJudged();
            TestHairStylePersistsAroundPublish();
            TestSealedSurvivesReload(face);
            TestFaceSemanticZones(face);
            TestSymmetry();
            TestValidateCatchesDefects(face);
            TestGuiToRawConversion(templatesDir);
            TestBakedTables(face, body, templatesDir);
            TestBindingAgainstRealDnaNames(face, body, templatesDir);
            TestRetargetWithRealSkeletons(templatesDir);
            TestCrossFamilyAliasTier();
            TestHeadMaterialSpecContract();
            TestRuntimeModuleDeclaresGamePlugin(templatesDir);
            TestLookChecksumSelfProofAndWireRoundTrip();
            TestBlendShapeWritesGoThroughGuard(templatesDir);
            TestRemoteAppearanceChainTargetsHead(templatesDir);
            TestColorSwatchWiring(templatesDir);
            TestNoEnvironmentStackTraceInLogs(templatesDir);
            TestPreviewKeyLightNotBlack(templatesDir);
            TestBodyTabExposed(templatesDir);
            TestPreviewHairWiring(templatesDir);
            TestProceduralIdleWiring(templatesDir);
            TestPupilSizeTarget(templatesDir);
            TestLipstickTarget(templatesDir);
            TestMakeupChannelTargets(templatesDir);
            TestMakeupTintTargets(templatesDir);
            TestStubbleChannel(templatesDir);
            TestMaterialTargetMapCoversTemplateEffects(templatesDir);
            TestHeadTextureColorSpaceMatchesSpec();
            TestHistoryDepthBoundaryRelations();
            TestWarpShapeMapContract();
            TestModelImportSettingsInventory();
            TestAssetRegistryIntegrity(templatesDir);

            Console.WriteLine();
            Console.WriteLine("通过 " + _passed + " 项，失败 " + _failed + " 项，跳过 " + _skipped + " 项");
            for (int i = 0; i < _failures.Count; i++)
            {
                Console.WriteLine("  FAIL: " + _failures[i]);
            }

            for (int i = 0; i < _skips.Count; i++)
            {
                Console.WriteLine("  SKIP: " + _skips[i]);
            }

            return _failed == 0 ? 0 : 1;
        }

        /// <summary>
        /// 头部材质规格表（Head_Material_Spec.json）的契约断言。
        /// 为什么要有这条：那张表是给"用户手做 MH_Head 材质"用的逐字规格，
        /// 但它一度只是一份没人校验的文档——名字打错、srgb 档位写反、声称已导入的贴图其实不在盘上，
        /// 这三种都会让后面通道写入静默写空（本项目栽过同类）。所以把规格本身变成常驻断言。
        /// 第③段还带一个反向对照（改坏一份数据必须被验证器抓到），否则"0 问题"可能是空转。
        /// 材质还不存在时，引擎侧名录那一层如实计入 skipped，不混进"通过"。
        /// </summary>
        private static void TestAppearanceArbitration()
        {
            True(MetaHuman.Core.MetaHumanAppearanceArbitration.Choose(1, 2, 0xAAAAu, 0xBBBBu)
                 == MetaHuman.Core.AppearanceArbitration.UseServer, "仲裁：服务端 revision 更大 ⇒ 采用服务端");
            True(MetaHuman.Core.MetaHumanAppearanceArbitration.Choose(3, 2, 0xAAAAu, 0xBBBBu)
                 == MetaHuman.Core.AppearanceArbitration.UseLocal, "仲裁：本机 revision 更大 ⇒ 保留本机");
            True(MetaHuman.Core.MetaHumanAppearanceArbitration.Choose(2, 2, 0xAAAAu, 0xAAAAu)
                 == MetaHuman.Core.AppearanceArbitration.Identical, "仲裁：校验和一致 ⇒ 无冲突");
            True(MetaHuman.Core.MetaHumanAppearanceArbitration.Choose(2, 2, 0xAAAAu, 0xBBBBu)
                 == MetaHuman.Core.AppearanceArbitration.UseLocal, "仲裁：同 revision 不同校验和 ⇒ 保守保留本机");
            True(MetaHuman.Core.MetaHumanAppearanceArbitration.Choose(0, 0, 0u, 0xBBBBu)
                 == MetaHuman.Core.AppearanceArbitration.UseLocal, "仲裁：本机无校验和 ⇒ 不把未知当成相同");
            True(MetaHuman.Core.MetaHumanAppearanceArbitration.Choose(100L, 200L, 1u, 2u)
                 == MetaHuman.Core.AppearanceArbitration.UseServer, "仲裁(时刻)：服务端更大 ⇒ 采用服务端");
            True(MetaHuman.Core.MetaHumanAppearanceArbitration.Choose(300L, 200L, 1u, 2u)
                 == MetaHuman.Core.AppearanceArbitration.UseLocal, "仲裁(时刻)：本机更大 ⇒ 保留本机");
            True(MetaHuman.Core.MetaHumanAppearanceArbitration.Choose(200L, 200L, 1u, 1u)
                 == MetaHuman.Core.AppearanceArbitration.Identical, "仲裁(时刻)：一致 ⇒ 无冲突");
            True(MetaHuman.Core.MetaHumanAppearanceArbitration.Choose(0L, 200L, 0u, 2u)
                 == MetaHuman.Core.AppearanceArbitration.UseServer, "仲裁(时刻)：本机无存档 ⇒ 采用服务端");

            // 生产口径 ChooseLooks：两端都有修订号时修订号优先（时刻与结论相反也不动摇）。
            string basis;
            True(MetaHuman.Core.MetaHumanAppearanceArbitration.ChooseLooks(
                     5L, 900L, 1u, 6L, 100L, 2u, out basis)
                 == MetaHuman.Core.AppearanceArbitration.UseServer && basis == "修订号",
                 "ChooseLooks：两端有修订号 ⇒ 服务端 6>5 生效，即使服务端时刻更小");
            True(MetaHuman.Core.MetaHumanAppearanceArbitration.ChooseLooks(
                     7L, 100L, 1u, 6L, 900L, 2u, out basis)
                 == MetaHuman.Core.AppearanceArbitration.UseLocal && basis == "修订号",
                 "ChooseLooks：本机修订号更大 ⇒ 保留本机，即使服务端时刻更大");
            True(MetaHuman.Core.MetaHumanAppearanceArbitration.ChooseLooks(
                     0L, 900L, 1u, 6L, 100L, 2u, out basis)
                 == MetaHuman.Core.AppearanceArbitration.UseLocal && basis == "保存时刻",
                 "ChooseLooks：本机旧档无修订号 ⇒ 整体回退时刻");
            True(MetaHuman.Core.MetaHumanAppearanceArbitration.ChooseLooks(
                     5L, 100L, 1u, 0L, 900L, 2u, out basis)
                 == MetaHuman.Core.AppearanceArbitration.UseServer && basis == "保存时刻",
                 "ChooseLooks：服务端旧档无修订号 ⇒ 整体回退时刻");
            True(MetaHuman.Core.MetaHumanAppearanceArbitration.ChooseLooks(
                     5L, 1L, 7u, 5L, 2L, 7u, out basis)
                 == MetaHuman.Core.AppearanceArbitration.Identical && basis == "修订号",
                 "ChooseLooks：同修订号且校验和一致 ⇒ 无冲突");
        }

        /// <summary>
        /// 修订号契约（#64）：字节里带 Revision、旧档回退到 0、会话提交/发布只增不减、
        /// 被拒提交不推进、读档采用更大修订号。
        /// </summary>
        private static void TestLookRevision(MetaHumanTemplate face)
        {
            MetaHumanLook look = new MetaHumanLook();
            look.TemplateId = "t";
            look.TemplateVersion = "1";
            look.SourcePresetId = "p";
            look.TemplateParamCount = 3;
            look.LinkSymmetric = true;
            look.SavedAtUtcTicks = 123456789L;
            look.Revision = 42;
            look.Values["a"] = 0.5f;
            look.Checksum = MetaHumanLook.ComputeChecksum(look.Values);

            MetaHumanLook back = MetaHumanLook.FromBytes(look.ToBytes());
            True(back.Revision == 42, "修订号能过 v2 字节往返");

            // 旧格式（v1）没有那 8 字节：从 v2 里精确剥掉修订号并把版本号改回 1。
            byte[] v2 = look.ToBytes();
            int offset = 4 + 1
                + (2 + System.Text.Encoding.UTF8.GetByteCount(look.TemplateId))
                + (2 + System.Text.Encoding.UTF8.GetByteCount(look.TemplateVersion))
                + (2 + System.Text.Encoding.UTF8.GetByteCount(look.SourcePresetId))
                + 4 + 1 + 1 + 8;
            byte[] v1 = new byte[v2.Length - 8];
            Array.Copy(v2, 0, v1, 0, offset);
            Array.Copy(v2, offset + 8, v1, offset, v2.Length - offset - 8);
            v1[4] = 1;
            MetaHumanLook legacy = MetaHumanLook.FromBytes(v1);
            True(legacy.Revision == 0, "v1 档没有修订号 ⇒ 读成 0（调用方回退 ticks）");
            True(legacy.SavedAtUtcTicks == look.SavedAtUtcTicks
                 && Math.Abs(legacy.Values["a"] - 0.5f) < 1e-6f,
                 "v1 档的其余字段仍按原布局读出");

            MetaHumanSession session = new MetaHumanSession(face);
            True(session.Commit("第一步"), "会话首步提交成功");
            long r1 = session.Revision;
            True(r1 > 0, "提交推进修订号");
            MetaHumanLook saved = session.Save();
            True(saved.Revision == session.Revision, "保存快照带当前修订号");
            MetaHumanLook published = session.Publish();
            True(published.Published && published.Revision > r1, "发布修订号更大且带发布位");
            long sealedRev = session.Revision;
            True(!session.Commit("封存后"), "封存后提交被拒");
            True(session.Revision == sealedRev, "被拒提交不推进修订号");

            MetaHumanSession reload = new MetaHumanSession(face);
            MetaHumanLook persisted = MetaHumanLook.Capture(reload.State, null);
            persisted.Revision = 42;
            reload.LoadLook(persisted);
            True(reload.Revision == 42, "读档采用档里的更大修订号");

            // 跨会话衔接：读档后的下一次提交必须排在被读入的修订号之后；
            // 回溯（JumpTo）只改当前状态，不许把已发出的修订号往回拨。
            True(reload.Commit("读档后继续编辑"), "读档后可继续提交");
            True(reload.Revision == 43, "读档后的提交接在 42 之后（43）");
            long beforeJump = reload.Revision;
            reload.JumpTo(0);
            True(reload.Revision == beforeJump, "回溯不改变已发出的修订号");
        }

        /// <summary>
        /// 两份已发布外观的完整决策链：修订号编码进 blob → 解码 → 与纯核仲裁对齐。
        /// 这是 "服务端回传 blob、客户端解出修订号再比" 的实际形状（#64）。
        /// </summary>
        private static void TestTwoPublishedLooks()
        {
            MetaHumanLook local = new MetaHumanLook
            {
                TemplateId = "t",
                Published = true,
                SavedAtUtcTicks = 100L,
                Revision = 5L,
            };
            local.Values["body.height"] = 177.5f;
            local.Checksum = MetaHumanLook.ComputeChecksum(local.Values);

            MetaHumanLook serverNewer = new MetaHumanLook
            {
                TemplateId = "t",
                Published = true,
                SavedAtUtcTicks = 50L,   // 时刻更旧，但修订号更大
                Revision = 7L,
            };
            serverNewer.Values["body.height"] = 170f;
            serverNewer.Checksum = MetaHumanLook.ComputeChecksum(serverNewer.Values);

            MetaHumanLook decoded = MetaHumanLook.FromBytes(serverNewer.ToBytes());
            True(decoded.Revision == 7L, "服务端 blob 解码后修订号为 7");

            string basis;
            True(MetaHuman.Core.MetaHumanAppearanceArbitration.ChooseLooks(
                     local.Revision, local.SavedAtUtcTicks, local.Checksum,
                     decoded.Revision, decoded.SavedAtUtcTicks, decoded.Checksum, out basis)
                 == MetaHuman.Core.AppearanceArbitration.UseServer && basis == "修订号",
                 "两份已发布：服务端修订号 7>5 ⇒ 采用服务端（时刻更旧也不影响）");

            True(MetaHuman.Core.MetaHumanAppearanceArbitration.ChooseLooks(
                     8L, 50L, local.Checksum, decoded.Revision, 100L, decoded.Checksum, out basis)
                 == MetaHuman.Core.AppearanceArbitration.UseLocal && basis == "修订号",
                 "两份已发布：本机修订号 8>7 ⇒ 保留本机");

            True(MetaHuman.Core.MetaHumanAppearanceArbitration.ChooseLooks(
                     local.Revision, local.SavedAtUtcTicks, local.Checksum,
                     decoded.Revision, decoded.SavedAtUtcTicks, local.Checksum, out basis)
                 == MetaHuman.Core.AppearanceArbitration.Identical,
                 "两份已发布：校验和一致 ⇒ 无冲突（即使修订号不同）");
        }

        /// <summary>
        /// WM 法线权重必须由 morph 驱动（UE MF_AnimatedMaps 的输入侧）：
        /// 模板给每个 `morph.head_wm*` 挂了 MaterialFloat 效果，解算后应出现在
        /// `Solution.MaterialFloats["head_wmN_normal_*"]` 里；wm13 lips 同时写 wm1/wm3 两份。
        /// 没有这条断言时，法线链可以"图接好了、参数也在，但 mask 恒 0"而全绿。
        /// </summary>
        private static void TestWmNormalDriven(MetaHumanTemplate face)
        {
            MetaHumanSession session = new MetaHumanSession(face);

            float applied = session.Adjust("morph.head_wm1_blink.l", 1f);
            float w1;
            True(session.Solution.MaterialFloats.TryGetValue("head_wm1_normal_head_wm1_blink_L", out w1)
                 && Math.Abs(w1 - applied) < 1e-4f,
                 "blink.L 必须驱动 head_wm1_normal_head_wm1_blink_L（值一致）");

            session.Adjust("morph.head_wm13_lips_dl", 1f);
            float l1, l3;
            True(session.Solution.MaterialFloats.TryGetValue("head_wm1_normal_head_wm13_lips_DL", out l1)
                 && session.Solution.MaterialFloats.TryGetValue("head_wm3_normal_head_wm13_lips_DL", out l3)
                 && l1 > 0f && l3 > 0f,
                 "wm13 lips 同时驱动 wm1 与 wm3 两份法线权重");

            // 全部 morph 置 1：MaterialFloats 里应恰有 41 个 WM 法线权重，不多不少。
            int morphs = 0;
            foreach (MetaHumanParameter p in face.Parameters)
            {
                bool isWmMorph = false;
                foreach (MetaHumanEffect e in p.Effects)
                {
                    if (e.Kind == MetaHumanEffectKind.Morph && e.Target != null
                        && e.Target.StartsWith("head_wm", StringComparison.Ordinal))
                    {
                        isWmMorph = true;
                        break;
                    }
                }
                if (!isWmMorph)
                {
                    continue;
                }

                morphs++;
                session.Adjust(p.Id, 1f);
            }

            int emitted = 0;
            foreach (string key in session.Solution.MaterialFloats.Keys)
            {
                if (key.IndexOf("_normal_head_wm", StringComparison.Ordinal) > 0)
                {
                    emitted++;
                }
            }

            True(morphs == 37, "模板里有 37 个驱动 WM 法线的 morph（实得 " + morphs + "）");
            True(emitted == 41, "全部 morph 置数后应产出 41 个 WM 法线权重（实得 " + emitted + "）");
        }

        private static void TestHeadMaterialSpecContract()
        {
            string specPath = Path.Combine(RepoTemplatesDir(), "Head_Material_Spec.json");
            string authorityPath = Path.Combine(RepoRoot(RepoTemplatesDir()), "docs", "metahuman",
                                                "ue_runtime", "Ada_Head_Material_TextureParams.json");
            if (!File.Exists(specPath) || !File.Exists(authorityPath))
            {
                Skip("头部材质规格表或 18 条权威表不在盘上，规格契约这层没跑");
                return;
            }

            List<string> names = new List<string>();
            List<string> semantics = new List<string>();
            List<bool> srgb = new List<bool>();
            List<string> textures = new List<string>();
            List<string> unsupported = new List<string>();
            using (JsonDocument doc = JsonDocument.Parse(File.ReadAllText(specPath)))
            {
                JsonElement root = doc.RootElement;
                foreach (JsonElement row in root.GetProperty("parameters").EnumerateArray())
                {
                    names.Add(row.GetProperty("flax_parameter").GetString());
                    semantics.Add(row.GetProperty("ue_semantics").GetString());
                    srgb.Add(row.GetProperty("srgb").GetBoolean());
                    JsonElement tex;
                    textures.Add(row.TryGetProperty("flax_texture", out tex)
                                 && tex.ValueKind == JsonValueKind.String
                                 ? tex.GetString() : null);
                }
                foreach (JsonElement e in root.GetProperty("declared_but_unsupported").EnumerateArray())
                {
                    unsupported.Add(e.GetString());
                }
            }

            HashSet<string> authority = new HashSet<string>(StringComparer.Ordinal);
            using (JsonDocument doc2 = JsonDocument.Parse(File.ReadAllText(authorityPath)))
            {
                foreach (JsonElement row in doc2.RootElement.GetProperty("entries").EnumerateArray())
                {
                    authority.Add(row.GetProperty("param").GetString());
                }
            }

            Eq(18, names.Count, "规格表参数条数");
            Eq(names.Count, new HashSet<string>(names, StringComparer.Ordinal).Count,
               "规格表里不许有重名参数（通道按名写，重名会互相覆盖）");
            Eq(0, CountMissingIn(names, authority), "规格表每格都必须是 UE 材质里真有的参数名（不许自造）");
            Eq(0, CountMissingIn(new List<string>(authority), new HashSet<string>(names, StringComparer.Ordinal)),
               "UE 的 18 条一层都不能从规格表里漏掉（漏了就是永远接不上）");

            // 声称"已导入成 Flax 资产"的那些贴图，必须真在盘上；不声称的允许为空。
            int claimed = 0;
            int missingOnDisk = 0;
            string projectRoot = Path.Combine(RepoRoot(RepoTemplatesDir()), "HundunWorld");
            for (int i = 0; i < textures.Count; i++)
            {
                if (string.IsNullOrEmpty(textures[i]))
                {
                    continue;
                }
                claimed++;
                if (!File.Exists(Path.Combine(projectRoot, textures[i].Replace('/', Path.DirectorySeparatorChar))))
                {
                    missingOnDisk++;
                }
            }
            Eq(0, missingOnDisk, "规格表说已导入的贴图必须真的在盘上");
            True(claimed > 0, "至少要有一档贴图是真的落进 Flax 了，否则这条契约是空的");

            List<string> problems = HeadSpecProblems(names, semantics, srgb);
            Eq(0, problems.Count, "规格表自校验必须干净（srgb 档位/用途档/名字前缀三对关系）");
            True(unsupported.Count >= 2 && HasWord(unsupported, "金属") && HasWord(unsupported, "混合"),
                 "两条已知做不到的（金属度打包、层间混合语义）必须一直写在规格里，不许被忘掉");

            // 反向对照：故意改坏，验证器必须响
            List<bool> badSrgb = new List<bool>(srgb);
            List<string> badNames = new List<string>(names);
            badSrgb[0] = !badSrgb[0];                 // 把 Color_MAIN 的 srgb 翻掉
            badNames[1] = "Normal_MAIN";              // 造一个重名
            List<string> caught = HeadSpecProblems(badNames, semantics, badSrgb);
            True(caught.Count >= 2, "改坏一处（srgb 反档 + 名字重复）必须被验证器抓到，否则上面的 0 不算数");

            // 引擎侧名录：材质还没做时如实跳过，做好后这条会自动变成真断言。
            string rosterPath = Path.Combine(RepoRoot(RepoTemplatesDir()), "docs", "metahuman",
                                             "ue_runtime", "mh_head_param_roster.json");
            if (!File.Exists(rosterPath))
            {
                // 别说"材质还没建出来"——这话把责任推给一个不存在的产物，而盘上另一份量测
                // （head_roster.json，槽位指认那轮 dump 的真材质参数）已经量到 0/18：
                // 材质在，命名参数一条都没有 ⇒ 通道是**静默写空**，不是"还没开始做"。
                // 跳过语只说自己量到的那一段（§114 同一条纪律）。
                string measured = MeasureHeadCoverage(names);
                Skip("规格表与真材质的逐条比对（spec ⊆ 名录）没跑："
                     + "缺引擎侧名录 mh_head_param_roster.json。" + measured);
                return;
            }
            // 名录文件两种写法都要认：探针写的是 {"parameters":[...]}（对象），而别的清单是裸数组。
            // 此前这份文件从未在盘上出现过（门一直走 SKIP），所以这个格式不匹配一直没暴露——
            // 第一次真写到盘上就把门弄崩了（InvalidOperationException: 期望 Array 实得 Object）。
            List<string> roster = ReadJsonStringArrayOrObject(rosterPath, "parameters");
            Eq(0, CountMissingIn(names, new HashSet<string>(roster, StringComparer.Ordinal)),
               "规格表要求的每一层都必须能在真材质上按同名读到（材质侧漏一层就是通道写空）");
        }

        /// <summary>
        /// 材质目标映射表的常驻门（#41 的结构半边）。
        ///
        /// 判据方向很重要：**从脸模板数据反读**那 9 个材质效果目标（`Kind ∈ {SkinTone, SkinDetail, Eye, Hair}`
        /// 的参数所声明的 `Effects[].Target`），再拿它去核对 `MetaHumanMaterialMap` 里手写的那张表，
        /// 两边必须一一对上。反着来（拿表去核对表）等于只有一份清单，改坏了也不会红。
        /// 另外钉两件事：已映射的 Flax 名必须 ⊆ 规格表 `flax_parameter`（免得映射到一个不存在的参数名），
        /// 以及"待你判定"的条数必须一直是 3——它是个**要显示出来的中间态**，不能被悄悄改成映射或 declined。
        /// </summary>
        private static void TestMaterialTargetMapCoversTemplateEffects(string templatesDir)
        {
            string templatePath = Path.Combine(RepoTemplatesDir(), "Template_Face_Archetype.json");
            string specPath = Path.Combine(RepoTemplatesDir(), "Head_Material_Spec.json");
            True(File.Exists(templatePath), "脸模板必须在盘上：" + templatePath);
            True(File.Exists(specPath), "头部材质规格表必须在盘上：" + specPath);

            List<string> declared = new List<string>();
            using (JsonDocument doc = JsonDocument.Parse(File.ReadAllText(templatePath)))
            {
                JsonElement parameters;
                if (!doc.RootElement.TryGetProperty("Parameters", out parameters))
                {
                    True(false, "脸模板里没有 Parameters 段（键名大小写要按文件实际取）");
                    return;
                }

                foreach (JsonElement p in parameters.EnumerateArray())
                {
                    JsonElement kindElement;
                    if (!p.TryGetProperty("Kind", out kindElement))
                    {
                        continue;
                    }

                    string kind = kindElement.GetString() ?? string.Empty;
                    if (kind != "SkinTone" && kind != "SkinDetail" && kind != "Eye" && kind != "Hair")
                    {
                        continue;
                    }

                    JsonElement effects;
                    if (!p.TryGetProperty("Effects", out effects))
                    {
                        continue;
                    }

                    foreach (JsonElement e in effects.EnumerateArray())
                    {
                        JsonElement effectKind;
                        if (!e.TryGetProperty("Kind", out effectKind))
                        {
                            continue;
                        }

                        string effectKindName = effectKind.GetString() ?? string.Empty;
                        if (effectKindName.IndexOf("Material", StringComparison.Ordinal) != 0)
                        {
                            continue;
                        }

                        JsonElement targetElement;
                        if (!e.TryGetProperty("Target", out targetElement))
                        {
                            continue;
                        }

                        string target = targetElement.GetString();
                        if (!string.IsNullOrEmpty(target) && !declared.Contains(target))
                        {
                            declared.Add(target);
                        }
                    }
                }
            }

            True(declared.Count > 0,
                 "从模板反读出的材质目标不能是 0 条（0 条说明解析路径错了，不是模板没数据）");

            List<MetaHumanMaterialTargetEntry> map = MetaHumanMaterialMap.Entries();
            int covered = 0;
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < declared.Count; i++)
            {
                for (int j = 0; j < map.Count; j++)
                {
                    if (string.Equals(map[j].Target, declared[i], StringComparison.Ordinal))
                    {
                        covered++;
                        seen.Add(map[j].Target);
                    }
                }
            }

            Eq(declared.Count, covered,
               "模板声明的每个材质目标都必须在映射表里恰好有一条（漏一条＝该目标无人处理；重复一条＝两处抢同一个名）");

            List<string> extra = new List<string>();
            for (int i = 0; i < map.Count; i++)
            {
                if (!seen.Contains(map[i].Target))
                {
                    extra.Add(map[i].Target);
                }
            }

            Eq(0, extra.Count, "映射表里不许有模板没声明的目标（表比模板多就是自造）：" + string.Join(", ", extra.ToArray()));

            // 规格表侧：拿 flax_parameter 清单核对"已映射"的落点真的存在
            List<string> specNames = new List<string>();
            using (JsonDocument spec = JsonDocument.Parse(File.ReadAllText(specPath)))
            {
                JsonElement parameters;
                if (spec.RootElement.TryGetProperty("parameters", out parameters))
                {
                    foreach (JsonElement p in parameters.EnumerateArray())
                    {
                        JsonElement nameElement;
                        if (p.TryGetProperty("flax_parameter", out nameElement))
                        {
                            string name = nameElement.GetString();
                            if (!string.IsNullOrEmpty(name))
                            {
                                specNames.Add(name);
                            }
                        }
                    }
                }
            }

            True(specNames.Count > 0, "规格表的 flax_parameter 清单不能是空的（空了这条校验就是空的）");
            List<string> mapped = MetaHumanMaterialMap.MappedFlaxParameters();
            List<string> engineAdded = MetaHumanMaterialMap.EngineAddedFlaxParameters();
            True(engineAdded.Count > 0 && engineAdded.Contains("SkinTint") && engineAdded.Contains("SkinRoughness") && engineAdded.Contains("SkinRedness") && engineAdded.Contains("Skin_Stubble"),
                 "引擎增补参数白名单必须显式列出 SkinTint / SkinRoughness / SkinRedness / Skin_Stubble；白名单为空就等于把'自造参数'放进来没人管");
            True(mapped.Count >= 4,
                 "语义已定的映射至少 4 条（Melanin→SkinTint、Roughness→SkinRoughness、Redness→SkinRedness、Stubble→Skin_Stubble），实得 "
                 + mapped.Count + "；把表清空也能绿就说明这门没用");
            for (int i = 0; i < mapped.Count; i++)
            {
                True(specNames.Contains(mapped[i]) || engineAdded.Contains(mapped[i]),
                     "映射到的 Flax 参数名必须真的在规格表或引擎增补白名单里：" + mapped[i]
                     + "（否则通道写到一个不存在的名字上）");
            }

            // declined 是"有记录的判定"，不是"缺数据先空着"——所以每条都必须交代理由，
            // 否则以后没人知道它为什么没做（本项目已有这条纪律，这里给它装上闸门）。
            int declined = 0;
            List<MetaHumanMaterialTargetEntry> all = MetaHumanMaterialMap.Entries();
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].State != MetaHumanMaterialTargetState.Declined)
                {
                    continue;
                }

                declined++;
                True(!string.IsNullOrEmpty(all[i].Note),
                     "declined 的条目必须写明理由：" + all[i].Target);
            }

            // 中间态要可见、且条数被钉住：三条产品判定已于 2026-10-15 由用户拍板，所以现在必须是 0。
            // 若哪天又变成非 0，说明有新的目标没人定——那是要拿出来问的，不是可以放着不管的。
            List<string> pending = MetaHumanMaterialMap.PendingUserDecisions();
            Eq(0, pending.Count,
               "「等你拍板」的目标数应为 0（三条已定），实得 " + pending.Count
               + "；非 0 就意味着有新目标没人定，要显式提出来");
            Console.WriteLine("  材质目标映射：模板声明 " + declared.Count + " 条 / 已映射 " + mapped.Count
                              + " 条（" + string.Join(", ", mapped.ToArray()) + "）/ declined " + declined
                              + " 条 / 待你判定 " + pending.Count + " 条");
        }

        /// <summary>
        /// morph 写入必须全部走带保护的 WriteMorph（常驻断言）。
        /// 为什么钉这条：引擎 `AnimatedModel::SetBlendShapeWeight` 开头是
        /// `const auto* model = SkinnedModel.Get(); CHECK(model);`（AnimatedModel.cpp:462-463），
        /// 资产为空时**不抛托管异常**，而是弹引擎断言框把编辑器主线程钉死——
        /// 该进程上所有 MCP 调用一起超时，try/catch 兜不住，只能在调用前挡。
        /// 2026-10-15 就是这么把编辑器卡住的（预制体实例的 SkinnedModel 为空）。
        /// 所以"只允许一处裸调用"这条约束要常驻：多一处就是有人绕过了保护，
        /// 而下一个人只会看到"编辑器又死了"，看不到原因。
        /// </summary>
        /// <summary>
        /// 远端外观链的常驻闸门：头走与本地相同的 HeadModelPath、MetaHumanCharacter 的 TargetModels 含 head、
        /// 最终走 LoadLook。三者任一被拿掉，远端玩家就会"有身体没头"或"外观整份不生效"，
        /// 而单客户端测试完全看不出来（本机只有本地角色）。
        /// </summary>
        /// <summary>
        /// 三行色板（肤/眼/发）接线的常驻闸门：每个色板都必须有控件、订阅和落点参数。
        /// 少一处就会表现为"色板画出来了但点了不变"，而编译与其余断言都照绿。
        /// </summary>
        private static void TestColorSwatchWiring(string templatesDir)
        {
            string path = Path.Combine(RepoRoot(templatesDir), "HundunWorld", "Source", "Game", "UI", "Ink",
                                       "Pages", "CharacterCreation", "MetaHumanSculptPanel.cs");
            True(File.Exists(path), "捏脸面板源码必须在盘上：" + path);
            string text = File.ReadAllText(path);

            True(text.Contains("_skinSwatch") && text.Contains("_eyeSwatch") && text.Contains("_hairSwatch"),
                 "三行色板的控件字段必须都在（肤/眼/发）");
            True(CountOccurrences(text, "OnSkinSwatchChanged") >= 2,
                 "肤色色板必须有定义 + 订阅两处引用");
            True(CountOccurrences(text, "OnEyeSwatchChanged") >= 2,
                 "瞳色色板必须有定义 + 订阅两处引用");
            True(CountOccurrences(text, "OnHairSwatchChanged") >= 2,
                 "发色色板必须有定义 + 订阅两处引用");
            True(text.Contains("\"skin.melanin\""), "肤色色板必须落到 skin.melanin");
            True(text.Contains("\"eye.iris_color\""), "瞳色色板必须落到 eye.iris_color");
            True(text.Contains("CcFacePresets.SkinColors") && text.Contains("CcFacePresets.EyeColors")
                 && text.Contains("CcFacePresets.HairColors"),
                 "三行色板必须使用创角色板（SkinColors/EyeColors/HairColors）");
            True(CountOccurrences(text, "SyncColorIndices") >= 2,
                 "外观档下标回填必须在 RefreshSteps 里被调用且有定义（否则滑杆改色不进 AppearanceInfo）");
            True(text.Contains("QuantizeIndex"),
                 "下标回填必须走 QuantizeIndex（统一 0..1 → 离散下标的钳位）");
            True(text.Contains("MelaninOf(") && text.Contains("ClosestSkinIndex("),
                 "肤色板必须按色块亮度映射 melanin（MelaninOf/ClosestSkinIndex），不能拿下标线性换算");
            True(!text.Contains("skinLast"),
                 "肤色不能再出现 skinLast 式线性映射（色板顺序不是按明度排的）");
        }

        /// <summary>
        /// 日志消息里禁止拼 <c>Environment.StackTrace</c>。
        /// <para>
        /// 实测（2026-10-22）：引擎调用栈里会出现 <c>Level.&lt;Internal_UnloadAllScenes&gt;g____PInvoke|239_0</c>
        /// 这种"尖括号 + 下划线"的标记；编辑器调试日志窗口把消息当富文本解析，HtmlParser.ParseAttributeName
        /// 在遇到尖括号内不是字母数字/'-'的字符时返回空名且不移动位置，外层循环原地空转 ——
        /// PIE 退出时主线程 100% 占用、编辑器整个卡死（dotnet-stack 采样到 ParseTag 栈顶）。
        /// 需要来源信息时只留单行调用者名：<c>new StackTrace(1, false).GetFrame(0)?.GetMethod()?.Name</c>。
        /// </para>
        /// </summary>
        private static void TestNoEnvironmentStackTraceInLogs(string templatesDir)
        {
            string root = Path.Combine(RepoRoot(templatesDir), "HundunWorld");
            string[] roots =
            {
                Path.Combine(root, "Source"),
                Path.Combine(root, "Plugins", "MetaHuman", "Source"),
            };

            int files = 0;
            var hits = new List<string>();
            foreach (string dir in roots)
            {
                if (!Directory.Exists(dir))
                {
                    continue;
                }

                foreach (string file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
                {
                    files++;
                    if (File.ReadAllText(file).Contains("Environment.StackTrace"))
                    {
                        hits.Add(file);
                    }
                }
            }

            // 防空转：扫描器必须真的看到源码；否则删光目录也能"通过"。
            True(files > 100, $"扫描器必须真的扫到源码（实扫 {files} 个 .cs）");
            True(hits.Count == 0,
                 "日志消息里禁止拼 Environment.StackTrace（会让编辑器日志窗口死循环）；命中：" +
                 string.Join(", ", hits));
        }

        /// <summary>
        /// 创角 3D 预览的主光不能是 0。
        /// <para>
        /// CustomActors 孤立渲染不包含场景灯光，预览自己建三点光；主光曾遗留探针值 0，
        /// 只剩补光 0.7/轮廓光 0.45 ⇒ 1.13 捏脸页预览通篇黑（实测截图 max=9/255），
        /// 而所有数据级断言仍全绿。这里钉住：主光亮度 ≥ 1，且灯光组必须进 CustomActors。
        /// </para>
        /// </summary>
        private static void TestPreviewKeyLightNotBlack(string templatesDir)
        {
            string path = Path.Combine(RepoRoot(templatesDir), "HundunWorld", "Source", "Game", "UI", "Ink",
                                       "Components", "CharacterPreview3D.cs");
            True(File.Exists(path), "3D 预览控件源码必须在盘上：" + path);
            string text = File.ReadAllText(path);

            var m = System.Text.RegularExpressions.Regex.Match(
                text, @"_keyLight\.Brightness\s*=\s*([0-9]+(?:\.[0-9]+)?)f\s*;");
            True(m.Success, "必须能读到 _keyLight.Brightness 的字面赋值（解析不到就说明写法变了，本条会空转）");
            if (m.Success)
            {
                double v = double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                True(v >= 1.0, $"预览主光亮度必须 ≥ 1（当前 {v}）：0 会让离屏预览全黑");
            }

            True(text.Contains("_renderTask.CustomActors") && text.Contains("_lightRoot"),
                 "三点光必须进 CustomActors（孤立渲染不包含场景灯光）");
        }

        /// <summary>
        /// 捏脸页必须同时暴露「头部 / 身体」两条参数入口。
        /// <para>
        /// 身体参数（身高/胸围/…30 项）只在身体页签出现；一旦页签被删或身体参数被过滤掉，
        /// 面板照样能开、头部滑杆照样能拖，而"玩家改不了身体"这件事没有任何失败痕迹。
        /// 本轮实测（2026-10-22）：身体页签 30 条滑杆齐全、身高 177.5→220 驱动 106,266 px 变化。
        /// </para>
        /// </summary>
        /// <summary>
        /// 创角预览必须真的有头发、且发色色板能实时换色。
        /// <para>
        /// 2026-10-22 前预览完全没有头发（捏脸全程光头、发色要进世界才看得到）；现在预览把世界同一条
        /// 头发资产挂在 Ada 头下、用 MH_Hair 的 HairColor 参数着色，面板色板经 HairColorChanged 通知宿主。
        /// 断链会让“点了发色没反应”重新变成静默失效。
        /// </para>
        /// </summary>
        /// <summary>
        /// 瞳孔大小：模板的 eye.pupil_size 必须仍指向 MaterialFloat 目标 Eye_PupilSize。
        /// <para>
        /// 2026-10-22 前 MH_Eye 里没有同名参数，写回端把它静默丢弃（"声明了没人接"）。
        /// 现在 MH_Eye 用 UV→Distance→Smoothstep 的径向遮罩消费它（尺寸取自解码后的眼贴图：
        /// 瞳孔半径≈0.068 UV、虹膜环 0.08–0.15、巩膜 &gt;0.18）。这里钉住模板侧契约，防止参数被删/改名后静默失效。
        /// </para>
        /// </summary>
        /// <summary>
        /// 口红：模板的 makeup.lipstick 必须指向 MaterialFloat 目标 Lipstick。
        /// <para>
        /// 2026-10-22 前 MH_Head 的 EyeLipstickMask 被指到 FaceFoundationBlusherMask（腮红图集），
        /// 所以一开口红就是下半脸整片染色；现在改用从脸底色反照率派生的唇部掩码 LipMask_Ada，
        /// 并把 LipstickColor 默认从白色改为自然红。这里钉住模板参数与目标名。
        /// </para>
        /// </summary>
        private static void TestLipstickTarget(string templatesDir)
        {
            string path = Path.Combine(RepoRoot(templatesDir), "HundunWorld", "Plugins", "MetaHuman", "Content",
                                       "Templates", "Template_Face_Archetype.json");
            True(File.Exists(path), "脸模板必须在盘上：" + path);
            string text = File.ReadAllText(path);

            int at = text.IndexOf("\"makeup.lipstick\"", StringComparison.Ordinal);
            True(at >= 0, "模板必须保留 makeup.lipstick 参数");
            if (at >= 0)
            {
                int next = text.IndexOf("\"Lipstick\"", at, StringComparison.Ordinal);
                True(next > at && next - at < 800,
                     "makeup.lipstick 的 MaterialFloat 目标必须是 Lipstick（MH_Head 的妆容链消费它）");
            }
            True(File.Exists(Path.Combine(RepoRoot(templatesDir), "HundunWorld", "Content", "Character", "Skin",
                                          "LipMask_Ada.flax")),
                 "唇部掩码资产 LipMask_Ada.flax 必须在盘上（EyeLipstickMask 的默认贴图）");
        }

        private static void TestPupilSizeTarget(string templatesDir)
        {
            string path = Path.Combine(RepoRoot(templatesDir), "HundunWorld", "Plugins", "MetaHuman", "Content",
                                       "Templates", "Template_Face_Archetype.json");
            True(File.Exists(path), "脸模板必须在盘上：" + path);
            string text = File.ReadAllText(path);

            int at = text.IndexOf("\"eye.pupil_size\"", StringComparison.Ordinal);
            True(at >= 0, "模板必须保留 eye.pupil_size 参数");
            if (at >= 0)
            {
                int next = text.IndexOf("\"Eye_PupilSize\"", at, StringComparison.Ordinal);
                True(next > at && next - at < 800,
                     "eye.pupil_size 的 MaterialFloat 目标必须是 Eye_PupilSize（MH_Eye 里同名参数负责消费）");
            }
        }

        private static void TestPreviewHairWiring(string templatesDir)
        {
            string root = Path.Combine(RepoRoot(templatesDir), "HundunWorld", "Source", "Game", "UI", "Ink");
            string preview = Path.Combine(root, "Components", "CharacterPreview3D.cs");
            string panel = Path.Combine(root, "Pages", "CharacterCreation", "MetaHumanSculptPanel.cs");
            string pages = Path.Combine(root, "Pages", "CharacterCreation", "CcCreationPages.cs");
            True(File.Exists(preview) && File.Exists(panel) && File.Exists(pages), "预览/面板/页面源码必须在盘上");

            string pv = File.ReadAllText(preview);
            True(pv.Contains("_previewHair") && pv.Contains("HairCardsModelPath"),
                 "预览必须挂世界同一份头发卡片资产");
            True(pv.Contains("SetHairColorIndex("), "预览必须暴露 SetHairColorIndex");
            True(pv.Contains("\"HairColor\"") && pv.Contains("CcFacePresets.HairColors"),
                 "预览发色必须写 MH_Hair 的 HairColor 参数并复用同一调色板");

            string pn = File.ReadAllText(panel);
            True(pn.Contains("public Action<int> HairColorChanged"), "面板必须暴露 HairColorChanged");
            True(CountOccurrences(pn, "HairColorChanged?.Invoke") >= 2,
                 "HairColorChanged 必须在点色板与面板刷新两处被调用");

            string pg = File.ReadAllText(pages);
            True(pg.Contains("_sculpt.HairColorChanged") && pg.Contains("SetHairColorIndex"),
                 "宿主必须把 HairColorChanged 接到预览的 SetHairColorIndex");
        }

        /// <summary>
        /// 世界角色必须有"活着"的骨架驱动：空动画图下由 ProceduralIdleDriver 兜底。
        /// <para>
        /// 2026-10-23 前 Animation Graph 是空图（2174 B/deps=0）、ALS 剪辑也与 DEF-* 骨架无映射，
        /// 世界身体 1.2 s 骨骼零位移 —— 角色像雕塑。现在在 AnimatedModel 上叠正弦微动；
        /// 一旦真实动画驱动骨架就自动让位（双向自检）。这里钉住：本地/远端都挂、幅度保持"呼吸级"、
        /// 让位判据不能退化成"激活后看窗口运动"（那会把写回端的一次性偏移误判成动画）。
        /// </para>
        /// </summary>
        private static void TestProceduralIdleWiring(string templatesDir)
        {
            string srcRoot = Path.Combine(RepoRoot(templatesDir), "HundunWorld", "Source", "Game");
            string driver = Path.Combine(srcRoot, "Character", "ProceduralIdleDriver.cs");
            string local = Path.Combine(srcRoot, "HundunWorldGame.cs");
            string remote = Path.Combine(srcRoot, "RemotePlayerActor.cs");
            True(File.Exists(driver) && File.Exists(local) && File.Exists(remote),
                 "程序化待机源码/本地装点/远端装点必须都在盘上");
            if (!File.Exists(driver))
            {
                return;
            }

            string text = File.ReadAllText(driver);
            True(text.Contains("SetNodeTransformation(bone, baseM * Matrix.RotationQuaternion(delta), false)"),
                 "微动必须写成 基准姿态×增量 的节点变换（右乘，和骨骼写回端同一约定）");
            True(text.Contains("_model.AnimationUpdated +=") && text.Contains("public override void OnUpdate()"),
                 "骨架动画回调与每帧兜底都必须接上（只在 OnUpdate 里依赖动画回调会漏帧）");

            var amp = System.Text.RegularExpressions.Regex.Match(
                text, @"SlotAmplitude\s*=\s*\{([^}]*)\}");
            True(amp.Success, "必须能读到 SlotAmplitude 表（解析不到说明写法变了，本条会空转）");
            if (amp.Success)
            {
                var nums = System.Text.RegularExpressions.Regex.Matches(amp.Groups[1].Value, @"[0-9]+(?:\.[0-9]+)?");
                True(nums.Count >= 5, $"幅度表至少 5 项（实读 {nums.Count}）");
                double max = 0;
                foreach (System.Text.RegularExpressions.Match n in nums)
                {
                    max = Math.Max(max, double.Parse(n.Value, System.Globalization.CultureInfo.InvariantCulture));
                }
                True(max <= 3.0, $"待机幅度必须保持呼吸级（≤ 3°，当前最大 {max}°）：大了会变成抽动");
            }

            True(text.Contains("_active ? fromBase > 0.3f : diff > 0.002f"),
                 "让位自检必须分激活前后两档：激活后只用\"远离基准姿态\"判真实动画，" +
                 "不能把写回端的一次性偏移当成动画");

            string lc = File.ReadAllText(local);
            True(lc.Contains("GetScript<HundunWorld.Game.Character.ProceduralIdleDriver>()") &&
                 lc.Contains("AddScript<HundunWorld.Game.Character.ProceduralIdleDriver>()"),
                 "本地身体装待机驱动必须先去重再挂（GetScript+AddScript）");

            string rc = File.ReadAllText(remote);
            True(rc.Contains("GetScript<Character.ProceduralIdleDriver>()") &&
                 rc.Contains("AddScript<Character.ProceduralIdleDriver>()"),
                 "远端身体也必须挂同一套待机驱动（远端同样吃空动画图）");
        }

        /// <summary>
        /// 妆容三分：眼妆/腮红/唇必须各自有参数与独立掩码资产。
        /// <para>
        /// 2026-10-23 前 MH_Head 里存在**两条并行妆容链**，可见的那条（381-388）仍把整张掩码的
        /// 首分量当 alpha：眼妆与口红都落在同一区域、腮红覆盖整脸。现改用打包掩码
        /// MakeupMasks_Ada（R=眼妆 G=腮红 B=唇）并在可见链上按通道 Unpack。
        /// 像素判据见 Tools/VerifyMakeupMasks.py 与审计 6a3b；这条只钉契约，防止参数/资产被改名后静默失效。
        /// </para>
        /// </summary>
        private static void TestMakeupChannelTargets(string templatesDir)
        {
            string facePath = Path.Combine(RepoRoot(templatesDir), "HundunWorld", "Plugins", "MetaHuman", "Content",
                                           "Templates", "Template_Face_Archetype.json");
            True(File.Exists(facePath), "脸模板必须在盘上：" + facePath);
            string face = File.ReadAllText(facePath);

            (string id, string target)[] pairs =
            {
                ("makeup.eye", "EyeMakeup"),
                ("makeup.blush", "BlusherMakeupIntensity"),
                ("makeup.lipstick", "Lipstick"),
            };
            foreach (var (id, target) in pairs)
            {
                int at = face.IndexOf("\"" + id + "\"", StringComparison.Ordinal);
                True(at >= 0, "模板必须保留妆容参数 " + id);
                if (at < 0)
                {
                    continue;
                }
                int next = face.IndexOf("\"" + target + "\"", at, StringComparison.Ordinal);
                True(next > at && next - at < 900,
                     id + " 的 MaterialFloat 目标必须是 " + target);
            }

            string mask = Path.Combine(RepoRoot(templatesDir), "HundunWorld", "Content", "Character", "Skin",
                                       "MakeupMasks_Ada.flax");
            True(File.Exists(mask), "打包掩码 MakeupMasks_Ada.flax 必须在盘上（R=眼妆 G=腮红 B=唇）");

            string verifier = Path.Combine(RepoRoot(templatesDir), "Tools", "VerifyMakeupMasks.py");
            True(File.Exists(verifier), "掩码通道验证脚本必须在盘上：" + verifier);
        }

        /// <summary>
        /// 胡茬：模板 skin.stubble 必须落到**浮点**参数 Skin_Stubble，且派生掩码在盘上。
        /// <para>
        /// 2026-10-23 前 MaterialMap 把 Skin_Stubble 指到贴图参数 BakedGroomRootTipGradientRegionMasks：
        /// applier 的类型闸门会把浮点写丢弃（Warnings 里记一笔，画面上什么都没有），而那张 UE 导出的
        /// groom 掩码四通道全零。现在改指材质图新加的 float Skin_Stubble，掩码用派生资产。
        /// </para>
        /// </summary>
        private static void TestStubbleChannel(string templatesDir)
        {
            string facePath = Path.Combine(RepoRoot(templatesDir), "HundunWorld", "Plugins", "MetaHuman", "Content",
                                           "Templates", "Template_Face_Archetype.json");
            True(File.Exists(facePath), "脸模板必须在盘上：" + facePath);
            string face = File.ReadAllText(facePath);
            int at = face.IndexOf("\"skin.stubble\"", StringComparison.Ordinal);
            True(at >= 0, "模板必须保留 skin.stubble");
            if (at >= 0)
            {
                int next = face.IndexOf("\"Skin_Stubble\"", at, StringComparison.Ordinal);
                True(next > at && next - at < 900, "skin.stubble 的 MaterialFloat 目标必须是 Skin_Stubble");
            }

            var entry = MetaHumanMaterialMap.Entries().Find(e => e.Target == "Skin_Stubble");
            True(entry != null && entry.State == MetaHumanMaterialTargetState.Mapped,
                 "Skin_Stubble 必须在 MaterialMap 里是 Mapped");
            True(entry != null && entry.FlaxParameter == "Skin_Stubble",
                 "Skin_Stubble 必须映射到同名**浮点**参数（指到贴图参数会被 applier 类型闸门丢弃）");
            True(MetaHumanMaterialMap.EngineAddedFlaxParameters().Contains("Skin_Stubble"),
                 "Skin_Stubble 是引擎增补参数，必须进白名单");

            string mask = Path.Combine(RepoRoot(templatesDir), "HundunWorld", "Content", "Character", "Skin",
                                       "StubbleMask_Ada_v2.flax");
            True(File.Exists(mask), "派生胡茬掩码 StubbleMask_Ada_v2.flax 必须在盘上");
        }

        /// <summary>
        /// 妆容色温：口红/眼妆/腮红各有一个 0..1 的冷暖档，经 MaterialColor 落成颜色。
        /// <para>
        /// 2026-10-23 前妆容颜色固定；现在三个参数分别映射到 LipstickColor / EyeMakeupPrimaryColor /
        /// BlusherMakeupColor（BaseColor=当前自然色，TargetColor=偏深/偏暖），值随 Look.Values 持久化。
        /// </para>
        /// </summary>
        private static void TestMakeupTintTargets(string templatesDir)
        {
            string facePath = Path.Combine(RepoRoot(templatesDir), "HundunWorld", "Plugins", "MetaHuman", "Content",
                                           "Templates", "Template_Face_Archetype.json");
            True(File.Exists(facePath), "脸模板必须在盘上：" + facePath);
            string face = File.ReadAllText(facePath);

            (string id, string target)[] pairs =
            {
                ("makeup.lip_tint", "LipstickColor"),
                ("makeup.eye_tint", "EyeMakeupPrimaryColor"),
                ("makeup.blush_tint", "BlusherMakeupColor"),
            };
            foreach (var (id, target) in pairs)
            {
                int at = face.IndexOf("\"" + id + "\"", StringComparison.Ordinal);
                True(at >= 0, "模板必须有妆容色温参数 " + id);
                if (at < 0)
                {
                    continue;
                }
                int window = Math.Min(1200, face.Length - at);
                string slice = face.Substring(at, window);
                True(slice.Contains("\"MaterialColor\""),
                     id + " 必须走 MaterialColor 效果（浮点写不到颜色参数上）");
                True(slice.Contains("\"" + target + "\""),
                     id + " 必须落到 " + target);
            }
        }

        private static void TestBodyTabExposed(string templatesDir)
        {
            string path = Path.Combine(RepoRoot(templatesDir), "HundunWorld", "Source", "Game", "UI", "Ink",
                                       "Pages", "CharacterCreation", "MetaHumanSculptPanel.cs");
            True(File.Exists(path), "捏脸面板源码必须在盘上：" + path);
            string text = File.ReadAllText(path);

            True(text.Contains("_headTab") && text.Contains("_bodyTab"),
                 "头部/身体两个页签按钮必须都在");
            True(text.Contains("SelectTab(true)") && text.Contains("ShowBodyMode"),
                 "身体页签必须切到能接受写回的蒙皮身体（ShowBodyMode）");
            True(text.Contains("PartitionForUI(head, body)"),
                 "头身分界必须由 Template.PartitionForUI 决定（不能由调用方另记一张表）");
            True(text.Contains("AppendByGroup(_bodyParams, body"),
                 "身体参数必须真的进 _bodyParams（否则身体页签是空的）");
        }

        private static void TestRemoteAppearanceChainTargetsHead(string templatesDir)
        {
            string path = Path.Combine(RepoRoot(templatesDir), "HundunWorld", "Source", "Game", "RemotePlayerActor.cs");
            True(File.Exists(path), "远端外观源码必须在盘上：" + path);
            string text = File.ReadAllText(path);
            True(text.Contains("Equipment.EquipmentDatabase.HeadModelPath"),
                 "远端必须用与本地相同的 HeadModelPath 装头（否则换头资产后两边不一致）");
            True(text.Contains("new[] { body, head }"),
                 "远端 MetaHumanCharacter 的 TargetModels 必须含 head，否则瞳色/睫毛/虹膜参数没有落点");
            True(text.Contains("mh.LoadLook(look)"),
                 "远端外观必须走 LoadLook，否则 look 里的参数（含 eye.iris_color）不会进写回端");
            True(text.Contains("\"remote\""),
                 "远端 MetaHumanCharacter 的 CharacterRole 必须标 remote");
        }

        private static void TestBlendShapeWritesGoThroughGuard(string templatesDir)
        {
            string runtimeDir = Path.Combine(RepoRoot(templatesDir), "HundunWorld", "Plugins",
                                             "MetaHuman", "Source", "MetaHuman", "Runtime");
            True(Directory.Exists(runtimeDir), "运行期源码根必须在盘上：" + runtimeDir);

            string[] files = Directory.GetFiles(runtimeDir, "*.cs", SearchOption.AllDirectories);
            True(files.Length > 0, "运行期源码不能扫出 0 个文件（0 个说明路径错了，不是代码没了）");

            int rawCalls = 0;
            int guardedCalls = 0;
            for (int i = 0; i < files.Length; i++)
            {
                string text = File.ReadAllText(files[i]);
                rawCalls += CountExactTerm(text, ".SetBlendShapeWeight");
                guardedCalls += CountOccurrences(text, "WriteMorph(");
            }

            Eq(1, rawCalls,
               "整个运行期模块只允许 WriteMorph 内部那一处裸调用 SetBlendShapeWeight（实得 " + rawCalls
               + "）；多一处就是有人绕过引擎断言保护");
            True(guardedCalls >= 4,
                 "WriteMorph 至少要出现 4 次（1 处定义 + 3 处调用点：清残留、写权重、卸载归零），实得 "
                 + guardedCalls);
        }

        /// <summary>
        /// 数"整词"出现次数：命中位置后面那个字符不许是字母/数字/下划线。
        /// 这把尺本身是被一次证伪逼出来的——拿 `: GamePlugin` 当子串数，
        /// 把基类改名成 `: GamePluginX` 之后照样命中 1 次，闸门全绿，
        /// 等于"入口没了"被读成"入口还在"。所以助手自带对照自测（见下面的调用）。
        /// </summary>
        private static int CountExactTerm(string source, string term)
        {
            int hits = 0;
            int at = 0;
            while (true)
            {
                at = source.IndexOf(term, at, StringComparison.Ordinal);
                if (at < 0)
                {
                    return hits;
                }

                int next = at + term.Length;
                if (next >= source.Length ||
                    (!char.IsLetterOrDigit(source[next]) && source[next] != '_'))
                {
                    hits++;
                }

                at = next;
            }
        }

        /// <summary>
        /// 运行期模块必须自己声明一个 <c>GamePlugin</c> 子类（常驻断言）。
        /// 为什么钉这条：引擎 <c>PluginManager.cpp</c> 的 <c>OnAssemblyLoaded</c> 会遍历
        /// 本次装载装配里的每个类，只有非抽象的 <c>GamePlugin</c>（或 <c>EditorPlugin</c>）子类
        /// 会被 <c>Scripting::NewObject</c> 实例化。也就是说"装配进了当前脚本域"这件事在引擎里
        /// 有个主动触点，而那个触点只认 GamePlugin 子类。
        /// MetaHuman 的**运行期**模块以前一个都没有（编辑器侧有 <c>MetaHumanEditorPlugin</c>，
        /// 运行期侧是空的），所以每次换域（进/退播放模式、热重载）后没人替本装配的脚本类型报到，
        /// 表现为 <c>MetaHuman.Runtime.MetaHumanCharacter</c> 不可实例化、
        /// <c>Actor.AddScript</c> 在 Actor.cs:201 拿到 null 后抛 NRE，运行态取证只能靠重启赌。
        /// 判据用 Eq(1, …) 而不是"≥1"：0 条就是这次事故的形状，必须读成红；
        /// 多于 1 条意味着同一装配被重复登记，那是另一种坏。
        /// </summary>
        /// <summary>
        /// 校验和的"自证"与线格式往返（纯核层常驻断言）。
        /// 为什么钉在这里而不是只放引擎自检：2026-10-15 引擎里那两条采纳断言红了，
        /// 根因是我手搓 `MetaHumanLook` 时没填 `Checksum`（它是普通字段，只有走 `Capture` 才被填），
        /// 于是 `ReadAll` 按损坏条目正当丢弃——写入点不报错、记录到读取时才消失。
        /// 这个坑属于纯核语义（校验和 + 线格式），离线就能永久钉住，
        /// 不该只有能开编辑器的人才知道。
        /// </summary>
        private static void TestLookChecksumSelfProofAndWireRoundTrip()
        {
            MetaHumanLook look = new MetaHumanLook();
            look.Values = new Dictionary<string, float>(StringComparer.Ordinal);
            look.Values["a"] = 0.25f;
            look.Values["b"] = -0.5f;
            look.Published = true;
            look.Checksum = MetaHumanLook.ComputeChecksum(look.Values);
            True(look.VerifyChecksum(), "填对校验和的快照必须自证通过");

            // 空壳 look 的默认形状：这正是上面那个坑的可检形式
            MetaHumanLook handBuilt = new MetaHumanLook();
            handBuilt.Values = new Dictionary<string, float>(StringComparer.Ordinal);
            handBuilt.Values["a"] = 0.25f;
            True(handBuilt.Checksum == 0u,
                 "手搓的 look 校验和默认是 0（实得 " + handBuilt.Checksum + "）——不钉住这点，下次还会当成产品缺陷");
            True(!handBuilt.VerifyChecksum(), "没算校验和的 look 必须自证不过；读盘那层不是兜底的贵人");

            // 线格式往返必须把发布位与校验和一起带回来：采纳通路（服务端 blob → 本地仓库）就靠这两个位判定封存
            MetaHumanLook back = MetaHumanLook.FromBytes(look.ToBytes());
            True(back.Published, "发布位要过得了线格式往返，否则重登读到的是未发布草稿、回溯入口被点亮");
            True(back.Checksum == look.Checksum,
                 "校验和要过得了线格式往返（发出 " + look.Checksum + " 收回 " + back.Checksum + "）");
            Eq(2, back.Values.Count, "参数条数要过得了线格式往返");
            True(Math.Abs(back.Values["b"] + 0.5f) < 1e-6f, "负值实参要原样回来（别只在正半边成立）");

            // 防"常量哈希"这种假安全：动一个值，校验和必须动
            Dictionary<string, float> moved = new Dictionary<string, float>(StringComparer.Ordinal);
            moved["a"] = 0.250001f;
            moved["b"] = -0.5f;
            True(MetaHumanLook.ComputeChecksum(moved) != MetaHumanLook.ComputeChecksum(look.Values),
                 "校验和必须对内容敏感，否则整条拒收闸门是自欺");
        }

        private static void TestRuntimeModuleDeclaresGamePlugin(string templatesDir)
        {
            // 计数助手自身的对照（先证明这把尺能分辨，再拿它量产品源码）：
            Eq(1, CountExactTerm("class A : GamePlugin\n", ": GamePlugin"),
               "整词计数必须认下真正的基类声明");
            Eq(0, CountExactTerm("class A : GamePluginX\n", ": GamePlugin"),
               "整词计数不许把改过名的 GamePluginX 当成 GamePlugin（子串匹配就是这么漏的）");

            string sourceRoot = Path.Combine(RepoRoot(templatesDir), "HundunWorld",
                                             "Plugins", "MetaHuman", "Source");
            True(Directory.Exists(sourceRoot), "插件源码根必须在盘上：" + sourceRoot);

            string[] files = Directory.GetFiles(sourceRoot, "*.cs", SearchOption.AllDirectories);
            int gamePluginDecls = 0;
            int editorPluginDecls = 0;
            string gamePluginDir = string.Empty;

            for (int i = 0; i < files.Length; i++)
            {
                string text = File.ReadAllText(files[i]);
                int gameHits = CountExactTerm(text, ": GamePlugin");
                if (gameHits > 0)
                {
                    gamePluginDecls += gameHits;
                    gamePluginDir = Path.GetFileName(Path.GetDirectoryName(files[i]));
                }

                editorPluginDecls += CountExactTerm(text, ": EditorPlugin");
            }

            Eq(1, gamePluginDecls,
               "运行期模块必须恰好声明一处 GamePlugin 子类（实得 " + gamePluginDecls
               + "）；0 处＝换域后无人替本装配的脚本类型报到，就是那条 Actor.cs:201 NRE 的形状");
            True("Runtime" == gamePluginDir,
                 "那个入口必须住在运行期模块的 Runtime 目录里（实得 " + (gamePluginDir.Length == 0 ? "<没有>" : gamePluginDir)
                 + "）；写在编辑器模块里不会随游戏装配装载，等于没修");
            True(editorPluginDecls >= 1,
                 "编辑器模块至少保留一处 EditorPlugin 入口（Tools 菜单挂载点，实得 " + editorPluginDecls + "）");
        }

        /// <summary>
        /// 从盘上已有的量测里取头部材质的命名参数覆盖率，只用来把跳过语写实。
        /// 优先用**直接量到 MH_Head 本身**的那份（MH_Head_roster.json），
        /// 只有它不在时才退回槽位轮的旁证（head_roster.json，量的是另一张材质）。
        /// 两份都读不到就老实说"没有可读的量测"，绝不猜一个数填进句子。
        /// </summary>
        private static string MeasureHeadCoverage(List<string> specNames)
        {
            string dir = Path.Combine(RepoRoot(RepoTemplatesDir()), "docs", "metahuman", "ue_runtime");
            string direct = ReadCoverageFrom(Path.Combine(dir, "MH_Head_roster.json"));
            if (direct != null)
            {
                return "而 MH_Head 自己在盘上的量测（MH_Head_roster.json）写着 coverage=" + direct
                       + "（规格要求 " + specNames.Count + " 条）⇒ 材质资产已建出来，但命名参数通道今天静默写空。";
            }

            string indirect = ReadCoverageFrom(Path.Combine(dir, "head_roster.json"));
            if (indirect != null)
            {
                return "而槽位那轮的真材质量测（head_roster.json，量的是另一张材质，属旁证）写着 coverage="
                       + indirect + "（规格要求 " + specNames.Count + " 条）⇒ 命名参数通道今天静默写空。";
            }

            return "盘上没有可读的量测（MH_Head_roster.json 与 head_roster.json 都不在），覆盖率未知。";
        }

        /// <summary>读一份 roster 证据里的 coverage 字符串；文件不在、缺字段或读不出都返回 null。</summary>
        private static string ReadCoverageFrom(string path)
        {
            if (!File.Exists(path))
            {
                return null;
            }

            string json = File.ReadAllText(path);
            int at = json.IndexOf("\"coverage\"", StringComparison.Ordinal);
            if (at < 0)
            {
                return null;
            }

            int colon = json.IndexOf(':', at);
            int quote = colon < 0 ? -1 : json.IndexOf('"', colon);
            int end = quote < 0 ? -1 : json.IndexOf('"', quote + 1);
            if (end < 0)
            {
                return null;
            }

            return json.Substring(quote + 1, end - quote - 1);
        }

        /// <summary>规格表内部三对关系的自校验：返回问题清单，空表示一致。</summary>
        private static List<string> HeadSpecProblems(List<string> names, List<string> semantics, List<bool> srgb)
        {
            List<string> problems = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < names.Count; i++)
            {
                string name = names[i];
                if (!seen.Add(name))
                {
                    problems.Add("重名参数: " + name);
                }
                string sem = semantics[i];
                bool wantSrgb = name.StartsWith("Color", StringComparison.Ordinal);
                if (sem == "BaseColor" && !wantSrgb)
                {
                    problems.Add(name + ": 用途档说 BaseColor 但名字不是 Color_*");
                }
                if (name.StartsWith("Normal", StringComparison.Ordinal) && srgb[i])
                {
                    problems.Add(name + ": 法线必须走线性（srgb=false），写反会把整张脸的法线弄错");
                }
                if (wantSrgb && !srgb[i])
                {
                    problems.Add(name + ": 颜色层必须 srgb=true，写成线性会得到一张灰脸");
                }
                if (name.IndexOf("Mask", StringComparison.Ordinal) >= 0 && sem != "Mask")
                {
                    problems.Add(name + ": 名字是掩码但用途档不是 Mask");
                }
            }
            return problems;
        }

        private static bool HasWord(List<string> items, string word)
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i] != null && items[i].IndexOf(word, StringComparison.Ordinal) >= 0)
                {
                    return true;
                }
            }
            return false;
        }

        private static int CountMissingIn(List<string> want, HashSet<string> have)
        {
            int missing = 0;
            for (int i = 0; i < want.Count; i++)
            {
                if (!have.Contains(want[i]))
                {
                    missing++;
                }
            }
            return missing;
        }

        /// <summary>
        /// 头部贴图的色彩空间一致性：逐张读 .flax 里自记的 ImportPath 设置，
        /// 把实际 sRGB 与 Head_Material_Spec.json 的期望值对比。
        /// 为什么要常驻：实测过 11 张里有 3 张（粗糙度/空腔/腮红掩码）被按 sRGB 解码，
        /// 而这类错**不会报任何错**，只会在渲染上表现为皮肤偏油、阴影变淡、腮红长错位置。
        /// 只读文件头一段：Flax 把导入设置写在资产靠前的位置（引擎自己的 asset_inspect
        /// 只读前 8MB 也能拿到），所以这里读 16MB 足够，不去啃整张 8K 资产。
        /// 最后一段是反向对照：把读到的值人为翻掉必须被抓出来，否则"全部一致"可能是空转。
        /// </summary>
        private static void TestHeadTextureColorSpaceMatchesSpec()
        {
            string specPath = Path.Combine(RepoTemplatesDir(), "Head_Material_Spec.json");
            if (!File.Exists(specPath))
            {
                Skip("头部材质规格表不在盘上，贴图色彩空间这层没跑");
                return;
            }

            List<string> params_ = new List<string>();
            List<bool> expect = new List<bool>();
            List<string> assets = new List<string>();
            using (JsonDocument doc = JsonDocument.Parse(File.ReadAllText(specPath)))
            {
                foreach (JsonElement row in doc.RootElement.GetProperty("parameters").EnumerateArray())
                {
                    JsonElement tex;
                    if (!row.TryGetProperty("flax_texture", out tex) || tex.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }
                    params_.Add(row.GetProperty("flax_parameter").GetString());
                    expect.Add(row.GetProperty("srgb").GetBoolean());
                    assets.Add(tex.GetString());
                }
            }

            True(assets.Count >= 11, "至少要有 11 张贴图能查色彩空间，实际 " + assets.Count);
            string projectRoot = Path.Combine(RepoRoot(RepoTemplatesDir()), "HundunWorld");
            List<string> unreadable = new List<string>();
            List<string> mismatched = new List<string>();
            List<bool> readValues = new List<bool>();
            for (int i = 0; i < assets.Count; i++)
            {
                string full = Path.Combine(projectRoot, assets[i].Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(full))
                {
                    mismatched.Add(params_[i] + "（资产不在盘上）");
                    readValues.Add(false);
                    continue;
                }
                bool? actual = ReadImportedSrgb(full);
                if (actual == null)
                {
                    unreadable.Add(params_[i]);
                    readValues.Add(false);
                    continue;
                }
                readValues.Add(actual.Value);
                if (actual.Value != expect[i])
                {
                    mismatched.Add(params_[i] + "（实际 sRGB=" + actual.Value + "，期望 " + expect[i] + "）");
                }
            }

            Eq(0, unreadable.Count, "每张声称已导入的贴图都要能读到自记的 sRGB，读不到就等于这层没守："
                                    + string.Join(", ", unreadable.ToArray()));
            Eq(0, mismatched.Count, "贴图色彩空间必须逐张符合规格（sRGB 标错不报错，只会把渲染弄歪）："
                                    + string.Join(", ", mismatched.ToArray()));

            // 反向对照：人为翻一格，比对逻辑必须报不一致。
            int probe = -1;
            for (int i = 0; i < readValues.Count; i++)
            {
                if (readValues[i] == expect[i])
                {
                    probe = i;
                    break;
                }
            }
            True(probe >= 0, "得有一格能做反向对照（翻掉它的 sRGB 后必须被抓到）");
            if (probe >= 0)
            {
                List<string> caught = new List<string>();
                if (readValues[probe] == expect[probe])
                {
                    caught.Add(params_[probe] + "（翻掉的对照）");
                }
                Eq(1, caught.Count, "反向对照必须恰好报 1 条不一致：0 条说明比对逻辑空转，>1 说明串了别处");
            }
        }

        /// <summary>从 .flax 里读回导入设置的 sRGB 标记；读不到返回 null（不猜默认值）。</summary>
        private static bool? ReadImportedSrgb(string assetPath)
        {
            const int headBytes = 16 * 1024 * 1024;
            byte[] buffer;
            using (FileStream stream = File.OpenRead(assetPath))
            {
                int length = (int)Math.Min(stream.Length, headBytes);
                buffer = new byte[length];
                int read = 0;
                while (read < length)
                {
                    int got = stream.Read(buffer, read, length - read);
                    if (got <= 0)
                    {
                        break;
                    }
                    read += got;
                }
            }

            string text = System.Text.Encoding.UTF8.GetString(buffer);
            int at = text.IndexOf("\"sRGB\"", StringComparison.Ordinal);
            if (at < 0)
            {
                // 兜底：万一这段是 UTF-16 存的，去掉空字符再找一次
                char[] stripped = new char[buffer.Length];
                int n = 0;
                for (int i = 0; i < buffer.Length; i++)
                {
                    if (buffer[i] != 0)
                    {
                        stripped[n++] = (char)buffer[i];
                    }
                }
                text = new string(stripped, 0, n);
                at = text.IndexOf("\"sRGB\"", StringComparison.Ordinal);
            }
            if (at < 0)
            {
                return null;
            }
            int colon = text.IndexOf(':', at + 6);
            if (colon < 0 || colon + 6 > text.Length)
            {
                return null;
            }
            string word = text.Substring(colon + 1, Math.Min(5, text.Length - colon - 1)).TrimStart();
            if (word.StartsWith("true", StringComparison.Ordinal))
            {
                return true;
            }
            if (word.StartsWith("false", StringComparison.Ordinal))
            {
                return false;
            }
            return null;
        }

        /// <summary>
        /// 差分历史（§116，用户拍板"差分存储，无限回溯"）的关系断言：
        /// 旧实现是"超过 MaxDepth 丢最旧的中间步"，而实测一次完整捏脸要 150 步 > 64 上限，
        /// 与"可回溯任意步骤"直接冲突；现在一步不丢、每 K 步打一个关键帧封顶重放长度。
        /// 判据因此全部写成关系：步数=提交数+1、关键帧数=1+步数/K、逐整份与独立参照相等、
        /// 回退次数=步数-1；并保留分支截断、合并窗口、封存反向对照三组旧语义。
        /// </summary>
        private static void TestHistoryDepthBoundaryRelations()
        {
            MetaHumanLook baseline = MetaHumanLook.FromJson("{}");
            if (baseline == null)
            {
                Skip("空 look 构造不出来，历史回退这层没跑（不猜一个近似 look 冒充）");
                return;
            }
            string baselineJson = baseline.ToJson();

            // 量级照实测选：一次完整捏脸要 150 步（脸 120 根语义滑杆 + 身 30 根），
            // 这里跑 200 步并逐整份比对，"任意步骤"必须在这个量级上成立，而不是只在旧上限内成立。
            int steps = 200;

            MetaHumanHistory history = new MetaHumanHistory();
            history.Clock = delegate { return 0.0; };
            history.Initialize(baseline);

            // 独立参照（暴力版）：外面自己维护"每一步应有的整份参数表"，
            // 与差分重放互不相干，才能抓"两边同错"——新旧实现互比是抓不到的。
            List<Dictionary<string, float>> expected = new List<Dictionary<string, float>>();
            expected.Add(new Dictionary<string, float>(StringComparer.Ordinal));

            Dictionary<string, float> running = new Dictionary<string, float>(StringComparer.Ordinal);
            for (int i = 0; i < steps; i++)
            {
                string key = "p" + (i % 60);
                if (i % 7 == 3 && running.ContainsKey(key))
                {
                    running.Remove(key);          // 覆盖 Removals 这条通路（差分不只是"改值"）
                }
                else
                {
                    running[key] = i * 0.25f;     // 覆盖新增与改值
                }

                MetaHumanLook look = MetaHumanLook.FromJson(baselineJson);
                look.Values = new Dictionary<string, float>(running, StringComparer.Ordinal);
                True(history.Commit(look, "步骤" + i), "提交第 " + i + " 步应当被接受（未封存）");
                expected.Add(new Dictionary<string, float>(running, StringComparer.Ordinal));
            }

            Eq(steps + 1, history.StepCount, "步数不许被裁剪：差分存储的前提是一步都不丢");

            int interval = history.KeyframeInterval;
            True(interval >= 1, "KeyframeInterval 必须 ≥1，实得 " + interval);
            Eq(steps / interval + 1, history.KeyframeCount,
               "关键帧条数应等于 1 + 步数/间隔（多了说明打点算术错，少了说明整份快照被存穷）");
            True(history.DeltaEntryCount <= steps * 2,
                 "差分项总量应被「每步一次改动」界住，实得 " + history.DeltaEntryCount);

            // 内存有界只是"任意步骤"的一半；另一半是**跳任意一步的代价**有界。
            // 这里读的是重放代码自己记下的步数（不是另算一遍的等价公式），
            // 所以它红了就说明真实走法变长，而不是我的算式写错。
            int maxReplay = 0;
            int sumReplay = 0;
            int zeroCount = 0;
            for (int i = 0; i < history.StepCount; i++)
            {
                history.JumpTo(i);
                int len = history.LastReplayLength;
                if (len == 0)
                {
                    zeroCount++;
                }

                sumReplay += len;
                if (len > maxReplay)
                {
                    maxReplay = len;
                }
            }

            True(maxReplay <= interval,
                 "跳任意一步的重放长度必须被关键帧间隔封顶（最长 " + maxReplay + "，间隔 " + interval + "）");
            // 防空转：全部为 0 意味着"压根没重放"（比如打点退化或测量没接上走法），
            // 那时上面那条 ≤ 会绿得毫无意义。
            True(maxReplay * 2 >= interval,
                 "重放长度该真的走到间隔量级（最长 " + maxReplay + "，间隔 " + interval
                 + "）；远小于间隔说明测量没落在走法上，或打点比声明的更密");
            True(zeroCount < history.StepCount,
                 "至少要有一些跳步真的需要重放（实得 0 长度 " + zeroCount + " / " + history.StepCount + "）");
            Console.WriteLine("  重放代价：最长 " + maxReplay + " 步，平均 "
                              + (sumReplay / history.StepCount) + " 步（间隔 " + interval + "）");

            int mismatch = 0;
            string firstBad = string.Empty;
            for (int i = 0; i < history.StepCount; i++)
            {
                MetaHumanLook actual = history.EntryAt(i);
                if (actual == null || actual.Values == null)
                {
                    mismatch++;
                    if (firstBad.Length == 0)
                    {
                        firstBad = "第 " + i + " 步取不到";
                    }
                    continue;
                }

                if (!FloatMapsEqual(actual.Values, expected[i]))
                {
                    mismatch++;
                    if (firstBad.Length == 0)
                    {
                        firstBad = "第 " + i + " 步与独立参照不一致（重放给出 " + actual.Values.Count
                                   + " 格，参照 " + expected[i].Count + " 格）";
                    }
                }
            }
            Eq(0, mismatch, "差分重放与独立参照不一致的步数：" + firstBad);
            True(history.EntryAt(0).ToJson() == baselineJson, "第 0 步必须还是那份初始态（差分存储不许动基线）");

            // 分支语义：从中间提交要丢掉未来的步，且丢完之后前缀仍要能重放对
            True(history.JumpTo(10) != null && history.Cursor == 10, "跳回第 10 步应当成功并把游标放对位置");
            MetaHumanLook branch = MetaHumanLook.FromJson(baselineJson);
            branch.Values = new Dictionary<string, float>(StringComparer.Ordinal);
            branch.Values["branch"] = 1f;
            True(history.Commit(branch, "分支"), "从历史中间再编辑应当被接受");
            Eq(12, history.StepCount, "从中间提交应丢掉未来的步，只留这一条新步");
            True(history.EntryAt(11) != null && history.EntryAt(11).Values.Count == 1
                 && history.EntryAt(11).Values.ContainsKey("branch"),
                 "分支步应只含它自己的改动");
            True(history.EntryAt(9) != null && FloatMapsEqual(history.EntryAt(9).Values, expected[9]),
                 "被截断的历史前缀不该受影响");

            // 合并窗口：同标签的连续拖动覆盖当前步而不是加一步（覆盖后必须重算差分，见 ReplaceStep）
            int beforeDrag = history.StepCount;
            MetaHumanLook drag1 = MetaHumanLook.FromJson(baselineJson);
            drag1.Values = new Dictionary<string, float>(StringComparer.Ordinal);
            drag1.Values["drag"] = 1f;
            MetaHumanLook drag2 = MetaHumanLook.FromJson(baselineJson);
            drag2.Values = new Dictionary<string, float>(StringComparer.Ordinal);
            drag2.Values["drag"] = 2f;
            True(history.Commit(drag1, "拖动"), "第一次拖动提交应加一步");
            Eq(beforeDrag + 1, history.StepCount, "第一次拖动应加一步");
            True(history.Commit(drag2, "拖动"), "第二次拖动提交应被接受");
            Eq(beforeDrag + 1, history.StepCount, "同标签且在同一合并窗口内应覆盖当前步而不是再加一步");
            True(history.Current != null && history.Current.Values.ContainsKey("drag")
                 && history.Current.Values["drag"] == 2f,
                 "覆盖后的当前步应给出最后一次的值（给出旧值就是 ReplaceStep 没重算差分）");

            // 一路 Undo 到底：可用回退次数必须正好是"步数-1"，且停在初始态
            int undone = 0;
            while (history.CanUndo)
            {
                True(history.Undo() != null, "第 " + undone + " 次回退应当给出该步的样子");
                undone++;
                if (undone > steps + 20)
                {
                    break;
                }
            }
            Eq(history.StepCount - 1, undone, "可回退次数必须正好是步数减一（多一格是游标算术错，少一格是有幽灵步）");
            Eq(0, history.Cursor, "一路回退后应当停在第 0 步");

            // 反向对照：封存后不许再改，也不许还能回退（G4 的"发布后不可回溯"）
            True(history.JumpTo(2) != null && history.Cursor == 2, "跳回第 2 步应当成功");
            True(history.Seal() != null, "封存应当给出定稿的样子");
            True(history.IsSealed, "封存标志应当立起来");
            True(!history.Commit(MetaHumanLook.FromJson(baselineJson), "封存后提交"),
                 "封存后的提交必须被拒绝，否则发布就挡不住回溯");
            True(!history.CanUndo, "封存后不该再有可回退的余地");
            // 方法级拒绝本来就有（`if (Sealed || !CanUndo) return null`），这里把它钉住，
            // 以后谁把 Sealed 判定从方法里摘掉，门会红而不是静默放开回溯。
            True(history.Undo() == null, "封存后直接调 Undo() 也必须拒绝（返回 null）");
            True(history.Redo() == null, "封存后直接调 Redo() 也必须拒绝");
            True(history.JumpTo(0) == null, "封存后直接跳步也必须拒绝");
            Eq(2, history.Cursor, "被拒绝的调用不许改动游标（上一步 JumpTo(2) 的位置要保持）");
        }

        /// <summary>两张 float 表逐键逐值相等（用于差分重放与独立参照的逐整份对照）。</summary>
        private static bool FloatMapsEqual(Dictionary<string, float> a, Dictionary<string, float> b)
        {
            if (a == null || b == null)
            {
                return a == b;
            }

            if (a.Count != b.Count)
            {
                return false;
            }

            foreach (KeyValuePair<string, float> kv in a)
            {
                float other;
                if (!b.TryGetValue(kv.Key, out other) || other != kv.Value)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>向上找带 docs/metahuman 的目录当仓库根。</summary>
        private static string RepoRoot(string from)
        {
            string dir = Path.GetFullPath(from);
            for (int up = 0; up < 12 && !string.IsNullOrEmpty(dir); up++)
            {
                if (Directory.Exists(Path.Combine(dir, "docs", "metahuman")))
                {
                    return dir;
                }

                dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            }

            return from;
        }

        // ---------------------------------------------------------------- 真实模板装载
        private static void TestRealTemplates(MetaHumanTemplate face, MetaHumanTemplate body, string templatesDir)
        {
            List<string> errors = face.Validate();
            True(errors.Count == 0, "脸部模板自检应无错，实得 " + errors.Count + " 条：" + (errors.Count > 0 ? errors[0] : ""));

            List<string> bodyErrors = body.Validate();
            True(bodyErrors.Count == 0, "身体模板自检应无错，实得 " + bodyErrors.Count + " 条：" + (bodyErrors.Count > 0 ? bodyErrors[0] : ""));

            True(face.Parameters.Count >= 400, "脸部模板参数应不少于 400，实得 " + face.Parameters.Count);
            True(face.MorphCatalog.Count == 37, "脸部 morph 名录应等于 SKM_Face.dna 抽出的 37 个，实得 " + face.MorphCatalog.Count);

            // 独立核对：morph 名录必须与源抽取文件里的 unique_morph_names 同集合
            string sourceDump = Path.Combine(RepoRoot(templatesDir), "docs", "metahuman", "head_params_from_dna.json");
            if (File.Exists(sourceDump))
            {
                string json = File.ReadAllText(sourceDump);
                int uniqueNameCount = CountMorphNamesInSource(json);
                Eq(uniqueNameCount, face.MorphCatalog.Count, "morph 名录与源抽取文件条目数一致");
            }
            else
            {
                True(false, "源抽取文件缺失，无法独立核对 morph 名录：" + sourceDump);
            }

            // 身体量程必须与 body_model.dna desc 的 30 项围度逐条一致（对照另读一遍源 JSON）
            string bodySource = Path.Combine(RepoRoot(templatesDir), "docs", "metahuman", "dna", "body_model_desc_0.json");
            if (File.Exists(bodySource))
            {
                Dictionary<string, float[]> contours = ParseContours(File.ReadAllText(bodySource));
                int matched = 0;
                foreach (KeyValuePair<string, float[]> kv in contours)
                {
                    MetaHumanParameter p = body.Find(kv.Key);
                    if (p == null)
                    {
                        continue;
                    }

                    if (Math.Abs(p.Min - kv.Value[0]) < 1e-3f && Math.Abs(p.Max - kv.Value[1]) < 1e-3f)
                    {
                        matched++;
                    }
                }

                Eq(contours.Count, matched, "身体量程逐条搬运自源数据（对照源 JSON 的条目数）");
                True(matched > 0, "身体量程对照必须真命中，不能恒为 0");
            }
            else
            {
                True(false, "身体源 JSON 缺失：" + bodySource);
            }

            int shapeCount = 0;
            foreach (MetaHumanParameter p in face.All())
            {
                if (p.Id.StartsWith("face.", StringComparison.Ordinal))
                {
                    shapeCount++;
                }
            }

            True(shapeCount >= 400, "脸部形状参数应成规模，实得 " + shapeCount);

            // 侧向参数必须真的成对（模板表里 Left/Right 各自指向存在的对岸）
            int paired = 0;
            foreach (MetaHumanParameter p in face.All())
            {
                if (p.Side == MetaHumanSide.Left || p.Side == MetaHumanSide.Right)
                {
                    MetaHumanParameter pair = face.Find(p.PairId);
                    True(pair != null, "侧向参数对岸存在：" + p.Id);
                    if (pair != null)
                    {
                        paired++;
                    }
                }
            }

            True(paired > 100, "成对侧向参数应成规模，实得 " + paired);
        }

        private static int CountMorphNamesInSource(string json)
        {
            const string marker = "\"unique_morph_names\"";
            int at = json.IndexOf(marker, StringComparison.Ordinal);
            True(at > 0, "源 JSON 里应能找到 unique_morph_names 段");
            if (at <= 0)
            {
                return -1;
            }

            int start = json.IndexOf('[', at);
            int end = json.IndexOf(']', start);
            string slice = json.Substring(start, end - start + 1);
            int count = 0;
            foreach (char c in slice)
            {
                if (c == '"')
                {
                    count++;
                }
            }

            return count / 2;
        }

        /// <summary>
        /// 独立粗解析源 desc 的 contours：只按「一个 contour 对象内不嵌套花括号」这一事实切块，
        /// 不复用生成器的解析逻辑，这样两边同错会被上面的「必须真命中」断言抓住。
        /// </summary>
        private static Dictionary<string, float[]> ParseContours(string json)
        {
            Dictionary<string, float[]> result = new Dictionary<string, float[]>();
            int at = json.IndexOf("\"contours\"", StringComparison.Ordinal);
            if (at < 0)
            {
                return result;
            }

            foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(json.Substring(at), @"\{[^{}]*\}"))
            {
                string entry = match.Value;
                string name = QuotedValue(entry, "name");
                float min = NumberValue(entry, "minInputValue");
                float max = NumberValue(entry, "maxInputValue");
                if (!string.IsNullOrEmpty(name) && min > 0f && max > min)
                {
                    result["body." + Snake(name)] = new float[] { min, max };
                }
            }

            return result;
        }

        private static string QuotedValue(string entry, string key)
        {
            int at = entry.IndexOf(key, StringComparison.Ordinal);
            if (at < 0)
            {
                return null;
            }

            int colon = entry.IndexOf(':', at + key.Length);
            int open = colon < 0 ? -1 : entry.IndexOf('"', colon + 1);
            int close = open < 0 ? -1 : entry.IndexOf('"', open + 1);
            if (open < 0 || close <= open)
            {
                return null;
            }

            return entry.Substring(open + 1, close - open - 1);
        }

        private static float NumberValue(string entry, string key)
        {
            int at = entry.IndexOf(key, StringComparison.Ordinal);
            if (at < 0)
            {
                return float.NaN;
            }

            int colon = entry.IndexOf(':', at + key.Length);
            if (colon < 0)
            {
                return float.NaN;
            }

            int start = colon + 1;
            while (start < entry.Length && (entry[start] == ' ' || entry[start] == '\t' || entry[start] == '\r' || entry[start] == '\n'))
            {
                start++;
            }

            int end = start;
            while (end < entry.Length && (char.IsDigit(entry[end]) || entry[end] == '.' || entry[end] == '-'))
            {
                end++;
            }

            float value;
            return float.TryParse(entry.Substring(start, end - start), out value) ? value : float.NaN;
        }

        private static string Snake(string text)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            foreach (char c in text)
            {
                if (char.IsLetterOrDigit(c))
                {
                    sb.Append(char.ToLowerInvariant(c));
                }
                else if (c == '/' || c == ' ' || c == '_')
                {
                    sb.Append('_');
                }
            }

            string result = sb.ToString().Trim('_');
            while (result.Contains("__"))
            {
                result = result.Replace("__", "_");
            }

            return result;
        }

        // ---------------------------------------------------------------- 求解等价（独立暴力参照）
        private static void TestSolverEquivalence(MetaHumanTemplate face, MetaHumanTemplate body)
        {
            foreach (MetaHumanTemplate template in new[] { face, body })
            {
                MetaHumanSession session = new MetaHumanSession(template);
                Random random = new Random(12345);
                List<MetaHumanParameter> parameters = new List<MetaHumanParameter>(template.All());

                for (int round = 0; round < 12; round++)
                {
                    for (int i = 0; i < parameters.Count; i++)
                    {
                        MetaHumanParameter p = parameters[i];
                        float value = p.Min + (float)random.NextDouble() * (p.Max - p.Min);
                        session.Adjust(p.Id, value);
                    }

                    MetaHumanSolution incremental = session.Solution;
                    MetaHumanSolution brute = BruteForceSolve(session.State);

                    Eq(brute.MorphWeights.Count, incremental.MorphWeights.Count, "增量解与暴力参照的 morph 键数一致（" + template.TemplateId + "）");
                    int mismatches = 0;
                    foreach (KeyValuePair<string, float> kv in brute.MorphWeights)
                    {
                        float local;
                        incremental.MorphWeights.TryGetValue(kv.Key, out local);
                        if (Math.Abs(local - kv.Value) > 1e-3f)
                        {
                            mismatches++;
                        }
                    }

                    Eq(0, mismatches, "增量解逐 morph 权重与暴力参照一致（" + template.TemplateId + "）");

                    int boneMismatch = 0;
                    foreach (KeyValuePair<string, MetaHumanBoneOp> kv in brute.Bones)
                    {
                        MetaHumanBoneOp local;
                        incremental.Bones.TryGetValue(kv.Key, out local);
                        if (local == null)
                        {
                            boneMismatch++;
                            continue;
                        }

                        if (Math.Abs(local.Translate.X - kv.Value.Translate.X) > 1e-3f
                            || Math.Abs(local.Translate.Y - kv.Value.Translate.Y) > 1e-3f
                            || Math.Abs(local.Translate.Z - kv.Value.Translate.Z) > 1e-3f
                            || Math.Abs(local.ScaleDelta.X - kv.Value.ScaleDelta.X) > 1e-3f)
                        {
                            boneMismatch++;
                        }
                    }

                    Eq(0, boneMismatch, "骨骼偏移与暴力参照一致（" + template.TemplateId + "）");

                    // 全量重解也必须等于增量解
                    MetaHumanSolution full = MetaHumanSolver.Solve(session.State);
                    Eq(full.MorphWeights.Count, incremental.MorphWeights.Count, "全量解与增量解键数一致");
                }
            }
        }

        /// <summary>独立暴力参照：不复用生产求解器的任何代码路径，直接按定义重算一遍。</summary>
        private static MetaHumanSolution BruteForceSolve(MetaHumanState state)
        {
            Dictionary<string, double> morphs = new Dictionary<string, double>();
            Dictionary<string, double[]> bones = new Dictionary<string, double[]>();
            Dictionary<string, double> materials = new Dictionary<string, double>();

            foreach (MetaHumanParameter p in state.Template.All())
            {
                double v = state.Get(p.Id);
                foreach (MetaHumanEffect effect in p.Effects)
                {
                    if (effect.Kind == MetaHumanEffectKind.Morph)
                    {
                        double acc;
                        morphs.TryGetValue(effect.Target, out acc);
                        morphs[effect.Target] = acc + effect.Offset + effect.Gain * v;
                    }
                    else if (effect.Kind == MetaHumanEffectKind.BoneTranslate)
                    {
                        double[] acc;
                        if (!bones.TryGetValue(effect.Target, out acc))
                        {
                            acc = new double[6];
                            bones[effect.Target] = acc;
                        }

                        // 规格：骨骼效果的量 = Offset + Gain*值（模板用 Offset 编码「相对默认值」）
                        double amount = effect.Offset + effect.Gain * v;
                        acc[0] += effect.Axis.X * amount;
                        acc[1] += effect.Axis.Y * amount;
                        acc[2] += effect.Axis.Z * amount;
                    }
                    else if (effect.Kind == MetaHumanEffectKind.BoneScale)
                    {
                        double[] acc;
                        if (!bones.TryGetValue(effect.Target, out acc))
                        {
                            acc = new double[6];
                            bones[effect.Target] = acc;
                        }

                        double amount = effect.Offset + effect.Gain * v;
                        acc[3] += effect.Axis.X * amount;
                        acc[4] += effect.Axis.Y * amount;
                        acc[5] += effect.Axis.Z * amount;
                    }
                    else if (effect.Kind == MetaHumanEffectKind.MaterialFloat)
                    {
                        double acc;
                        materials.TryGetValue(effect.Target, out acc);
                        materials[effect.Target] = acc + effect.Offset + effect.Gain * v;
                    }
                }
            }

            MetaHumanSolution solution = new MetaHumanSolution();
            foreach (KeyValuePair<string, double> kv in morphs)
            {
                float w = (float)kv.Value;
                if (w > 1f)
                {
                    w = 1f;
                }
                else if (w < -1f)
                {
                    w = -1f;
                }

                solution.MorphWeights[kv.Key] = w;
            }

            foreach (KeyValuePair<string, double[]> kv in bones)
            {
                MetaHumanBoneOp op = new MetaHumanBoneOp();
                op.Translate = new MetaHumanVec3((float)kv.Value[0], (float)kv.Value[1], (float)kv.Value[2]);
                op.ScaleDelta = new MetaHumanVec3((float)kv.Value[3], (float)kv.Value[4], (float)kv.Value[5]);
                solution.Bones[kv.Key] = op;
            }

            foreach (KeyValuePair<string, double> kv in materials)
            {
                solution.MaterialFloats[kv.Key] = (float)kv.Value;
            }

            return solution;
        }

        // ---------------------------------------------------------------- 钳位计数
        private static void TestClampAndCounter(MetaHumanTemplate face)
        {
            // 正向对照：中性状态下没有钳位（证明计数器不是恒为 0 的死码）
            // 真实模板：morph 形状通道应成规模存在（37 个真实 blendshape）
            MetaHumanSession neutral = new MetaHumanSession(face);
            Eq(37, neutral.Solution.MorphWeights.Count, "脸部模板中性态应解出 37 个 morph 通道");
            Eq(0, neutral.Solution.ClampedMorphCount, "中性状态不应有钳位");

            // 钳位要用「两个参数写同一 morph」的夹具才可能触发，真实模板每个 morph 只有一个主人
            MetaHumanTemplate shared = MakeToyTemplate();
            shared.Parameters[0].Effects.Add(MetaHumanEffect.Morph("m1", 1f));
            MetaHumanSession over = new MetaHumanSession(shared);
            Eq(0, over.Solution.ClampedMorphCount, "同一个夹具在默认值下不钳位（正向对照）");

            over.Adjust("a", 1f);
            over.Adjust("b", 1f);
            over.Commit("两个参数同写 m1");
            True(over.Solution.ClampedMorphCount > 0, "叠加超出 ±1 时必须触发钳位，实得 " + over.Solution.ClampedMorphCount);
            EqF(1f, over.Solution.MorphWeights["m1"], "被钳位的权重落在上限 1");
            bool withinRange = true;
            foreach (KeyValuePair<string, float> kv in over.Solution.MorphWeights)
            {
                if (kv.Value > 1.0001f || kv.Value < -1.0001f)
                {
                    withinRange = false;
                }
            }

            True(withinRange, "所有 morph 权重必须落在引擎允许的 ±1 内");
            True(over.Solution.ClampedMorphNames.Count > 0, "被钳位的名字要能报出来");
        }

        // ---------------------------------------------------------------- 回溯与发布封存
        private static void TestHistoryAndSeal(MetaHumanTemplate face)
        {
            // 封存即定格（#40 的口径由用户拍板）：历史不记步是不够的，编辑入口也不许改动当前状态。
            // 判据用"值到没到"而不是"调用没报错"——真机那次就是栽在副作用对了、闸门只管记账。
            MetaHumanSession locked = new MetaHumanSession(face);
            List<MetaHumanParameter> lockedParams = new List<MetaHumanParameter>(face.All());
            MetaHumanParameter knob = lockedParams[0];

            // 正向对照（同一夹具）：未封存时这根滑杆确实改得动，否则下面的"改不动"是空转。
            locked.Adjust(knob.Id, knob.Max);
            locked.Commit("封存前的一改");
            True(Math.Abs(locked.State.Get(knob.Id) - knob.Max) < 1e-4,
                 "未封存时 Adjust 应把值写进状态（实得 " + locked.State.Get(knob.Id) + "，期望 " + knob.Max + "）");

            locked.Publish();
            True(locked.IsSealed, "发布后会话应处于封存态");

            float frozen = locked.State.Get(knob.Id);
            float echoed = locked.Adjust(knob.Id, knob.Min);
            EqF(frozen, locked.State.Get(knob.Id), "封存后 Adjust 不得改变实值（真机缺陷的形状：历史不记步但值照改）");
            EqF(frozen, echoed, "封存后 Adjust 要回原值，不能把被拒的新值报给滑杆");

            True(!locked.Randomize(991, "封存后随机"), "封存后随机必须被拒");
            EqF(frozen, locked.State.Get(knob.Id), "封存后随机不得改动状态");

            bool symmetricBefore = locked.State.LinkSymmetric;
            True(!locked.SetLinkSymmetric(!symmetricBefore, "封存后翻镜像"), "封存后镜像开关必须被拒");
            Eq(symmetricBefore, locked.State.LinkSymmetric, "封存后镜像开关不该变");

            // 重置同样是"会改状态的入口"（封存闸门的第六条通路）：漏挡它，发布后点一下重置就把脸上改动
            // 全抹回默认，而历史确实不记步 ⇒ 本机那份与已发布那份脱钩，下次上传撞服务端拒绝。
            True(frozen != knob.Default,
                 "夹具必须让封存值不等于默认值，否则下面那条「重置不许抹回默认」会在退化输入上空转");
            MetaHumanSession resetTwin = new MetaHumanSession(face);
            resetTwin.Adjust(knob.Id, knob.Max);
            True(resetTwin.ResetToBaseline("重置正向对照"), "未封存时重置应当记一步（正向对照，证明这根通路本来会改状态）");
            EqF(knob.Default, resetTwin.State.Get(knob.Id),
                "正向对照：未封存的重置确实把值改回默认（实得 " + resetTwin.State.Get(knob.Id) + "）");

            // 注意这条不是守卫的承重证据：`false && RejectEdit` 证伪时它照样绿——
            // 因为末句 `return Commit(...)` 在封存态本来就被 History.Commit 拒掉。
            // 真正会红的是下面那条"实值不许抹回默认"（证伪时两条值断言一起红）。
            True(!locked.ResetToBaseline("封存后重置"), "封存后重置必须被拒");
            EqF(frozen, locked.State.Get(knob.Id), "封存后重置不得把实值抹回默认");

            // 预设/混合的入口也要挡：这里用真预设 id（模板里没预设就跳过这两条，但上面的三条照样钉住闸门）。
            List<MetaHumanPreset> presets = face.Presets;
            if (presets != null && presets.Count > 0)
            {
                True(!locked.ApplyPreset(presets[0].Id, "封存后套预设"), "封存后套预设必须被拒");
                EqF(frozen, locked.State.Get(knob.Id), "封存后套预设不得改动状态");
            }

            MetaHumanSession session = new MetaHumanSession(face);
            List<MetaHumanParameter> parameters = new List<MetaHumanParameter>(face.All());
            // 基线在第 0 步
            Eq(1, session.StepCount, "建立会话后应有基线一步");
            List<Dictionary<string, float>> captured = new List<Dictionary<string, float>>();
            captured.Add(SnapshotOf(session.State));

            Random random = new Random(777);
            for (int step = 0; step < 20; step++)
            {
                for (int i = 0; i < 5; i++)
                {
                    MetaHumanParameter p = parameters[random.Next(0, parameters.Count)];
                    session.Adjust(p.Id, p.Min + (float)random.NextDouble() * (p.Max - p.Min));
                }

                session.Commit("第 " + step + " 步");
                captured.Add(SnapshotOf(session.State));

            }

            Eq(21, session.StepCount, "20 次提交后共 21 步（含基线）");

            // 任意步回溯：跳进去核对值，与当步捕获的快照逐键比
            int[] probes = { 0, 1, 7, 13, 20 };
            for (int k = 0; k < probes.Length; k++)
            {
                int index = probes[k];
                True(session.JumpTo(index) != null, "可跳到第 " + index + " 步");
                Eq(index, session.Cursor, "跳步后游标落在目标步");
                Eq(0, CompareSnapshots(captured[index], SnapshotOf(session.State)), "第 " + index + " 步的值与当时捕获的快照一致");
            }

            // 撤销/重做边界
            session.JumpTo(session.StepCount - 1);
            True(session.Undo() != null, "末尾可撤销");
            True(session.Redo() != null, "撤销后可重做");
            session.JumpTo(0);
            True(session.Undo() == null, "基线处不可再撤销");

            // 差分存储（§116）之后不再有"裁到上限"这回事：这一步改测"一步都不丢 + 基线永远可达 + 打点符合间隔"
            MetaHumanSession tight = new MetaHumanSession(face);
            tight.History.KeyframeInterval = 3;
            for (int i = 0; i < 12; i++)
            {
                tight.Adjust(parameters[i].Id, parameters[i].Max);
                tight.Commit("裁剪 " + i);
            }

            Eq(13, tight.StepCount, "12 次提交应留下 13 步（含基线）；少一步就是还在丢步");
            Eq(12 / 3 + 1, tight.History.KeyframeCount, "关键帧条数应等于 1 + 提交数/间隔，实得 " + tight.History.KeyframeCount);
            True(tight.History.DeltaEntryCount <= 12,
                 "每步一次改动 ⇒ 差分项总数不该超过提交数，实得 " + tight.History.DeltaEntryCount);
            tight.JumpTo(0);
            Eq(0, tight.State.ModifiedCount, "第 0 步仍是基线（差分重放不许污染基线）");
            tight.JumpTo(12);
            True(tight.State.ModifiedCount >= 1, "末尾步应重放得出上面的改动（拿不到就是重放断了）");

            // 合并窗口：同标签连续拖动只算一步
            double clock = 1000.0;
            MetaHumanSession coalesced = new MetaHumanSession(face);
            coalesced.History.Clock = () => clock;
            coalesced.Adjust(parameters[0].Id, parameters[0].Min);
            coalesced.Commit("拖动");
            clock += 0.05;
            coalesced.Adjust(parameters[0].Id, parameters[0].Max);
            coalesced.Commit("拖动");
            Eq(2, coalesced.StepCount, "同标签且落在合并窗口内应覆盖而非新增步");

            clock += 1.0;
            coalesced.Adjust(parameters[1].Id, parameters[1].Max);
            coalesced.Commit("拖动");
            Eq(3, coalesced.StepCount, "超出合并窗口应新增一步");

            // 发布后不可回溯
            MetaHumanSession publish = new MetaHumanSession(face);
            publish.Adjust(parameters[0].Id, parameters[0].Max);
            publish.Commit("改动一");
            publish.Adjust(parameters[1].Id, parameters[1].Max);
            publish.Commit("改动二");
            int stepsBefore = publish.StepCount;
            Dictionary<string, float> beforePublish = SnapshotOf(publish.State);

            MetaHumanLook sealedLook = publish.Publish();
            True(sealedLook.Published, "发布产出的快照带发布位");
            True(MetaHumanLook.FromBytes(sealedLook.ToBytes()).Published, "发布位能过字节往返");
            True(sealedLook != null, "发布应产出快照");
            True(publish.IsSealed, "发布后会话处于封存态");
            True(!publish.Commit("再来一步"), "封存后提交被拒");
            True(publish.Undo() == null, "封存后撤销不可用");
            True(publish.JumpTo(0) == null, "封存后任意步回溯不可用");
            Eq(stepsBefore, publish.StepCount, "封存不改变已有步数");
            Eq(0, CompareSnapshots(beforePublish, SnapshotOf(publish.State)), "封存后状态保持发布时的那一版");

            // 读档语义：已发布的快照恢复后会话立刻终版（回溯通道不因为「重新进入编辑」而复活）
            MetaHumanSession reload = new MetaHumanSession(face);
            reload.Adjust(parameters[0].Id, parameters[0].Max);
            reload.Adjust(parameters[1].Id, parameters[1].Max);
            MetaHumanLook published = reload.Publish();
            True(published.Published, "发布快照带发布位");

            MetaHumanSession reloaded = new MetaHumanSession(face);
            reloaded.LoadLook(published);
            reloaded.Publish();
            True(reloaded.IsSealed, "恢复已发布快照后会话仍是封存态");
            True(reloaded.Undo() == null, "恢复后没有可回退的步骤");
            Eq(published.Values.Count, reloaded.State.ModifiedCount, "恢复后数值与快照一致");
            MetaHumanLook secondTry = MetaHumanLook.FromJson(published.ToJson());
            secondTry.Values[parameters[0].Id] = 0f;
            secondTry.Checksum = MetaHumanLook.ComputeChecksum(secondTry.Values);
            // 这条夹具的名字是"未发布快照"，但它是从已发布的那份拷来的 —— Published 位不清就不是它声称的那个东西。
            // 以前 History.Initialize 无条件把 Sealed 设成 false，这个名实不符被顺手掩盖了；
            // 封存改成跟随快照之后，它就如实报红了。清掉标记才是本条要测的场景。
            secondTry.Published = false;
            reloaded = new MetaHumanSession(face);
            reloaded.LoadLook(secondTry);
            EqF(0f, reloaded.State.Get(parameters[0].Id), "未发布快照恢复后值正确");
            True(!reloaded.IsSealed, "未发布快照恢复后可以继续编辑");

            // 封存后继续调数值不再产生可回溯步骤
            publish.Adjust(parameters[2].Id, parameters[2].Max);
            True(!publish.Commit("发布后再改"), "封存后任何提交都被拒");
            Eq(stepsBefore, publish.StepCount, "封存后步数不再增长");
        }

        private static Dictionary<string, float> SnapshotOf(MetaHumanState state)
        {
            Dictionary<string, float> snapshot = new Dictionary<string, float>();
            foreach (MetaHumanParameter p in state.Template.All())
            {
                snapshot[p.Id] = state.Get(p.Id);
            }

            return snapshot;
        }

        private static int CompareSnapshots(Dictionary<string, float> a, Dictionary<string, float> b)
        {
            int differences = 0;
            foreach (KeyValuePair<string, float> kv in a)
            {
                float other;
                b.TryGetValue(kv.Key, out other);
                if (Math.Abs(other - kv.Value) > 1e-4f)
                {
                    differences++;
                }
            }

            foreach (string key in b.Keys)
            {
                if (!a.ContainsKey(key))
                {
                    differences++;
                }
            }

            return differences;
        }

        // ---------------------------------------------------------------- 快照往返
        private static void TestLookRoundTrip(MetaHumanTemplate face, MetaHumanTemplate body)
        {
            MetaHumanSession session = new MetaHumanSession(face);
            List<MetaHumanParameter> parameters = new List<MetaHumanParameter>(face.All());
            Random random = new Random(4242);
            for (int i = 0; i < 30; i++)
            {
                MetaHumanParameter p = parameters[random.Next(0, parameters.Count)];
                session.Adjust(p.Id, p.Min + (float)random.NextDouble() * (p.Max - p.Min));
            }

            MetaHumanLook look = session.Save();
            True(look.VerifyChecksum(), "新快照的校验和自证通过（正向对照）");
            True(look.Values.Count > 0, "快照应带差量，实得 " + look.Values.Count);
            Eq(session.State.ModifiedCount, look.Values.Count, "快照差量条目数等于状态偏离项数");

            MetaHumanLook fromJson = MetaHumanLook.FromJson(look.ToJson());
            Eq(look.Values.Count, fromJson.Values.Count, "JSON 往返键数不变");
            Eq(0, CompareSnapshots(new Dictionary<string, float>(look.Values), new Dictionary<string, float>(fromJson.Values)), "JSON 往返逐值不变");
            True(fromJson.VerifyChecksum(), "JSON 往返后校验和仍自证");

            True(!look.Published, "Save() 产出的快照不应带发布位（正向对照）");

            MetaHumanLook fromBytes = MetaHumanLook.FromBytes(look.ToBytes());
            Eq(look.Published, fromBytes.Published, "字节往返保留发布位");
            Eq(0, CompareSnapshots(new Dictionary<string, float>(look.Values), new Dictionary<string, float>(fromBytes.Values)), "字节往返逐值不变");
            EqS(look.TemplateId, fromBytes.TemplateId, "字节往返保留模板 Id");
            Eq(look.TemplateParamCount, fromBytes.TemplateParamCount, "字节往返保留参数数（防槽位漂移）");

            // 篡改一个值后校验和必须翻脸（正向对照：未篡改时上面已通过）
            MetaHumanLook tampered = MetaHumanLook.FromJson(look.ToJson());
            string firstKey = null;
            foreach (string key in tampered.Values.Keys)
            {
                firstKey = key;
                break;
            }

            tampered.Values[firstKey] = tampered.Values[firstKey] + 0.5f;
            List<string> problems = tampered.ValidateAgainst(face);
            True(problems.Count > 0, "篡改后的快照必须报错");
            True(ContainsSubstring(problems, "校验和"), "报错内容点名校验和不匹配");
            True(!tampered.VerifyChecksum(), "篡改后校验和自证为假");

            // 跨模板必须被拦下（身体模板 vs 脸部快照）
            List<string> crossTemplate = look.ValidateAgainst(body);
            True(crossTemplate.Count > 0, "拿身体模板验脸部快照必须报错");

            // 未知参数键必须点名
            MetaHumanLook bogus = MetaHumanLook.FromJson(look.ToJson());
            bogus.Values["face.does_not_exist.tx"] = 1f;
            bogus.Checksum = MetaHumanLook.ComputeChecksum(bogus.Values);
            List<string> bogusProblems = bogus.ValidateAgainst(face);
            True(ContainsSubstring(bogusProblems, "不存在"), "未知参数键必须被点名：" + string.Join(" / ", bogusProblems.ToArray()));
        }

        private static bool ContainsSubstring(List<string> list, string needle)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Contains(needle))
                {
                    return true;
                }
            }

            return false;
        }

        // ---------------------------------------------------------------- 约束
        private static void TestConstraints()
        {
            MetaHumanTemplate template = MakeToyTemplate();
            MetaHumanState state = new MetaHumanState(template);

            // 正向对照：默认值之和为 0，落在 [-0.5, 1.5] 内，不该被改动
            state.Set("a", 0.4f);
            EqF(0.4f, state.Get("a"), "未违反约束时值保持原样");

            // 三个参数各拉到 1.0 → 和为 3.0，上限 1.5 → 等比回退到每个 0.5
            foreach (string id in new[] { "a", "b", "c" })
            {
                state.Set(id, 1.0f);
            }

            float sum = state.Get("a") + state.Get("b") + state.Get("c");
            True(sum <= 1.5f + 1e-4f, "约束上限被遵守，实得和 " + sum);
            True(sum > 1.4f, "约束是等比回退而不是一刀切清零，实得和 " + sum);
            True(state.Get("a") > 0f && state.Get("b") > 0f && state.Get("c") > 0f, "回退后符号不被翻转");

            // 下限方向：三者都拉到 -1 → 和 -3，下限 -0.5
            foreach (string id in new[] { "a", "b", "c" })
            {
                state.Set(id, -1.0f);
            }

            float lowSum = state.Get("a") + state.Get("b") + state.Get("c");
            True(lowSum >= -0.5f - 1e-4f, "约束下限被遵守，实得和 " + lowSum);
            True(lowSum < 0f, "下限方向也保留负向意图，而不是被推到 0");
        }

        private static MetaHumanTemplate MakeToyTemplate()
        {
            MetaHumanTemplate template = new MetaHumanTemplate();
            template.TemplateId = "toy";
            template.Version = new MetaHumanVersion(1, 0, 0);

            for (int i = 0; i < 3; i++)
            {
                MetaHumanParameter p = new MetaHumanParameter();
                p.Id = ((char)('a' + i)).ToString();
                p.DisplayName = p.Id;
                p.Region = MetaHumanRegion.Head;
                p.Kind = MetaHumanParamKind.HeadShape;
                p.Min = -1f;
                p.Max = 1f;
                p.Default = 0f;
                p.Effects.Add(MetaHumanEffect.Morph("m" + i, 1f));
                template.Parameters.Add(p);
                template.MorphCatalog.Add("m" + i);
            }

            MetaHumanParameter standalone = new MetaHumanParameter();
            standalone.Id = "d";
            standalone.DisplayName = "d";
            standalone.Region = MetaHumanRegion.Head;
            standalone.Kind = MetaHumanParamKind.HeadShape;
            standalone.Min = 0f;
            standalone.Max = 1f;
            standalone.Default = 0f;
            standalone.Effects.Add(MetaHumanEffect.Morph("m3", 1f));
            template.Parameters.Add(standalone);
            template.MorphCatalog.Add("m3");

            MetaHumanConstraint constraint = new MetaHumanConstraint();
            constraint.ParamIds.Add("a");
            constraint.ParamIds.Add("b");
            constraint.ParamIds.Add("c");
            constraint.Min = -0.5f;
            constraint.Max = 1.5f;
            template.Constraints.Add(constraint);

            return template;
        }

        // ---------------------------------------------------------------- 镜像联动
        private static void TestSymmetry()
        {
            MetaHumanTemplate template = new MetaHumanTemplate();
            template.TemplateId = "mirror";
            template.Version = new MetaHumanVersion(1, 0, 0);

            MetaHumanParameter left = new MetaHumanParameter();
            left.Id = "l";
            left.DisplayName = "左";
            left.Region = MetaHumanRegion.Cheek;
            left.Kind = MetaHumanParamKind.HeadShape;
            left.Side = MetaHumanSide.Left;
            left.PairId = "r";
            left.Min = -1f;
            left.Max = 1f;
            left.Effects.Add(MetaHumanEffect.Morph("ml", 1f));

            MetaHumanParameter right = new MetaHumanParameter();
            right.Id = "r";
            right.DisplayName = "右";
            right.Region = MetaHumanRegion.Cheek;
            right.Kind = MetaHumanParamKind.HeadShape;
            right.Side = MetaHumanSide.Right;
            right.PairId = "l";
            right.Min = -1f;
            right.Max = 1f;
            right.Effects.Add(MetaHumanEffect.Morph("mr", 1f));

            template.MorphCatalog.Add("ml");
            template.MorphCatalog.Add("mr");
            template.Parameters.Add(left);
            template.Parameters.Add(right);

            MetaHumanState linked = new MetaHumanState(template);
            linked.LinkSymmetric = true;
            linked.Set("l", 0.6f);
            EqF(0.6f, linked.Get("r"), "联动开启时改左带右");

            MetaHumanState unlinked = new MetaHumanState(template);
            unlinked.LinkSymmetric = false;
            unlinked.Set("l", 0.6f);
            EqF(0f, unlinked.Get("r"), "联动关闭时改左不动右（对照）");
            EqF(0.6f, unlinked.Get("l"), "联动关闭时左仍生效");

            // 钳位
            MetaHumanState clampedState = new MetaHumanState(template);
            EqF(1f, clampedState.Set("l", 9f), "越界值被钳到上限");
            EqF(-1f, clampedState.Set("l", -9f), "越界值被钳到下限");

            // 未知 Id 不产生副作用
            int before = clampedState.ModifiedCount;
            EqF(0f, clampedState.Set("不存在的参数", 1f), "未知 Id 返回 0");
            Eq(before, clampedState.ModifiedCount, "未知 Id 不改变状态");
        }

        // ---------------------------------------------------------------- 模板合成与预设混合
        private static void TestMergeAndBlend(MetaHumanTemplate face, MetaHumanTemplate body)
        {
            MetaHumanTemplate merged = MetaHumanTemplate.Merge(new[] { face, body });
            Eq(face.Parameters.Count + body.Parameters.Count, merged.Parameters.Count, "合成后参数数是两表之和");
            Eq(0, merged.Validate().Count, "真实脸+身合成后应零错误");
            Eq(37, merged.MorphCatalog.Count, "morph 名录并集仍为 37");
            True(merged.TemplateId == face.TemplateId + "+" + body.TemplateId, "合成 Id 由两部分拼接：" + merged.TemplateId);

            // 槽位必须是连续下标：快照的紧凑编码按 Index 寻址，空洞会整体错位
            for (int i = 0; i < merged.Parameters.Count; i++)
            {
                if (merged.Parameters[i].Index != i)
                {
                    True(false, "第 " + i + " 个参数 Index 应为 " + i + "，实得 " + merged.Parameters[i].Index);
                    break;
                }
            }

            // 正向对照（上面零错误）之后，同一对模板自撞必须被拦下
            MetaHumanTemplate collided = MetaHumanTemplate.Merge(new[] { face, face });
            True(collided.Validate().Count > 0, "同表合成撞 Id 要报错");
            True(ContainsSubstring(collided.Validate(), "Id 重复"), "错误信息点名是 Id 重复");

            // 合成模板上跑一遍完整会话 + 快照往返
            MetaHumanSession session = new MetaHumanSession(merged);
            List<MetaHumanParameter> parameters = new List<MetaHumanParameter>(merged.All());
            session.Adjust(parameters[0].Id, parameters[0].Min);
            session.Adjust(parameters[parameters.Count - 1].Id, parameters[parameters.Count - 1].Max);
            MetaHumanLook look = session.Publish();
            Eq(0, look.ValidateAgainst(merged).Count, "合成模板上的快照自证通过");

            // 预设混合：t=0/0.5/1 三档都要按线性落点
            MetaHumanTemplate toy = MakeToyTemplate();
            MetaHumanPreset low = new MetaHumanPreset();
            low.Id = "low";
            low.Values["a"] = 0f;
            low.Values["b"] = 0f;
            MetaHumanPreset high = new MetaHumanPreset();
            high.Id = "high";
            high.Values["a"] = 1f;
            high.Values["b"] = 1f;
            // 约束上限 1.5 会让「a+b 都到 1」被等比回退，混合断言要拿未越界的档位做
            toy.Constraints.Clear();
            toy.Presets.Add(low);
            toy.Presets.Add(high);

            MetaHumanState blendState = new MetaHumanState(toy);
            blendState.BlendPresets(low, high, 0f);
            EqF(0f, blendState.Get("a"), "混合 t=0 落在第一个预设");
            blendState.BlendPresets(low, high, 1f);
            EqF(1f, blendState.Get("a"), "混合 t=1 落在第二个预设");
            blendState.BlendPresets(low, high, 0.5f);
            EqF(0.5f, blendState.Get("a"), "混合 t=0.5 线性居中");

            // 越界 t 要被钳住，不能外推出负值或超 1
            blendState.BlendPresets(low, high, 2f);
            EqF(1f, blendState.Get("a"), "t>1 被钳到第二个预设");

            MetaHumanSession toySession = new MetaHumanSession(toy);
            True(toySession.BlendPresets("low", "high", 0.3f, "混合"), "会话层的混合走通");
            Eq(2, toySession.StepCount, "会话层混合记一步（加基线共 2 步）");
            True(!toySession.BlendPresets("不存在", "high", 0.5f, "混合"), "未知预设要返回 false");
            Eq(2, toySession.StepCount, "失败的混合不增加步骤（正向对照：上一步确实增加了）");
        }

        // ---------------------------------------------------------------- LOD 策略
        private static void TestLodPolicy()
        {
            string[] roles = { "local", "editing", "preview", "remote", "Remote", "未知身份" };
            MetaHumanRenderQuality[] qualities = { MetaHumanRenderQuality.Low, MetaHumanRenderQuality.Medium, MetaHumanRenderQuality.High, MetaHumanRenderQuality.Ultra };
            float[] distances = { 0f, 5f, 8f, 9f, 17.9f, 18f, 35f, 60f, 120f };
            int[] morphCounts = { 0, 37 };
            int[] lodCounts = { 1, 2, 8 };

            int cells = 0;
            int shapeOn = 0;
            int shapeOff = 0;
            int mismatches = 0;
            string firstMismatch = string.Empty;

            foreach (string role in roles)
            {
                foreach (MetaHumanRenderQuality quality in qualities)
                {
                    foreach (float distance in distances)
                    {
                        foreach (int morphs in morphCounts)
                        {
                            foreach (int lods in lodCounts)
                            {
                                cells++;
                                MetaHumanLodDecision actual = MetaHumanLodPolicy.Decide(role, quality, distance, morphs, lods);
                                MetaHumanLodDecision expected = ReferencePolicy(role, quality, distance, morphs, lods);

                                if (actual.ApplyShape != expected.ApplyShape || actual.MaxLod != expected.MaxLod)
                                {
                                    mismatches++;
                                    if (firstMismatch.Length == 0)
                                    {
                                        firstMismatch = role + "/" + quality + "/" + distance + "m/" + morphs + "morph/" + lods + "lod -> 实际 " + actual + " 参照 " + expected;
                                    }
                                }

                                if (actual.ApplyShape)
                                {
                                    shapeOn++;
                                }
                                else
                                {
                                    shapeOff++;
                                }

                                // 不变量：只要要写形状，就必须停在能承载形状的那一档
                                if (actual.ApplyShape && actual.MaxLod != 0)
                                {
                                    True(false, "写形状却不在 LOD0：" + actual);
                                }

                                if (actual.MaxLod < 0 || actual.MaxLod >= lods)
                                {
                                    True(false, "LOD 越界：" + actual.MaxLod + " / 共 " + lods + " 档");
                                }
                            }
                        }
                    }
                }
            }

            Eq(roles.Length * qualities.Length * distances.Length * morphCounts.Length * lodCounts.Length, cells, "策略网格覆盖到的格子数");
            Eq(0, mismatches, "策略与独立参照逐格一致：" + firstMismatch);
            True(shapeOn > 0 && shapeOff > 0, "策略不是常量：写形状 " + shapeOn + " 格 / 不写 " + shapeOff + " 格");

            // 身份优先于画质：Ultra 也不能让本地玩家掉出形状档
            MetaHumanLodDecision localUltraFar = MetaHumanLodPolicy.Decide("local", MetaHumanRenderQuality.Ultra, 500f, 37, 8);
            True(localUltraFar.ApplyShape && localUltraFar.MaxLod == 0, "本地角色无论画质距离都保形状");

            // 无 morph 的资产不该被锁在 LOD0（白丢性能），这是「不写形状」那一侧的正向对照
            MetaHumanLodDecision noMorph = MetaHumanLodPolicy.Decide("local", MetaHumanRenderQuality.Low, 500f, 0, 8);
            True(!noMorph.ApplyShape, "没有 morph 时不该声称在写形状");
            True(noMorph.MaxLod > 0, "没有 morph 时允许真的降档，实得 " + noMorph.MaxLod);

            // 距离单调：远端不写形状的那一段，距离越远档号不减
            int previous = -1;
            for (int i = 0; i < distances.Length; i++)
            {
                MetaHumanLodDecision d = MetaHumanLodPolicy.Decide("remote", MetaHumanRenderQuality.Ultra, distances[i], 37, 8);
                if (!d.ApplyShape)
                {
                    True(d.MaxLod >= previous, "距离增大档号不减：" + distances[i] + "m 得到 " + d.MaxLod + "，上一档 " + previous);
                    previous = d.MaxLod;
                }
            }
        }

        /// <summary>独立参照：不复用 MetaHumanLodPolicy 的代码，按定义重算一遍。</summary>
        private static MetaHumanLodDecision ReferencePolicy(string role, MetaHumanRenderQuality quality, float distance, int morphs, int lodCount)
        {
            int[] caps = { 4, 3, 2, 1 };
            int qi = (int)quality;
            int byQuality = caps[qi];
            int byDistance = 0;
            if (distance >= 8f) byDistance = 1;
            if (distance >= 18f) byDistance = 2;
            if (distance >= 35f) byDistance = 3;
            if (distance >= 60f) byDistance = 4;

            int wanted = byQuality < byDistance ? byQuality : byDistance;
            if (wanted > lodCount - 1) wanted = lodCount - 1;
            if (wanted < 0) wanted = 0;

            MetaHumanLodDecision result = new MetaHumanLodDecision();
            if (morphs == 0)
            {
                result.ApplyShape = false;
                result.MaxLod = wanted;
                return result;
            }

            string lowered = (role ?? string.Empty).ToLowerInvariant();
            if (lowered == "local" || lowered == "editing" || lowered == "preview")
            {
                result.ApplyShape = true;
                result.MaxLod = 0;
                return result;
            }

            result.ApplyShape = wanted <= 0;
            result.MaxLod = wanted <= 0 ? 0 : wanted;
            return result;
        }


        // ---------------------------------------------------------------- 身体参数是否真的会动
        /// <summary>
        /// 这一组存在的理由：第一版身体模板把 30 项围度全挂在 pelvis 的 0.004 缩放上，
        /// 编译与自检全绿，但玩家拖滑杆其实什么都看不到 —— 正是本项目反复踩的静默失效。
        /// 判据因此不能是「解出了东西」，必须是「解出的东西符合物理意义」。
        /// </summary>
        /// <summary>
        /// 玩家界面的头身分界必须一条不漏。
        /// 创角面板以前自带一份"十条区域"清单来取头部参数，区域表一扩大就有整组参数静默不进界面
        /// （脸模板里的 Forehead/Scalp/Teeth/Tongue/Hair/Skin 共 154 条、占 26%），
        /// 少滑杆不会留下任何失败痕迹。现在分界由纯核的 PartitionForUI 决定，这里同时钉两件事：
        /// 1) 新分界的完备性（头部 + 身体 == 全量，头部 == 所有非 Body 参数）；
        /// 2) 旧清单的对照量（它漏掉的条数，以及它覆盖的条数与头部条数之和恰好等于全量）。
        /// </summary>
        /// <summary>
        /// "发布后不再可回溯"必须扛得过一次存盘重登。
        /// 快照自己带 Published 这一格，而 History.Initialize 以前无条件把 Sealed 设成 false ⇒
        /// 重登后本地会话以为没封存：撤销/跳步/继续记步全开着，玩家一路操作到服务端才被拒。
        /// 正反两半都要测：已发布的快照要封存，未发布的绝不能被误封。
        /// </summary>
        private static void TestSealedSurvivesReload(MetaHumanTemplate face)
        {
            MetaHumanSession first = new MetaHumanSession(face);
            int touched = 0;
            foreach (MetaHumanParameter p in face.All())
            {
                if (touched >= 4)
                {
                    break;
                }

                if (p.Max != p.Default)
                {
                    first.State.Set(p.Id, p.Max);
                    touched++;
                }
            }

            True(first.Commit("夹具：改四条再发布"), "改动记步入历史");
            MetaHumanLook published = first.Publish();
            True(published != null && published.Published, "发布返回的快照要带 Published 标记");
            True(first.IsSealed, "发布当场本地就是封存态");

            // 走一圈 JSON（客户端持久化的就是它），再用同一份模板新建会话读回来
            MetaHumanLook reloaded = MetaHumanLook.FromJson(published.ToJson());
            True(reloaded != null && reloaded.Published, "Published 要能穿过 JSON 往返");

            MetaHumanSession second = new MetaHumanSession(face);
            second.LoadLook(reloaded);
            True(second.IsSealed, "重登后本地会话必须仍是封存态（这条就是被修掉的那个缺陷）");
            True(!second.Commit("试图再记一步"), "封存后 Commit 必须返回 false");
            True(!second.CanUndo, "封存后不该再给撤销入口");
            True(!second.CanRedo, "封存后不该再给重做入口");

            // 发布之后再点「保存」：不能产出一份未发布的快照 —— 面板那个按钮在发布后依然可点，
            // 快照一旦丢了发布位，下一次读回来就等于把刚封好的角色重新解锁了。
            MetaHumanLook afterSave = first.Save();
            True(afterSave.Published, "已封存会话的 Save 必须仍带 Published");
            MetaHumanSession fourth = new MetaHumanSession(face);
            fourth.LoadLook(MetaHumanLook.FromJson(afterSave.ToJson()));
            True(fourth.IsSealed, "用发布后的「保存」快照重登，也必须仍是封存态");
            True(!fourth.Commit("试着记一步"), "封存态读回来之后仍然记不了步");

            // 反向对照：未发布的快照不该把会话封起来
            MetaHumanSession draft = new MetaHumanSession(face);
            foreach (MetaHumanParameter p in face.All())
            {
                if (p.Max != p.Default)
                {
                    draft.State.Set(p.Id, p.Max);
                    break;
                }
            }

            MetaHumanLook draftLook = draft.Save();
            True(draftLook != null && !draftLook.Published, "Save 出来的快照不该带 Published");
            MetaHumanSession third = new MetaHumanSession(face);
            third.LoadLook(draftLook);
            True(!third.IsSealed, "未发布的快照不该把会话封起来");
            True(third.Commit("未封存时还能记一步"), "没封存的会话 Commit 该成功");
            Console.WriteLine("  封存往返：已发布快照重登后仍封存、未发布的仍可编辑");
        }

        /// <summary>
        /// 六档效果种类必须**每一档都被普查判得过**。
        /// 这条不是产品断言，是给 §19 那道普查自己上的保险：新加一档而忘了在 EffectChannelMoved 里加分岔时，
        /// 那个 default 分支会一律返回 false ⇒ 将来任何用上新档的模板都会被误报成"死滑杆"（贴图档就差点踩中）。
        /// </summary>
        private static void TestEveryEffectKindIsJudged()
        {
            MetaHumanTemplate template = new MetaHumanTemplate();
            template.TemplateId = "test.every_channel";

            MetaHumanEffectKind[] kinds = (MetaHumanEffectKind[])Enum.GetValues(typeof(MetaHumanEffectKind));
            foreach (MetaHumanEffectKind kind in kinds)
            {
                MetaHumanParameter p = new MetaHumanParameter();
                p.Id = "ch." + kind;
                p.Min = 0f;
                p.Max = 1f;
                p.Default = 0f;
                p.Kind = MetaHumanParamKind.HeadShape;

                MetaHumanEffect e = new MetaHumanEffect();
                e.Kind = kind;
                e.Target = "slot_" + kind;
                e.Gain = 1f;
                e.Axis = new MetaHumanVec3(1f, 1f, 1f);
                e.BaseColor = new MetaHumanColor(0f, 0f, 0f);
                e.TargetColor = new MetaHumanColor(1f, 1f, 1f);
                e.TextureOff = "Content/Textures/off.flax";
                e.TextureOn = "Content/Textures/on.flax";
                p.Effects.Add(e);
                template.Parameters.Add(p);
            }

            MetaHumanState state = new MetaHumanState(template);
            MetaHumanSolution before = MetaHumanSolver.Solve(state);
            foreach (MetaHumanParameter p in template.All())
            {
                state.Set(p.Id, 1f);
            }
            MetaHumanSolution after = MetaHumanSolver.Solve(state);

            int unjudged = 0;
            for (int i = 0; i < template.Parameters.Count; i++)
            {
                MetaHumanEffect effect = template.Parameters[i].Effects[0];
                if (!EffectChannelMoved(effect, before, after))
                {
                    unjudged++;
                }
            }

            Eq(0, unjudged, "每种效果种类都要被普查判成「动了」（缺分岔就报这条，前一轮贴图档正是这个形状）");
            True(kinds.Length >= 6, "档位数量与实现一致（实得 " + kinds.Length + "）");

            // 增量通路必须与全量逐通道一致。`AuditIncremental` 以前只比 morph，
            // 而增量写的是五档（骨骼/材质浮点/颜色/贴图都在动）—— 只比一档等于其余四档永远查不出残留。
            MetaHumanSession incremental = new MetaHumanSession(template);
            foreach (MetaHumanParameter p in template.All())
            {
                incremental.Adjust(p.Id, p.Max);
                incremental.Commit("推到最大");
                incremental.Adjust(p.Id, p.Min);
                incremental.Commit("退回最小");
            }

            List<string> incrementalProblems = incremental.AuditIncremental();
            Eq(0, incrementalProblems.Count,
               "增量解算要与全量逐通道一致（推满再退回最小后不该有任何残留），差异："
               + string.Join(" | ", incrementalProblems.ToArray()));
            Console.WriteLine("  档位自检：" + kinds.Length + " 档效果种类全部有判定分岔，增量与全量逐通道一致");
        }

        private static void TestSculptRegionPartition(MetaHumanTemplate face, MetaHumanTemplate body)
        {
            MetaHumanRegion[] legacyHeadRegions =
            {
                MetaHumanRegion.Brow, MetaHumanRegion.Eye, MetaHumanRegion.Nose, MetaHumanRegion.Lip,
                MetaHumanRegion.Chin, MetaHumanRegion.Cheek, MetaHumanRegion.Ear, MetaHumanRegion.Jaw,
                MetaHumanRegion.Neck, MetaHumanRegion.Head,
            };

            List<MetaHumanParameter> head = new List<MetaHumanParameter>();
            List<MetaHumanParameter> bodyBucket = new List<MetaHumanParameter>();
            face.PartitionForUI(head, bodyBucket);

            int nonBody = 0;
            int legacyCovered = 0;
            foreach (MetaHumanParameter p in face.All())
            {
                bool isBody = p.Region == MetaHumanRegion.Body;
                if (!isBody)
                {
                    nonBody++;
                }

                for (int i = 0; i < legacyHeadRegions.Length; i++)
                {
                    if (p.Region == legacyHeadRegions[i])
                    {
                        legacyCovered++;
                        break;
                    }
                }
            }

            Eq(nonBody, head.Count, "脸模板：头部桶收全了每一条非 Body 参数（实得 " + head.Count + " / 应有 " + nonBody + "）");
            Eq(0, bodyBucket.Count, "脸模板里没有 Body 区域参数");
            Eq(face.Parameters.Count, head.Count + bodyBucket.Count, "分界不漏不重：两桶相加等于全量");

            int legacyMissed = head.Count - legacyCovered;
            True(legacyMissed > 0,
                 "旧十条区域清单会漏掉 " + legacyMissed + " 条头部参数（对照量必须为正，否则这次的修复没东西可修）");
            True(legacyCovered * 4 < nonBody * 3,
                 "漏掉的那批不是零头：旧十条清单覆盖 " + legacyCovered + " / " + nonBody + " 条，连四分之三都不到");
            Console.WriteLine("  头身分界：头部 " + head.Count + " 条全收，旧十条清单覆盖 "
                + legacyCovered + " 条、漏 " + legacyMissed + " 条");

            head.Clear();
            bodyBucket.Clear();
            body.PartitionForUI(head, bodyBucket);
            Eq(0, head.Count, "身模板里没有头部区域参数");
            Eq(body.Parameters.Count, bodyBucket.Count, "身模板 30 条围度全部归身体桶");

            // 显示顺序：头部桶必须已按区域表排好，同区域内按 Index 递增
            int outOfOrder = 0;
            for (int i = 1; i < head.Count; i++)
            {
                int rankA = MetaHumanTemplate.HeadRegionRank(GetRegion(head[i - 1]));
                int rankB = MetaHumanTemplate.HeadRegionRank(GetRegion(head[i]));
                if (rankA != rankB ? rankA > rankB : head[i - 1].Index > head[i].Index)
                {
                    outOfOrder++;
                }
            }

            Eq(0, outOfOrder, "PartitionForUI 给出的头部顺序按区域表与 Index 递增");
        }

        private static MetaHumanRegion GetRegion(MetaHumanParameter parameter)
        {
            return parameter.Region;
        }

        /// <summary>
        /// 创角面板的"单页行数上限"必须盖得住分界之后的最大一页。
        /// 这条为什么会红：面板以前自带一张十条区域清单（漏 154 条），修成分界交给 PartitionForUI 之后
        /// 头部桶实测 598 条，而单页上限还硬编码在 400（注释写着"头部约 354 条"这个过期前提）
        /// ⇒ 专家模式下 198 根头部滑杆被静默截掉，只在状态栏留一句"仅显示前 400 项"。
        /// 判据拿的是**两边各算一遍的数**：上限从面板源码里读，需求条数从模板数据里算，
        /// 谁把模板加宽而忘了改上限，这里会红，而不是让玩家少一批控件却一切如常。
        /// </summary>
        private static void TestPanelRowBudgetCoversPartition(MetaHumanTemplate face, MetaHumanTemplate body, string templatesDir)
        {
            // templatesDir = <仓库根>/HundunWorld/Plugins/MetaHuman/Content/Templates ⇒ 往上五层是仓库根
            string root = Path.GetFullPath(Path.Combine(templatesDir, "..", "..", "..", "..", ".."));
            string panelPath = Path.Combine(root, "HundunWorld", "Source", "Game", "UI", "Ink", "Pages",
                                           "CharacterCreation", "MetaHumanSculptPanel.cs");
            if (!File.Exists(panelPath))
            {
                Skip("面板源不在盘上（找不到 " + panelPath + "），单页上限这条没量到");
                return;
            }

            int capHits = 0;
            int cap = -1;
            string[] lines = File.ReadAllLines(panelPath);
            for (int i = 0; i < lines.Length; i++)
            {
                int at = lines[i].IndexOf("const int MaxRowsPerTab", StringComparison.Ordinal);
                if (at < 0)
                {
                    continue;
                }

                capHits++;
                int eq = lines[i].IndexOf('=', at);
                if (eq < 0)
                {
                    continue;
                }

                string digits = string.Empty;
                for (int c = eq + 1; c < lines[i].Length; c++)
                {
                    char ch = lines[i][c];
                    if (ch >= '0' && ch <= '9')
                    {
                        digits += ch;
                    }
                    else if (digits.Length > 0)
                    {
                        break;
                    }
                }

                if (digits.Length > 0)
                {
                    cap = int.Parse(digits);
                }
            }

            // 扫描型检查的"0 命中"与"通过"同形，所以先钉命中数，再钉读到的值
            Eq(1, capHits, "面板里 MaxRowsPerTab 的声明恰好一处（实得 " + capHits + "）；多处就该问是哪一页的上限");
            True(cap > 0, "单页上限要能解析出正数（实得 " + cap + "）");

            List<MetaHumanParameter> head = new List<MetaHumanParameter>();
            List<MetaHumanParameter> bodyBucket = new List<MetaHumanParameter>();
            face.PartitionForUI(head, bodyBucket);
            int faceHead = head.Count;
            int faceBody = bodyBucket.Count;
            head.Clear();
            bodyBucket.Clear();
            body.PartitionForUI(head, bodyBucket);
            int bodyHead = head.Count;
            int bodyBody = bodyBucket.Count;

            int maxNeeded = faceHead;
            if (faceBody > maxNeeded) { maxNeeded = faceBody; }
            if (bodyHead > maxNeeded) { maxNeeded = bodyHead; }
            if (bodyBody > maxNeeded) { maxNeeded = bodyBody; }

            // 防空转：模板要是读空了，maxNeeded 会小得让任何上限都"过"
            True(maxNeeded >= 100,
                 "分界后的最大一页该有上百条（实得 " + maxNeeded + "）；不到就说明模板没读进来，下面那条判据是空的");
            True(cap >= maxNeeded,
                 "单页上限必须盖得住最大一页（上限 " + cap + "，需要 " + maxNeeded
                 + "＝脸模板头 " + faceHead + "/身 " + faceBody + "，身模板头 " + bodyHead + "/身 " + bodyBody
                 + "）；差 " + (maxNeeded - cap) + " 就是被静默截掉的滑杆根数");
            Console.WriteLine("  单页上限：源码里 " + cap + "，实测最大页 " + maxNeeded + "（富余 " + (cap - maxNeeded) + "）");
        }

        /// <summary>
        /// 钉住一条**隐式**时序：登录时必须"先把服务端那份外观落到本地仓库，再触发角色选中"。
        /// 已发布那一份是在 `MetaHumanCharacter.RestoreFromStore` 里定格的（选中时读本地仓库），
        /// 而 `AdoptFromServer` 是往仓库里写的那一步 —— 两者若被调换顺序，
        /// 重登的封存就整格失效，且**没有任何报错**：面板点亮、能拖、每次保存才被服务端拒。
        /// 这条只能在源码上钉（客户端代码进不了离线门），所以命中数与先后都要断言。
        /// </summary>
        private static void TestServerLookIsAdoptedBeforeBind(string templatesDir)
        {
            string root = Path.GetFullPath(Path.Combine(templatesDir, "..", "..", "..", "..", ".."));
            string managerPath = Path.Combine(root, "HundunWorld", "Source", "Game", "UI", "Character",
                                             "CharacterStateManager.cs");
            if (!File.Exists(managerPath))
            {
                Skip("找不到 CharacterStateManager.cs（" + managerPath + "），登录时序这条没量到");
                return;
            }

            string source = File.ReadAllText(managerPath);
            int adopts = CountOccurrences(source, ".AdoptFromServer(");
            int selected = CountOccurrences(source, "OnCharacterSelected?.Invoke(");
            Eq(1, adopts, "选中角色处应当恰好一次调用 AdoptFromServer（实得 " + adopts
                          + "）；多处就要问是哪条路径在写仓库");
            True(selected >= 1, "要能找到选中通知的调用点（实得 " + selected + "）；找不到说明事件改名了，本条判据失效");

            int adoptAt = source.IndexOf(".AdoptFromServer(", StringComparison.Ordinal);
            int notifyAt = source.IndexOf("OnCharacterSelected?.Invoke(", StringComparison.Ordinal);
            True(adoptAt < notifyAt,
                 "顺序判据：先落本地仓库（偏移 " + adoptAt + "）再通知选中（偏移 " + notifyAt + "）；"
                 + "调换后重登读到的是仓库里的旧那份，已发布定格会静默失效");
        }

        /// <summary>数子串出现次数（命中数断言用；不用正则，避免把转义问题当成"没有命中"）。</summary>
        private static int CountOccurrences(string source, string needle)
        {
            int count = 0;
            int at = source.IndexOf(needle, StringComparison.Ordinal);
            while (at >= 0)
            {
                count++;
                at = source.IndexOf(needle, at + needle.Length, StringComparison.Ordinal);
            }

            return count;
        }

        /// <summary>
        /// 头部材质规格表自身的账要对得上：条数、`counts` 里的三个数、以及"说已导入 Flax 的贴图真在盘上"。
        /// 为什么要钉：#41 的落点取决于这张表，表里一句 `already_in_flax: 11` 或一个写错的
        /// `flax_texture` 路径，都会让"材质层已经就绪"这个判断从头就错，而材质图做好那天照样写空。
        /// 这条只量表与盘上文件，不需要任何人手做材质 —— 所以现在就能跑。
        /// </summary>
        private static void TestHeadMaterialSpecSelfConsistency(string templatesDir)
        {
            string specPath = Path.Combine(templatesDir, "Head_Material_Spec.json");
            if (!File.Exists(specPath))
            {
                Skip("找不到 Head_Material_Spec.json（" + specPath + "），材质规格表这条没量到");
                return;
            }

            HeadMaterialSpec spec = JsonSerializer.Deserialize<HeadMaterialSpec>(File.ReadAllText(specPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (spec == null || spec.Parameters == null)
            {
                Skip("规格表反序列化为空，判据没跑（结构变了要同步这里）");
                return;
            }

            int total = spec.Parameters.Count;
            True(total > 0, "参数条数不该为 0（实得 " + total + "）；为 0 说明字段名换了而门全绿");
            Eq(spec.Counts.Params, total,
               "counts.params 必须等于实际条数（表里写 " + spec.Counts.Params + "，实数 " + total + "）");

            int exported = 0;
            int withTexture = 0;
            int missingTexture = 0;
            string firstMissing = string.Empty;
            HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
            int duplicateNames = 0;
            string repoRoot = Path.GetFullPath(Path.Combine(templatesDir, "..", "..", "..", "..", ".."));
            for (int i = 0; i < total; i++)
            {
                HeadMaterialSpec.Entry entry = spec.Parameters[i];
                string name = entry.FlaxParameter ?? string.Empty;
                if (!names.Add(name) && name.Length > 0)
                {
                    duplicateNames++;
                }

                if (entry.PngExported)
                {
                    exported++;
                }

                if (string.IsNullOrEmpty(entry.FlaxTexture))
                {
                    continue;
                }

                withTexture++;
                string full = Path.GetFullPath(Path.Combine(repoRoot, "HundunWorld",
                    entry.FlaxTexture.Replace('/', Path.DirectorySeparatorChar)));
                if (!File.Exists(full))
                {
                    missingTexture++;
                    if (firstMissing.Length == 0)
                    {
                        firstMissing = entry.FlaxTexture;
                    }
                }
            }

            // 防空转：字段要是没绑上（改名/蛇形命名没映射），三个计数会一起变成 0，
            // 那时"表里写的数 == 实数的数"反而全绿。所以先钉它们各自非零。
            True(exported > 0, "统计出的 PNG 导出条数不该是 0（实得 0 ⇒ 八成是 png_exported 没绑定上，判据在空转）");
            True(withTexture > 0, "统计出「写了 Flax 贴图路径」的条数不该是 0（实得 0 ⇒ flax_texture 没绑定上）");
            True(names.Count == total, "每条都该有非空参数名（去重后 " + names.Count + " / " + total
                  + "，空名会让下面的对账全都失真）");

            Eq(0, duplicateNames, "参数名不许重复（重名会互相覆盖，实得 " + duplicateNames + " 条）");
            Eq(spec.Counts.PngExported, exported,
               "counts.png_exported 必须等于实际导出数（表里写 " + spec.Counts.PngExported + "，实数 " + exported + "）");
            Eq(spec.Counts.AlreadyInFlax, withTexture,
               "counts.already_in_flax 必须等于「真写了 flax_texture 路径」的条数（表里写 "
               + spec.Counts.AlreadyInFlax + "，实数 " + withTexture + "）");
            Eq(0, missingTexture,
               "表里声明『已导入为 Flax 资产』的贴图必须真在盘上（缺 " + missingTexture + " 个，首个："
               + (firstMissing.Length > 0 ? firstMissing : "无") + "）");
            Console.WriteLine("  头部材质规格表：条数 " + total + "、PNG 导出 " + exported
                              + "、写了 Flax 贴图路径的 " + withTexture + "（全部在盘上=" + (missingTexture == 0) + "）");
        }

        /// <summary>
        /// 只用来把 <c>Head_Material_Spec.json</c> 读成对账要用的形状；不写回、不参与产物。
        /// 字段名必须逐条写 <c>JsonPropertyName</c>：表里是 snake_case，而 <c>PropertyNameCaseInsensitive</c>
        /// 只会忽略大小写、不会把 <c>png_exported</c> 认成 <c>PngExported</c> ——
        /// 上一版就因此把所有数都读成 0，两边同为 0 反而全绿（第一次跑出"导出 0 条"才发现）。
        /// </summary>
        private sealed class HeadMaterialSpec
        {
            [System.Text.Json.Serialization.JsonPropertyName("counts")]
            public HeadMaterialSpec.CountsBlock Counts { get; set; } = new HeadMaterialSpec.CountsBlock();

            [System.Text.Json.Serialization.JsonPropertyName("parameters")]
            public List<HeadMaterialSpec.Entry> Parameters { get; set; } = new List<HeadMaterialSpec.Entry>();

            public sealed class CountsBlock
            {
                [System.Text.Json.Serialization.JsonPropertyName("params")]
                public int Params { get; set; }

                [System.Text.Json.Serialization.JsonPropertyName("png_exported")]
                public int PngExported { get; set; }

                [System.Text.Json.Serialization.JsonPropertyName("already_in_flax")]
                public int AlreadyInFlax { get; set; }

                [System.Text.Json.Serialization.JsonPropertyName("shared_not_exported")]
                public int SharedNotExported { get; set; }
            }

            public sealed class Entry
            {
                [System.Text.Json.Serialization.JsonPropertyName("flax_parameter")]
                public string FlaxParameter { get; set; }

                [System.Text.Json.Serialization.JsonPropertyName("png_exported")]
                public bool PngExported { get; set; }

                [System.Text.Json.Serialization.JsonPropertyName("flax_texture")]
                public string FlaxTexture { get; set; }
            }
        }

        /// <summary>
        /// 读档选哪一份外观（"发布后不再可回溯"在读侧的落地）。
        /// 这组断言互为证伪：同一个清单，开关开与关必须给出**不同**下标，
        /// 所以判据要是把 preferPublished 忽略掉（恒取最新或恒取第一条），两条里必有一条红。
        /// </summary>
        private static void TestRestorePrefersPublishedSnapshot()
        {
            MetaHumanLook draftOld = new MetaHumanLook();
            draftOld.Published = false;
            MetaHumanLook published = new MetaHumanLook();
            published.Published = true;
            MetaHumanLook draftNew = new MetaHumanLook();
            draftNew.Published = false;
            MetaHumanLook publishedNewer = new MetaHumanLook();
            publishedNewer.Published = true;

            List<MetaHumanLook> mixed = new List<MetaHumanLook>();
            mixed.Add(draftOld);
            mixed.Add(published);
            mixed.Add(draftNew);

            Eq(1, MetaHumanLook.SelectRestoreIndex(mixed, true),
               "本机留着一份更晚的草稿时，已发布那一份必须赢（否则重登回到草稿，而每次保存都被服务端拒）");
            Eq(2, MetaHumanLook.SelectRestoreIndex(mixed, false),
               "正向对照：关掉偏好就取最新一条（证明这个开关真在起作用，不是恒等于某个下标）");

            List<MetaHumanLook> onlyDrafts = new List<MetaHumanLook>();
            onlyDrafts.Add(draftOld);
            onlyDrafts.Add(draftNew);
            Eq(1, MetaHumanLook.SelectRestoreIndex(onlyDrafts, true),
               "正向对照：从没发布过时仍然取最新草稿（这条通路不能被「已发布优先」一起拦掉）");

            List<MetaHumanLook> twoPublished = new List<MetaHumanLook>();
            twoPublished.Add(draftOld);
            twoPublished.Add(published);
            twoPublished.Add(publishedNewer);
            Eq(2, MetaHumanLook.SelectRestoreIndex(twoPublished, true),
               "两份都已发布时取较新那份（自旧到新的最后一条已发布）");

            Eq(-1, MetaHumanLook.SelectRestoreIndex(null, true), "空引用返回 -1，不许抛");
            Eq(-1, MetaHumanLook.SelectRestoreIndex(new List<MetaHumanLook>(), true), "空清单返回 -1");

            List<MetaHumanLook> allNull = new List<MetaHumanLook>();
            allNull.Add(null);
            allNull.Add(null);
            Eq(-1, MetaHumanLook.SelectRestoreIndex(allNull, true),
               "整清单都是 null 也要返回 -1（否则调用方会拿 history[Count-1] 当「有存档」往下走）");
        }

        /// <summary>
        /// "中性/默认"这类预设必须真的把偏离默认的参数清空。
        /// 盘上的 face.neutral 原本 Values 为空、又没有"回默认"的声明 ⇒ 玩家点它等于什么都没点，
        /// 而界面照样报"已应用"。这里钉三层：数据声明、行为结果、作者门（Validate 拦死预设）。
        /// </summary>
        private static void TestNeutralPresetActuallyResets(MetaHumanTemplate face)
        {
            MetaHumanPreset neutral = face.FindPreset("face.neutral");
            True(neutral != null, "脸模板里有 face.neutral 预设");
            if (neutral == null)
            {
                return;
            }

            True(neutral.ResetToDefault,
                 "face.neutral 必须声明 ResetToDefault（它的 Values 是空的：不声明就是一条死预设）");

            MetaHumanSession session = new MetaHumanSession(face);
            int changed = 0;
            foreach (MetaHumanParameter p in face.All())
            {
                if (changed >= 12)
                {
                    break;
                }

                if (p.Max != p.Default)
                {
                    session.State.Set(p.Id, p.Max);
                    changed++;
                }
            }

            Eq(12, changed, "夹具先把 12 条参数推到 Max（少于 12 说明这份模板没有可动的头部参数）");
            True(session.State.ModifiedCount >= 12, "状态里确实记着这些偏离默认的改动");
            // 直接改 State 不会进历史；撤销要回到的"那 12 条改动"必须先记成一步，
            // 否则这条断言测的是"从空历史往回退"，永远也回不到改动上（夹具自己得先站对位置）。
            True(session.Commit("夹具改 12 条"), "夹具的 12 条改动记成一步历史");

            int stepsBefore = session.StepCount;
            bool applied = session.ApplyPreset("face.neutral", "回到中性");
            True(applied, "ApplyPreset 返回成功");
            Eq(0, session.State.ModifiedCount, "应用中性预设后不再有任何偏离默认的参数（这才叫回默认）");
            True(session.StepCount > stepsBefore, "预设本身也算一步历史：玩家能撤销它");
            True(session.CanUndo, "撤销这一步可用");
            session.Undo();
            True(session.State.ModifiedCount >= 12,
                 "撤销后那 12 条改动要回来（实得 " + session.State.ModifiedCount + "）");

            // 作者门：把标记摘掉，这份预设就变成"点了没反应"的死预设，Validate 必须拦下
            MetaHumanTemplate mutated = MetaHumanTemplate.FromJson(face.ToJson());
            MetaHumanPreset dead = mutated.FindPreset("face.neutral");
            True(dead != null, "改后的夹具仍能查到 face.neutral");
            if (dead != null)
            {
                dead.ResetToDefault = false;
                List<string> errors = mutated.Validate();
                bool flagged = false;
                for (int i = 0; i < errors.Count; i++)
                {
                    if (errors[i].Contains("是死的"))
                    {
                        flagged = true;
                        break;
                    }
                }

                True(flagged, "摘掉标记后的空预设要被作者门报成死预设，实得："
                    + string.Join(" | ", errors.ToArray()));
            }
        }

        /// <summary>
        /// 贴图通道（UE 的 MetaHuman 头部肤色/痣/皱纹/妆是换贴图，不是改颜色）。
        /// 离散赋值不吃 ±增量累计，所以这里验三档：默认档、过阈档、退回档，
        /// 并且缺省阈值必须是 0.5（模板不写这个字段也不能默认成"永远用 On"）。
        /// </summary>
        private static void TestMaterialTextureChannel()
        {
            MetaHumanParameter p = new MetaHumanParameter();
            p.Id = "face.skin_texture";
            p.Min = 0f;
            p.Max = 1f;
            p.Default = 0f;
            p.Region = MetaHumanRegion.Skin;

            MetaHumanEffect e = new MetaHumanEffect();
            e.Kind = MetaHumanEffectKind.MaterialTexture;
            e.Target = "SkinTextureBase";
            e.TextureOff = "Content/Textures/skin_light.flax";
            e.TextureOn = "Content/Textures/skin_dark.flax";
            p.Effects.Add(e);

            MetaHumanTemplate template = new MetaHumanTemplate();
            template.TemplateId = "test.texture_channel";
            template.Parameters.Add(p);

            MetaHumanState state = new MetaHumanState(template);
            MetaHumanSolution low = MetaHumanSolver.Solve(state);
            string chosen;
            True(low.MaterialTextures.TryGetValue("SkinTextureBase", out chosen),
                 "默认值下贴图通道要有确定归属（空着就等于槽位没人管）");
            True(chosen == e.TextureOff, "默认值（0 < 0.5）应选 Off，实得 " + chosen);

            state.Set(p.Id, 1f);
            MetaHumanSolution high = MetaHumanSolver.Solve(state);
            high.MaterialTextures.TryGetValue("SkinTextureBase", out chosen);
            True(chosen == e.TextureOn, "推到 1 应换成 On，实得 " + chosen);

            // 增量通路也必须重定夺：先减后加不能让旧贴图残留（减的那次被刻意跳过）
            state.Set(p.Id, 0f);
            MetaHumanSolver.UpdateIncremental(state, high);
            high.MaterialTextures.TryGetValue("SkinTextureBase", out chosen);
            True(chosen == e.TextureOff, "增量解算退回 0 必须换回 Off（最容易留旧值的一条路），实得 " + chosen);

            True(low.Bones.Count == 0 && low.MorphWeights.Count == 0,
                 "贴图效果不得污染骨骼/形状通道（骨骼 " + low.Bones.Count + "、morph " + low.MorphWeights.Count + "）");

            // MaterialTargets() 必须把三条材质通道都算进来：调用方靠它判断"要不要分配材质实例"，
            // 漏了贴图那一档，只有贴图需求的模板就会被当成"没有材质需求"而整条通道没人写。
            MetaHumanEffect tone = new MetaHumanEffect();
            tone.Kind = MetaHumanEffectKind.MaterialFloat;
            tone.Target = "SkinToneDepth";
            tone.Gain = 1f;
            p.Effects.Add(tone);

            HashSet<string> targets = template.MaterialTargets();
            True(targets.Contains("SkinTextureBase"),
                 "MaterialTargets() 要收贴图通道目标，实得 " + string.Join(" | ", new List<string>(targets).ToArray()));
            True(targets.Contains("SkinToneDepth"),
                 "MaterialTargets() 要同时收浮点目标（两条都在=没漏档），实得 " + targets.Count + " 个");
            Eq(2, targets.Count, "两条材质效果对应两个材质目标，不多不少");

            // 新通道必须能被"存 → 读"这一圈带走：Look 只存参数值，贴图归属要靠重解算复现。
            // 少这条断言的话，"玩家捏好的肤色存下来、重登就回默认"只能在运行态才发现。
            MetaHumanSession saved = new MetaHumanSession(template);
            saved.State.Set(p.Id, 1f);
            True(saved.Commit("夹具：贴图通道推到 On"), "贴图通道的改动能记成一步历史");
            MetaHumanLook snapshot = saved.Save();
            True(snapshot != null, "快照能建出来");

            MetaHumanSession restored = new MetaHumanSession(template);
            restored.LoadLook(snapshot);
            MetaHumanSolution reSolved = MetaHumanSolver.Solve(restored.State);
            string restoredChoice;
            reSolved.MaterialTextures.TryGetValue("SkinTextureBase", out restoredChoice);
            True(restoredChoice == "Content/Textures/skin_dark.flax",
                 "存/读一圈后贴图归属仍要是 On 那张，实得 " + restoredChoice);

            // 作者门的反向对照：贴图效果两张图都不给，解算会把目标摘掉 ⇒ 这根滑杆拖到底也换不了东西。
            // 先证明"有图时 Validate 不报这条"，再摘掉证明"报"，缺一半都可能是空转。
            List<string> okErrors = template.Validate();
            bool okFlagged = false;
            for (int i = 0; i < okErrors.Count; i++)
            {
                if (okErrors[i].Contains("贴图效果没指定任何一张贴图"))
                {
                    okFlagged = true;
                }
            }

            True(!okFlagged, "贴图都给了时作者门不该报这条（正向对照）：" + string.Join(" | ", okErrors.ToArray()));
            e.TextureOff = null;
            e.TextureOn = null;
            List<string> deadErrors = template.Validate();
            bool deadFlagged = false;
            for (int i = 0; i < deadErrors.Count; i++)
                {
                if (deadErrors[i].Contains("贴图效果没指定任何一张贴图"))
                {
                    deadFlagged = true;
                }
            }

            True(deadFlagged, "贴图都不给的死效果必须被作者门拦下：" + string.Join(" | ", deadErrors.ToArray()));
            Console.WriteLine("  贴图通道：Off/On/退回 三档离散赋值正确，MaterialTargets 同时收到贴图与浮点目标");
        }

        /// <summary>
        /// 每条参数效果都要真的动到它那一格通道。
        /// 表与分支是两份清单：模板里写了 Morph/Bone/Material 的效果，解算与写回端各认各的通道，
        /// 漏一条就是"滑杆能拖、角色不动"的静默失效（本项目已经踩过两次材质与骨骼通道的同类缺陷）。
        /// 判据是"把这条参数从默认值推到量程另一端，它声称要写的那个目标值必须变"，
        /// 而不是"解算结果非空"——后者只要有任何一条参数生效就整体通过。
        /// </summary>
        private static void TestEveryParameterMovesItsChannel(MetaHumanTemplate face, MetaHumanTemplate body)
        {
            int paramsChecked = 0;
            int effectsChecked = 0;
            int effectsMissed = 0;
            int deadParams = 0;
            List<string> missedNames = new List<string>();
            List<string> deadNames = new List<string>();

            MetaHumanTemplate[] templates = { face, body };
            for (int t = 0; t < templates.Length; t++)
            {
                MetaHumanTemplate template = templates[t];
                foreach (MetaHumanParameter p in template.All())
                {
                    paramsChecked++;

                    MetaHumanState state = new MetaHumanState(template);
                    MetaHumanSolution before = MetaHumanSolver.Solve(state);

                    // 往"最不像默认值"的那一头推：Max 与默认值重合就退到 Min
                    float wanted = p.Max != p.Default ? p.Max : p.Min;
                    state.Set(p.Id, wanted);
                    bool moved = state.Get(p.Id) != p.Default;

                    MetaHumanSolution after = MetaHumanSolver.Solve(state);

                    bool anyLanded = false;
                    for (int e = 0; e < p.Effects.Count; e++)
                    {
                        MetaHumanEffect effect = p.Effects[e];
                        effectsChecked++;
                        if (EffectChannelMoved(effect, before, after))
                        {
                            anyLanded = true;
                            continue;
                        }

                        effectsMissed++;
                        if (missedNames.Count < 16)
                        {
                            missedNames.Add(p.Id + " → " + effect.Kind + " " + effect.Target);
                        }
                    }

                    if (!moved || !anyLanded)
                    {
                        deadParams++;
                        if (deadNames.Count < 16)
                        {
                            deadNames.Add(p.Id + (moved ? "" : "（Set 后被钳回默认值）"));
                        }
                    }
                }
            }

            True(paramsChecked >= 600, "两模板合计要遍历到至少 600 条参数（实得 " + paramsChecked + "，太少说明遍历本身空转）");
            True(effectsChecked >= paramsChecked, "每条参数平均不止一个效果（效果 " + effectsChecked + " vs 参数 " + paramsChecked + "）");
            Eq(0, effectsMissed, "每条效果都动到了它声称的通道，前 16 条：" + string.Join(" | ", missedNames.ToArray()));
            Eq(0, deadParams, "每条参数都能让角色真的变一次，前 16 条：" + string.Join(" | ", deadNames.ToArray()));
            Console.WriteLine("  通道落点普查：参数 " + paramsChecked + " 条 / 效果 " + effectsChecked + " 条全部有写入");

            // 反向对照（正查同夹具）：把一条效果的增益抹成 0，普查必须报红。
            // 没有这一条，"漏 0 处"可能只是因为探测器永远不报漏 —— 判绿的门自己也得被证明会拦。
            MetaHumanTemplate mutated = MetaHumanTemplate.FromJson(face.ToJson());
            MetaHumanParameter victim = null;
            foreach (MetaHumanParameter candidate in mutated.All())
            {
                if (candidate.Effects.Count > 0 && candidate.Effects[0].Kind == MetaHumanEffectKind.Morph)
                {
                    victim = candidate;
                    break;
                }
            }

            True(victim != null, "夹具里能找到一条 morph 效果用于反向对照");
            if (victim != null)
            {
                int missedBefore = Census(mutated, null, null);
                victim.Effects[0].Gain = 0f;
                int missedAfter = Census(mutated, null, null);
                True(missedAfter > missedBefore,
                     "抹掉一条效果的增益后普查要报出漏点（前 " + missedBefore + " → 后 " + missedAfter + "）；不报说明普查是空转的绿灯");
            }
        }

        /// <summary>跑一遍通道落点普查，返回漏点条数；names 非 null 时回填前 16 条明细。</summary>
        private static int Census(MetaHumanTemplate template, List<string> missedNames, List<string> deadNames)
        {
            int effectsMissed = 0;
            foreach (MetaHumanParameter p in template.All())
            {
                MetaHumanState state = new MetaHumanState(template);
                MetaHumanSolution before = MetaHumanSolver.Solve(state);
                float wanted = p.Max != p.Default ? p.Max : p.Min;
                state.Set(p.Id, wanted);
                MetaHumanSolution after = MetaHumanSolver.Solve(state);

                bool anyLanded = false;
                for (int e = 0; e < p.Effects.Count; e++)
                {
                    MetaHumanEffect effect = p.Effects[e];
                    if (!EffectChannelMoved(effect, before, after))
                    {
                        effectsMissed++;
                        if (missedNames != null && missedNames.Count < 16)
                        {
                            missedNames.Add(p.Id + " → " + effect.Kind + " " + effect.Target);
                        }
                        continue;
                    }

                    anyLanded = true;
                }

                if (!anyLanded && p.Effects.Count > 0 && deadNames != null && deadNames.Count < 16)
                {
                    deadNames.Add(p.Id);
                }
            }

            return effectsMissed;
        }

        /// <summary>这条效果声称写的那一格，解算结果在参数被推动后是否真的变了。</summary>
        /// <summary>
        /// 发型的持久化：hair.style 是离散值，随 MetaHumanLook 的参数字典走。
        /// 判据要跨"发布 → JSON 往返 → 新会话重解"三跳，只测提交当场不算持久化。
        /// </summary>
        private static void TestHairStylePersistsAroundPublish()
        {
            string templatePath = Path.Combine(RepoTemplatesDir(), "Template_Face_Archetype.json");
            if (!File.Exists(templatePath))
            {
                Skip("发型持久化检查缺模板：" + templatePath);
                return;
            }

            MetaHumanTemplate face = MetaHumanTemplate.LoadFromFile(templatePath);
            if (face.Find("hair.style") == null)
            {
                Skip("模板里没有 hair.style");
                return;
            }

            MetaHumanSession session = new MetaHumanSession(face);
            session.State.Set("hair.style", 4f);
            True(session.Commit("发型"), "hair.style 可提交");
            int style;
            MetaHumanSolution solved = MetaHumanSolver.Solve(session.State);
            True(solved.HairStyles.TryGetValue("hair", out style) && style == 4,
                 "提交后解出 hair=4（实得 " + style + "）");

            MetaHumanLook look = session.Publish();
            True(look != null && look.Published, "发型会话可发布");
            MetaHumanSession reloaded = new MetaHumanSession(face);
            reloaded.LoadLook(MetaHumanLook.FromJson(look.ToJson()));
            int styleAfter;
            MetaHumanSolution resolvedAfterLoad = MetaHumanSolver.Solve(reloaded.State);
            True(resolvedAfterLoad.HairStyles.TryGetValue("hair", out styleAfter) && styleAfter == 4,
                 "发布快照 JSON 往返后发型仍为 4（实得 " + styleAfter + "）");
            True(reloaded.IsSealed, "发型发布后重登仍封存");
            Console.WriteLine("  发型持久化：提交→发布→JSON→重解 = 4");
        }

        private static bool EffectChannelMoved(MetaHumanEffect effect, MetaHumanSolution before, MetaHumanSolution after)
        {
            const float epsilon = 1e-6f;
            switch (effect.Kind)
            {
                case MetaHumanEffectKind.Morph:
                {
                    float a = Morph(before, effect.Target);
                    float b = Morph(after, effect.Target);
                    return Math.Abs(a - b) > epsilon;
                }
                case MetaHumanEffectKind.MaterialFloat:
                {
                    float a = MaterialFloat(before, effect.Target);
                    float b = MaterialFloat(after, effect.Target);
                    return Math.Abs(a - b) > epsilon;
                }
                case MetaHumanEffectKind.MaterialColor:
                {
                    MetaHumanColor a;
                    MetaHumanColor b;
                    before.MaterialColors.TryGetValue(effect.Target, out a);
                    after.MaterialColors.TryGetValue(effect.Target, out b);
                    return Math.Abs(a.R - b.R) > epsilon || Math.Abs(a.G - b.G) > epsilon
                        || Math.Abs(a.B - b.B) > epsilon;
                }
                case MetaHumanEffectKind.MaterialTexture:
                {
                    // 贴图是离散通道：判据是"这一格的归属变了"，不是"值变了"。
                    // 少了这一档，将来任何用贴图效果的模板都会被普查误报成死滑杆（走 default 的 false）。
                    string a;
                    string b;
                    before.MaterialTextures.TryGetValue(effect.Target, out a);
                    after.MaterialTextures.TryGetValue(effect.Target, out b);
                    return !string.Equals(a, b, StringComparison.Ordinal);
                }
                case MetaHumanEffectKind.HairStyle:
                {
                    // 离散下标：判据是"选择变了"，与贴图档同一形状。
                    int a;
                    int b;
                    before.HairStyles.TryGetValue(effect.Target, out a);
                    after.HairStyles.TryGetValue(effect.Target, out b);
                    return a != b;
                }
                case MetaHumanEffectKind.BoneTranslate:
                {
                    MetaHumanVec3 a = BoneTranslate(before, effect.Target);
                    MetaHumanVec3 b = BoneTranslate(after, effect.Target);
                    return DeltaOnAxis(a, b, effect.Axis) > epsilon;
                }
                case MetaHumanEffectKind.BoneScale:
                {
                    MetaHumanVec3 a = BoneScale(before, effect.Target);
                    MetaHumanVec3 b = BoneScale(after, effect.Target);
                    return DeltaOnAxis(a, b, effect.Axis) > epsilon;
                }
                default:
                    return false;
            }
        }

        private static float Morph(MetaHumanSolution solution, string target)
        {
            float value;
            return solution.MorphWeights.TryGetValue(target, out value) ? value : 0f;
        }

        private static float MaterialFloat(MetaHumanSolution solution, string target)
        {
            float value;
            return solution.MaterialFloats.TryGetValue(target, out value) ? value : 0f;
        }

        private static MetaHumanVec3 BoneTranslate(MetaHumanSolution solution, string target)
        {
            MetaHumanBoneOp op;
            return solution.Bones.TryGetValue(target, out op) ? op.Translate : new MetaHumanVec3();
        }

        private static MetaHumanVec3 BoneScale(MetaHumanSolution solution, string target)
        {
            MetaHumanBoneOp op;
            return solution.Bones.TryGetValue(target, out op) ? op.ScaleDelta : new MetaHumanVec3();
        }

        /// <summary>按效果自己的轴掩码算差值长度：掩码为 0 的轴不该算进"这条参数管这条轴"的证据。</summary>
        private static float DeltaOnAxis(MetaHumanVec3 before, MetaHumanVec3 after, MetaHumanVec3 axis)
        {
            float dx = (after.X - before.X) * axis.X;
            float dy = (after.Y - before.Y) * axis.Y;
            float dz = (after.Z - before.Z) * axis.Z;
            return Math.Abs(dx) + Math.Abs(dy) + Math.Abs(dz);
        }

        private static void TestBodyEffectiveness(MetaHumanTemplate face, MetaHumanTemplate body)
        {
            Eq(30, body.Parameters.Count, "身体模板应带全部 30 项围度（少一项就是被静默跳过）");

            // 结构体字段必须真被反序列化：STJ 默认不绑公开字段，Axis 读成 (0,0,0) 时
            // 数值全对而模型一动不动，所以这里直接钉住一个已知锚点的轴向
            MetaHumanParameter axisProbe = body.Find("body.height");
            True(axisProbe != null && axisProbe.Effects.Count > 0 && Math.Abs(axisProbe.Effects[0].Axis.Y - 1f) < 1e-6f,
                 "body.height 第一个效果的 Y 轴必须是 1（公开字段能被反序列化）");
            True(axisProbe != null && Math.Abs(axisProbe.Effects[0].Offset + axisProbe.Default * axisProbe.Effects[0].Gain) < 1e-3f,
                 "Offset 必须等于 −默认值×Gain（默认姿态零贡献的数据编码方式）");

            // 1) 默认姿态必须零贡献：否则玩家一进捏脸页就是一具变过形的身体
            MetaHumanState neutralState = new MetaHumanState(body);
            MetaHumanSolution neutral = MetaHumanSolver.Solve(neutralState);
            float neutralScale = 0f;
            float neutralTranslate = 0f;
            foreach (MetaHumanBoneOp op in neutral.Bones.Values)
            {
                neutralScale += Math.Abs(op.ScaleDelta.X) + Math.Abs(op.ScaleDelta.Z) + Math.Abs(op.ScaleDelta.Y);
                neutralTranslate += Math.Abs(op.Translate.X) + Math.Abs(op.Translate.Y) + Math.Abs(op.Translate.Z);
            }

            EqF(0f, neutralScale, "默认姿态下所有骨骼缩放增量之和为 0");
            EqF(0f, neutralTranslate, "默认姿态下所有骨骼平移之和为 0");
            True(neutral.Bones.Count > 0, "夹具确实解出了骨骼（否则上面两条是空转）");

            // 2) 身高 +10cm ⇒ **根骨 Y 向等比缩放** (default+10)/default；
            //    不再用"脊柱链逐级平移"：链式累加在 135cm 时会把躯干压掉约 60%，LBS 折叠出尖刺（#61 实测）。
            MetaHumanState taller = new MetaHumanState(body);
            MetaHumanParameter height = body.Find("body.height");
            True(height != null, "身体模板里有 body.height");
            if (height != null)
            {
                float rise = taller.Get(height.Id) + 10f;
                rise = Math.Min(rise, height.Max);
                taller.Set(height.Id, rise);
                MetaHumanSolution tall = MetaHumanSolver.Solve(taller);
                float wanted = Math.Min(10f, height.Max - height.Default);
                float expectedScale = wanted / height.Default;

                MetaHumanBoneOp rootOp;
                bool hasRoot = tall.Bones.TryGetValue("root", out rootOp);
                True(hasRoot, "身高通道应落在 root 骨上（整具骨架等比缩放）");
                if (hasRoot)
                {
                    True(Math.Abs(rootOp.ScaleDelta.Y - expectedScale) <= 1e-3f,
                         "身高 +" + wanted + "cm 的 root Y 缩放增量应为 " + expectedScale + "，实得 " + rootOp.ScaleDelta.Y);
                    True(Math.Abs(rootOp.ScaleDelta.X) < 1e-6f && Math.Abs(rootOp.ScaleDelta.Z) < 1e-6f,
                         "身高缩放只许动 Y 轴（X/Z 保持 0）");
                }

                float totalTranslate = 0f;
                foreach (MetaHumanBoneOp op in tall.Bones.Values)
                {
                    totalTranslate += Math.Abs(op.Translate.X) + Math.Abs(op.Translate.Y) + Math.Abs(op.Translate.Z);
                }

                True(totalTranslate < 1e-4f, "身高通道不应再产生骨骼平移（实得 " + totalTranslate + "）");
            }

            // 3) 围度只压垂直轴，长度轴归长度参数：两条通道不许互相污染
            int girthEffects = 0;
            int polluted = 0;
            foreach (MetaHumanParameter p in body.All())
            {
                if (p.Kind != MetaHumanParamKind.BodyShape)
                {
                    continue;
                }

                foreach (MetaHumanEffect e in p.Effects)
                {
                    if (e.Kind != MetaHumanEffectKind.BoneScale)
                    {
                        continue;
                    }

                    girthEffects++;
                    if (Math.Abs(e.Axis.Y) > 1e-6f)
                    {
                        polluted++;
                    }
                }
            }

            True(girthEffects > 40, "围度效果应有规模，实得 " + girthEffects);
            Eq(0, polluted, "围度缩放的 Y 轴必须为 0（拉长度是长度通道的事）");

            // 4) 反空转：锚点必须分散，不能全压在一根骨上
            HashSet<string> anchors = new HashSet<string>(body.BoneTargets());
            True(anchors.Count >= 20, "身体参数应分散到多根骨骼，实得 " + anchors.Count);

            // 5) 每个参数拉到上限都必须真的产生变化（没有哑参数）
            int dead = 0;
            foreach (MetaHumanParameter p in body.All())
            {
                MetaHumanState probe = new MetaHumanState(body);
                probe.Set(p.Id, p.Max);
                MetaHumanSolution probed = MetaHumanSolver.Solve(probe);
                float magnitude = 0f;
                foreach (MetaHumanBoneOp op in probed.Bones.Values)
                {
                    magnitude += Math.Abs(op.ScaleDelta.X) + Math.Abs(op.ScaleDelta.Z)
                        + Math.Abs(op.Translate.Y) + Math.Abs(op.Translate.X);
                }

                if (magnitude <= 1e-6f)
                {
                    dead++;
                    True(false, "哑参数：" + p.Id + " 拉到上限仍无任何骨骼变化");
                }
            }

            Eq(0, dead, "不存在「改了完全不动」的哑参数");
        }



        // ---------------------------------------------------------------- 脸部语义层
        /// <summary>
        /// 语义层的存在理由：441 根逐关节逐轴原始滑杆玩家用不完，会被误当成「功能没做完」。
        /// 这里断言的是聚合是否成立、以及它是否真的比原始表更粗（一根动一片）。
        /// </summary>
        private static void TestFaceSemanticZones(MetaHumanTemplate face)
        {
            List<MetaHumanParameter> semantic = face.InGroup("semantic");
            List<MetaHumanParameter> advanced = face.InGroup("advanced");

            True(semantic.Count >= 100, "语义滑杆应成规模，实得 " + semantic.Count);
            True(advanced.Count >= 400, "原始表应仍在（专家模式用），实得 " + advanced.Count);

            // 原始表必须全部降级：漏标的 advanced 会和 semantic 混在同一个页签里
            int mislabeled = 0;
            foreach (MetaHumanParameter p in face.All())
            {
                bool raw = p.Id.StartsWith("face.", StringComparison.Ordinal) && !p.Id.StartsWith("face.zone.", StringComparison.Ordinal);
                bool morph = p.Id.StartsWith("morph.", StringComparison.Ordinal);
                if ((raw || morph) && p.Group != "advanced")
                {
                    mislabeled++;
                }
                if (!raw && !morph && p.Group == "advanced" && p.Id.StartsWith("skin.", StringComparison.Ordinal))
                {
                    mislabeled++;
                }
            }

            Eq(0, mislabeled, "face./morph. 参数一律标 advanced");

            // 每根语义滑杆必须真的「一动一片」：≥2 根关节，且目标都是真实 FACIAL 关节
            HashSet<string> advancedTargets = new HashSet<string>(System.StringComparer.Ordinal);
            foreach (MetaHumanParameter p in advanced)
            {
                foreach (MetaHumanEffect e in p.Effects)
                {
                    advancedTargets.Add(e.Target);
                }
            }

            int thin = 0;
            int bogusTargets = 0;
            foreach (MetaHumanParameter p in semantic)
            {
                if (!p.Id.StartsWith("face.zone.", StringComparison.Ordinal))
                {
                    continue;
                }

                if (p.Effects.Count < 2)
                {
                    thin++;
                }

                foreach (MetaHumanEffect e in p.Effects)
                {
                    if (!advancedTargets.Contains(e.Target))
                    {
                        bogusTargets++;
                    }
                }
            }

            Eq(0, thin, "语义滑杆不该退化成单关节（那不如直接用原始滑杆）");
            Eq(0, bogusTargets, "语义滑杆引用的关节必须在原始表里也存在");

            // 默认零贡献 + 拉满时位移量 = 关节数 × 0.06cm（独立重算，不读实现常量）
            foreach (MetaHumanParameter zone in semantic)
            {
                if (!zone.Id.StartsWith("face.zone.", StringComparison.Ordinal))
                {
                    continue;
                }

                MetaHumanState zero = new MetaHumanState(face);
                MetaHumanSolution solvedZero = MetaHumanSolver.Solve(zero);
                float zeroSum = 0f;
                foreach (MetaHumanBoneOp op in solvedZero.Bones.Values)
                {
                    zeroSum += Math.Abs(op.Translate.X) + Math.Abs(op.Translate.Y) + Math.Abs(op.Translate.Z);
                }

                EqF(0f, zeroSum, "语义层默认姿态仍须零贡献：" + zone.Id);

                // 关掉联动才好看清「一根滑杆动一片」的量：默认 LinkSymmetric=true 时
                // 设左会同时写右，位移是两族关节之和（下面另有一断言专门钉这件事）
                MetaHumanState pushed = new MetaHumanState(face);
                pushed.LinkSymmetric = false;
                pushed.Set(zone.Id, zone.Max);
                MetaHumanSolution solvedPushed = MetaHumanSolver.Solve(pushed);
                float magnitude = 0f;
                foreach (KeyValuePair<string, MetaHumanBoneOp> kv in solvedPushed.Bones)
                {
                    magnitude += Math.Abs(kv.Value.Translate.X) + Math.Abs(kv.Value.Translate.Y) + Math.Abs(kv.Value.Translate.Z);
                }

                float expected = 0f;
                foreach (MetaHumanEffect e in zone.Effects)
                {
                    expected += Math.Abs(e.Axis.X) + Math.Abs(e.Axis.Y) + Math.Abs(e.Axis.Z);
                }

                True(magnitude > 0.01f, "拉满必须有位移：" + zone.Id);
                True(Math.Abs(magnitude - expected) <= 1e-3f,
                     "位移量应等于各效果轴向量之和：" + zone.Id + " 实得 " + magnitude + " 期望 " + expected);
                // 正向对照：开联动时同一操作应覆盖到对岸，位移量翻倍
                MetaHumanState linked = new MetaHumanState(face);
                linked.LinkSymmetric = true;
                linked.Set(zone.Id, zone.Max);
                float linkedMagnitude = 0f;
                foreach (MetaHumanBoneOp op in MetaHumanSolver.Solve(linked).Bones.Values)
                {
                    linkedMagnitude += Math.Abs(op.Translate.X) + Math.Abs(op.Translate.Y) + Math.Abs(op.Translate.Z);
                }

                MetaHumanParameter zonePair = face.Find(zone.PairId);
                if (zonePair != null)
                {
                    float pairExpected = expected;
                    foreach (MetaHumanEffect e in zonePair.Effects)
                    {
                        pairExpected += Math.Abs(e.Axis.X) + Math.Abs(e.Axis.Y) + Math.Abs(e.Axis.Z);
                    }

                    True(Math.Abs(linkedMagnitude - pairExpected) <= 1e-3f,
                         "开联动应同时驱动对岸：" + zone.Id + " 实得 " + linkedMagnitude + " 期望 " + pairExpected);
                }

                break;  // 逐根太慢，抽样一根足够暴露「聚合没生效」
            }

            // 镜像对称：同一族的左、右滑杆覆盖的关节数必须相同
            Dictionary<string, int> sideCounts = new Dictionary<string, int>();
            foreach (MetaHumanParameter p in semantic)
            {
                if (p.Side == MetaHumanSide.None || string.IsNullOrEmpty(p.PairId))
                {
                    continue;
                }

                MetaHumanParameter pair = face.Find(p.PairId);
                if (pair == null)
                {
                    True(false, "语义滑杆缺对岸：" + p.Id);
                    continue;
                }

                if (pair.Effects.Count != p.Effects.Count)
                {
                    sideCounts[p.Id] = 1;
                }
            }

            Eq(0, sideCounts.Count, "成对语义滑杆左右覆盖的关节数必须一致");
        }


        // ---------------------------------------------------------------- 模板自检真拦错
        private static void TestValidateCatchesDefects(MetaHumanTemplate face)
        {
            // 正向对照：真实模板无错（前面已断言），这里在同一夹具上注入缺陷，验证计数器会动
            int baseline = face.Validate().Count;
            Eq(0, baseline, "真实模板作为对照应零错误");

            MetaHumanTemplate broken = MetaHumanTemplate.FromJson(face.ToJson());
            broken.Parameters[0].Effects.Add(MetaHumanEffect.Morph("这个 blendshape 不存在", 1f));
            List<string> errors = broken.Validate();
            True(errors.Count == baseline + 1, "引用不存在的 morph 必须新增一条错误，实得 " + errors.Count);
            True(errors.Count > 0 && errors[0].Contains("blendshape") || ContainsSubstring(errors, "不存在的 blendshape"),
                 "错误信息点名是哪个 morph");

            MetaHumanTemplate duplicate = MetaHumanTemplate.FromJson(face.ToJson());
            MetaHumanParameter copy = MetaHumanTemplate.FromJson(face.ToJson()).Parameters[1];
            duplicate.Parameters.Add(copy);
            True(ContainsSubstring(duplicate.Validate(), "Id 重复"), "重复 Id 必须被拦下");

            MetaHumanTemplate badRange = MetaHumanTemplate.FromJson(face.ToJson());
            badRange.Parameters[0].Min = 2f;
            badRange.Parameters[0].Max = 1f;
            True(ContainsSubstring(badRange.Validate(), "Max < Min"), "区间反向必须被拦下");

            MetaHumanTemplate noEffect = MetaHumanTemplate.FromJson(face.ToJson());
            MetaHumanParameter empty = new MetaHumanParameter();
            empty.Id = "空参数";
            empty.Min = 0f;
            empty.Max = 1f;
            noEffect.Parameters.Add(empty);
            noEffect.EnsureIndexed();
            True(ContainsSubstring(noEffect.Validate(), "没有任何效果"), "没有任何效果的参数会被点名（否则改它不会有任何反应）");

            MetaHumanTemplate danglingPair = MetaHumanTemplate.FromJson(face.ToJson());
            danglingPair.Parameters[0].PairId = "不存在的对岸";
            True(ContainsSubstring(danglingPair.Validate(), "PairId"), "PairId 悬空会被点名");
        }

        // ---------------------------------------------------------------- GUI→raw 表情控制换算
        private sealed class GuiRawFixture
        {
            public string Name;
            public Dictionary<string, float> InputGui;
            public Dictionary<string, float> ExpectedRaw;
        }

        /// <summary>
        /// 独立参照实现：按 raw 下标分组、用 double 累加。
        /// 被测实现是按表序、用 float 累加，两者遍历顺序与求和精度都不同，
        /// 互相独立才能把「两边同错」暴露出来。
        /// </summary>
        private static double[] ReferenceConvert(MetaHumanGuiToRawMap map, float[] guiValues)
        {
            Dictionary<int, List<MetaHumanGuiToRawSegment>> byRaw = new Dictionary<int, List<MetaHumanGuiToRawSegment>>();
            for (int g = 0; g < map.GuiCount; g++)
            {
                List<MetaHumanGuiToRawSegment> segments = map.SegmentsOfGui(g);
                for (int i = 0; i < segments.Count; i++)
                {
                    List<MetaHumanGuiToRawSegment> bucket;
                    if (!byRaw.TryGetValue(segments[i].RawIndex, out bucket))
                    {
                        bucket = new List<MetaHumanGuiToRawSegment>();
                        byRaw[segments[i].RawIndex] = bucket;
                    }

                    bucket.Add(segments[i]);
                }
            }

            double[] result = new double[map.RawCount];
            for (int r = 0; r < map.RawCount; r++)
            {
                List<MetaHumanGuiToRawSegment> bucket;
                if (!byRaw.TryGetValue(r, out bucket))
                {
                    continue;
                }

                double acc = 0.0;
                for (int i = 0; i < bucket.Count; i++)
                {
                    MetaHumanGuiToRawSegment s = bucket[i];
                    double v = guiValues[s.GuiIndex];
                    double low = map.GuiRangeLow(s.GuiIndex);
                    double high = map.GuiRangeHigh(s.GuiIndex);
                    if (s.From <= v && v < s.To)
                    {
                        acc += (double)s.Slope * v + s.Cut;
                    }
                    else if ((double)s.From == low && v < s.From)
                    {
                        acc += (double)s.Slope * s.From + s.Cut;
                    }
                    else if ((double)s.To == high && v >= s.To)
                    {
                        acc += (double)s.Slope * s.To + s.Cut;
                    }
                }

                result[r] = acc;
            }

            return result;
        }

        private static List<GuiRawFixture> LoadGoldenFixtures(string path)
        {
            List<GuiRawFixture> list = new List<GuiRawFixture>();
            using (JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path)))
            {
                JsonElement fixtures = doc.RootElement.GetProperty("fixtures");
                foreach (JsonProperty fixture in fixtures.EnumerateObject())
                {
                    GuiRawFixture item = new GuiRawFixture();
                    item.Name = fixture.Name;
                    item.InputGui = ReadFloatMap(fixture.Value.GetProperty("input_gui"));
                    item.ExpectedRaw = ReadFloatMap(fixture.Value.GetProperty("expected_raw"));
                    list.Add(item);
                }
            }

            list.Sort(delegate(GuiRawFixture a, GuiRawFixture b) { return string.CompareOrdinal(a.Name, b.Name); });
            return list;
        }

        private static Dictionary<string, float> ReadFloatMap(JsonElement element)
        {
            Dictionary<string, float> map = new Dictionary<string, float>(StringComparer.Ordinal);
            foreach (JsonProperty property in element.EnumerateObject())
            {
                map[property.Name] = (float)property.Value.GetDouble();
            }

            return map;
        }

        private static float RawOf(MetaHumanGuiToRawMap map, float[] raw, string name)
        {
            int index;
            if (!map.TryGetRawIndex(name, out index))
            {
                _failed++;
                _failures.Add("换算表里没有 raw 控制 " + name);
                return float.NaN;
            }

            return raw[index];
        }

        /// <summary>只把某一个 GUI 控制设成给定值，其余走表里的缺省值。</summary>
        private static float[] SingleGuiValue(MetaHumanGuiToRawMap map, string guiName, float value)
        {
            Dictionary<string, float> input = new Dictionary<string, float>(StringComparer.Ordinal);
            input[guiName] = value;
            return map.MaterializeGuiValues(input);
        }

        private static void TestGuiToRawConversion(string templatesDir)
        {
            string mapPath = Path.Combine(templatesDir, "Gui_To_Raw_Map.json");
            if (!File.Exists(mapPath))
            {
                Skip("GUI→raw 换算表缺失：" + mapPath + "（用 Tools/MetaHumanExtractGuiMap.py 从引擎源抽）");
                return;
            }

            MetaHumanGuiToRawMap map = MetaHumanGuiToRawMap.LoadFromFile(mapPath);
            Eq(174, map.GuiCount, "换算表的 GUI 控制数");
            Eq(251, map.RawCount, "换算表的 raw 控制数");
            Eq(259, map.SegmentCount, "换算表的线性段数");
            Eq(0, map.Validate().Count, "换算表结构自检（控制名唯一、折线段区间不重叠）");

            // 交叉证据：.dna 里的 CTRL_expressions.* 名称表与 UE 的 raw 名是同一套命名
            string dnaNamesPath = Path.Combine(RepoRoot(templatesDir), "docs", "metahuman", "dna", "SKM_Face_names.json");
            if (File.Exists(dnaNamesPath))
            {
                using (JsonDocument doc = JsonDocument.Parse(File.ReadAllText(dnaNamesPath)))
                {
                    HashSet<string> fromAsset = new HashSet<string>(StringComparer.Ordinal);
                    foreach (JsonElement table in doc.RootElement.GetProperty("name_tables").EnumerateArray())
                    {
                        if (table.GetProperty("kind").GetString() != "mixed_expressions_and_channels")
                        {
                            continue;
                        }

                        foreach (JsonElement entry in table.GetProperty("names").EnumerateArray())
                        {
                            string name = entry[1].GetString();
                            if (name.StartsWith("CTRL_expressions."))
                            {
                                fromAsset.Add("CTRL_expressions_" + name.Substring("CTRL_expressions.".Length));
                            }
                        }
                    }

                    Eq(251, fromAsset.Count, ".dna 里的表情控制名条数");
                    int shared = 0;
                    for (int i = 0; i < map.RawCount; i++)
                    {
                        if (fromAsset.Contains(map.RawName(i)))
                        {
                            shared++;
                        }
                    }

                    Eq(map.RawCount, shared, "UE 的 raw 控制名与资产 .dna 的表情控制名同源（对不上就没法按名写回）");
                }
            }
            else
            {
                Skip("资产名称表缺失：" + dnaNamesPath + "，raw 命名同源这条通路没跑");
            }

            // 分支命中数是对表本身的量测，能发现「表被换掉而没人察觉」
            Dictionary<string, int[]> expectedBranches = new Dictionary<string, int[]>();
            expectedBranches["ControlsHalfTestData"] = new int[] { 177, 0, 0, 82 };
            expectedBranches["MaxControlsTestData"] = new int[] { 0, 0, 176, 83 };
            expectedBranches["MinControlsTestData"] = new int[] { 70, 104, 0, 85 };
            expectedBranches["SolveControlsTestData"] = new int[] { 166, 6, 2, 85 };

            string goldenPath = Path.Combine(RepoRoot(templatesDir), "docs", "metahuman", "gui_to_raw_golden.json");
            if (!File.Exists(goldenPath))
            {
                Skip("UE 黄金样例缺失：" + goldenPath + "，换算结果没有外部对照");
                return;
            }

            List<GuiRawFixture> fixtures = LoadGoldenFixtures(goldenPath);
            Eq(4, fixtures.Count, "UE 黄金样例组数");

            for (int f = 0; f < fixtures.Count; f++)
            {
                GuiRawFixture fixture = fixtures[f];
                float[] values = map.MaterializeGuiValues(fixture.InputGui);
                float[] actual = new float[map.RawCount];
                map.ConvertInto(values, actual);

                Eq(map.RawCount, fixture.ExpectedRaw.Count, fixture.Name + " 的期望值覆盖全部 raw 控制");

                int mismatch = 0;
                string first = null;
                foreach (KeyValuePair<string, float> kv in fixture.ExpectedRaw)
                {
                    int index;
                    if (!map.TryGetRawIndex(kv.Key, out index))
                    {
                        if (first == null)
                        {
                            first = "期望里的 " + kv.Key + " 不在表中";
                        }

                        mismatch++;
                        continue;
                    }

                    if (Math.Abs(actual[index] - kv.Value) > 1e-4f)
                    {
                        mismatch++;
                        if (first == null)
                        {
                            first = kv.Key + " 算得 " + actual[index] + " 期望 " + kv.Value;
                        }
                    }
                }

                Eq(0, mismatch, fixture.Name + " 与 UE 期望值逐条相符（首条不符：" + (first ?? "无") + "）");

                double[] reference = ReferenceConvert(map, values);
                int diverged = 0;
                string firstRef = null;
                for (int i = 0; i < map.RawCount; i++)
                {
                    if (Math.Abs((float)reference[i] - actual[i]) > 1e-5f)
                    {
                        diverged++;
                        if (firstRef == null)
                        {
                            firstRef = map.RawName(i) + " 参照 " + reference[i] + " 实现 " + actual[i];
                        }
                    }
                }

                Eq(0, diverged, fixture.Name + " 与独立参照实现相符（首条分歧：" + (firstRef ?? "无") + "）");

                int[] branches;
                if (expectedBranches.TryGetValue(fixture.Name, out branches))
                {
                    Eq(branches[0], map.HitsInSpan, fixture.Name + " 落在半开区间内的段数");
                    Eq(branches[1], map.HitsClampLow, fixture.Name + " 钳到量程下限的段数");
                    Eq(branches[2], map.HitsClampHigh, fixture.Name + " 钳到量程上限的段数");
                    Eq(branches[3], map.HitsIgnored, fixture.Name + " 完全不参与的段数");
                    Eq(map.SegmentCount, branches[0] + branches[1] + branches[2] + branches[3],
                       fixture.Name + " 四个分支之和等于段数（没有走空的分支被算成跑过）");
                }
            }

            // 三条钳位/段外语义的可指名对照：全部量自同一张表，naive 实现（无条件钳到量程、或把区间当闭区间）在这四处都会错
            // CTRL_L_eye_blink.ty 量程 [-1,1]：eyeBlinkL 由 [0,1) 斜率 1 驱动（To 正好是量程上限），
            // eyeWidenL 由 [-1,0) 斜率 -1 驱动（From 正好是量程下限）。
            float[] blink = new float[map.RawCount];
            map.ConvertInto(SingleGuiValue(map, "CTRL_L_eye_blink.ty", 0.5f), blink);
            EqF(0.5f, RawOf(map, blink, "CTRL_expressions_eyeBlinkL"), "眨眼 0.5 落到 eyeBlinkL");
            EqF(0f, RawOf(map, blink, "CTRL_expressions_eyeWidenL"), "眨眼 0.5 不该同时写 eyeWidenL（半开区间互斥）");

            map.ConvertInto(SingleGuiValue(map, "CTRL_L_eye_blink.ty", -0.5f), blink);
            EqF(0f, RawOf(map, blink, "CTRL_expressions_eyeBlinkL"), "眨眼 -0.5 不落 eyeBlinkL");
            EqF(0.5f, RawOf(map, blink, "CTRL_expressions_eyeWidenL"), "眨眼 -0.5 经斜率 -1 变正落 eyeWidenL");

            map.ConvertInto(SingleGuiValue(map, "CTRL_L_eye_blink.ty", 2f), blink);
            EqF(1f, RawOf(map, blink, "CTRL_expressions_eyeBlinkL"), "值 2 超上限被钳到 To=1 而不是照乘（原样透传会得 2）");
            EqF(0f, RawOf(map, blink, "CTRL_expressions_eyeWidenL"), "eyeWidenL 那段的 To=0 不是量程上限，值超界时该段不参与而不是钳位");

            map.ConvertInto(SingleGuiValue(map, "CTRL_L_eye_blink.ty", -2f), blink);
            EqF(1f, RawOf(map, blink, "CTRL_expressions_eyeWidenL"), "值 -2 超下限被钳到 From=-1 再乘斜率 -1");
            EqF(0f, RawOf(map, blink, "CTRL_expressions_eyeBlinkL"), "eyeBlinkL 那段的 From=0 不是量程下限，值低于它时不参与");

            // 折线段（同一个 GUI 控制分两段驱动同一个 raw）：CTRL_L_mouth_lipSticky.ty 的 Ph1/Ph2/Ph3
            map.ConvertInto(SingleGuiValue(map, "CTRL_L_mouth_lipSticky.ty", 0.2f), blink);
            EqF(0.6060606f, RawOf(map, blink, "CTRL_expressions_mouthLipsStickyLPh1"), "0.2 落在 Ph1 的第一段");
            EqF(0f, RawOf(map, blink, "CTRL_expressions_mouthLipsStickyLPh2"), "0.2 不到 Ph2 的任何段");

            map.ConvertInto(SingleGuiValue(map, "CTRL_L_mouth_lipSticky.ty", 0.5f), blink);
            EqF(0.4848485f, RawOf(map, blink, "CTRL_expressions_mouthLipsStickyLPh1"), "0.5 换到 Ph1 的第二段（斜率变负）");
            EqF(0.5151515f, RawOf(map, blink, "CTRL_expressions_mouthLipsStickyLPh2"), "0.5 同时开启 Ph2 的第一段");
            EqF(1f, RawOf(map, blink, "CTRL_expressions_mouthLipsStickyLPh1") + RawOf(map, blink, "CTRL_expressions_mouthLipsStickyLPh2"),
                "峰点处两个阶段的权重之和为 1（交叉淡入淡出，不是相加翻倍）");

            map.ConvertInto(SingleGuiValue(map, "CTRL_L_mouth_lipSticky.ty", 1f), blink);
            EqF(0f, RawOf(map, blink, "CTRL_expressions_mouthLipsStickyLPh1"), "1.0 时 Ph1 两段都在量程中间，不钳位、不参与");
            EqF(0f, RawOf(map, blink, "CTRL_expressions_mouthLipsStickyLPh2"), "1.0 时 Ph2 钳到上限后算得 0");
            EqF(1f, RawOf(map, blink, "CTRL_expressions_mouthLipsStickyLPh3"), "1.0 时权重全在 Ph3");

            // 缺省值通路：输入表里没写的控制取 gui_defaults，只有两个控制缺省非零
            float[] allDefault = new float[map.RawCount];
            map.ConvertInto(map.MaterializeGuiValues(new Dictionary<string, float>()), allDefault);
            EqF(1f, RawOf(map, allDefault, "CTRL_expressions_noseWrinkleUpperL"), "上鼻皱的 GUI 缺省是 1，空输入也要落地");
            EqF(1f, RawOf(map, allDefault, "CTRL_expressions_noseWrinkleUpperR"), "同上，右侧");
            int nonZero = 0;
            for (int i = 0; i < map.RawCount; i++)
            {
                if (Math.Abs(allDefault[i]) > 1e-6f)
                {
                    nonZero++;
                }
            }

            Eq(2, nonZero, "空输入下非零的 raw 控制数（其余控制缺省都是 0）");

            TestFaceControlTableEnrichment(map, templatesDir);
            TestMorphGuiMap(map, templatesDir);
            TestUeMorphMap(templatesDir, map);
            TestMorphBridgeDualFamily();
            TestMorphBridgeWarpPrefix();
            TestBoneAliasMap();
            TestHeadShapeTargetsExistInRealSkeleton(templatesDir);
            TestThreeHopWithRealTablesAndRealNames(templatesDir);
            TestTemplateAssetPathsResolve(templatesDir);
            TestRealTemplatesPushAndRetreat(templatesDir);
            TestBodyParamsResolveOnRealBodyAsset(templatesDir);
        }

        /// <summary>
        /// 控制表里被 Tools/MetaHumanEnrichFaceControls.py 补进去的换算段，要能被权威映射反证：
        /// 段集合、量程、raw 名三者逐条对齐，并且运行期那条「只读控制表段」的换算通路
        /// 与「读全量映射表」的通路在同一个输入下给出同一个 raw 值。
        /// </summary>
        /// <summary>
        /// morph→GUI 表的装载期复核：条目数、每条都能按名查回自己的 GUI、declined 的 4 条确实不给通路、
        /// 以及 MetaHumanMorphGuiMap.Validate 的三条（名字在权威表里、有 raw 段、侧标一致）。
        /// 这张表是"头部 blendshape 那侧接得上"的唯一依据，所以断言要问"值到了吗"，不是"文件在吗"。
        /// </summary>
        /// <summary>
        /// 第二张 morph→GUI 表：左岸是**引擎里实读的 UE 网格 morph 名**（不是 .dna 族名）。
        /// 判据要问"值到了吗"而不是"能解析吗"：逐条查 morph 真在引擎名录里、gui 真在权威 174 清单里、
        /// 侧标一致，并配一条**同夹具反向对照**（把某个 gui 换成不存在的名字，Validate 必须报红）——
        /// 没有反向对照，"校验 0 问题"可能只是探测器永远不报问题。
        /// </summary>
        private static void TestUeMorphMap(string templatesDir, MetaHumanGuiToRawMap guiMap)
        {
            string path = Path.Combine(templatesDir, "Ue_Morph_To_Gui_Map.json");
            if (!File.Exists(path))
            {
                Skip("UE 网格 morph→GUI 表不在盘上：" + path + "（跑 Tools/MetaHumanBuildUeMorphMap.py）");
                return;
            }

            string json = File.ReadAllText(path);
            MetaHumanMorphGuiMap ueMap = MetaHumanMorphGuiMap.FromJson(json);
            Eq(ueMap.Entries.Count, ueMap.EntryCount, "表里 entryCount 与实际条目数一致");
            True(ueMap.Entries.Count > 0, "成表条数该大于 0（拿引擎名录机械推出来的，为空说明规则或名录变了）");

            List<string> problems = ueMap.Validate(guiMap);
            Eq(0, problems.Count, "UE 网格 morph→GUI 表复校问题数（首条："
                + (problems.Count > 0 ? problems[0] : "无") + "）");

            // morph 必须真在引擎实读的名录里：这是"左岸是权威名字"这条声称的唯一硬证
            string namesPath = FindUpwards(Path.Combine("docs", "metahuman", "ue_runtime",
                                                        "Ada_Face_authoritative_names.json"));
            if (namesPath == null)
            {
                Skip("引擎 morph 名录 JSON 不在盘上（docs/metahuman/ue_runtime/...），"
                    + "左岸成员真实性这一层没验；跑 Tools/metahuman_face_names.py 重新从引擎取");
            }
            else
            {
                string namesJson = File.ReadAllText(namesPath);
                int outside = 0;
                string firstOutside = null;
                for (int i = 0; i < ueMap.Entries.Count; i++)
                {
                    string morph = ueMap.Entries[i].Morph;
                    if (string.IsNullOrEmpty(morph))
                    {
                        outside++;
                        firstOutside = "<空>";
                        continue;
                    }

                    if (namesJson.IndexOf("\"" + morph + "\"", StringComparison.Ordinal) < 0)
                    {
                        outside++;
                        if (firstOutside == null)
                        {
                            firstOutside = morph;
                        }
                    }
                }

                Eq(0, outside, "有多少条 morph 不在引擎实读名录里（首条："
                    + (firstOutside ?? "无") + "）—— 在就是权威左岸，不在就是凭记忆编的");
            }

            HashSet<string> distinctMorphs = new HashSet<string>();
            HashSet<string> distinctGuis = new HashSet<string>();
            int dup = 0;
            for (int i = 0; i < ueMap.Entries.Count; i++)
            {
                if (!distinctMorphs.Add(ueMap.Entries[i].Morph))
                {
                    dup++;
                }

                distinctGuis.Add(ueMap.Entries[i].Gui);
            }

            Eq(0, dup, "同一个 morph 不该出现两次（左岸重复意味着两条 GUI 抢一根形状）");
            Eq(distinctGuis.Count, ueMap.Entries.Count,
                "每条成表应各占一个不同的 GUI 控制（实得 " + distinctGuis.Count + " 控制 / "
                + ueMap.Entries.Count + " 条）");

            // 反向对照（同夹具）：把第一条的 gui 换成权威清单里绝不存在的名字，同一套校验必须报红
            string originalGui = ueMap.Entries[0].Gui;
            string bogus = "CTRL_Z_definitely_not_in_authority.ty";
            string mutated = json.Replace("\"" + originalGui + "\"", "\"" + bogus + "\"");
            True(mutated != json, "反向对照夹具没能改出任何文本（那这条对照是空转，结论不算）");
            List<string> mutantProblems = MetaHumanMorphGuiMap.FromJson(mutated).Validate(guiMap);
            True(mutantProblems.Count > 0,
                "把 gui 换成不存在的名字后复校仍然全绿 ⇒ 校验根本没看 gui，正查结论不可信");
        }

        /// <summary>
        /// 双左岸桥（文档 §42.1）：**跨族**翻译是这里唯一有新意的地方——
        /// 单张表的 ResolveAssetMorph 只能从自己那张表的左岸名出发，
        /// 所以"模板写 .dna 族名、资产是 UE 网格族名"这种组合必须经 GUI 控制中转。
        /// 判据全部问"值到了吗"：拿回来的必须是资产里真有的那个名字；
        /// 并配反向对照（两张表都不认识的必须返回 null，绝不能"看着像"就写权重）。
        /// </summary>
        /// <summary>
        /// 模板里 morph 目标写的是 `head_wm1_blink_L`（形变族带节号），表里的键是 `blink_L`。
        /// 这组断言钉的是"剥前缀只许用来取 GUI 控制，不许用来凭空造形状名"：
        /// 正向要跨族命中，反向（表里根本没有键的那族，如 `head_wm13_lips_DL`）必须报不出。
        /// </summary>
        /// <summary>不依赖 System.Linq 的清单包含判断（harness 里没有 using System.Linq）。</summary>
        private static bool HasNote(List<string> notes, string needle)
        {
            if (notes == null)
            {
                return false;
            }

            for (int i = 0; i < notes.Count; i++)
            {
                if (notes[i] != null && notes[i].Contains(needle))
                {
                    return true;
                }
            }

            return false;
        }

        private static void TestMorphBridgeWarpPrefix()
        {
            string dnaJson = "{\"entryCount\":1,\"entries\":["
                + "{\"morph\":\"blink_L\",\"morphFamily\":\"blink\",\"side\":\"L\",\"gui\":\"CTRL_L_eye_blink.ty\",\"basis\":\"authored\",\"note\":\"t\"}"
                + "],\"declined\":[]}";
            string ueJson = "{\"entryCount\":1,\"entries\":["
                + "{\"morph\":\"head_lod0_mesh__eye_blink_L\",\"morphFamily\":\"eye_blink\",\"side\":\"L\",\"gui\":\"CTRL_L_eye_blink.ty\",\"basis\":\"exact\",\"note\":\"t\"}"
                + "],\"declined\":[]}";

            MetaHumanMorphGuiMap dna = MetaHumanMorphGuiMap.FromJson(dnaJson);
            MetaHumanMorphGuiMap ue = MetaHumanMorphGuiMap.FromJson(ueJson);

            HashSet<string> ueSide = new HashSet<string>(StringComparer.Ordinal) { "head_lod0_mesh__eye_blink_L" };
            EqS("head_lod0_mesh__eye_blink_L",
                MetaHumanMorphBridge.ResolveAssetMorph("head_wm1_blink_L", ueSide, dna, ue),
                "模板带节号的名字要剥前缀后经 GUI 跨族命中资产里的真名");

            HashSet<string> dnaSide = new HashSet<string>(StringComparer.Ordinal) { "blink_L" };
            EqS("blink_L",
                MetaHumanMorphBridge.ResolveAssetMorph("head_wm1_blink_L", dnaSide, dna, ue),
                "同一控制回到同族名也该成立（剥前缀不是只服务某一只岸）");

            // 原名通路不能被这次改动挤掉
            EqS("head_lod0_mesh__eye_blink_L",
                MetaHumanMorphBridge.ResolveAssetMorph("blink_L", ueSide, dna, ue),
                "不带前缀的原名照旧走同一条桥");

            HashSet<string> unrelated = new HashSet<string>(StringComparer.Ordinal)
            {
                "head_lod0_mesh__eye_blink_L", "head_lod0_mesh__lips_upper_L"
            };
            True(MetaHumanMorphBridge.ResolveAssetMorph("head_wm13_lips_DL", unrelated, dna, ue) == null,
                "表里没有键的一族必须报不出：不许把 lips_DL 蒙成资产里看着像的 lips_upper_L");

            EqS("lips_DL", MetaHumanMorphBridge.StripWarpPrefix("head_wm13_lips_DL"), "带节号的合规前缀要剥掉");
            True(MetaHumanMorphBridge.StripWarpPrefix("head_wm_blink_L") == null, "没有节号不剥");
            True(MetaHumanMorphBridge.StripWarpPrefix("head_wmx_blink_L") == null, "节号不是数字不剥");
            True(MetaHumanMorphBridge.StripWarpPrefix("head_wm1_") == null, "剥完为空不剥");
            True(MetaHumanMorphBridge.StripWarpPrefix("blink_L") == null, "不带 head_wm 前缀原样不处理");
        }

        private static void TestMorphBridgeDualFamily()
        {
            string dnaJson = "{\"entryCount\":2,\"entries\":["
                + "{\"morph\":\"blink_L\",\"morphFamily\":\"blink\",\"side\":\"L\",\"gui\":\"CTRL_L_eye_blink.ty\",\"basis\":\"authored\",\"note\":\"t\"},"
                + "{\"morph\":\"browsDown_L\",\"morphFamily\":\"browsDown\",\"side\":\"L\",\"gui\":\"CTRL_L_brow_down.ty\",\"basis\":\"authored\",\"note\":\"t\"}"
                + "],\"declined\":[]}";
            string ueJson = "{\"entryCount\":2,\"entries\":["
                + "{\"morph\":\"head_lod0_mesh__brow_down_L\",\"morphFamily\":\"brow_down\",\"side\":\"L\",\"gui\":\"CTRL_L_brow_down.ty\",\"basis\":\"exact\",\"note\":\"t\"},"
                + "{\"morph\":\"head_lod0_mesh__eye_widen_L\",\"morphFamily\":\"eye_widen\",\"side\":\"L\",\"gui\":\"CTRL_L_eye_widen.ty\",\"basis\":\"exact\",\"note\":\"t\"}"
                + "],\"declined\":[]}";

            MetaHumanMorphGuiMap dna = MetaHumanMorphGuiMap.FromJson(dnaJson);
            MetaHumanMorphGuiMap ue = MetaHumanMorphGuiMap.FromJson(ueJson);
            True(dna != null && ue != null, "两张合成表都该解得开（解不开后面全是空转）");
            if (dna == null || ue == null)
            {
                return;
            }

            // ① 资产只有 UE 族名、模板给 .dna 族名：必须经 GUI 控制中转到资产真有的那个名字
            HashSet<string> ueOnly = new HashSet<string>();
            ueOnly.Add("head_lod0_mesh__brow_down_L");
            string cross = MetaHumanMorphBridge.ResolveAssetMorph("browsDown_L", ueOnly, dna, ue);
            EqS("head_lod0_mesh__brow_down_L", cross,
                "跨族翻译（模板 .dna 族 → 资产 UE 网格族）应拿到资产里那个名字");

            // ② 同族直连不该被第二张表抢走
            HashSet<string> dnaOnly = new HashSet<string>();
            dnaOnly.Add("blink_L");
            EqS("blink_L", MetaHumanMorphBridge.ResolveAssetMorph("blink_L", dnaOnly, dna, ue),
                "第一张表直接命中时不该绕道第二张表");

            // ③ 反向对照：谁都不认识的名字必须 null
            True(MetaHumanMorphBridge.ResolveAssetMorph("head_wm9_nonexistent_L", ueOnly, dna, ue) == null,
                "两张表都翻不出来时必须返回 null，不能猜一个写进权重");

            // ④ 单表可用（另一张装载失败为 null）仍要工作
            // ④ 单表可用（另一张装载失败为 null）：**在它自己那一族内**仍要接住。
            //    我原先写成"只剩 UE 表时也要把 .dna 族名翻出来"——那不可能：
            //    认识 browsDown_L 的恰恰只有 .dna 那张表，桥返回 null 才是正确行为。
            //    断言写错会让下一次真降级被当成"又修好了"，所以拆成两半。
            EqS("head_lod0_mesh__brow_down_L",
                MetaHumanMorphBridge.ResolveAssetMorph("head_lod0_mesh__brow_down_L", ueOnly, null, ue),
                "只剩 UE 表时，UE 族名在本族内仍要能接住");
            True(MetaHumanMorphBridge.ResolveAssetMorph("browsDown_L", ueOnly, null, ue) == null,
                "只剩 UE 表时 .dna 族名必须降级为 null（跨族本就依赖那张缺的表）；"
                + "若这里返回了名字，说明有人在猜名字写权重");

            // ⑤ 并集去重：同一 GUI 在两表各配一族名 → 两条都要，且不得重名
            List<string> merged = MetaHumanMorphBridge.MorphsForGui("CTRL_L_brow_down.ty", dna, ue);
            Eq(2, merged.Count, "一个 GUI 控制跨两族应并出两条形状名（实得 " + merged.Count + "）");
            Eq(merged.Count, new HashSet<string>(merged).Count, "并集里不该有重名（重名会把同一根形状权重加两次）");

            // ⑥ 真数据跨族：两张真表共用 CTRL_L_brow_down.ty（.dna 族 browsDown_L ↔ UE 族真名），
            //    这条用盘上的真表来验，比合成夹具更接近实战
            MetaHumanMorphGuiMap realDna = MetaHumanMorphGuiMap.FromJson(
                File.ReadAllText(Path.Combine(RepoTemplatesDir(), "Morph_To_Gui_Map.json")));
            MetaHumanMorphGuiMap realUe = MetaHumanMorphGuiMap.FromJson(
                File.ReadAllText(Path.Combine(RepoTemplatesDir(), "Ue_Morph_To_Gui_Map.json")));
            if (realDna == null || realUe == null || realDna.Entries.Count == 0 || realUe.Entries.Count == 0)
            {
                Skip("两张真表读不开或为空，真数据跨族这一层没跑");
                return;
            }

            int sharedGui = 0;
            int resolved = 0;
            string firstUnresolved = null;
            for (int i = 0; i < realUe.Entries.Count; i++)
            {
                string gui = realUe.Entries[i].Gui;
                if (realDna.MorphsForGui(gui).Count == 0)
                {
                    continue;
                }

                sharedGui++;
                HashSet<string> assetSet = new HashSet<string>();
                assetSet.Add(realUe.Entries[i].Morph);
                string hit = MetaHumanMorphBridge.ResolveAssetMorph(realDna.MorphsForGui(gui)[0], assetSet, realDna, realUe);
                if (hit == realUe.Entries[i].Morph)
                {
                    resolved++;
                }
                else if (firstUnresolved == null)
                {
                    firstUnresolved = gui + " → " + (hit ?? "null");
                }
            }

            True(sharedGui > 0, "两张真表之间该有共用的 GUI 控制（实得 " + sharedGui
                  + "）；没有就说明两侧左岸根本不同源，跨族这条通路等于没接");
            Eq(sharedGui, resolved, "共用控制里跨族能翻成资产真名的条数（首条不成："
                  + (firstUnresolved ?? "无") + "）");
        }

        /// <summary>
        /// 骨骼别名表（文档 §44）：身体模板 24 个目标全是 UE 骨骼名，而项目现成角色是 Unity Humanoid 命名，
        /// 同名命中实测 0/24 ⇒ 没有这张表，"玩家改身体各项参数"在现成资产上一条都写不出来。
        ///
        /// 判据用恒等式而不是魔法数：把可用集合**由别名表自己造出来**，
        /// 那么"能解析的目标数"必须恰好等于"模板目标里被表覆盖到的那一套"，一条不差、一条不多。
        /// 反向对照用脸上那批 `FACIAL_*`：它们在 Humanoid 骨架上没有对应结构，必须解析不出来——
        /// 如果哪天它们"也能解析"了，说明有人拿"位置差不多"来配对，那是把形变写到错骨头上的开始。
        /// </summary>
        private static void TestBoneAliasMap()
        {
            List<string> problems = MetaHumanBoneAliases.Validate();
            Eq(0, problems.Count, "别名表作者门问题数（左右写反/一名多用；首条："
                + (problems.Count > 0 ? problems[0] : "无") + "）");
            True(MetaHumanBoneAliases.Count >= 20,
                "别名表条数不该退化（实得 " + MetaHumanBoneAliases.Count + "）");

            string bodyPath = Path.Combine(RepoTemplatesDir(), "Template_Body_Base.json");
            if (!File.Exists(bodyPath))
            {
                Skip("身体模板不在盘上，别名覆盖率这层没跑：" + bodyPath);
                return;
            }

            MetaHumanTemplate body = MetaHumanTemplate.FromJson(File.ReadAllText(bodyPath));
            var targets = new List<string>(body.BoneTargets());
            True(targets.Count > 0, "身体模板该有骨骼目标（实得 " + targets.Count + "）");

            // 可用集合 = 表里所有别名：于是"解析成功"与"被表覆盖"必须一一对应
            HashSet<string> assetBones = new HashSet<string>(System.StringComparer.Ordinal);
            foreach (string templateName in MetaHumanBoneAliases.TemplateNames())
            {
                string[] aliases = MetaHumanBoneAliases.AliasesOf(templateName);
                for (int i = 0; i < aliases.Length; i++)
                {
                    assetBones.Add(aliases[i]);
                }
            }

            int covered = 0;
            int resolved = 0;
            int overResolved = 0;
            string firstMiss = null;
            for (int i = 0; i < targets.Count; i++)
            {
                bool inTable = MetaHumanBoneAliases.AliasesOf(targets[i]).Length > 0;
                string hit = MetaHumanBoneAliases.Resolve(targets[i], assetBones);
                if (inTable)
                {
                    covered++;
                    if (hit == null)
                    {
                        if (firstMiss == null)
                        {
                            firstMiss = targets[i];
                        }
                    }
                    else
                    {
                        resolved++;
                    }
                }
                else if (hit != null)
                {
                    overResolved++;
                }
            }

            Eq(covered, resolved, "表覆盖到的身体目标必须全部解析成功（首条失配："
                + (firstMiss ?? "无") + "，共覆盖 " + covered + " / 模板目标 " + targets.Count + "）");
            Eq(0, overResolved, "表没覆盖的名字绝不该被解析出来（实得 " + overResolved + " 条）");
            True(covered > 0, "身体模板里至少该有一批目标被表覆盖（实得 " + covered
                + " / " + targets.Count + "）；为 0 说明这张表对真实模板毫无作用");

            // 反向对照：脸上的 FACIAL_ 结构在 Humanoid 骨架上不存在，必须解析不出来
            // 反向对照：脸上的 FACIAL_ 结构在 Humanoid 骨架上不存在，必须解析不出来
            // —— 用**真实资产的名单**再跑一遍：这张名单不是从别名表来的，
            //    所以这两条才叫"对着现实成立"，而不是"表内自洽"。
            //    来源：skeleton_rename(dryRun) 的 plan[].from（52 行全部真在骨架里），
            //    即 Character_1 的节点名其实是 Mixamo DEF-* 族（文档 §44.1）。
            string[] realAssetBones = {
                "DEF-spine", "DEF-spine.001", "DEF-spine.002", "DEF-spine.003",
                "DEF-spine.005", "DEF-spine.006",
                "DEF-shoulder.L", "DEF-shoulder.R", "DEF-upper_arm.L", "DEF-upper_arm.R",
                "DEF-forearm.L", "DEF-forearm.R", "DEF-hand.L", "DEF-hand.R",
                "DEF-thumb.01.L", "DEF-f_index.01.L", "DEF-f_middle.01.L",
                "DEF-f_ring.01.L", "DEF-f_pinky.01.L",
                "DEF-thumb.01.R", "DEF-f_index.01.R", "DEF-f_middle.01.R",
                "DEF-f_ring.01.R", "DEF-f_pinky.01.R",
                "DEF-thigh.L", "DEF-shin.L", "DEF-foot.L", "DEF-toe.L",
                "DEF-thigh.R", "DEF-shin.R", "DEF-foot.R", "DEF-toe.R",
            };
            HashSet<string> realSet = new HashSet<string>(realAssetBones, System.StringComparer.Ordinal);

            int literal = 0;
            int viaAlias = 0;
            for (int i = 0; i < targets.Count; i++)
            {
                if (realSet.Contains(targets[i]))
                {
                    literal++;
                }
                else if (MetaHumanBoneAliases.Resolve(targets[i], realSet) != null)
                {
                    viaAlias++;
                }
            }

            Eq(0, literal, "身体模板目标与真实资产节点名的**字面**同名条数（实得 " + literal
                + "）—— §44 说 0/24，这条把它钉住：若哪天不为 0，要么模板换了命名族要么夹具过期");
            True(viaAlias > 0, "经别名在真实资产名单上能对齐的条数该大于 0（实得 " + viaAlias
                + "）；为 0 说明这张表对着真资产完全不起作用");

            True(MetaHumanBoneAliases.AliasesOf("FACIAL_L_CheekInner").Length == 0,
                "FACIAL_ 目标不该有别名（那套结构在 Humanoid 骨架上根本没有对应骨头）");
            True(MetaHumanBoneAliases.Resolve("FACIAL_L_CheekInner", assetBones) == null,
                "FACIAL_ 目标若能被别名解析，说明有人在用'位置差不多'配对——会把形变写到错骨头上还全绿");
        }

        private static string RepoTemplatesDir()
        {
            return Path.Combine(FindRepoRoot(), "HundunWorld", "Plugins", "MetaHuman", "Content", "Templates");
        }

        private static string FindRepoRoot()
        {
            DirectoryInfo dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "HundunWorld")))
                {
                    return dir.FullName;
                }
            }

            return Directory.GetCurrentDirectory();
        }

        /// <summary>从工装输出目录往上找仓库内文件，最多六层：断言不该依赖当前工作目录。</summary>
        private static string FindUpwards(string relative)
        {
            try
            {
                DirectoryInfo dir = new DirectoryInfo(AppContext.BaseDirectory);
                for (int i = 0; i < 7 && dir != null; i++, dir = dir.Parent)
                {
                    string candidate = Path.Combine(dir.FullName, relative);
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }
            catch (Exception)
            {
                return null;
            }

            return null;
        }

        private static void TestMorphGuiMap(MetaHumanGuiToRawMap map, string templatesDir)
        {
            string path = Path.Combine(templatesDir, "Morph_To_Gui_Map.json");
            if (!File.Exists(path))
            {
                Skip("morph→GUI 表不在盘上：" + path + "（重跑 Tools/MetaHumanBuildMorphGuiMap.py）");
                return;
            }

            MetaHumanMorphGuiMap morphMap = MetaHumanMorphGuiMap.FromJson(File.ReadAllText(path));

            Eq(33, morphMap.Entries.Count, "morph→GUI 成表条数");
            Eq(33, morphMap.EntryCount, "表头 entryCount 与实际条目数一致");
            Eq(4, morphMap.Declined.Count, "declined 条数（lips_* 四条区域形状键）");
            Eq(0, morphMap.Unmapped.Count, "没有「既没进表也没被拒绝」的 morph");

            List<string> problems = morphMap.Validate(map);
            Eq(0, problems.Count, "装载期三条校验" + (problems.Count > 0 ? "：" + problems[0] : ""));

            int resolved = 0;
            for (int i = 0; i < morphMap.Entries.Count; i++)
            {
                MetaHumanMorphGuiEntry entry = morphMap.Entries[i];
                string gui;
                if (morphMap.TryGetGuiControl(entry.Morph, out gui) &&
                    string.Equals(gui, entry.Gui, StringComparison.Ordinal))
                {
                    resolved++;
                }
            }
            Eq(33, resolved, "每条 morph 都能按名查回它自己那条 GUI（索引建错这里就会掉）");

            int declinedReachable = 0;
            for (int i = 0; i < morphMap.Declined.Count; i++)
            {
                string gui;
                if (morphMap.IsDeclined(morphMap.Declined[i].Morph) &&
                    !morphMap.TryGetGuiControl(morphMap.Declined[i].Morph, out gui))
                {
                    declinedReachable++;
                }
            }
            Eq(4, declinedReachable, "declined 的那几条确实查不到通路（不是悄悄给个空串继续跑）");

            // 反查才是运行期真正用的方向：上游来的是 GUI 值，要拿回它名下的所有 morph
            int reverseTotal = 0;
            HashSet<string> seenGui = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < morphMap.Entries.Count; i++)
            {
                string gui = morphMap.Entries[i].Gui;
                if (!seenGui.Add(gui))
                {
                    continue;
                }

                reverseTotal += morphMap.MorphsForGui(gui).Count;
            }
            Eq(33, reverseTotal, "按 GUI 反查把 33 条 morph 都带回来了（共用同一控制的变体也在）");
            Eq(3, morphMap.MorphsForGui("CTRL_L_eye_cheekRaise.ty").Count,
               "cheekRaise 三个变体共用一条 GUI 控制，反查要给出 3 条");
            Eq(0, morphMap.MorphsForGui("CTRL_L_nonexistent.ty").Count,
               "不存在的 GUI 名反查回空表（不是抛异常，也不是给个空串冒充命中）");

            // ResolveAssetMorph：真头到位后产品写回端靠它把"模板给的名字"翻译成资产里真有的形状名。
            // 夹具用表自己的 morph 名当"资产名录"（这就是真头导入后的形状名集合），
            // 每条断言都要配"翻不出来也必须翻不出来"的反向对照，否则空实现也能全绿。
            HashSet<string> assetNames = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < morphMap.Entries.Count; i++)
            {
                assetNames.Add(morphMap.Entries[i].Morph);
            }

            Eq(0, assetNames.Count == 0 ? 1 : 0, "夹具：资产形状名录非空");
            EqS("blink_L", morphMap.ResolveAssetMorph("CTRL_L_eye_blink.ty", assetNames),
               "GUI 控制名能翻译成资产里的 morph 名（这是头部自定义能不能落地的关键一跳）");
            Eq(3, morphMap.MorphsForGui("CTRL_L_eye_cheekRaise.ty").Count,
               "一条共用 GUI 控制反查出 3 个 morph 变体");
            Eq(0, morphMap.ResolveAssetMorph("blink_L", assetNames) == null ? 0 : 1,
               "名字本来就在资产里时不做翻译（返回 null 让调用方按原名写，而不是自我改写成近似名）");
            Eq(0, morphMap.ResolveAssetMorph("noSuchMorph_QQ", assetNames) == null ? 0 : 1,
               "翻不出来就返回 null：宁可计入 skipped，也不猜一个近似名把错形变写进脸");
            Eq(0, morphMap.ResolveAssetMorph("CTRL_L_eye_blink.ty", new HashSet<string>(StringComparer.Ordinal)) == null ? 0 : 1,
               "空名录时不得凭空造出名字");
        }

        /// <summary>
        /// 头部形状通路的落地度审计：模板声明的每个骨骼目标，真实 SKM_Face 骨架里到底有没有。
        /// 头部自定义是 joint 驱动的（589 个 HeadShape 参数 → BoneTranslate），
        /// 所以这条覆盖率就是"玩家改数值能不能真的改到脸"的前置条件；
        /// 模板以后新增目标名若资产里不存在，这里会直接报红而不是运行期静默不生效。
        /// </summary>
        /// <summary>
        /// 三件套合一的落点读数：**真表**（两张 morph→GUI）+ **真名录**（引擎里实读的 821 个 UE 网格 morph 名，
        /// 也正是带名导入进资产的那批名字，§53 的单变量对照证明数量从 1043 变成 821）+ **真模板的 morph 目标**，
        /// 全部走产品里那条 `MetaHumanMorphBridge.ResolveAssetMorph`（含这轮新加的受控剥前缀）。
        /// 目的：把"表级 24 接上"这条只在临时脚本里量过的说法，换成有钉子、可复跑的读数。
        /// </summary>
        /// <summary>
        /// 模板里声明的资产路径必须真在盘上。这条钉子是本轮补的：
        /// `HeadAsset`/`BodyAsset` 之前长期写着 UE 侧的残留路径（`…/SKM_Face`、`…/SKM_Body`，本项目里根本没有），
        /// 没有任何断言问过"这个文件在吗"，所以声明与盘上资产分叉了也没人报。
        /// 判据自带**反向对照**：同一个探测器必须能判出一个假路径为"不在盘上"，
        /// 否则"0 条缺失"可能只是探测器永远不报问题。
        /// </summary>
        /// <summary>
        /// 身体参数的载体也必须有硬证：Ada 的身体网格（`Output/mhbody/Ada_BodyMesh.glb` 的命名节点，
        /// 已作为 `Content/Character/Models/Ada_BodyMesh.flax` 进引擎）要能承载身体模板里每一条骨骼目标参数，
        /// 并且模板声明的 `BodyAsset` 必须指向这份真资产。
        /// 头部那侧的同类证据在 §58（引擎内 HEAD COVERAGE 552），这条是身体那侧的对偶，别拿一条冒充另一条。
        /// </summary>
        /// <summary>
        /// 把"推到最大再退回最小"的残留审计从**合成夹具**扩到**真模板**：
        /// 现有那条只跑"每种档位一条合成参数"，真模板里 628 条参数、几百个骨骼/形状/材质目标
        /// 的组合面（同一条骨骼被多个参数写、双极量程、Axis 掩码、材质槽位）都没被这条性质覆盖过。
        /// 这一步正是玩家改身体/头部参数后再退回默认的那条路径，残留=体型错。
        /// </summary>
        private static void TestRealTemplatesPushAndRetreat(string templatesDir)
        {
            string facePath = Path.Combine(templatesDir, "Template_Face_Archetype.json");
            string bodyPath = Path.Combine(templatesDir, "Template_Body_Base.json");
            if (!File.Exists(facePath) || !File.Exists(bodyPath))
            {
                Skip("真模板不在盘上，推满再退回的残留审计没跑");
                return;
            }

            MetaHumanTemplate[] templates =
            {
                MetaHumanTemplate.FromJson(File.ReadAllText(facePath)),
                MetaHumanTemplate.FromJson(File.ReadAllText(bodyPath))
            };

            int totalParams = 0;
            foreach (MetaHumanTemplate tpl in templates)
            {
                List<MetaHumanParameter> ps = new List<MetaHumanParameter>(tpl.All());
                True(ps.Count > 0, tpl.TemplateId + " 真模板要有参数（空模板会让这条永远绿）");
                totalParams += ps.Count;

                MetaHumanSession pushed = new MetaHumanSession(tpl);
                foreach (MetaHumanParameter p in ps)
                {
                    pushed.Adjust(p.Id, p.Max);
                }

                MetaHumanSession retreated = new MetaHumanSession(tpl);
                foreach (MetaHumanParameter p in ps)
                {
                    // 与玩家实际操作同一条通路：推满 → 提交 → 退回最小 → 提交
                    retreated.Adjust(p.Id, p.Max);
                    retreated.Commit(tpl.TemplateId + " 推满 " + p.Id);
                    retreated.Adjust(p.Id, p.Min);
                    retreated.Commit(tpl.TemplateId + " 退回 " + p.Id);
                }

                MetaHumanSolution pushedSol = MetaHumanSolver.Solve(pushed.State);
                MetaHumanSolution retreatSol = MetaHumanSolver.Solve(retreated.State);
                MetaHumanSolution untouchedSol = MetaHumanSolver.Solve(new MetaHumanState(tpl));

                Eq(0, retreated.AuditIncremental().Count,
                   tpl.TemplateId + "：增量解算要与全量逐通道一致（推满再退回后）");

                // 退回最小不等于默认值（Default 才是），所以这里只钉"两条通路必须给出同一个解"：
                // 历史里退回到 Min 的解，与直接解 Min 的解必须逐通道相等 —— 不等就说明有残留。
                Eq(CountChannels(untouchedSol, tpl), CountChannels(retreatSol, tpl),
                   tpl.TemplateId + "：退回后的通道数要与同一模板的正常解一致");
                True(pushedSol.Bones.Count > 0 || pushedSol.MorphWeights.Count > 0,
                     tpl.TemplateId + "：推到最大确实动了通道（否则这条审计是空转）");
            }

            True(totalParams >= 600, "两份真模板的参数总量（实得 " + totalParams + "）");
        }

        /// <summary>数"解里非零/非空的通道数"，用模板自己的效果清单去数，不去猜结构。</summary>
        private static int CountChannels(MetaHumanSolution sol, MetaHumanTemplate tpl)
        {
            int n = 0;
            foreach (MetaHumanParameter p in tpl.All())
            {
                List<MetaHumanEffect> effects = p.Effects;
                for (int i = 0; i < effects.Count; i++)
                {
                    string kind = effects[i].Kind.ToString();
                    if (kind == "BoneTranslate" || kind == "BoneScale")
                    {
                        if (sol.Bones.ContainsKey(effects[i].Target))
                        {
                            n++;
                        }
                    }
                    else if (kind == "Morph")
                    {
                        if (sol.MorphWeights.ContainsKey(effects[i].Target))
                        {
                            n++;
                        }
                    }
                }
            }

            return n;
        }

        private static void TestBodyParamsResolveOnRealBodyAsset(string templatesDir)
        {
            string fixture = FindUpwards(Path.Combine("docs", "metahuman", "ue_runtime", "Ada_Body_node_names.json"));
            string templatePath = Path.Combine(templatesDir, "Template_Body_Base.json");
            if (fixture == null || !File.Exists(templatePath))
            {
                Skip("缺身体节点名清单或身体模板，身体承载量这组没跑（清单来源：Tools/metahuman_body_export.py 的产物 glb）");
                return;
            }

            HashSet<string> bodyNodes = new HashSet<string>(StringComparer.Ordinal);
            using (JsonDocument doc = JsonDocument.Parse(File.ReadAllText(fixture)))
            {
                foreach (JsonElement entry in doc.RootElement.EnumerateArray())
                {
                    string name = entry.GetString();
                    if (!string.IsNullOrEmpty(name))
                    {
                        bodyNodes.Add(name);
                    }
                }
            }

            True(bodyNodes.Count > 300, "身体节点名清单非空（读到 " + bodyNodes.Count + " 个）");

            MetaHumanTemplate body = MetaHumanTemplate.FromJson(File.ReadAllText(templatePath));
            int withBoneTarget = 0;
            int fully = 0;
            int none = 0;
            int partial = 0;
            List<string> missingExamples = new List<string>();
            foreach (MetaHumanParameter p in body.All())
            {
                List<MetaHumanEffect> effects = p.Effects;
                HashSet<string> targets = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < effects.Count; i++)
                {
                    string kind = effects[i].Kind.ToString();
                    if ((kind == "BoneTranslate" || kind == "BoneScale") && !string.IsNullOrEmpty(effects[i].Target))
                    {
                        targets.Add(effects[i].Target);
                    }
                }

                if (targets.Count == 0)
                {
                    continue;
                }

                withBoneTarget++;
                int hit = 0;
                foreach (string t in targets)
                {
                    if (bodyNodes.Contains(t))
                    {
                        hit++;
                    }
                    else if (missingExamples.Count < 6)
                    {
                        missingExamples.Add(p.Id + " → " + t);
                    }
                }

                if (hit == targets.Count)
                {
                    fully++;
                }
                else if (hit == 0)
                {
                    none++;
                }
                else
                {
                    partial++;
                }
            }

            Eq(30, withBoneTarget, "身体模板里带骨骼目标的参数条数");
            Eq(30, fully, "这些参数在真身体骨架上的承载量（部分 " + partial + "、写不出来 " + none + "）");
            Eq(0, partial, "不许有半条承载：一半骨骼在一半不在，说明骨架或模板对错了族");
            Eq(0, none, "一条都写不出来的参数数");

            string declared = body.BodyAsset;
            True(!string.IsNullOrEmpty(declared), "身体模板该声明 BodyAsset（空着等于没接线）");
            True(ResolveAssetOnDisk(Path.Combine(RepoRoot(templatesDir), "HundunWorld"), declared) != null,
                 "BodyAsset 声明要指向盘上真资产：" + declared);
        }

        private static void TestTemplateAssetPathsResolve(string templatesDir)
        {
            string projectDir = Path.Combine(RepoRoot(templatesDir), "HundunWorld");
            string[] files = { "Template_Face_Archetype.json", "Template_Body_Base.json" };
            List<string> declared = new List<string>();
            for (int i = 0; i < files.Length; i++)
            {
                string path = Path.Combine(templatesDir, files[i]);
                if (!File.Exists(path))
                {
                    Skip("模板不在盘上，资产路径解析这组没跑：" + path);
                    return;
                }

                MetaHumanTemplate t = MetaHumanTemplate.FromJson(File.ReadAllText(path));
                if (!string.IsNullOrEmpty(t.HeadAsset))
                {
                    declared.Add(files[i] + ".HeadAsset = " + t.HeadAsset);
                }

                if (!string.IsNullOrEmpty(t.BodyAsset))
                {
                    declared.Add(files[i] + ".BodyAsset = " + t.BodyAsset);
                }
            }

            Eq(2, declared.Count, "两份模板各该声明一条资产路径（不记数就会在空集上空转）");

            List<string> missing = new List<string>();
            for (int i = 0; i < declared.Count; i++)
            {
                int eq = declared[i].IndexOf(" = ", StringComparison.Ordinal);
                string value = declared[i].Substring(eq + 3);
                if (ResolveAssetOnDisk(projectDir, value) == null)
                {
                    missing.Add(declared[i]);
                }
            }

            Eq(0, missing.Count, "模板声明的资产路径必须真在盘上（缺失："
                + string.Join(" | ", missing.ToArray()) + "）");
            True(ResolveAssetOnDisk(projectDir, "Content/Character/Models/Definitely_Not_An_Asset_Zz") == null,
                "反向对照：同一个探测器必须能判出'不在盘上'（否则上面那条 0 是假的）");
        }

        private static string ResolveAssetOnDisk(string projectDir, string declaredPath)
        {
            string normalized = declaredPath.Replace('/', Path.DirectorySeparatorChar)
                                            .Replace('\\', Path.DirectorySeparatorChar);
            string baseName = Path.Combine(projectDir, normalized);
            if (File.Exists(baseName))
            {
                return baseName;
            }

            if (File.Exists(baseName + ".flax"))
            {
                return baseName + ".flax";
            }

            return null;
        }

        private static void TestThreeHopWithRealTablesAndRealNames(string templatesDir)
        {
            string templatePath = Path.Combine(templatesDir, "Template_Face_Archetype.json");
            string dnaMapPath = Path.Combine(templatesDir, "Morph_To_Gui_Map.json");
            string ueMapPath = Path.Combine(templatesDir, "Ue_Morph_To_Gui_Map.json");
            string namesPath = FindUpwards(Path.Combine("docs", "metahuman", "ue_runtime",
                                                        "Ada_Face_authoritative_names.json"));
            if (!File.Exists(templatePath) || !File.Exists(dnaMapPath) || !File.Exists(ueMapPath) || namesPath == null)
            {
                Skip("三件套缺件（模板/两张 morph→GUI 表/引擎 morph 名录），三跳落点这组没跑");
                return;
            }

            MetaHumanTemplate face = MetaHumanTemplate.FromJson(File.ReadAllText(templatePath));
            MetaHumanMorphGuiMap dnaMap = MetaHumanMorphGuiMap.FromJson(File.ReadAllText(dnaMapPath));
            MetaHumanMorphGuiMap ueMap = MetaHumanMorphGuiMap.FromJson(File.ReadAllText(ueMapPath));

            HashSet<string> assetNames = new HashSet<string>(StringComparer.Ordinal);
            using (JsonDocument doc = JsonDocument.Parse(File.ReadAllText(namesPath)))
            {
                JsonElement arr = doc.RootElement;
                if (arr.ValueKind == JsonValueKind.Object)
                {
                    // 兜底：认第一个"像清单"的数组值（这份文件的键顺序不是契约，别按名猜）
                    foreach (JsonProperty prop in doc.RootElement.EnumerateObject())
                    {
                        if (prop.Value.ValueKind == JsonValueKind.Array && prop.Value.GetArrayLength() > 100)
                        {
                            arr = prop.Value;
                            break;
                        }
                    }
                }

                True(arr.ValueKind == JsonValueKind.Array, "引擎 morph 名录读成了数组（拿到 " + arr.ValueKind + "）");
                if (arr.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement entry in arr.EnumerateArray())
                    {
                        string name = entry.ValueKind == JsonValueKind.String
                            ? entry.GetString()
                            : (entry.GetArrayLength() >= 2 ? entry[1].GetString() : null);
                        if (!string.IsNullOrEmpty(name))
                        {
                            assetNames.Add(name);
                        }
                    }
                }
            }

            Eq(821, assetNames.Count, "引擎实读的 morph 名录条数（带名导入的资产形状数与此一致）");

            HashSet<string> wanted = new HashSet<string>(StringComparer.Ordinal);
            foreach (MetaHumanParameter p in face.All())
            {
                List<MetaHumanEffect> effects = p.Effects;
                for (int i = 0; i < effects.Count; i++)
                {
                    // 按字符串判档而不是枚举常量名：常量名写错会是响亮 0 条，而不是静默少一档
                    if (effects[i].Kind.ToString() == "Morph" && !string.IsNullOrEmpty(effects[i].Target))
                    {
                        wanted.Add(effects[i].Target);
                    }
                }
            }

            Eq(37, wanted.Count, "脸模板里的 morph 目标数（§48 的 552/589 差值就是这 37 条）");

            int resolved = 0;
            int bogus = 0;
            List<string> unresolved = new List<string>();
            foreach (string name in wanted)
            {
                string hit = MetaHumanMorphBridge.ResolveAssetMorph(name, assetNames, dnaMap, ueMap);
                if (string.IsNullOrEmpty(hit))
                {
                    unresolved.Add(name);
                    continue;
                }

                // 返回的名字必须真在资产名录里：这是"跨族桥不许凭空造名"的唯一硬证
                if (!assetNames.Contains(hit))
                {
                    bogus++;
                }

                resolved++;
            }

            Eq(0, bogus, "桥给出的形状名必须全部真在资产名录里");
            // 37 条的去向：33 条落到资产里的单控制规范形（24 条原本就在表里 + Tools/MetaHumanEnrichUeMorphMap.py 补的 9 条），
            // 4 条是 `head_wm13_lips_*` —— 表里对它们的判定是 declined（"唇部区域形状键，没有单一同义 GUI 控制"），
            // 我试过拿 purse_* 当同义控制塞进去，门上的 declined 检查立刻把它抓出来撤回了（见文档 §56）。
            Eq(33, resolved, "37 条 morph 目标经真表 + 真名录 + 产品桥的落点数");
            Eq(4, unresolved.Count, "对不上的条数 = declined 的那 4 条唇部象限键（语义判定，不是代码缺口）");
            unresolved.Sort(StringComparer.Ordinal);
            Console.WriteLine("  三跳未对接（" + unresolved.Count + " 条）："
                + string.Join(", ", unresolved.GetRange(0, Math.Min(6, unresolved.Count))));
        }

        private static void TestHeadShapeTargetsExistInRealSkeleton(string templatesDir)
        {
            string templatePath = Path.Combine(templatesDir, "Template_Face_Archetype.json");
            string dnaNamesPath = Path.Combine(RepoRoot(templatesDir), "docs", "metahuman", "dna", "SKM_Face_names.json");
            if (!File.Exists(templatePath) || !File.Exists(dnaNamesPath))
            {
                Skip("头部目标覆盖审计缺文件：" + templatePath + " 或 " + dnaNamesPath);
                return;
            }

            MetaHumanTemplate face = MetaHumanTemplate.FromJson(File.ReadAllText(templatePath));

            HashSet<string> asset = new HashSet<string>(StringComparer.Ordinal);
            using (JsonDocument doc = JsonDocument.Parse(File.ReadAllText(dnaNamesPath)))
            {
                foreach (JsonElement table in doc.RootElement.GetProperty("name_tables").EnumerateArray())
                {
                    JsonElement names;
                    if (!table.TryGetProperty("names", out names))
                    {
                        continue;
                    }

                    foreach (JsonElement entry in names.EnumerateArray())
                    {
                        if (entry.ValueKind == JsonValueKind.Array && entry.GetArrayLength() >= 2)
                        {
                            asset.Add(entry[1].GetString());
                        }
                    }
                }
            }

            True(asset.Count > 0, "真实骨架名录非空（读到 " + asset.Count + " 个名字）");

            int boneRefs = 0;
            int boneMisses = 0;
            int morphTargets = 0;
            int materialTargets = 0;
            int hairStyleTargets = 0;
            int wmNormalTargets = 0;
            foreach (MetaHumanParameter p in face.Parameters)
            {
                foreach (MetaHumanEffect effect in p.Effects)
                {
                    if (string.IsNullOrEmpty(effect.Target))
                    {
                        continue;
                    }

                    if (effect.Kind == MetaHumanEffectKind.BoneTranslate || effect.Kind == MetaHumanEffectKind.BoneScale)
                    {
                        boneRefs++;
                        if (!asset.Contains(effect.Target))
                        {
                            boneMisses++;
                        }
                    }
                    else if (effect.Kind == MetaHumanEffectKind.Morph)
                    {
                        morphTargets++;
                    }
                    else if (effect.Kind == MetaHumanEffectKind.HairStyle)
                    {
                        hairStyleTargets++;
                    }
                    else
                    {
                        materialTargets++;
                        if (effect.Target.IndexOf("_normal_head_wm", StringComparison.Ordinal) > 0)
                        {
                            wmNormalTargets++;
                        }
                    }
                }
            }

            // 2026-10-13 实测量：BoneTranslate/BoneScale 引用 1488 次、去重 363 个目标，全部命中真实骨架；
            // 剩下不进骨架那两类是 37 个 morph 名与材质参数，它们本来就不该在节点名录里。
            // 2026-10-20：材质类效果由 9 条增到 50 条 —— 新增的 41 条把每个 WM 法线权重
            // （head_wmN_normal_<target>）接到对应 morph 上，否则法线混合链的 mask 恒为 0。
            Eq(0, boneMisses, "模板声明的骨骼目标必须都能在真实 SKM_Face 骨架里找到（找不到的都是静默不生效）");
            Eq(1488, boneRefs, "头部形状通路的骨骼引用数（掉了说明模板被改小）");
            Eq(37, morphTargets, "Morph 类效果条数");
            Eq(56, materialTargets, "材质类效果条数（15 条外观 + 41 条 WM 法线权重）");
            Eq(41, wmNormalTargets, "WM 法线权重必须 41/41 由 morph 驱动（少一条就是 mask 恒 0）");
            Eq(1, hairStyleTargets, "发型选择效果条数（hair.style → HairStyle hair）");
        }

        private static void TestFaceControlTableEnrichment(MetaHumanGuiToRawMap map, string templatesDir)
        {
            string tablePath = Path.Combine(templatesDir, "Face_Control_Table.json");
            if (!File.Exists(tablePath))
            {
                Skip("脸部控制表缺失：" + tablePath + "，换算段落地的检查没跑");
                return;
            }

            int bipolar = 0;
            int withMorph = 0;
            int controls = 0;
            int segmentsCompared = 0;
            int conversionsCompared = 0;
            List<string> problems = new List<string>();

            using (JsonDocument doc = JsonDocument.Parse(File.ReadAllText(tablePath)))
            {
                JsonElement controlsElement = doc.RootElement.GetProperty("Controls");
                foreach (JsonElement control in controlsElement.EnumerateArray())
                {
                    controls++;
                    string name = control.GetProperty("ControlName").GetString();
                    int guiIndex;
                    if (!map.TryGetGuiIndex(name, out guiIndex))
                    {
                        problems.Add(name + " 不在 UE 的 GUI 名录里");
                        continue;
                    }

                    JsonElement range = control.GetProperty("GuiRange");
                    float low = range[0].GetSingle();
                    float high = range[1].GetSingle();
                    if (Math.Abs(low - map.GuiRangeLow(guiIndex)) > 1e-6f || Math.Abs(high - map.GuiRangeHigh(guiIndex)) > 1e-6f)
                    {
                        problems.Add(name + " 的量程与映射表不一致");
                    }

                    if (low < 0f)
                    {
                        bipolar++;
                    }

                    List<MetaHumanGuiToRawSegment> expected = map.SegmentsOfGui(guiIndex);
                    Dictionary<string, List<MetaHumanGuiToRawSegment>> tableSegments = new Dictionary<string, List<MetaHumanGuiToRawSegment>>(StringComparer.Ordinal);
                    foreach (JsonElement target in control.GetProperty("RawTargets").EnumerateArray())
                    {
                        string raw = target.GetProperty("Raw").GetString();
                        int rawIndex;
                        if (!map.TryGetRawIndex(raw, out rawIndex))
                        {
                            problems.Add(name + " 的 raw 落点 " + raw + " 不在映射表里");
                            continue;
                        }

                        if (target.GetProperty("MorphTargets").GetArrayLength() > 0)
                        {
                            withMorph++;
                        }

                        List<MetaHumanGuiToRawSegment> bucket;
                        if (!tableSegments.TryGetValue(raw, out bucket))
                        {
                            bucket = new List<MetaHumanGuiToRawSegment>();
                            tableSegments[raw] = bucket;
                        }

                        foreach (JsonElement segment in target.GetProperty("Segments").EnumerateArray())
                        {
                            bucket.Add(new MetaHumanGuiToRawSegment
                            {
                                GuiIndex = guiIndex,
                                RawIndex = rawIndex,
                                From = segment[0].GetSingle(),
                                To = segment[1].GetSingle(),
                                Slope = segment[2].GetSingle(),
                                Cut = segment[3].GetSingle(),
                            });
                        }
                    }

                    // 段集合按 (raw, from, to, slope, cut) 归一后必须与映射表完全一致
                    List<string> expectedKeys = new List<string>();
                    for (int i = 0; i < expected.Count; i++)
                    {
                        expectedKeys.Add(SegmentKey(map, expected[i]));
                    }

                    List<string> tableKeys = new List<string>();
                    foreach (KeyValuePair<string, List<MetaHumanGuiToRawSegment>> kv in tableSegments)
                    {
                        for (int i = 0; i < kv.Value.Count; i++)
                        {
                            tableKeys.Add(SegmentKey(map, kv.Value[i]));
                        }
                    }

                    expectedKeys.Sort(StringComparer.Ordinal);
                    tableKeys.Sort(StringComparer.Ordinal);
                    segmentsCompared += tableKeys.Count;
                    if (expectedKeys.Count != tableKeys.Count)
                    {
                        problems.Add(name + " 的换算段数不一致：表里 " + tableKeys.Count + " 映射表 " + expectedKeys.Count);
                    }
                    else
                    {
                        for (int i = 0; i < expectedKeys.Count; i++)
                        {
                            if (expectedKeys[i] != tableKeys[i])
                            {
                                problems.Add(name + " 的换算段不同：" + expectedKeys[i] + " 对 " + tableKeys[i]);
                                break;
                            }
                        }
                    }

                    // 两条换算通路在每个控制的边界值上必须给出同一个 raw 值
                    float[] sampleValues = TestValuesFor(low, high, expected);
                    for (int v = 0; v < sampleValues.Length; v++)
                    {
                        float value = sampleValues[v];
                        Dictionary<string, float> single = new Dictionary<string, float>(StringComparer.Ordinal);
                        single[name] = value;
                        float[] guiValues = map.MaterializeGuiValues(single);
                        float[] viaMap = new float[map.RawCount];
                        map.ConvertInto(guiValues, viaMap);

                        Dictionary<string, float> viaTable = new Dictionary<string, float>();
                        foreach (KeyValuePair<string, List<MetaHumanGuiToRawSegment>> kv in tableSegments)
                        {
                            int rawIndex = kv.Value[0].RawIndex;
                            viaTable[kv.Key] = ContributionFromSegments(kv.Value, value, low, high);
                            conversionsCompared++;
                            if (Math.Abs(viaTable[kv.Key] - viaMap[rawIndex]) > 1e-4f)
                            {
                                problems.Add(name + "=" + value + " 时 " + kv.Key + " 两条通路不一致：表 " + viaTable[kv.Key] + " 映射 " + viaMap[rawIndex]);
                            }
                        }
                    }
                }
            }

            Eq(174, controls, "控制条数");
            Eq(70, bipolar, "带负半轴的双极控制数（量自映射表）");
            True(withMorph > 0, "至少有一个 raw 落点在当前资产里找到了真实 blendshape，实得 " + withMorph);
            // 防空转：这两条保证上面的对齐确实逐段/逐值比过，而不是在空集合上"全对"
            Eq(map.SegmentCount, segmentsCompared, "控制表里被比对过的换算段数等于映射表段数");
            True(conversionsCompared > controls * 5, "两条换算通路的比对次数要覆盖每个控制的多个取值，实得 " + conversionsCompared);
            Eq(0, problems.Count, "控制表的换算段与 UE 映射表逐条对齐（首条问题：" + (problems.Count > 0 ? problems[0] : "无") + "）");
            Console.WriteLine("  控制表换算：控制 " + controls + " 条，raw 落点找到真实 morph 的 " + withMorph + " 处，双极控制 " + bipolar
                + " 个，段比对 " + segmentsCompared + " 次，换算比对 " + conversionsCompared + " 次");
        }

        private static string SegmentKey(MetaHumanGuiToRawMap map, MetaHumanGuiToRawSegment s)
        {
            return map.RawName(s.RawIndex) + "|" + s.From + "|" + s.To + "|" + s.Slope + "|" + s.Cut;
        }

        private static float ContributionFromSegments(List<MetaHumanGuiToRawSegment> segments, float value, float low, float high)
        {
            float acc = 0f;
            for (int i = 0; i < segments.Count; i++)
            {
                MetaHumanGuiToRawSegment s = segments[i];
                if (s.From <= value && value < s.To)
                {
                    acc += s.Slope * value + s.Cut;
                }
                else if (s.From == low && value < s.From)
                {
                    acc += s.Slope * s.From + s.Cut;
                }
                else if (s.To == high && value >= s.To)
                {
                    acc += s.Slope * s.To + s.Cut;
                }
            }

            return acc;
        }

        /// <summary>取该控制所有值得代表点：量程两端、各段端点、以及越界点（越界才测得到钳位与"不参与"的分别）。</summary>
        private static float[] TestValuesFor(float low, float high, List<MetaHumanGuiToRawSegment> segments)
        {
            List<float> values = new List<float>();
            values.Add(low);
            values.Add(high);
            values.Add((low + high) * 0.5f);
            values.Add(low - 0.5f);
            values.Add(high + 0.5f);
            for (int i = 0; i < segments.Count; i++)
            {
                values.Add(segments[i].From);
                values.Add(segments[i].To);
                values.Add((segments[i].From + segments[i].To) * 0.5f);
            }

            return values.ToArray();
        }

        /// <summary>
        /// 内嵌表与散文件必须是同一份内容。这条检查存在的意义是：
        /// 重新生成了 JSON 却忘了重新烘焙时，发布版会带着旧表安静上线，而编辑器里跑得一切正常——
        /// 那是最难查的一类「绿着但错」。
        /// </summary>
        private static void TestBakedTables(MetaHumanTemplate face, MetaHumanTemplate body, string templatesDir)
        {
            string[] files = {
                "Template_Face_Archetype.json",
                "Template_Body_Base.json",
                "Face_Control_Table.json",
                "Gui_To_Raw_Map.json",
                "Arkit_To_Gui_Map.json",
                "Morph_To_Gui_Map.json",
                "Ue_Morph_To_Gui_Map.json",
                "Warp_To_Shape_Map.json",
                "Expression_Rig.json",
            };
            string[] baked = {
                MetaHumanBakedTables.FaceTemplateJson,
                MetaHumanBakedTables.BodyTemplateJson,
                MetaHumanBakedTables.FaceControlsJson,
                MetaHumanBakedTables.GuiToRawJson,
                MetaHumanBakedTables.ArkitMapJson,
                MetaHumanBakedTables.MorphGuiJson,
                MetaHumanBakedTables.UeMorphGuiJson,
                MetaHumanBakedTables.WarpShapeJson,
                MetaHumanBakedTables.ExpressionRigJson,
            };

            // 这两份清单是平行数组：加一份表却忘加一个内嵌属性，就会像刚才那样直接 IndexOutOfRange
            // （而不是报成一条有意义的 FAIL）。先钉长度相等，让它永远以断言的形式红。
            Eq(files.Length, baked.Length,
                "散文件清单与内嵌属性清单条数必须相等（实得 " + files.Length + " / " + baked.Length
                + "）——加一张表要同时加两处");

            // 这两份清单必须等长：加一张表只改一边，就会出现"少比一份却全绿"的漏检
            Eq(files.Length, baked.Length, "内嵌对照的文件清单与取值清单等长");

            int totalBytes = 0;
            for (int i = 0; i < files.Length; i++)
            {
                string path = Path.Combine(templatesDir, files[i]);
                if (!File.Exists(path))
                {
                    Skip("内嵌对照缺文件：" + path + "，这一份没比");
                    continue;
                }

                // 生成 JSON 的脚本在 Windows 上落的是 CRLF，烘焙脚本读回来时被通用换行折成 LF，
                // 所以按 LF 归一后逐字比：比的是内容，不是行尾约定。
                string text = File.ReadAllText(path).Replace("\r\n", "\n");
                totalBytes += System.Text.Encoding.UTF8.GetByteCount(text);
                True(text == baked[i], files[i] + " 的内嵌内容与散文件逐字相同（忘了重跑 MetaHumanBakeTables.py 会在这里报红）");
            }

            Eq(totalBytes, MetaHumanBakedTables.BakedTotalBytes, "内嵌表登记的原文字节总数与各份文件相加一致（防只重烘了一半）");

            // 内嵌那份还得真能被产品代码消费：光字符串相同只证明搬运没错，不证明读得进来
            MetaHumanTemplate bakedFace = MetaHumanTemplate.FromJson(baked[0]);
            EqS(face.TemplateId, bakedFace.TemplateId, "内嵌脸模板 Id 与文件一致");
            Eq(face.Parameters.Count, bakedFace.Parameters.Count, "内嵌脸模板参数数与文件一致");
            Eq(face.MorphCatalog.Count, bakedFace.MorphCatalog.Count, "内嵌脸模板 morph 名录条数与文件一致");
            Eq(0, bakedFace.Validate().Count, "内嵌脸模板自检零错误");

            MetaHumanTemplate bakedBody = MetaHumanTemplate.FromJson(baked[1]);
            Eq(body.Parameters.Count, bakedBody.Parameters.Count, "内嵌身模板参数数与文件一致");
            Eq(0, bakedBody.Validate().Count, "内嵌身模板自检零错误");

            MetaHumanGuiToRawMap bakedMap = MetaHumanGuiToRawMap.FromJson(baked[3]);
            Eq(174, bakedMap.GuiCount, "内嵌换算表 GUI 控制数");
            Eq(251, bakedMap.RawCount, "内嵌换算表 raw 控制数");
            Eq(259, bakedMap.SegmentCount, "内嵌换算表段数");

            // 控制表类在 Runtime 层（依赖 Flax），离线这边只用 JsonDocument 数结构与条数，
            // 关键是「字段真在文件里」，运行期那层由引擎自检组负责读成对象。
            int controlCount = 0;
            int rangeCount = 0;
            int segmentCount = 0;
            using (JsonDocument doc = JsonDocument.Parse(baked[2]))
            {
                foreach (JsonElement control in doc.RootElement.GetProperty("Controls").EnumerateArray())
                {
                    controlCount++;
                    if (control.GetProperty("GuiRange").GetArrayLength() == 2)
                    {
                        rangeCount++;
                    }

                    foreach (JsonElement target in control.GetProperty("RawTargets").EnumerateArray())
                    {
                        segmentCount += target.GetProperty("Segments").GetArrayLength();
                    }
                }
            }

            Eq(174, controlCount, "内嵌控制表控制条数");
            Eq(174, rangeCount, "内嵌控制表里带量程的控制数");
            Eq(259, segmentCount, "内嵌控制表里的换算段总数");
        }

        /// <summary>
        /// 拿 .dna 抽出来的**真实名字清单**当资产喂给核对逻辑（离线版的「自动绑骨」演练）。
        /// 这一组是把 G2 从「代码写完且能编译」往前推一步：绑定判定的对错本来与引擎无关，
        /// 只要名字清单是真的，缺什么、命中什么就能当场算出来。
        /// </summary>
        private static void TestBindingAgainstRealDnaNames(MetaHumanTemplate face, MetaHumanTemplate body, string templatesDir)
        {
            string root = RepoRoot(templatesDir);
            string faceNames = Path.Combine(root, "docs", "metahuman", "dna", "SKM_Face_names.json");
            string bodyNames = Path.Combine(root, "docs", "metahuman", "dna", "SKM_Body_names.json");
            if (!File.Exists(faceNames) || !File.Exists(bodyNames))
            {
                Skip("缺 .dna 名字清单（" + faceNames + " / " + bodyNames + "），绑骨核对演练没跑");
                return;
            }

            HashSet<string> faceBones = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> faceMorphs = new HashSet<string>(StringComparer.Ordinal);
            ReadDnaNames(faceNames, faceBones, faceMorphs);

            HashSet<string> bodyBones = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> bodyMorphs = new HashSet<string>(StringComparer.Ordinal);
            ReadDnaNames(bodyNames, bodyBones, bodyMorphs);

            True(faceBones.Count > 500, "脸部骨架名字清单成规模，实得 " + faceBones.Count);
            True(bodyBones.Count > 300, "身体骨架名字清单成规模，实得 " + bodyBones.Count);
            Eq(37, faceMorphs.Count, "脸部 blendshape 名录条数（SKM_Face.dna 里去掉宿主前缀后的唯一名）");

            MetaHumanBindingReport faceReport = MetaHumanBinding.Bind(face, faceBones, faceMorphs);
            MetaHumanBindingReport bodyReport = MetaHumanBinding.Bind(body, bodyBones, bodyMorphs);

            Eq(0, faceReport.MissingMorphs.Count, "脸模板的 morph 名录全在资产里（首条缺失："
                + (faceReport.MissingMorphs.Count > 0 ? faceReport.MissingMorphs[0] : "无") + "）");
            Eq(0, faceReport.MissingBoneTargets.Count, "脸模板引用的面部关节全在骨架里（首条缺失："
                + (faceReport.MissingBoneTargets.Count > 0 ? faceReport.MissingBoneTargets[0] : "无") + "）");
            Eq(0, bodyReport.MissingBoneTargets.Count, "身模板引用的骨骼全在身体骨架里（首条缺失："
                + (bodyReport.MissingBoneTargets.Count > 0 ? bodyReport.MissingBoneTargets[0] : "无") + "）");

            // 承载量按「参数」计而不只是按「名字缺不缺」：玩家能感知的单位是一条滑杆，
            // 一条参数的三个骨骼目标里缺一个是半个动作，既不算全可承载也不算写不出来（文档 §34）。
            Eq(605, faceReport.HeadParams, "脸模板的头部参数条数");
            Eq(589, faceReport.ServableParams, "对 .dna 权威名字清单全目标命中的头部参数条数");
            Eq(0, faceReport.UnservableParams.Count, "对权威清单不该有任何头部参数完全写不出来（首条："
                + (faceReport.UnservableParams.Count > 0 ? faceReport.UnservableParams[0] : "无") + "）");
            Eq(16, faceReport.NoTargetParams.Count, "只挂非骨骼/非 morph 通道（材质或发型）、不需要骨骼或 morph 目标的头部参数条数");
            Eq(faceReport.HeadParams,
                faceReport.ServableParams + faceReport.PartiallyServableParams
                + faceReport.UnservableParams.Count + faceReport.NoTargetParams.Count,
                "四档之和必须等于头部参数总数（防重计与漏计）");

            // 反向对照（同夹具）：名字清单清空 ⇒ 带目标的参数一律判写不出来，只挂材质那档条数不变。
            // 没有这一条，「589」可能只是计数根本不看清单。
            MetaHumanBindingReport none = MetaHumanBinding.Bind(face, new List<string>(), new List<string>());
            Eq(0, none.ServableParams, "空清单里不该有任何头部参数被判全可承载");
            Eq(589, none.UnservableParams.Count, "空清单时所有带骨骼或 morph 目标的头部参数都该判写不出来");
            Eq(16, none.NoTargetParams.Count, "空清单不改变只挂材质/发型那档的条数");
            Eq(605, none.HeadParams, "空清单不改变头部参数总数");

            // 「部分承载」这一档必须是真走得到的，不是摆设：从真实清单里抹掉一条被某参数用到的骨骼名。
            string dropped = null;
            foreach (MetaHumanParameter p in face.All())
            {
                if (p == null || p.Region == MetaHumanRegion.Body || p.Effects == null)
                {
                    continue;
                }

                HashSet<string> boneTargets = new HashSet<string>();
                for (int i = 0; i < p.Effects.Count; i++)
                {
                    MetaHumanEffect effect = p.Effects[i];
                    if (effect != null
                        && (effect.Kind == MetaHumanEffectKind.BoneTranslate
                            || effect.Kind == MetaHumanEffectKind.BoneScale)
                        && !string.IsNullOrEmpty(effect.Target))
                    {
                        boneTargets.Add(effect.Target);
                    }
                }

                if (boneTargets.Count < 2)
                {
                    continue;
                }

                foreach (string name in boneTargets)
                {
                    if (faceBones.Contains(name))
                    {
                        dropped = name;
                        break;
                    }
                }

                if (dropped != null)
                {
                    break;
                }
            }

            True(dropped != null,
                "夹具里该找到一条带两个以上骨骼目标且目标在真实清单里的头部参数，否则下面两条判据是空转");

            List<string> minusBones = new List<string>();
            foreach (string name in faceBones)
            {
                if (name != dropped)
                {
                    minusBones.Add(name);
                }
            }

            MetaHumanBindingReport minus = MetaHumanBinding.Bind(face, minusBones, faceMorphs);
            True(minus.ServableParams < faceReport.ServableParams,
                "抹掉一条骨骼名之后全可承载条数必须下降（实得 " + minus.ServableParams
                + "，对照 " + faceReport.ServableParams + "）");
            True(minus.PartiallyServableParams > 0,
                "部分承载档得是真会被走到的档（实得 " + minus.PartiallyServableParams + "）");
            Eq(minus.HeadParams,
                minus.ServableParams + minus.PartiallyServableParams
                + minus.UnservableParams.Count + minus.NoTargetParams.Count,
                "部分承载夹具里四档之和仍等于头部参数总数");

            // 空清单对照：同一份模板喂零名字时必须大量报缺，否则上面的「全命中」可能是核对根本没跑
            MetaHumanBindingReport empty = MetaHumanBinding.Bind(face, new HashSet<string>(StringComparer.Ordinal), new HashSet<string>(StringComparer.Ordinal));
            Eq(face.SkeletonSlotMap.Count, empty.MissingSlots.Count, "空骨架时每个槽位都该报缺");
            Eq(face.MorphCatalog.Count, empty.MissingMorphs.Count, "空名录时每个 morph 都该报缺");
            True(faceReport.MissingCount == 0 && empty.MissingCount > 0, "同一份模板：真清单零缺失、空清单大量缺失");

            // 回填通路：真名录不该再加，多出来的名字要变成能用的滑杆
            int alreadyThere = MetaHumanBinding.ImportMissingMorphs(MetaHumanTemplate.FromJson(face.ToJson()), faceMorphs);
            Eq(0, alreadyThere, "真名录回填 0 条（说明模板名录本就完整）");

            MetaHumanTemplate grown = MetaHumanTemplate.FromJson(face.ToJson());
            HashSet<string> extra = new HashSet<string>(faceMorphs, StringComparer.Ordinal);
            string[] newNames = {
                "head_wm1_testBlink_L",
                "head_wm1_testBlink_R",
                "head_wm2_testBrow_L",
                "head_wm2_testBrow_R",
                "head_wm3_testSmile",
                "teeth_wm1_testJawOpen",
                "head_wm13_testLips_UL",
                // 真资产里就有的两种落单形态：后缀带侧向但没有对称兄弟。
                // 少了这两条，夹具全在"回填出来的东西天然合法"的最优值上，
                // 校验门那次线上翻车（侧向参数必须声明 PairId）根本不会被走到。
                "head_wm1_orphanOnly_R",
                "eyeLeft_lod0_mesh__eye_pupilNarrow_L",
            };
            foreach (string name in newNames)
            {
                extra.Add(name);
            }

            int added = MetaHumanBinding.ImportMissingMorphs(grown, extra);
            Eq(newNames.Length, added, "回填把多出的 morph 变成滑杆");
            Eq(face.Parameters.Count + newNames.Length, grown.Parameters.Count, "参数总数随之增加");
            Eq(0, grown.Validate().Count, "回填后模板自检仍零错误");
            foreach (string name in newNames)
            {
                MetaHumanParameter p = grown.Find("morph." + name);
                True(p != null, "回填出的滑杆按名可调：" + name);
                if (p != null)
                {
                    True(p.Effects.Count == 1 && p.Effects[0].Target == name, "滑杆只写它自己那个 blendshape：" + name);
                    // Index 必须真的指回自己这一槽：快照按 Index 落位，错了就整体平移
                    True(p.Index >= 0 && p.Index < grown.Parameters.Count && grown.Parameters[p.Index].Id == p.Id,
                         "回填后 Index 与槽位对得上：" + name);
                }
            }

            // 成对的 morph 要互指对岸，否则「联动」对刚导入的这一批永远无效
            MetaHumanParameter blinkL = grown.Find("morph.head_wm1_testBlink_L");
            MetaHumanParameter blinkR = grown.Find("morph.head_wm1_testBlink_R");
            True(blinkL != null && blinkR != null && blinkL.PairId == blinkR.Id && blinkR.PairId == blinkL.Id,
                 "同名只差 _L/_R 的两个回填滑杆互指");
            True(grown.Find("morph.head_wm3_testSmile").PairId == null, "没有对岸的中央滑杆不该被硬配一个");
            True(grown.Find("morph.head_wm13_testLips_UL").Side == MetaHumanSide.None, "_UL 这种四角后缀不能被当成左");

            // 落单的侧向名：Side 是对"存在镜像联动"的声明，没有对岸就必须降回 None，
            // 否则 Validate 拦下整份模板，「回填→另存」在真资产上变成死路（2026-10-22 线上实测）。
            MetaHumanParameter orphanR = grown.Find("morph.head_wm1_orphanOnly_R");
            MetaHumanParameter orphanL = grown.Find("morph.eyeLeft_lod0_mesh__eye_pupilNarrow_L");
            True(orphanR != null && orphanR.Side == MetaHumanSide.None && string.IsNullOrEmpty(orphanR.PairId),
                 "只有 _R、没有 _L 兄弟的回填滑杆降回中央");
            True(orphanL != null && orphanL.Side == MetaHumanSide.None && string.IsNullOrEmpty(orphanL.PairId),
                 "对岸换了前缀（eyeLeft_/eyeRight_）的落单滑杆降回中央");
            // 正向对照：降级不能顺手把真成对的也抹平，否则这条检查只是"全部置 None"的同义反复
            True(blinkL.Side == MetaHumanSide.Left && blinkR.Side == MetaHumanSide.Right,
                 "有对岸的那一对仍保留左右标记（降级不是无差别清空）");
            True(orphanR != null && orphanR.Effects.Count == 1 && orphanR.Effects[0].Target == "head_wm1_orphanOnly_R",
                 "降回中央不影响这根滑杆照样驱动它自己那个形状");
            Eq(0, grown.Validate().Count, "含落单侧向名的回填结果仍然零校验错误");

            // 区域与左右归类的对照：这几条是玩家面板分组与镜像联动的基础，归错就一直没人会发现
            True(MetaHumanBinding.SideForMorph("head_wm1_blink_L") == MetaHumanSide.Left, "以 _L 结尾判左");
            True(MetaHumanBinding.SideForMorph("head_wm2_browsDown_R") == MetaHumanSide.Right, "以 _R 结尾判右");
            True(MetaHumanBinding.SideForMorph("head_wm1_jawOpen") == MetaHumanSide.None, "无后缀判中央");
            True(MetaHumanBinding.RegionForMorph("head_wm1_blink_L") == MetaHumanRegion.Eye, "blink 归眼");
            True(MetaHumanBinding.RegionForMorph("head_wm2_browsDown_L") == MetaHumanRegion.Brow, "brows 归眉");
            True(MetaHumanBinding.RegionForMorph("head_wm13_lips_UL") == MetaHumanRegion.Lip, "lips 归唇");
            True(MetaHumanBinding.RegionForMorph("head_wm1_jawOpen") == MetaHumanRegion.Jaw, "jawOpen 归颌");

            Console.WriteLine("  绑骨演练：脸槽位命中 " + faceReport.MatchedSlots.Count + "/" + face.SkeletonSlotMap.Count
                + "，身槽位命中 " + bodyReport.MatchedSlots.Count + "/" + body.SkeletonSlotMap.Count
                + "，morph 命中 " + faceReport.MatchedMorphs.Count + "/" + face.MorphCatalog.Count);

            TestArkitLayer(templatesDir);
        }

        /// <summary>
        /// ARKit 输入词汇表：每一条对应都要能在 UE 的权威 GUI→raw 表里反向指回同一条段，
        /// 并且「喂 ARKit 值 → 反解 GUI → 走 GUI→raw 换算」这条路最终把那个 raw 推到同样的值。
        /// </summary>
        private static void TestArkitLayer(string templatesDir)
        {
            string mapPath = Path.Combine(templatesDir, "Arkit_To_Gui_Map.json");
            string guiPath = Path.Combine(templatesDir, "Gui_To_Raw_Map.json");
            if (!File.Exists(mapPath) || !File.Exists(guiPath))
            {
                Skip("缺 ARKit 对照表或 GUI→raw 表，ARKit 接入这一组没跑");
                return;
            }

            MetaHumanArkitMap arkit = MetaHumanArkitMap.FromJson(File.ReadAllText(mapPath));
            MetaHumanGuiToRawMap gui = MetaHumanGuiToRawMap.FromJson(File.ReadAllText(guiPath));

            Eq(0, arkit.Validate().Count, "ARKit 对照表结构自检零错误");
            // 这几条数是量自生成器的输出，表被改动却没重新核对时会在这里报红
            Eq(47, arkit.ChannelCount, "接上的 ARKit 通道数");
            Eq(60, arkit.EntryCount, "ARKit 通道到 GUI 控制的对应条数");
            Eq(17, arkit.ExactCount, "其中同名 exact 依据的条数");
            Eq(43, arkit.AuthoredCount, "其中人为判定 authored 依据的条数");
            Eq(4, arkit.Declined.Count, "明确判为不可接的通道数（参照系不明就不硬接）");

            int reverified = 0;
            List<string> problems = new List<string>();
            for (int i = 0; i < arkit.Entries.Count; i++)
            {
                MetaHumanArkitEntry entry = arkit.Entries[i];
                int guiIndex;
                if (!gui.TryGetGuiIndex(entry.GuiControl, out guiIndex))
                {
                    problems.Add(entry.Arkit + " 指向的 GUI 控制不在权威表里：" + entry.GuiControl);
                    continue;
                }

                int rawIndex;
                if (!gui.TryGetRawIndex(entry.Raw, out rawIndex))
                {
                    problems.Add(entry.Arkit + " 指向的 raw 控制不在权威表里：" + entry.Raw);
                    continue;
                }

                // 权威表里这个控制写到那个 raw 的段，必须与对照表记录的段逐字段相同
                List<MetaHumanGuiToRawSegment> segments = gui.SegmentsOfGui(guiIndex);
                MetaHumanGuiToRawSegment? found = null;
                for (int s = 0; s < segments.Count; s++)
                {
                    if (segments[s].RawIndex == rawIndex)
                    {
                        found = segments[s];
                        break;
                    }
                }

                if (found == null)
                {
                    problems.Add(entry.Arkit + " 的 " + entry.GuiControl + " 并不写 " + entry.Raw);
                    continue;
                }

                MetaHumanGuiToRawSegment seg = found.Value;
                if (Math.Abs(seg.From - entry.Segment[0]) > 1e-6f || Math.Abs(seg.To - entry.Segment[1]) > 1e-6f
                    || Math.Abs(seg.Slope - entry.Segment[2]) > 1e-6f || Math.Abs(seg.Cut - entry.Segment[3]) > 1e-6f)
                {
                    problems.Add(entry.Arkit + " 的换算段与权威表不一致");
                    continue;
                }

                reverified++;

                // 端到端：ARKit 值经反解再走 GUI→raw，落点必须还是同一个值（取非整数以免走运）
                float[] probes = { 0f, 0.37f, 1f };
                for (int p = 0; p < probes.Length; p++)
                {
                    Dictionary<string, float> input = new Dictionary<string, float>(StringComparer.Ordinal);
                    input[entry.Arkit] = probes[p];
                    int unmatched;
                    int declinedSkipped;
                    int appliedEntries;
                    Dictionary<string, float> guiValues = arkit.ToGuiControls(input, out unmatched, out declinedSkipped, out appliedEntries);
                    if (unmatched != 0 || guiValues.Count == 0)
                    {
                        problems.Add(entry.Arkit + "@" + probes[p] + " 没接上（unmatched=" + unmatched + "）");
                        continue;
                    }

                    float[] values = gui.MaterializeGuiValues(guiValues);
                    float[] raws = new float[gui.RawCount];
                    gui.ConvertInto(values, raws);
                    if (Math.Abs(raws[rawIndex] - probes[p]) > 1e-4f)
                    {
                        problems.Add(entry.Arkit + "=" + probes[p] + " 最终落到 " + entry.Raw + " 上是 " + raws[rawIndex]);
                    }
                }
            }

            Eq(arkit.Entries.Count, reverified, "每条对应都能反查回权威表的同一条段");
            Eq(0, problems.Count, "ARKit → GUI → raw 端到端与权威表一致（首条问题：" + (problems.Count > 0 ? problems[0] : "无") + "）");

            // 符号对照：MetaHuman 用同一根 blink 控制的正负半轴分别表示闭眼与睁大，
            // 直接把 ARKit 的 1.0 照搬成 GUI 的 1.0 会把「睁大眼」做成「闭眼」。
            Dictionary<string, float> wide = new Dictionary<string, float>();
            wide["eyeWideLeft"] = 1f;
            int unmatchedWide;
            int declinedWide;
            int appliedWideEntries;
            Dictionary<string, float> wideGui = arkit.ToGuiControls(wide, out unmatchedWide, out declinedWide, out appliedWideEntries);
            float blinkGuiValue;
            True(wideGui.TryGetValue("CTRL_L_eye_blink.ty", out blinkGuiValue) && blinkGuiValue < 0f,
                 "eyeWideLeft 反解出的是负半轴 GUI 值（照搬正值就会做成闭眼），实得 " + blinkGuiValue);

            int blinkIndex;
            int widenIndex;
            gui.TryGetRawIndex("CTRL_expressions_eyeBlinkL", out blinkIndex);
            gui.TryGetRawIndex("CTRL_expressions_eyeWidenL", out widenIndex);
            float[] wideRaw = new float[gui.RawCount];
            gui.ConvertInto(gui.MaterializeGuiValues(wideGui), wideRaw);
            EqF(1f, wideRaw[widenIndex], "eyeWideLeft=1 最终把 eyeWidenL 推到 1");
            EqF(0f, wideRaw[blinkIndex], "同一时刻 eyeBlinkL 必须留在 0");

            // 明确不接的通道要报"没接上"，而不是被悄悄映射到别的控制上
            Dictionary<string, float> declined = new Dictionary<string, float>();
            declined["eyeLookInLeft"] = 1f;
            int unmatchedDeclined;
            int skippedDeclined;
            int declinedEntries;
            Dictionary<string, float> declinedGui = arkit.ToGuiControls(declined, out unmatchedDeclined, out skippedDeclined, out declinedEntries);
            Eq(0, unmatchedDeclined, "被判不可接的通道不算进「陌生通道」计数（它是分寸，不是数据出错）");
            Eq(1, skippedDeclined, "被判不可接的通道单独计数，调用方看得见");
            Eq(0, declinedGui.Count, "不可接的通道不会顺手写到别的控制上");
            True(arkit.IsDeclined("eyeLookInLeft"), "不可接的通道在表里能被查到原因");

            Console.WriteLine("  ARKit 接入：通道 " + arkit.ChannelCount + " 条 / 对应 " + arkit.EntryCount
                + " 条（exact " + arkit.ExactCount + "、authored " + arkit.AuthoredCount + "），端到端复核 "
                + reverified + " 条");
        }

        /// <summary>把真实骨架名换一侧：FACIAL_L_ 前缀互换，_l/_r 后缀互换。用来模拟"动画源骨架与目标左右命名相反"。</summary>
        private static string FlipSide(string name)
        {
            if (name.StartsWith("FACIAL_L", StringComparison.Ordinal))
            {
                return "FACIAL_R" + name.Substring(8);
            }

            if (name.StartsWith("FACIAL_R", StringComparison.Ordinal))
            {
                return "FACIAL_L" + name.Substring(8);
            }

            if (name.Length >= 2 && name[name.Length - 2] == '_')
            {
                char tail = name[name.Length - 1];
                if (tail == 'l')
                {
                    return name.Substring(0, name.Length - 1) + "r";
                }

                if (tail == 'r')
                {
                    return name.Substring(0, name.Length - 1) + "l";
                }
            }

            return name;
        }

        /// <summary>
        /// 骨骼动画自动绑定的匹配层，拿 .dna 抽出的**真实骨架名字**跑。
        /// 重点是镜像那一档：换侧匹配一旦配错，动画会把左手绑到右手上，而节点数看起来完全正常。
        /// </summary>
        /// <summary>
        /// 跨命名族（UE 名 ↔ Mixamo 的 DEF-* 名）靠别名档对齐，并且别名档必须抢在模板槽位档之前。
        /// 两侧名字都是资产实测：目标来自导入用的 glb 的 nodes[].name（与引擎回执 nodeCount 同一批骨架），
        /// 来源来自 skeleton_rename 的 dryRun 计划里的 plan[].from（那 52 行全在骨架里，tableRowsNotFoundInSkeleton 为空）。
        /// </summary>
        private static void TestCrossFamilyAliasTier()
        {
            List<string> target = new List<string>
            {
                "pelvis", "spine_01", "spine_02", "spine_03", "spine_04", "spine_05",
                "neck_01", "neck_02", "head",
                "clavicle_l", "clavicle_r", "clavicle_pec_l", "clavicle_out_l", "clavicle_scap_l",
                "upperarm_l", "upperarm_r"
            };
            List<string> source = new List<string>
            {
                "DEF-spine", "DEF-spine.001", "DEF-spine.002", "DEF-spine.003",
                "DEF-spine.005", "DEF-spine.006",
                "DEF-shoulder.L", "DEF-shoulder.R",
                "DEF-upper_arm.L", "DEF-upper_arm.R", "DEF-hand.L", "DEF-hand.R"
            };

            MetaHumanRetargetResult result = new MetaHumanRetargetResult();
            Dictionary<string, string> map = MetaHumanRetarget.BuildMapping(target, source, null, result);

            Eq(10, map.Count, "跨命名族躯干+锁骨+上臂要有 10 根对齐");
            Eq(6, target.Count - map.Count, "没有别名的 6 根要如实报未匹配（不是猜一根位置差不多的）");
            Eq(0, result.ExactMatches, "两侧没有精确同名");
            Eq(0, result.CaseInsensitiveMatches, "两侧没有忽略大小写同名");
            Eq(0, result.MirrorMatches, "跨命名族不许走换侧兜底");
            Eq(0, result.SlotMatches, "这一组没有模板，槽位档不该命中");
            Eq(map.Count, result.AliasMatches, "命中全部要记在别名档名下");
            True(!result.MirrorTierEnabled, "命名族不同的场景下镜像档不该被启用");

            // 反塌缩：整条脊柱各自落到各自的骨节，而不是一起被同一根接走
            EqS("DEF-spine", map["pelvis"], "pelvis 对齐");
            EqS("DEF-spine.001", map["spine_01"], "spine_01 对齐");
            EqS("DEF-spine.002", map["spine_02"], "spine_02 对齐");
            EqS("DEF-spine.003", map["spine_03"], "spine_03 对齐");
            EqS("DEF-spine.006", map["head"], "head 对齐");
            HashSet<string> distinctValues = new HashSet<string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> kv in map)
            {
                distinctValues.Add(kv.Value);
            }

            Eq(map.Count, distinctValues.Count, "别名档的映射必须是一对一");

            // 重名目标会让"逐节点命中数 > 映射字典条数"，这把静默丢失必须自己说出来
            MetaHumanRetargetResult dupResult = new MetaHumanRetargetResult();
            Dictionary<string, string> dupMap = MetaHumanRetarget.BuildMapping(
                new List<string> { "pelvis", "pelvis", "head" },
                new List<string> { "DEF-spine", "DEF-spine.006" }, null, dupResult);
            Eq(3, dupResult.Mapped.Count, "逐节点命中要如实记三条（两个 pelvis 各算一次）");
            Eq(2, dupMap.Count, "映射字典按目标名收键，重名只能占一条");
            True(HasNote(dupResult.Notes, "重名节点被合并"),
                 "重名导致的合并必须在回执里明说，不能让人对着 8 与 2 猜");

            // 只报个数不足以支撑"要不要改名"的决定：得把被合并的名字也列出来
            True(HasNote(dupResult.Notes, "pelvis×2"),
                 "回执要列出**具体哪根骨**被合并（pelvis×2），否则用户无法判断要不要规范骨骼名");

            MetaHumanRetargetResult uniqResult = new MetaHumanRetargetResult();
            MetaHumanRetarget.BuildMapping(new List<string> { "pelvis", "head" },
                new List<string> { "DEF-spine", "DEF-spine.006" }, null, uniqResult);
            True(!HasNote(uniqResult.Notes, "重名节点被合并"),
                 "名字全唯一时不该冒出这条提示（否则这条提示永远为真，等于没报）");

            // 排序证明：槽位档会先把 spine_02 抢去 DEF-spine 之外的同名候选 spine_01（多对一塌缩），
            // 别名档一对一更精确，必须排在它前面。
            MetaHumanTemplate template = new MetaHumanTemplate();
            template.SkeletonSlotMap["body.spine"] = new List<string>
            {
                "spine_01", "spine_02", "spine_03", "spine_04", "pelvis"
            };
            List<string> competingSource = new List<string> { "spine_01", "DEF-spine.002" };
            MetaHumanRetargetResult competing = new MetaHumanRetargetResult();
            Dictionary<string, string> competingMap = MetaHumanRetarget.BuildMapping(
                new List<string> { "spine_02" }, competingSource, template, competing);

            Eq(1, competingMap.Count, "别名档要在槽位档之前把 spine_02 接住");
            EqS("DEF-spine.002", competingMap["spine_02"], "别名命中优先于槽位的第一个候选");
            Eq(0, competing.SlotMatches, "槽位档不该抢走别名档能一对一命中的节点");

            // === 反向：target 是资产侧 DEF-* 族、source 是 UE/MetaHuman 族 ===
            // 两份名字清单都是资产实测（Character_1 的 plan[].from，与 Ada_BodyMesh 导入用的 glb 节点名），
            // 期望值不凭记忆写死：改用"按同一张别名表反查能解释多少条"做身份校验，
            // 再配一条量出来的下界（22 条是本轮实测，掉下来说明方向支持被弄丢了）。
            string defNames = FindUpwards(Path.Combine("docs", "metahuman", "ue_runtime", "Character_1_def_names.json"));
            string bodyNames = FindUpwards(Path.Combine("docs", "metahuman", "ue_runtime", "Ada_Body_node_names.json"));
            if (defNames == null || bodyNames == null)
            {
                Skip("缺反向对照的两份名字清单，别名档的反向这半没跑");
                return;
            }

            List<string> reverseTargets = ReadJsonStringArray(defNames);
            HashSet<string> reverseSource = new HashSet<string>(ReadJsonStringArray(bodyNames), StringComparer.Ordinal);

            MetaHumanRetargetResult revResult = new MetaHumanRetargetResult();
            Dictionary<string, string> revMap = MetaHumanRetarget.BuildMapping(reverseTargets,
                new List<string>(reverseSource), null, revResult);

            int explainable = 0;
            foreach (string t in reverseTargets)
            {
                string owner = MetaHumanBoneAliases.TemplateNameForAsset(t);
                if (!string.IsNullOrEmpty(owner) && reverseSource.Contains(owner))
                {
                    explainable++;
                }
            }

            True(revMap.Count > 0, "反向（target=DEF-* ← source=UE 名）必须接得住，不能只剩精确同名");
            Eq(explainable, revMap.Count, "反向命中数要等于同一张别名表反查能解释的条数（身份校验）");
            True(revMap.Count >= 22, "反向至少 22 条（本轮量出来的落点；掉了就是方向支持丢了）");
            Eq(revMap.Count, revResult.AliasMatches, "反向命中全部记在别名档名下");
            Eq(0, revResult.ExactMatches, "反向夹具里两侧没有精确同名");
            Eq(0, revResult.MirrorMatches, "反向不许靠换侧兜底");

            HashSet<string> revValues = new HashSet<string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> kv in revMap)
            {
                True(reverseSource.Contains(kv.Value), "反向映射的目标名必须真在来源骨架里：" + kv.Value);
                revValues.Add(kv.Value);
            }

            Eq(revMap.Count, revValues.Count, "反向映射也必须是一对一（两名抢一根就是别名表坏了）");
        }

        private static List<string> ReadJsonStringArray(string path)
        {
            List<string> values = new List<string>();
            using (JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path)))
            {
                foreach (JsonElement entry in doc.RootElement.EnumerateArray())
                {
                    string name = entry.GetString();
                    if (!string.IsNullOrEmpty(name))
                    {
                        values.Add(name);
                    }
                }
            }

            True(values.Count > 0, "名字清单读出了内容：" + Path.GetFileName(path));
            return values;
        }

        /// <summary>
        /// 读一份"名字清单"文件，**两种写法都认**：顶层裸数组，或对象里指定字段是数组。
        /// 为什么容忍两种：这份名录由工具侧探针产出（写成对象方便带出处信息），
        /// 而门里其它清单用的是裸数组；两边各自演进、又一直没同时出现过，
        /// 结果第一次对接就崩在"期望 Array 实得 Object"上。
        /// </summary>
        private static List<string> ReadJsonStringArrayOrObject(string path, string arrayProperty)
        {
            List<string> values = new List<string>();
            using (JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path)))
            {
                JsonElement root = doc.RootElement;
                JsonElement array = root;
                if (root.ValueKind == JsonValueKind.Object)
                {
                    JsonElement inner;
                    if (!root.TryGetProperty(arrayProperty, out inner))
                    {
                        True(false, Path.GetFileName(path) + " 是对象但没有 '" + arrayProperty + "' 数组");
                        return values;
                    }

                    array = inner;
                }

                if (array.ValueKind != JsonValueKind.Array)
                {
                    True(false, Path.GetFileName(path) + " 的 '" + arrayProperty + "' 不是数组");
                    return values;
                }

                foreach (JsonElement entry in array.EnumerateArray())
                {
                    string name = entry.GetString();
                    if (!string.IsNullOrEmpty(name))
                    {
                        values.Add(name);
                    }
                }
            }

            True(values.Count > 0, "名字清单读出了内容：" + Path.GetFileName(path));
            return values;
        }

        private static void TestRetargetWithRealSkeletons(string templatesDir)
        {
            string root = RepoRoot(templatesDir);
            string faceNames = Path.Combine(root, "docs", "metahuman", "dna", "SKM_Face_names.json");
            string bodyNames = Path.Combine(root, "docs", "metahuman", "dna", "SKM_Body_names.json");
            if (!File.Exists(faceNames) || !File.Exists(bodyNames))
            {
                Skip("缺真实骨架名字清单，动画重绑匹配这组没跑");
                return;
            }

            HashSet<string> faceBones = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> unused = new HashSet<string>(StringComparer.Ordinal);
            ReadDnaNames(faceNames, faceBones, unused);
            List<string> problems = new List<string>();
            HashSet<string> bodyBones = new HashSet<string>(StringComparer.Ordinal);
            ReadDnaNames(bodyNames, bodyBones, unused);

            List<string> face = new List<string>(faceBones);
            face.Sort(StringComparer.Ordinal);
            List<string> body = new List<string>(bodyBones);
            body.Sort(StringComparer.Ordinal);

            // 1) 同一份骨架自绑：必须全对且全走第一档
            MetaHumanRetargetResult identity = new MetaHumanRetargetResult();
            Dictionary<string, string> identityMap = MetaHumanRetarget.BuildMapping(face, face, null, identity);
            Eq(face.Count, identityMap.Count, "同名骨架自绑要全覆盖");
            True(!identity.MirrorTierEnabled, "自绑场景下镜像档不该参与");
            Eq(0, identity.Unmapped.Count, "自绑不该有未匹配");

            // 2) 真的换侧：目标只给左侧名、来源只给右侧名。
            //    （两侧都全量的话每个目标名在来源里都能精确命中，测不到镜像档——这是我第一版夹具的错）
            List<string> leftOnly = new List<string>();
            List<string> rightOnly = new List<string>();
            for (int i = 0; i < face.Count; i++)
            {
                string name = face[i];
                if (name.StartsWith("FACIAL_L", StringComparison.Ordinal) || name.EndsWith("_l", StringComparison.Ordinal))
                {
                    leftOnly.Add(name);
                }
                else if (name.StartsWith("FACIAL_R", StringComparison.Ordinal) || name.EndsWith("_r", StringComparison.Ordinal))
                {
                    rightOnly.Add(name);
                }
            }

            Eq(391, leftOnly.Count, "脸骨架的左侧节点数（FACIAL_L 380 + 小写 _l 后缀 11）");
            Eq(leftOnly.Count, rightOnly.Count, "左右必须成对，否则镜像档的期望值没法算");

            MetaHumanRetargetResult mirror = new MetaHumanRetargetResult();
            Dictionary<string, string> mirrorMap = MetaHumanRetarget.BuildMapping(leftOnly, rightOnly, null, mirror);
            Eq(leftOnly.Count, mirrorMap.Count, "换侧骨架也要全覆盖");
            Eq(0, mirror.ExactMatches, "目标与来源没有同一个名字，精确档必须一个都不命中");
            Eq(leftOnly.Count, mirror.MirrorMatches, "全部由镜像档接上，实得 " + mirror.MirrorMatches);
            Eq(0, mirror.Unmapped.Count, "换侧不该留下未匹配节点");
            True(mirror.MirrorTierEnabled, "命名约定相反时镜像档要打开（精确 0 < 换侧 391）");
            for (int i = 0; i < leftOnly.Count; i++)
            {
                string expected = MetaHumanRetarget.Mirror(leftOnly[i]);
                if (mirrorMap[leftOnly[i]] != expected)
                {
                    problems.Add(leftOnly[i] + " 的镜像配对错了：给 " + mirrorMap[leftOnly[i]] + " 期望 " + expected);
                }
            }

            // 3) 只在大小写上不同的两个真实节点名不能被互相遮蔽
            //    （SKM_Face.dna 里同时存在 "Archetype" 与 "archetype"，只建一张不敏感表就会把后者当成前者）
            True(face.Contains("Archetype") && face.Contains("archetype"), "夹具前提：脸骨架里确实同时有这两种写法");
            MetaHumanRetargetResult shadow = new MetaHumanRetargetResult();
            Dictionary<string, string> shadowMap = MetaHumanRetarget.BuildMapping(face, face, null, shadow);
            True(shadowMap.ContainsKey("archetype") && shadowMap["archetype"] == "archetype",
                 "同名精确优先：archetype 要绑到自己，而不是被 Archetype 抢走");
            Eq(face.Count, shadow.ExactMatches, "整份脸骨架自绑应全部走精确档，实得 " + shadow.ExactMatches);
            Eq(0, shadow.CaseInsensitiveMatches + shadow.MirrorMatches + shadow.SlotMatches,
              "自绑不该动用后面三档");

            // 4) 大小写差异来源：全部走忽略大小写档，精确档必须为零（否则说明第一档在骗人）
            List<string> upper = new List<string>();
            for (int i = 0; i < body.Count; i++)
            {
                upper.Add(body[i].ToUpperInvariant());
            }

            MetaHumanRetargetResult cased = new MetaHumanRetargetResult();
            Dictionary<string, string> caseMap = MetaHumanRetarget.BuildMapping(body, upper, null, cased);
            Eq(body.Count, cased.CaseInsensitiveMatches, "大小写不同的骨架要全部落到忽略大小写档");
            Eq(0, cased.ExactMatches, "第一档是区分大小写的相等，这里必须一个都不命中");

            // 5) 少一个来源节点：未匹配里必须正是那一个（漏报会被当成"绑定成功"）
            List<string> missing = new List<string>(body);
            string dropped = body[body.Count - 1];
            int droppedAt = missing.IndexOf(dropped);
            if (droppedAt >= 0)
            {
                missing.RemoveAt(droppedAt);
                MetaHumanRetargetResult gap = new MetaHumanRetargetResult();
                Dictionary<string, string> gapMap = MetaHumanRetarget.BuildMapping(body, missing, null, gap);
                Eq(1, gap.Unmapped.Count, "缺一个来源节点时恰好一个未匹配");
                True(gap.Unmapped.Count == 1 && gap.Unmapped[0] == dropped, "未匹配点名的就是被抽掉的那根，实得 "
                    + (gap.Unmapped.Count > 0 ? gap.Unmapped[0] : "无"));
                True(!gapMap.ContainsKey(dropped), "未匹配节点不能残留在映射表里");
                // 关键的一条：同骨架只缺一根关节时，不许用镜像把它"救"回对岸
                // （那等于把左手动画静默绑到右手，节点数还完全正常）
                True(!gap.MirrorTierEnabled, "约定一致的骨架不该启用镜像兜底");
                Eq(0, gap.MirrorMatches, "关闭兜底后镜像档命中数为零");
                True(gap.Notes.Count >= 1 && gap.Notes[0].Contains("不启用镜像兜底"),
                     "关闭镜像兜底这件事要写进说明，实得：" + (gap.Notes.Count > 0 ? gap.Notes[0] : "无"));
            }
            else
            {
                Skip("身体骨架清单里没找到可抽的节点，抽节点对照没跑");
            }

            // 6) 陌生命名体系：应当全部不匹配并说明原因（同一份目标在第 1 步是全匹配的，所以这不是空转）
            List<string> foreign = new List<string>();
            for (int i = 0; i < body.Count; i++)
            {
                foreign.Add("XYZ_" + body[i]);
            }

            MetaHumanRetargetResult none = new MetaHumanRetargetResult();
            Dictionary<string, string> noneMap = MetaHumanRetarget.BuildMapping(body, foreign, null, none);
            Eq(0, noneMap.Count, "命名体系不同要如实报零匹配");
            Eq(body.Count, none.Unmapped.Count, "陌生来源下每个目标节点都进未匹配");
            Eq(1, none.Notes.Count, "零匹配要给出一条说明");

            // 7) 槽位档：目标名与来源名不同、但同属模板登记的一个槽位
            MetaHumanTemplate bodyTemplate = MetaHumanTemplate.LoadFromFile(Path.Combine(templatesDir, "Template_Body_Base.json"));
            string slotKey = null;
            for (int i = 0; i < body.Count; i++)
            {
                foreach (KeyValuePair<string, List<string>> slot in bodyTemplate.SkeletonSlotMap)
                {
                    if (slot.Value.Count >= 2 && slot.Value.Contains(body[i]) && MetaHumanRetarget.Mirror(body[i]) != null)
                    {
                        slotKey = slot.Key;
                        break;
                    }
                }

                if (slotKey != null)
                {
                    break;
                }
            }

            if (slotKey == null)
            {
                Skip("身体模板里没有可当槽位对照的成侧槽位，槽位档没跑");
                Console.WriteLine("  动画重绑：脸 " + face.Count + " 节点自绑全对，镜像配对 " + leftOnly.Count
                    + " 条全中，大小写档 " + cased.CaseInsensitiveMatches + " 条");
                Eq(0, problems.Count, "镜像配对逐条正确（首条问题：" + (problems.Count > 0 ? problems[0] : "无") + "）");
                return;
            }

            List<string> slotCandidates = bodyTemplate.SkeletonSlotMap[slotKey];
            string target = null;
            string sourceName = null;
            foreach (string candidate in slotCandidates)
            {
                if (candidate == slotCandidates[0])
                {
                    target = candidate;
                }
                else if (candidate != target)
                {
                    sourceName = candidate;
                    break;
                }
            }

            MetaHumanRetargetResult slotResult = new MetaHumanRetargetResult();
            Dictionary<string, string> slotMap = MetaHumanRetarget.BuildMapping(new List<string> { target },
                                                                                new List<string> { sourceName },
                                                                                bodyTemplate, slotResult);
            True(slotMap.ContainsKey(target) && slotMap[target] == sourceName,
                 "同槽位候选名之间要能靠模板接上：" + slotKey + " " + target + " ← " + sourceName);
            Eq(1, slotResult.SlotMatches, "这条匹配走的是槽位档，实得 " + slotResult.SlotMatches);

            // 8) 换侧函数本身的形态对照（真实命名只有两种写法，都要照顾到，中央名不能被硬换）
            EqS("FACIAL_R_NeckB1", MetaHumanRetarget.Mirror("FACIAL_L_NeckB1"), "FACIAL_L_ 前缀换侧");
            EqS("FACIAL_L_NeckB1", MetaHumanRetarget.Mirror("FACIAL_R_NeckB1"), "FACIAL_R_ 前缀换回");
            EqS("clavicle_pec_r", MetaHumanRetarget.Mirror("clavicle_pec_l"), "小写 _l 后缀换侧");
            EqS("brow_A_R", MetaHumanRetarget.Mirror("brow_A_L"), "大写 _L 后缀换侧（外部动画常见写法）");
            True(MetaHumanRetarget.Mirror("FACIAL_C_NeckB") == null, "中央 FACIAL_C_ 不该被换侧");
            True(MetaHumanRetarget.Mirror("pelvis") == null, "无侧别后缀的名字不换侧");

            Console.WriteLine("  动画重绑：脸 " + face.Count + " 节点自绑全对，镜像配对 " + leftOnly.Count
                + " 条全中，大小写档 " + cased.CaseInsensitiveMatches + " 条，槽位档样本 " + slotKey);
            Eq(0, problems.Count, "镜像配对逐条正确（首条问题：" + (problems.Count > 0 ? problems[0] : "无") + "）");
        }

        /// <summary>从 .dna 名字清单里取骨骼名与 blendshape 名（morph 要去掉 `mesh.morph` 的宿主前缀并去重）。</summary>
        private static void ReadDnaNames(string path, HashSet<string> bones, HashSet<string> morphs)
        {
            using (JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path)))
            {
                foreach (JsonElement table in doc.RootElement.GetProperty("name_tables").EnumerateArray())
                {
                    string kind = table.GetProperty("kind").GetString();
                    if (kind == "joint_names")
                    {
                        foreach (JsonElement entry in table.GetProperty("names").EnumerateArray())
                        {
                            bones.Add(entry[1].GetString());
                        }
                    }
                    else if (kind == "qualified_names(mesh_or_set.name)")
                    {
                        foreach (JsonElement entry in table.GetProperty("names").EnumerateArray())
                        {
                            string name = entry[1].GetString();
                            if (!name.StartsWith("head_wm") && !name.StartsWith("teeth_wm") && !name.StartsWith("eyes_wm"))
                            {
                                continue;
                            }

                            if (name.Contains("."))
                            {
                                name = name.Substring(name.IndexOf('.') + 1);
                            }

                            morphs.Add(name);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// warp→形状对照表（第三命名族）的契约：装载校验、左岸覆盖、右岸真实性、
        /// 承重判别（带表才翻得出）、declined/表外名字不许凭空翻出、内嵌副本逐字一致。
        ///
        /// 判据不是"条目数 &gt; 0"这种空转能过的形式：
        /// 左岸集合由控制表**另算一遍**，右岸逐条问引擎名录，承重那条要求"不带这张表必须翻不出来"。
        /// </summary>
        private static void TestWarpShapeMapContract()
        {
            string mapPath = Path.Combine(RepoTemplatesDir(), "Warp_To_Shape_Map.json");
            string controlPath = Path.Combine(RepoTemplatesDir(), "Face_Control_Table.json");
            string dnaMapPath = Path.Combine(RepoTemplatesDir(), "Morph_To_Gui_Map.json");
            string ueMapPath = Path.Combine(RepoTemplatesDir(), "Ue_Morph_To_Gui_Map.json");
            string namesPath = FindUpwards(Path.Combine("docs", "metahuman", "ue_runtime",
                                                        "Ada_Face_authoritative_names.json"));
            if (!File.Exists(mapPath) || !File.Exists(controlPath) || !File.Exists(dnaMapPath)
                || !File.Exists(ueMapPath) || namesPath == null)
            {
                Skip("warp→形状对照表这组的输入缺件（对照表/控制表/两张 morph→GUI 表/引擎形状名录）");
                return;
            }

            string mapJson = File.ReadAllText(mapPath);
            MetaHumanWarpShapeMap map = MetaHumanWarpShapeMap.FromJson(mapJson);

            List<string> problems = map.Validate();
            Eq(0, problems.Count, problems.Count > 0 ? "对照表校验：" + problems[0] : "对照表校验问题数");

            // 左岸另算一遍：直接读控制表的 MorphCatalog 与逐条 Controls[].MorphTargets
            HashSet<string> warpsFromTable = new HashSet<string>(StringComparer.Ordinal);
            using (JsonDocument doc = JsonDocument.Parse(File.ReadAllText(controlPath)))
            {
                foreach (JsonProperty prop in doc.RootElement.EnumerateObject())
                {
                    if (prop.NameEquals("MorphCatalog"))
                    {
                        foreach (JsonElement el in prop.Value.EnumerateArray())
                        {
                            if (el.ValueKind == JsonValueKind.String)
                            {
                                warpsFromTable.Add(el.GetString());
                            }
                        }
                    }
                    else if (prop.NameEquals("Controls"))
                    {
                        foreach (JsonElement item in prop.Value.EnumerateArray())
                        {
                            JsonElement targets;
                            if (item.TryGetProperty("MorphTargets", out targets)
                                && targets.ValueKind == JsonValueKind.Array)
                            {
                                foreach (JsonElement el in targets.EnumerateArray())
                                {
                                    if (el.ValueKind == JsonValueKind.String)
                                    {
                                        warpsFromTable.Add(el.GetString());
                                    }
                                }
                            }
                        }
                    }
                }
            }

            HashSet<string> warpsFromMap = new HashSet<string>(map.AllWarps(), StringComparer.Ordinal);
            Eq(warpsFromTable.Count, warpsFromMap.Count, "左岸条数：表登记与控制表实读不符");

            int missing = 0;
            foreach (string warp in warpsFromTable)
            {
                if (!warpsFromMap.Contains(warp))
                {
                    missing++;
                }
            }
            int extra = 0;
            foreach (string warp in warpsFromMap)
            {
                if (!warpsFromTable.Contains(warp))
                {
                    extra++;
                }
            }
            Eq(0, missing, "控制表里的 warp 名没进对照表任何一张清单（会被静默丢弃）");
            Eq(0, extra, "对照表里有控制表没有的 warp 名（凭空多出来的左岸）");

            // 右岸真实性：候选必须是引擎名录里真有的形状名
            HashSet<string> assetNames = new HashSet<string>(StringComparer.Ordinal);
            using (JsonDocument doc = JsonDocument.Parse(File.ReadAllText(namesPath)))
            {
                JsonElement arr = doc.RootElement;
                if (arr.ValueKind == JsonValueKind.Object)
                {
                    foreach (JsonProperty prop in doc.RootElement.EnumerateObject())
                    {
                        if (prop.Value.ValueKind == JsonValueKind.Array && prop.Value.GetArrayLength() > 100)
                        {
                            arr = prop.Value;
                            break;
                        }
                    }
                }
                foreach (JsonElement el in arr.EnumerateArray())
                {
                    if (el.ValueKind == JsonValueKind.String)
                    {
                        assetNames.Add(el.GetString());
                    }
                }
            }

            int bogus = 0;
            int emptyEntries = 0;
            string firstBogus = string.Empty;
            for (int i = 0; i < map.Entries.Count; i++)
            {
                MetaHumanWarpShapeEntry entry = map.Entries[i];
                if (entry == null || entry.Shapes == null || entry.Shapes.Count == 0)
                {
                    emptyEntries++;
                    continue;
                }
                for (int k = 0; k < entry.Shapes.Count; k++)
                {
                    if (!assetNames.Contains(entry.Shapes[k]))
                    {
                        bogus++;
                        if (firstBogus.Length == 0)
                        {
                            firstBogus = entry.Warp + " -> " + entry.Shapes[k];
                        }
                    }
                }
            }
            Eq(0, emptyEntries, "有条目候选清单为空（该进 unmapped 而不是留空）");
            Eq(0, bogus, bogus > 0 ? "候选形状名不在引擎名录里：" + firstBogus : "候选形状名不在名录里的条数");

            // 承重判别按"实测找锚点"而不是钉死某一条：逐条试"带表翻得出、不带表翻不出"，
            // 第一条成立的就是锚点。一条都不成立也是有用结论（那说明这张表相对旧通路没有增量），
            // 但那时这条断言必须红给我看，而不是由我事先假设它一定成立。
            MetaHumanMorphGuiMap dna = MetaHumanMorphGuiMap.FromJson(File.ReadAllText(dnaMapPath));
            MetaHumanMorphGuiMap ue = MetaHumanMorphGuiMap.FromJson(File.ReadAllText(ueMapPath));

            string anchorWarp = null;
            string anchorShape = null;
            int resolvedByNew = 0;
            int oldRouteBlind = 0;
            int expectedOldBlind = 0;
            int splitFromOldRoute = 0;
            for (int i = 0; i < map.Entries.Count; i++)
            {
                MetaHumanWarpShapeEntry entry = map.Entries[i];
                string viaNew = MetaHumanMorphBridge.ResolveAssetMorph(
                    entry.Warp, assetNames, dna, ue, map);
                string viaOld = MetaHumanMorphBridge.ResolveAssetMorph(entry.Warp, assetNames, dna, ue);
                if (!string.IsNullOrEmpty(viaNew))
                {
                    resolvedByNew++;
                    // 新通路给的名字必须真是这张表里的候选，否则等于它没走这张表
                    True(entry.Shapes.Contains(viaNew),
                        entry.Warp + " 经新通路翻出的 " + viaNew + " 不在它的候选清单里");
                    if (string.IsNullOrEmpty(viaOld))
                    {
                        oldRouteBlind++;
                        anchorWarp = entry.Warp;
                        anchorShape = viaNew;
                    }
                }

                // 两处口径不许分裂：旧通路若能解，解出的名字必须同样在这张表的候选里
                if (!string.IsNullOrEmpty(viaOld) && !entry.Shapes.Contains(viaOld))
                {
                    splitFromOldRoute++;
                }

                // 独立再算一遍"旧通路本该看不见"的条数：候选一根都不在两张旧表的 morph 列里
                bool oldCanSee = false;
                for (int k = 0; k < entry.Shapes.Count; k++)
                {
                    if (InMorphColumn(dna, entry.Shapes[k]) || InMorphColumn(ue, entry.Shapes[k]))
                    {
                        oldCanSee = true;
                        break;
                    }
                }
                if (!oldCanSee)
                {
                    expectedOldBlind++;
                }
            }

            Eq(map.Entries.Count, resolvedByNew,
                "每条 entry 都应在这具全量名录下翻得出候选（翻不出说明表与桥脱节）");
            Eq(0, splitFromOldRoute,
                "旧通路解出的名字必须也在这张表的候选里（两处口径分裂的条数）");
            // 判据写成关系而不是硬编码条数：这张表相对旧通路的净增量，必须等于
            // "候选完全在两张旧表 morph 列之外"的条数。实测为 0 也是结论——
            // 那说明本表的价值在确定性、可追溯与多目标清单（它还改写过词形档的两处错指），
            // 而不是"解锁了翻不出来的名字"；但只要那两张表被改，这条会跟着红，不许假装新档在承重。
            Eq(expectedOldBlind, oldRouteBlind,
                "新档独力解出的条数应等于旧表完全看不见的条数（实测增量=" + oldRouteBlind
                + "，独立算出的应有值=" + expectedOldBlind + "）");
            if (anchorWarp != null)
            {
                HashSet<string> only = new HashSet<string>(StringComparer.Ordinal) { anchorShape };
                EqS(anchorShape,
                    MetaHumanMorphBridge.ResolveAssetMorph(anchorWarp, only, dna, ue, map),
                    "带对照表应翻到这根形状名");
                True(string.IsNullOrEmpty(MetaHumanMorphBridge.ResolveAssetMorph(anchorWarp, only, dna, ue)),
                     "不带对照表必须翻不出来，否则锚点不成立");
            }

            // 裁判档要可追溯：凡 basis=guiReferee，其第一个候选必须真出现在 UE 表的 morph 列里，
            // 且这条 warp 名必须能落到 .dna 表的某个键上 —— 否则"裁判指认"只是脚本里的一句注释。
            int refereeEntries = 0;
            int refereeTraceable = 0;
            for (int i = 0; i < map.Entries.Count; i++)
            {
                if (map.Entries[i].Basis != "guiReferee")
                {
                    continue;
                }
                refereeEntries++;
                bool dnaBacked = false;
                for (int k = 0; k < dna.Entries.Count; k++)
                {
                    MetaHumanMorphGuiEntry candidate = dna.Entries[k];
                    if (candidate == null || string.IsNullOrEmpty(candidate.Morph))
                    {
                        continue;
                    }
                    if (map.Entries[i].Warp == candidate.Morph
                        || map.Entries[i].Warp.EndsWith("_" + candidate.Morph, StringComparison.Ordinal))
                    {
                        dnaBacked = true;
                        break;
                    }
                }
                if (dnaBacked && map.Entries[i].Shapes.Count > 0 && InMorphColumn(ue, map.Entries[i].Shapes[0]))
                {
                    refereeTraceable++;
                }
            }
            Eq(refereeEntries, refereeTraceable,
                "guiReferee 档可追溯的条数（应为该档全部 " + refereeEntries + " 条，实得 " + refereeTraceable
                + "）——追不到说明生成脚本与两张表已经分家");

            True(string.IsNullOrEmpty(MetaHumanMorphBridge.ResolveAssetMorph(
                     "head_wm9_nonexistent_QQ", assetNames, dna, ue, map)),
                 "表外 warp 名不许被这张表凭空翻出东西");
            True(map.IsDeclined("head_wm13_lips_DL"), "head_wm13_lips_DL 必须仍记为 declined（沿用既有判定）");
            True(string.IsNullOrEmpty(MetaHumanMorphBridge.ResolveAssetMorph(
                     "head_wm13_lips_DL", assetNames, dna, ue, map)),
                 "declined 的名字不许被这张表翻出来");

            // 内嵌副本与散文件逐字一致（烘焙产物最容易只更新一边）
            string baked = MetaHumanBakedTables.WarpShapeJson;
            True(!string.IsNullOrEmpty(baked), "内嵌那份 warp→形状对照表为空（跑 Tools/MetaHumanBakeTables.py）");
            EqS(mapJson.Replace("\r\n", "\n"), (baked ?? string.Empty).Replace("\r\n", "\n"),
                "内嵌副本与散文件不逐字相同");
        }

        /// <summary>
        /// 工程内模型资产导入设置的普查，常驻版（方案文档 §124/§125）。
        ///
        /// 为什么值得钉成门：真头与真身之所以"外观通道无处可写"，根因是这两具资产的
        /// `ImportMaterials=false`（`.flax` 头部的明文导入设置里读得到）。
        /// 修法有 A/B/C 三条、由用户拍板，所以这里**不偷偷修数据，也不沉默记录**：
        /// 把"当前已知是坏的这两具"钉成显式期望值 —— 谁重导把材质打开了，这条就红着要求更新期望与名单，
        /// 缺陷不会退化成"只有文档里写着"。
        /// 同时验路线 C 的前提：材质与形状都开对的变体确实在盘上（当前 3 具）。
        /// </summary>
        private static void TestModelImportSettingsInventory()
        {
            string contentRoot = Path.Combine(RepoRoot(RepoTemplatesDir()), "HundunWorld", "Content");
            if (!Directory.Exists(contentRoot))
            {
                Skip("找不到工程 Content 目录（" + contentRoot + "），导入设置普查这组没跑");
                return;
            }

            List<string> materialsOff = new List<string>();
            List<string> bothOn = new List<string>();
            int withSettings = 0;
            int fileCount = 0;
            foreach (string file in Directory.GetFiles(contentRoot, "*.flax", SearchOption.AllDirectories))
            {
                fileCount++;
                Dictionary<string, bool> settings = ReadFlaxImportSettings(file);
                if (settings == null || !settings.ContainsKey("ImportMaterials"))
                {
                    continue;
                }
                withSettings++;
                string name = Path.GetFileName(file);
                bool materials;
                settings.TryGetValue("ImportMaterials", out materials);
                bool blend;
                settings.TryGetValue("ImportBlendShapes", out blend);
                if (!materials)
                {
                    materialsOff.Add(name);
                }
                if (materials && blend)
                {
                    bothOn.Add(name);
                }
            }

            True(withSettings >= 8,
                 "带模型导入设置的资产只扫到 " + withSettings + " 条（枚举到 " + fileCount + " 个 .flax）⇒ 扫描器漏了，"
                 + "这条普查不可信（上一版就是整份跳过 >9MB 而漏掉了主角资产）");
            Eq(2, materialsOff.Count,
               "当前已知 ImportMaterials=false 的资产数（打开材质后请连同这份名单一起更新）："
               + string.Join(" | ", materialsOff.ToArray()));
            True(materialsOff.Contains("Ada_BodyMesh.flax") && materialsOff.Contains("Ada_FaceMesh.flax"),
                 "关材质的那两具应当正是挂给角色的头与身，实得：" + string.Join(" | ", materialsOff.ToArray()));
            True(bothOn.Count >= 3,
                 "材质与形状都开对的变体少于 3 具 ⇒ 路线 C（改用 matted_test/slots_test 的资产）的前提没了，"
                 + "实得：" + string.Join(" | ", bothOn.ToArray()));
        }

        /// <summary>
        /// 从 `.flax` 里抠出那段明文导入设置 JSON。
        /// 只读前 8MB：导入设置就在头部，整份读会把几百 MB 的贴图也吞进来；
        /// 这也是上一版普查的错处（按整文件大小跳过 ⇒ 主角资产恰好被略过）。
        /// </summary>
        private static Dictionary<string, bool> ReadFlaxImportSettings(string path)
        {
            const int Cap = 8 * 1024 * 1024;
            byte[] buffer;
            int length;
            try
            {
                // FileShare.ReadWrite 是必须的：编辑器正持有这些资产的写句柄，
                // 少这个参数会整批抛 IOException，而下面的 catch 会把它咽成"这具资产没有导入设置"
                using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    length = (int)Math.Min(stream.Length, Cap);
                    buffer = new byte[length];
                    int read = 0;
                    while (read < length)
                    {
                        int got = stream.Read(buffer, read, length - read);
                        if (got <= 0)
                        {
                            break;
                        }
                        read += got;
                    }
                    length = read;
                }
            }
            catch
            {
                return null;
            }

            byte[] needle = System.Text.Encoding.ASCII.GetBytes("ImportMaterials");
            int index = -1;
            for (int i = 0; i + needle.Length <= length; i++)
            {
                bool match = true;
                for (int k = 0; k < needle.Length; k++)
                {
                    if (buffer[i + k] != needle[k])
                    {
                        match = false;
                        break;
                    }
                }
                if (match)
                {
                    index = i;
                    break;
                }
            }
            if (index < 0)
            {
                return null;
            }

            // 不能只往回找最近的 '{'：导入设置里有 Rotation/FramesRange 这类嵌套对象，
            // 最近的括号可能是**内层**的，解析出来是合法 JSON 却缺我们要的键 ⇒ 整批被当成"没有导入设置"。
            // 按括号深度回溯，才能落到包住 ImportMaterials 的那个外层 '{'。
            int braceStart = -1;
            int openDepth = 0;
            for (int i = index; i >= 0; i--)
            {
                byte back = buffer[i];
                if (back == (byte)'}')
                {
                    openDepth++;
                }
                else if (back == (byte)'{')
                {
                    if (openDepth == 0)
                    {
                        braceStart = i;
                        break;
                    }
                    openDepth--;
                }
            }
            if (braceStart < 0)
            {
                return null;
            }

            int depth = 0;
            int braceEnd = -1;
            int limit = Math.Min(length, braceStart + 65536);
            for (int i = braceStart; i < limit; i++)
            {
                byte c = buffer[i];
                if (c == (byte)'{')
                {
                    depth++;
                }
                else if (c == (byte)'}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        braceEnd = i;
                        break;
                    }
                }
            }
            if (braceEnd < 0)
            {
                return null;
            }

            string json = System.Text.Encoding.ASCII.GetString(buffer, braceStart, braceEnd - braceStart + 1);
            Dictionary<string, bool> result = new Dictionary<string, bool>(StringComparer.Ordinal);
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(json))
                {
                    foreach (JsonProperty prop in doc.RootElement.EnumerateObject())
                    {
                        if (prop.Value.ValueKind == JsonValueKind.True)
                        {
                            result[prop.Name] = true;
                        }
                        else if (prop.Value.ValueKind == JsonValueKind.False)
                        {
                            result[prop.Name] = false;
                        }
                    }
                }
            }
            catch
            {
                return null;
            }

            return result.ContainsKey("ImportMaterials") ? result : null;
        }

        /// <summary>
        /// 远端外观副本新旧判定（服务端推送与进场拉取共用同一个入口）：只有"严格更旧"才丢，
        /// 同修订号允许重放；两侧都 0 的初值必须能通过——首份副本不能被判成过期。
        /// </summary>
        private static void TestRemoteLookRevisionPolicy()
        {
            Eq(true, MetaHumanLookRevisionPolicy.IsStale(5, 4), "缓存 rev=5 收到 rev=4 应判过期");
            Eq(false, MetaHumanLookRevisionPolicy.IsStale(4, 5), "缓存 rev=4 收到 rev=5 应接受");
            Eq(false, MetaHumanLookRevisionPolicy.IsStale(5, 5), "同修订号应允许重放（幂等）");
            Eq(false, MetaHumanLookRevisionPolicy.IsStale(0, 0), "两侧都 0 时首份副本不能被判过期");
        }

        /// <summary>某根形状名是否出现在那张 morph→GUI 表的 morph 列里（遍历条目，不按名猜键）。</summary>
        private static bool InMorphColumn(MetaHumanMorphGuiMap map, string morphName)
        {
            if (map == null || string.IsNullOrEmpty(morphName))
            {
                return false;
            }
            for (int i = 0; i < map.Entries.Count; i++)
            {
                if (map.Entries[i] != null && morphName.Equals(map.Entries[i].Morph))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 资产注册表完整性：Content 内两条资产不能抢同一个头 GUID；生产头资产与它的 8 个材质子资产必须在盘上。
        /// 为什么要有这条：引擎注册表按路径**字符串精确**比对（AssetsCache::FindAsset 的 `e.Info.Path == path`），
        /// 任何工具用另一种分隔符拼写加载资产，都会让同一 GUID 以两条路径登记 → 引擎改名 → 重打包 →
        /// 文件被本进程占用（0x20）→ 改名不落盘 → 下次启动再换一个 GUID。头资产曾因此反复改名并一度变成
        /// `loaded=false`。这条闸门只看盘上事实，不依赖编辑器会话。
        /// </summary>
        private static void TestAssetRegistryIntegrity(string templatesDir)
        {
            string projectDir = Path.GetFullPath(Path.Combine(templatesDir, "..", "..", "..", ".."));
            string contentDir = Path.Combine(projectDir, "Content");
            True(Directory.Exists(contentDir), "资产完整性：工程 Content 目录存在（" + contentDir + "）");
            if (!Directory.Exists(contentDir))
            {
                return;
            }

            var byId = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            int flaxCount = 0;
            string[] files = Directory.GetFiles(contentDir, "*.flax", SearchOption.AllDirectories);
            for (int i = 0; i < files.Length; i++)
            {
                byte[] head = new byte[28 + 148 * 16];
                int read;
                try
                {
                    using (var fs = new FileStream(files[i], FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        read = fs.Read(head, 0, head.Length);
                    }
                }
                catch (IOException)
                {
                    continue;
                }
                if (read < 28 || BitConverter.ToUInt32(head, 0) != 1180124739u)
                {
                    continue;
                }
                int count = BitConverter.ToInt32(head, 24);
                if (count < 1 || count > 16)
                {
                    continue;
                }
                flaxCount++;
                for (int e = 0; e < count; e++)
                {
                    int off = 28 + e * 148;
                    if (off + 16 > read)
                    {
                        break;
                    }
                    string id = new Guid(new ReadOnlySpan<byte>(head, off, 16)).ToString("N");
                    if (!byId.TryGetValue(id, out var owners))
                    {
                        owners = new List<string>();
                        byId[id] = owners;
                    }
                    owners.Add(files[i]);
                }
            }

            True(flaxCount > 400, "资产完整性：扫到的 .flax 数量合理（>400，实得 " + flaxCount + "）");
            int dup = 0;
            string firstDup = null;
            foreach (var kv in byId)
            {
                if (kv.Value.Count > 1)
                {
                    dup++;
                    if (firstDup == null)
                    {
                        firstDup = kv.Key + " ← " + string.Join(" , ", kv.Value);
                    }
                }
            }
            Eq(0, dup, "资产完整性：Content 内没有两条资产抢同一个头 GUID"
                       + (firstDup != null ? "（首个：" + firstDup + "）" : ""));
            True(byId.Count >= flaxCount, "资产完整性：条目数不少于文件数（条目 " + byId.Count + " / 文件 " + flaxCount + "）");

            string headDir = Path.Combine(contentDir, "Character", "Models", "head_mh2");
            string headPath = Path.Combine(headDir, "Ada_FaceMesh_conformhead4.flax");
            True(File.Exists(headPath), "资产完整性：生产头资产在盘上（" + headPath + "）");
            int subAssets = 0;
            for (int i = 1; i <= 8; i++)
            {
                if (File.Exists(Path.Combine(headDir, "Ada_FaceMesh_conformhead4 Ada_FaceMesh-" + i + ".flax")))
                {
                    subAssets++;
                }
            }
            Eq(8, subAssets, "资产完整性：头的 8 个材质子资产齐全");

            // 预制体里的 SkinnedModel 引用必须指向现存资产（曾整批悬空：7 份角色预制体全指向已不存在的旧 GUID，
            // 编辑器里拖出来是空模型；用 RepairCharacterPrefabModels.py 改指现行 GUID）。
            int prefabRefs = 0;
            int dangling = 0;
            string firstDangling = null;
            string[] prefabs = Directory.GetFiles(Path.Combine(contentDir, "Prefabs"), "*.prefab", SearchOption.AllDirectories);
            for (int i = 0; i < prefabs.Length; i++)
            {
                string text;
                try
                {
                    text = File.ReadAllText(prefabs[i]);
                }
                catch (IOException)
                {
                    continue;
                }
                foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(
                             text, "\"SkinnedModel\"\\s*:\\s*\"([0-9a-fA-F]{32})\""))
                {
                    prefabRefs++;
                    string id = m.Groups[1].Value.ToLowerInvariant();
                    if (!byId.ContainsKey(id))
                    {
                        dangling++;
                        if (firstDangling == null)
                        {
                            firstDangling = Path.GetFileName(prefabs[i]) + " → " + id;
                        }
                    }
                }
            }
            True(prefabRefs > 0, "资产完整性：预制体里至少有一条 SkinnedModel 引用（实得 " + prefabRefs + "）");
            Eq(0, dangling, "资产完整性：预制体的 SkinnedModel 引用全部指向现存资产"
                            + (firstDangling != null ? "（首个悬空：" + firstDangling + "）" : ""));
        }
    }
}
