using System;
using System.Collections.Generic;
using System.IO;
using FlaxEngine;

namespace Game.Selection;

/// <summary>
/// 客户端文本表（<c>(modelKey, sayId) → 文案/起手动作</c>）的读侧。
/// <para>
/// <b>为什么线上只发号</b>：文案是展示物。把文案塞进实时包 = 把展示文本当身份用——
/// 翻译一次就作废全部旧引用（这条工程里栽过：分组 id 曾是分类名的哈希）。
/// 所以 DS 进战时只发 <c>SyncEventKind.MonsterSay</c> 带一个 <c>sayId</c>，
/// "这一号说哪句话"由随客户端包发布的 <c>Data/Monsters/client_texts.json</c> 决定，
/// 与外观表 <see cref="MonsterVisualCatalog"/> 同一口径。
/// </para>
/// <para>
/// <b>缓存按文件指纹失效，不按"读过一次"</b>：编辑器的脚本域跨 PIE 会话存活，
/// 第一次进世界时表还没导出就会被记成一辈子（那是外观表踩过的同一个坑，见
/// <c>MonsterVisualCatalog</c> 的注释）。这里读表前先比 <c>LastWriteTimeUtc + Length</c>，
/// 缺失文件回哨兵 <c>(MinValue, -1)</c> 与字段初值同形 ⇒ "表后来才被导出来"天然算一次变化。
/// </para>
/// </summary>
public static class MonsterTextCatalog
{
    private const string RelativePath = "Data/Monsters/client_texts.json";

    private struct Entry
    {
        public string Text;
        public int Emote;
    }

    private static readonly Dictionary<string, Entry> Entries = new Dictionary<string, Entry>();

    private static bool _loaded;
    private static string _source = string.Empty;
    private static DateTime _fileStampUtc = DateTime.MinValue;
    private static long _fileStampLength = -1L;

    /// <summary>已读入的条目数（回执与判据都问这个数，不问"读没读成功"那种布尔）。</summary>
    public static int Count => Entries.Count;

    /// <summary>最近一次读到的文件路径（诊断用）。</summary>
    public static string Source => _source;

    /// <summary>强制重读（面板"重新导出后刷新"用；正常路径靠指纹自动失效）。</summary>
    public static void Reload() => _loaded = false;

    private static void EnsureLoaded()
    {
        var path = ResolvePath();
        var stamp = ReadStamp(path);
        // 取指纹失败（文件不在/被锁）时**保留上次指纹**：宁可晚一句台词，也不在战斗流程里抛。
        if (stamp.HasValue)
        {
            if (_loaded && stamp.Value.Item1 == _fileStampUtc && stamp.Value.Item2 == _fileStampLength) return;
            _fileStampUtc = stamp.Value.Item1;
            _fileStampLength = stamp.Value.Item2;
        }
        else if (_loaded)
        {
            return;
        }

        Load(path);
        _loaded = true;
    }

    private static void Load(string path)
    {
        var previousCount = Entries.Count;
        Entries.Clear();
        _source = path ?? string.Empty;
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            // 这一句必须带"0 条"：静默返回会让"表没导出"与"表里本来就没配台词"读成同一件事。
            Debug.Log($"[MonsterTextCatalog] 文本表不存在，进战喊话无处解析：{_source}（条目 0，此前 {previousCount} 条）");
            return;
        }

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("texts", out var texts)
                || texts.ValueKind != System.Text.Json.JsonValueKind.Array)
            {
                Debug.LogWarning($"[MonsterTextCatalog] 文本表里没有 texts 数组：{_source}");
                return;
            }
            foreach (var item in texts.EnumerateArray())
            {
                var key = ReadString(item, "modelKey");
                var sayId = ReadInt(item, "sayId", 0);
                var text = ReadString(item, "text");
                if (string.IsNullOrEmpty(key) || sayId <= 0 || string.IsNullOrEmpty(text)) continue;
                Entries[Key(key, sayId)] = new Entry { Text = text, Emote = ReadInt(item, "emote", 0) };
            }
            Debug.Log($"[MonsterTextCatalog] 文本表已读入：{_source}（条目 {Entries.Count} 条，此前 {previousCount} 条）");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[MonsterTextCatalog] 文本表解析失败，进战喊话将静默无文案：{_source} {ex.Message}");
        }
    }

    /// <summary>组合键只有一个主人：写侧（本类的查）与判据都调它，别在别处再拼一次。</summary>
    public static string Key(string modelKey, int sayId) => modelKey + "|" + sayId;

    /// <summary>查一句文案。查不到返回 false（调用方安静不显示，不编一句）。</summary>
    public static bool TryResolve(string? modelKey, int sayId, out string text, out int emote)
    {
        text = string.Empty;
        emote = 0;
        if (string.IsNullOrEmpty(modelKey) || sayId <= 0) return false;
        EnsureLoaded();
        if (!Entries.TryGetValue(Key(modelKey!, sayId), out var hit)) return false;
        text = hit.Text;
        emote = hit.Emote;
        return true;
    }

    private static string? ResolvePath()
    {
        var root = Directory.GetCurrentDirectory();
        var candidates = new[]
        {
            Path.Combine(root, "Content", "Data", "Monsters", "client_texts.json"),
            Path.Combine(root, "Data", "Monsters", "client_texts.json"),
            Path.Combine(root, "HundunWorld", "Data", "Monsters", "client_texts.json"),
        };
        foreach (var c in candidates)
        {
            if (File.Exists(c)) return c;
        }
        return candidates[candidates.Length - 1];
    }

    private static (DateTime, long)? ReadStamp(string? path)
    {
        try
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return (DateTime.MinValue, -1L);
            var fi = new FileInfo(path);
            return (fi.LastWriteTimeUtc, fi.Length);
        }
        catch
        {
            return null;
        }
    }

    private static string ReadString(System.Text.Json.JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var child)
            || child.ValueKind != System.Text.Json.JsonValueKind.String) return string.Empty;
        return child.GetString() ?? string.Empty;
    }

    private static int ReadInt(System.Text.Json.JsonElement e, string name, int fallback)
    {
        if (!e.TryGetProperty(name, out var child)
            || child.ValueKind != System.Text.Json.JsonValueKind.Number) return fallback;
        return child.GetInt32();
    }
}
