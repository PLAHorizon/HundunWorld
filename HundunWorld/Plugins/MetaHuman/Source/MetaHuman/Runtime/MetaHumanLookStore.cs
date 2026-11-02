using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FlaxEngine;
using MetaHuman;

namespace MetaHuman.Runtime
{
    /// <summary>
    /// 外观快照的本地仓库：按角色 Id 存 MetaHumanLook 的字节流 + 一个发布标记位。
    /// 服务端通道（P5 提案）落地前，「保存」与「重新进入编辑时恢复」就靠它；
    /// 之后服务端只是多一层权威副本，客户端的读写口径不变。
    /// </summary>
    public static class MetaHumanLookStore
    {
        private const string FolderName = "MetaHuman/Looks";
        private const int MaxLooksPerCharacter = 12;

        private static MetaHumanLook _pending;

        /// <summary>
        /// 发布时角色还不存在（创角回执才有 CharacterId），所以先在这里挂一次「待认领」。
        /// 命名页 Publish → 服务端创建成功 → 回执带 Id → 取走并落到该 Id 名下。
        /// </summary>
        public static void SetPendingLook(MetaHumanLook look)
        {
            _pending = look;
        }

        /// <summary>取走待认领快照（取完即清空，避免下一次创建误领上一张脸）。</summary>
        public static bool TryTakePendingLook(out MetaHumanLook look)
        {
            look = _pending;
            _pending = null;
            return look != null;
        }

        public static bool HasPendingLook
        {
            get { return _pending != null; }
        }

        /// <summary>根目录：编辑器走工程目录，打包后走产品本地目录（可写）。</summary>
        public static string RootDirectory
        {
            get
            {
                try
                {
                    string local = Globals.ProductLocalFolder;
                    if (!string.IsNullOrEmpty(local))
                    {
                        return Path.Combine(local, FolderName);
                    }
                }
                catch
                {
                    // 某些目标里拿不到，继续回落到工程目录
                }

                try
                {
                    string project = Globals.ProjectFolder;
                    if (!string.IsNullOrEmpty(project))
                    {
                        return Path.Combine(project, "Saved", FolderName);
                    }
                }
                catch
                {
                }

                return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, FolderName);
            }
        }

        private static string FileFor(string characterId)
        {
            return Path.Combine(RootDirectory, Sanitize(characterId) + ".mhl");
        }

        /// <summary>角色 Id 会直接进文件名，这里挡掉路径穿越与非法字符。</summary>
        private static string Sanitize(string characterId)
        {
            if (string.IsNullOrEmpty(characterId))
            {
                return "_default";
            }

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < characterId.Length; i++)
            {
                char c = characterId[i];
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_')
                {
                    sb.Append(c);
                }
            }

