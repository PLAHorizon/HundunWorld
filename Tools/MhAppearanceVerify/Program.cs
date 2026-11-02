using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Horizon.Game.Message.Network;
using Horizon.Orleans.Interface;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Configuration;
using Orleans.Hosting;
using StackExchange.Redis;

namespace MhAppearanceVerify
{
    /// <summary>
    /// 数字人外观服务端通道的真集群对账：连已在跑的 Silo，把 CharacterGrain 的门禁逐条真调一遍，
    /// 每条拒绝路径都配同一份夹具上的正向对照（否则"存储本来就是空的"会让所有拒绝看起来都通过）。
    /// </summary>
    internal static class Program
    {
        private static int passed;
        private static int failed;
        // 前置不满足要单独记一类。上一轮真集群跑出「13 通过 / 9 失败」，
        // 那 9 条红其实全是同一个原因：该角色在**运行态**（grain）里已经发布过了 ——
        // SQL 行里 published=0 也不算数（权威在 grain），把前置当缺陷报会误导下一个人去改产品代码。
        private static int skippedPrecondition;
        private static bool sealedAtEntry;

        private static void Eq(object actual, object expected, string what)
        {
            bool ok = Equals(actual, expected);
            if (ok)
            {
                passed++;
                Console.WriteLine("  PASS " + what);
            }
            else
            {
                failed++;
                Console.WriteLine("  FAIL " + what + "：期望 [" + expected + "] 实得 [" + actual + "]");
            }
        }

        private static void True(bool condition, string what)
        {
            Eq(condition, true, what);
        }

        private static string Hex(byte[] bytes)
        {
            if (bytes == null)
            {
                return "<null>";
            }

            StringBuilder sb = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes)
            {
                sb.Append(b.ToString("x2"));
            }

            return sb.ToString();
        }

        /// <summary>
        /// FNV-1a 独立暴力版：故意不引用 Horizon.Game.Message 里的 MetaHumanLookBlobHash，
        /// 两边各算一遍才能把"服务端根本没用随包值"和"两边同错"分开。
        /// </summary>
        private static uint Fnv1a(byte[] bytes)
        {
            uint hash = 2166136261u;
            for (int i = 0; i < bytes.Length; i++)
            {
                hash ^= bytes[i];
                hash *= 16777619u;
            }

            return hash;
        }

        private static MetaHumanLookPayload Build(string templateId, byte[] blob, uint checksum, bool published)
        {
            return new MetaHumanLookPayload
            {
                TemplateId = templateId,
                TemplateVersion = "verify-1",
                TemplateParamCount = 8,
                EntryCount = 3,
                Blob = blob,
                BlobChecksum = checksum,
                Published = published,
                SavedAtUtcTicks = DateTime.UtcNow.Ticks,
            };
        }

        private static async Task<int> Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            if (args.Length < 1)
            {
                Console.WriteLine("用法：MhAppearanceVerify <characterId> [redis连接串] [--publish]");
                return 2;
            }

            long characterId = long.Parse(args[0]);
            string redis = args.Length > 1 && !args[1].StartsWith("--", StringComparison.Ordinal)
                ? args[1]
                : Environment.GetEnvironmentVariable("GATEWAY_REDIS_CONNECTION_STRING") ?? "127.0.0.1:6379";
            bool doPublish = Array.IndexOf(args, "--publish") >= 0;

            Console.WriteLine($"[外观对账] 角色 {characterId}，Redis {redis}，ClusterId=dev，--publish={(doPublish ? "开" : "关")}");

            HostApplicationBuilder builder = Host.CreateApplicationBuilder();
            builder.Logging.ClearProviders();
            builder.Logging.AddSimpleConsole(o =>
            {
                o.SingleLine = true;
                o.TimestampFormat = "HH:mm:ss ";
            });
            builder.Logging.SetMinimumLevel(Microsoft.Extensions.Logging.LogLevel.Warning);
            // 连不上集群在这里是真故障（不是可降级路径），所以不许刷屏，由本工装报一条摘要。
            builder.Logging.AddFilter("Orleans", Microsoft.Extensions.Logging.LogLevel.Error);
            builder.Logging.AddFilter("Microsoft.Extensions.Hosting", Microsoft.Extensions.Logging.LogLevel.None);
            builder.Logging.AddFilter("StackExchange.Redis", Microsoft.Extensions.Logging.LogLevel.Error);