            string result = sb.ToString();
            return string.IsNullOrEmpty(result) ? "_default" : result;
        }

        /// <summary>存一版。返回落盘路径；失败返回 null 并把原因写进 LastError。</summary>
        public static string Save(string characterId, MetaHumanLook look, out string error)
        {
            error = null;
            if (look == null)
            {
                error = "快照为空";
                return null;
            }

            string path = FileFor(characterId);
            try
            {
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                List<MetaHumanLook> history = ReadAll(characterId);
                history.Add(look);

                // 超预算时先删最旧的未发布版；未发布版删光还超，才动已发布版。
                // 顺序反过来会让「发布记录」被静默裁掉，而它是发布后不可回溯的唯一凭据。
                while (history.Count > MaxLooksPerCharacter)
                {
                    int victim = -1;
                    for (int i = 0; i < history.Count; i++)
                    {
                        if (!history[i].Published)
                        {
                            victim = i;
                            break;
                        }
                    }

                    history.RemoveAt(victim >= 0 ? victim : 0);
                }

                WriteAll(path, history);
                return path;
            }
            catch (Exception exception)
            {
                error = "写入失败：" + exception.Message;
                return null;
            }
        }

        /// <summary>读最近一版（用于进场景时恢复外观）。</summary>
        /// <summary>
        /// 把服务端下发的外观 blob 落成一份本地存档。
        /// blob 先经 FromBytes 解析（自带校验和与结构校验）再走正常的 Save，
        /// 而不是直接写盘：解析不过就是数据不对，那种情况下留档等于把坏数据固化成"上次的样子"。
        /// </summary>
        public static string SaveRawBytes(string characterId, byte[] blob, out string error)
        {
            error = null;
            if (blob == null || blob.Length == 0)
            {
                error = "下发的外观是空数据";
                return null;
            }

            MetaHumanLook look;
            try
            {
                look = MetaHumanLook.FromBytes(blob);
            }
            catch (Exception ex)
            {
                error = "下发的外观解析失败：" + ex.Message;
                return null;
            }

            if (look == null)
            {
                error = "下发的外观解析得到 null";
                return null;
            }

            // 自证不过就当场拒收。这一条不是防御性洁癖：ReadAll 会按校验和丢弃损坏条目，
            // 所以"收下坏 blob"的实际后果是记录**静默少一条**——HasPublished 变假、
            // 重登把回溯入口点亮，而写入那一刻什么错都没报（本项目记过的同一形状）。
            // 落盘前拒掉，才能把原因交到调用方手里。
            if (!look.VerifyChecksum())
            {
                error = "下发的外观校验和不符（自述 " + look.Checksum
                        + "，重算 " + MetaHumanLook.ComputeChecksum(look.Values) + "），已拒收不落盘";
                return null;
            }

            return Save(characterId, look, out error);
        }

        /// <summary>
        /// 待认领外观附带的模板元信息。发布那一刻才有完整的模板与条目数，
        /// 而角色 Id 要等建角回执；两者分开存，回执到达时才拼得出一次合法提交。
        /// </summary>
        public class PendingLookMeta
        {
            public string TemplateId;
            public string TemplateVersion;
            public int TemplateParamCount;
            public int EntryCount;
        }

        private static PendingLookMeta _pendingMeta;

        public static void SetPendingLookMeta(PendingLookMeta meta)
        {
            _pendingMeta = meta;
        }

        public static PendingLookMeta TakePendingLookMeta()
        {
            PendingLookMeta meta = _pendingMeta;
            _pendingMeta = null;
            return meta;
        }

        public static MetaHumanLook LoadLatest(string characterId)
        {
            List<MetaHumanLook> history = ReadAll(characterId);
            return history.Count == 0 ? null : history[history.Count - 1];
        }

        /// <summary>
        /// 读全部版本。磁盘上的每一条都要自证校验和；损坏的那条被丢弃并计数——
        /// 「读到 3 条、其中 1 条坏了」必须能被调用方看到，而不是静默返回 3 条坏数据。
        /// </summary>
        public static List<MetaHumanLook> ReadAll(string characterId)
        {
            List<MetaHumanLook> result = new List<MetaHumanLook>();
            CorruptCount = 0;

            string path = FileFor(characterId);
            if (!File.Exists(path))
            {
                return result;
            }

            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                using (MemoryStream ms = new MemoryStream(bytes))
                using (BinaryReader reader = new BinaryReader(ms, Encoding.UTF8))
                {
                    if (ms.Length < 4)
                    {
                        CorruptCount++;
                        return result;
                    }

                    uint magic = reader.ReadUInt32();
                    if (magic != FileMagic)
                    {
                        CorruptCount++;
                        return result;
                    }

                    int count = reader.ReadInt32();
                    for (int i = 0; i < count; i++)
                    {
                        int length = reader.ReadInt32();
                        byte[] blob = reader.ReadBytes(length);
                        try
                        {
                            MetaHumanLook look = MetaHumanLook.FromBytes(blob);
                            if (look.VerifyChecksum())
                            {
                                result.Add(look);
                            }
                            else
                            {
                                CorruptCount++;
                            }
                        }
                        catch
                        {
                            CorruptCount++;
                        }
                    }
                }
            }
            catch
            {
                CorruptCount++;
            }

            return result;
        }

        /// <summary>上一次读取里被丢弃的损坏条目数。</summary>
        public static int CorruptCount { get; private set; }

        // 这里原先有一个 MarkPublished(characterId, index)：把历史里的第 N 版改标成"已发布"。
        // 全仓零调用者（grep 实测），而且它与真实发布路径语义冲突 ——
        // 发布是"当前状态成为封存版"（Capture 一份带 Published 的快照再落盘），
        // 不是"回头把某个旧版本宣布成发布版"。留着它只会多一条没人走、但能把历史改写成矛盾状态的后门。

        /// <summary>该角色是否已有发布版：有则界面应当关掉回溯入口。</summary>
        public static bool HasPublished(string characterId)
        {
            List<MetaHumanLook> history = ReadAll(characterId);
            for (int i = 0; i < history.Count; i++)
            {
                if (history[i].Published)
                {
                    return true;
                }
            }

            return false;
        }

        private const uint FileMagic = 0x4C484D48u; // 'HHLH'

        private static void WriteAll(string path, List<MetaHumanLook> history)
        {
            using (MemoryStream ms = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(ms, Encoding.UTF8))
            {
                writer.Write(FileMagic);
                writer.Write(history.Count);
                for (int i = 0; i < history.Count; i++)
                {
                    byte[] blob = history[i].ToBytes();
                    writer.Write(blob.Length);
                    writer.Write(blob);
                }

                writer.Flush();
                File.WriteAllBytes(path, ms.ToArray());
            }
        }
    }
}