            ConfigurationOptions redisOptions = ConfigurationOptions.Parse(redis);
            builder.UseOrleansClient(client =>
            {
                client.UseRedisClustering(o => o.ConfigurationOptions = redisOptions);
                client.Configure<ClusterOptions>(o =>
                {
                    o.ClusterId = "dev";
                    o.ServiceId = "BaseService";
                });
            });

            IHost host = builder.Build();
            IClusterClient cluster;
            try
            {
                await host.StartAsync();
                cluster = host.Services.GetRequiredService<IClusterClient>();
            }
            catch (Exception ex)
            {
                Console.WriteLine("  FAIL 连不上 Orleans 集群（Silo 在跑吗？）：" + ex.Message);
                return 1;
            }

            Console.WriteLine("  已连集群。");
            ICharacterGrain grain = cluster.GetGrain<ICharacterGrain>(characterId);

            const string TemplateId = "metahuman.face_archetype+metahuman.body_base";

            // 夹具：两段互不相同的 blob，用来证"覆盖写 + revision 递增"，也用来给拒绝路径当正向对照。
            byte[] blobA = Encoding.ASCII.GetBytes("MHKLverify-A-0123456789abcdef");
            byte[] blobB = Encoding.ASCII.GetBytes("MHKLverify-B-fedcba9876543210");
            uint sumA = Fnv1a(blobA);
            uint sumB = Fnv1a(blobB);

            // 驱逐探针必须**先写后读**：默认的进场流程会先 GetAppearanceAsync，
            // 那一步就把 SQL 里的已发布标记补回状态，于是"状态被清"这一分支被读操作自己掩盖掉。
            // 这一条直写就是为了让它现形：修复生效 ⇒ 被拒（AppearanceSealed=true）；修复失效 ⇒ 被收。
            // 探针的结论必须按「库里那份到底发布没有」来给：同一句 Success=true，
            // 对着已发布的角色是闸门失效，对着未发布的角色是正向对照（能写才是对的）。
            // 工装不读库（读了就把状态补水回来、掩盖掉要测的那一格），所以这一格由命令行显式声明。
            if (Array.IndexOf(args, "--evict-probe") >= 0)
            {
                bool expectAccept = Array.IndexOf(args, "--expect-accept") >= 0;
                UpdateCharacterAppearanceResponse probe =
                    await grain.UpdateAppearanceAsync(Build(TemplateId, blobB, sumB, false));
                Console.WriteLine("  [驱逐探针] 直写结果：Success=" + probe.Success
                                  + " Sealed=" + probe.AppearanceSealed
                                  + " AuthorityCopyMissing=" + probe.AuthorityCopyMissing
                                  + " Error=" + probe.Error);
                if (expectAccept)
                {
                    // 正向对照：库里未发布 ⇒ 必须收。若这里被拒，说明探针测的已不是"冷状态能不能写"，
                    // 而是某道与封存无关的门（模板/校验和/尺寸），后面那条拒绝结论就不可信。
                    Console.WriteLine(probe.Success
                        ? "  PASS 冷状态（无热缓存键）下未发布的角色照常写得进去 ⇒ 探针没把正常通路一起拦死"
                        : "  FAIL 冷状态下未发布的角色被拒（" + probe.Error + "）⇒ 正向对照坏了，拒绝结论不作数");
                    await host.StopAsync();
                    return probe.Success ? 0 : 1;
                }

                Console.WriteLine(probe.Success
                    ? "  FAIL 热缓存里没有已发布存档时，改写了已发布的外观 ⇒ 封存判定没回看 SQL 权威副本"
                    : "  PASS 冷状态下仍拒改 ⇒ 封存判定确实回看了 SQL");
                await host.StopAsync();
                return probe.Success ? 1 : 0;
            }

            // 先只读 Grain 状态、不碰 SQL：这一条用来把"激活卡住"与"SQL 补水分支卡住"分开。
            bool pub0 = await grain.IsAppearancePublishedAsync();
            Console.WriteLine("  进场 IsAppearancePublishedAsync = " + pub0 + "（纯状态读，不查库）");
            sealedAtEntry = pub0;

            MetaHumanLookPayload baseline = await grain.GetAppearanceAsync();
            Console.WriteLine("  进场已有外观：" + (baseline == null ? "<无>" : Hex(baseline.Blob) + " rev=" + baseline.EntryCount + " published=" + baseline.Published));

            if (!sealedAtEntry)
            {
            Console.WriteLine("[1] 正向通路：合法提交要收");
            UpdateCharacterAppearanceResponse respA = await grain.UpdateAppearanceAsync(Build(TemplateId, blobA, sumA, false));
            Eq(respA.Success, true, "合法 blob 被接受（Error=" + respA.Error + "）");
            int revA = respA.StoredRevision;

            MetaHumanLookPayload backA = await grain.GetAppearanceAsync();
            True(backA != null, "提交后读得到外观（读不到就说明写入压根没落状态）");
            Eq(Hex(backA.Blob), Hex(blobA), "读回的 Blob 与提交逐字节一致");
            Eq(backA.TemplateId, TemplateId, "模板 Id 原样回来");
            Eq(backA.TemplateParamCount, 8, "槽位数原样回来（槽位漂移最阴，必须可见）");
            Eq(backA.BlobChecksum, sumA, "落库校验和等于本工装独立算出的 FNV-1a");

            Console.WriteLine("[2] 校验和门：服务端要自己算，不信随包值");
            UpdateCharacterAppearanceResponse respBad = await grain.UpdateAppearanceAsync(
                Build(TemplateId, blobB, Fnv1a(blobA), false));
            Eq(respBad.Success, false, "随包校验和与 Blob 不符时被拒");
            True(respBad.Error != null && respBad.Error.Contains("校验和"), "拒绝理由点名是校验和（不是笼统失败）");
            MetaHumanLookPayload afterBad = await grain.GetAppearanceAsync();
            Eq(Hex(afterBad.Blob), Hex(blobA), "被拒之后库里仍是上一份（同夹具正向对照：拒绝确实没写进去）");
            Eq(afterBad.BlobChecksum, respBad.StoredChecksum, "回执里的 StoredChecksum 与库里一致");

            Console.WriteLine("[3] 模板门：换模板要整体作废，不做字段平移");
            UpdateCharacterAppearanceResponse respTmpl = await grain.UpdateAppearanceAsync(
                Build("metahuman.other_template", blobB, sumB, false));
            Eq(respTmpl.Success, false, "模板 Id 与已存不一致时被拒");
            True(respTmpl.Error != null && respTmpl.Error.Contains("模板"), "拒绝理由点名是模板不一致");

            Console.WriteLine("[4] 尺寸门：超过 256KB 拒收");
            byte[] huge = new byte[256 * 1024 + 1];
            for (int i = 0; i < huge.Length; i++)
            {
                huge[i] = (byte)(i & 0xFF);
            }

            UpdateCharacterAppearanceResponse respHuge = await grain.UpdateAppearanceAsync(
                Build(TemplateId, huge, Fnv1a(huge), false));
            Eq(respHuge.Success, false, "超大 Blob 被拒");
            True(respHuge.Error != null && respHuge.Error.Contains("超出上限"), "拒绝理由点名是尺寸上限");

            Console.WriteLine("[5] 覆盖写 + revision 单调（玩家回溯到任意步的服务端前提）");
            UpdateCharacterAppearanceResponse respB = await grain.UpdateAppearanceAsync(Build(TemplateId, blobB, sumB, false));
            Eq(respB.Success, true, "第二次合法提交被接受（Error=" + respB.Error + "）");
            True(respB.StoredRevision > revA, "revision 严格递增（" + revA + " → " + respB.StoredRevision + "）");
            MetaHumanLookPayload backB = await grain.GetAppearanceAsync();
            Eq(Hex(backB.Blob), Hex(blobB), "读回到的是最新那份");

            bool published = await grain.IsAppearancePublishedAsync();
            Eq(published, false, "未发布时 IsAppearancePublishedAsync 为 false");

            Console.WriteLine("[5b] 修订号随 blob 过真集群（MetaHumanLook v2 追加字段）");
            var look = new MetaHuman.MetaHumanLook
            {
                TemplateId = TemplateId,
                TemplateVersion = "1",
                TemplateParamCount = 598,
                LinkSymmetric = true,
                Published = true,
                SavedAtUtcTicks = DateTime.UtcNow.Ticks,
                Revision = 7L,
            };
            look.Values["body.height"] = 177.5f;
            look.Checksum = MetaHuman.MetaHumanLook.ComputeChecksum(look.Values);
            byte[] lookBlob = look.ToBytes();
            var respRev = await grain.UpdateAppearanceAsync(Build(TemplateId, lookBlob, Fnv1a(lookBlob), true));
            Eq(respRev.Success, true, "带 v2 blob 的发布提交被接受（Error=" + respRev.Error + "）");
            MetaHumanLookPayload backRev = await grain.GetAppearanceAsync();
            var decoded = MetaHuman.MetaHumanLook.FromBytes(backRev.Blob);
            Eq(decoded.Revision, 7L, "服务端回传的 blob 里 Revision 仍是 7");
            Eq(decoded.Published, true, "服务端回传的 blob 保留发布位");
            Eq(decoded.Values["body.height"], 177.5f, "服务端回传的 blob 值未变");
            }
            else
            {
                // 前置不满足就明说"跳过了哪几组、为什么"，不要拿 9 条红去冒充 9 个产品缺陷。
                // 判据来源是**进场那一读**（grain 状态），不是 SQL 行 —— 权威在 grain，
                // 库里 published=0 也可能仍被封存（实测：删掉 Redis 键后复跑仍报"已发布"，因为 grain 在那个 Silo 里是激活态）。
                skippedPrecondition++;
                Console.WriteLine("[1]-[5] 跳过（前置不满足）：进场时该角色的外观在运行态**已发布**，");
                Console.WriteLine("        正向通路 / 覆盖写 / revision 递增 / 校验和与库内对照这些断言在这种状态下必然不过。");
                Console.WriteLine("        要跑完整链：换一个未发布的 character_id，或在停掉当前 Silo 后重启（删 Redis 状态键不足以放开）。");
            }

            if (doPublish)
            {
                Console.WriteLine("[6] 发布封存：置 Published 后服务端永久拒改（本步对真库不可逆）");
                UpdateCharacterAppearanceResponse respPub = await grain.UpdateAppearanceAsync(Build(TemplateId, blobB, sumB, true));
                if (!sealedAtEntry)
                {
                    Eq(respPub.Success, true, "带发布标记的提交被接受");
                }
                else
                {
                    // 已经是发布态，再置一次当然被拒 —— 这不是缺陷，与"闸门拒绝重复改"是同一件事。
                    Console.WriteLine("  SKIP(前置) 带发布标记的提交：进场时已发布，重复置位被拒属预期");
                }
                Eq(await grain.IsAppearancePublishedAsync(), true, "发布后服务端判定为已发布");

                UpdateCharacterAppearanceResponse respAfter = await grain.UpdateAppearanceAsync(Build(TemplateId, blobA, sumA, false));
                Eq(respAfter.Success, false, "已发布之后再改被拒（这就是「发布后不可回溯」的服务端闸门）");
                True(respAfter.Error != null && respAfter.Error.Contains("已发布"), "拒绝理由点名是已发布");
                Console.WriteLine("  ⚠ 该角色的外观现在已发布，API 层面无法撤销；要放开得直接改存储。");
            }
            else
            {
                Console.WriteLine("[6] 发布封存：未跑（没带 --publish；这一步对真库不可逆，故意留给显式授权）");
            }

            Console.WriteLine();
            Console.WriteLine("对账 SQL（另起一遍，别信工装的内存值）：");
            Console.WriteLine("  SELECT character_id, meta_human_template_id, meta_human_checksum, meta_human_revision,"
                              + " DATALENGTH(meta_human_look), meta_human_published"
                              + " FROM Game_HunduShijie_Character WHERE character_id = " + characterId + ";");
            Console.WriteLine($"合计：通过 {passed} 失败 {failed} 前置不满足跳过 {skippedPrecondition} 组");
            if (skippedPrecondition > 0)
            {
                Console.WriteLine("  注意：跳过的组数不是 0 时，这一轮**没有**覆盖发布前的通路；要全绿需换 id 或重启 Silo。");
            }
            return failed == 0 ? 0 : 1;
        }
    }
}
