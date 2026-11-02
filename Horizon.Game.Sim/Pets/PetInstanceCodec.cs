using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Horizon.Game.Sim.Pets;

/// <summary>
/// 个体表三个 JSON 列（<c>skill_slots_json</c> / <c>equipment_json</c> / <c>timed_mutation_json</c>）
/// 的<b>唯一</b>编解码出口。
/// <para>
/// 为什么要有这一个类而不是让 Grain、CLI、插件各写一遍：这三列是"结构藏在字符串里"的列，
/// EF 与 SQL 都看不见它的内部。任何一处读法不同，表现不是报错而是**静默丢数据**——
/// 技能槽少一格、穿戴清单空了、临时变异到期不回退，玩家看得见但没人能归因。
/// </para>
/// <para>
/// 解码失败一律<b>大声返回错误</b>并交出"部分结果 + 出错下标"，不许静默返回空数组：
/// "空槽表"和"读坏了"在下游看起来一模一样（记忆 <c>silent-zero-scanners-need-a-count-assertion</c>）。
/// </para>
/// <para>编码是确定性的：属性写入顺序固定，因此"同一状态 ⇒ 同一串字节"可被测试钉住。</para>
/// </summary>
public static class PetInstanceCodec
{
    /// <summary>编码格式版本（结构变更时 +1，读侧据此判断旧存档）。</summary>
    public const int Version = 1;

    /// <summary>技能槽数组的键名（写死一份，两侧同源）。</summary>
    public const string KeySkillId = "skillId";
    public const string KeyKind = "kind";
    public const string KeyLevel = "level";
    public const string KeyLocked = "locked";
    public const string KeyItemInstanceId = "itemInstanceId";
    public const string KeySlot = "slot";
    public const string KeyMutationKey = "key";
    public const string KeyStartTick = "startTick";
    public const string KeyExpireTick = "expireTick";
    public const string KeyQualityCapBps = "qualityCapBps";
    public const string KeyColorOverride = "colorOverride";
    public const string KeyExtraSkillSlots = "extraSkillSlots";

    // ==================== 技能槽 ====================

    /// <summary>技能槽 → JSON 数组文本（<c>[]</c> 表示"没有技能"，与"读坏了"不同：后者必返 error）。</summary>
    public static string EncodeSkillSlots(PetSkillSlot[]? slots)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartArray();
            if (slots != null)
            {
                for (var i = 0; i < slots.Length; i++)
                {
                    writer.WriteStartObject();
                    writer.WriteNumber(KeySkillId, slots[i].SkillId);
                    writer.WriteNumber(KeyKind, (byte)slots[i].Kind);
                    writer.WriteNumber(KeyLevel, slots[i].Level);
                    writer.WriteBoolean(KeyLocked, slots[i].Locked);
                    writer.WriteEndObject();
                }
            }
            writer.WriteEndArray();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>
    /// JSON 文本 → 技能槽数组。
    /// </summary>
    /// <param name="error">非 null 时说明<b>整串作废</b>，调用方必须把它落到日志/回执而不是继续用 <paramref name="slots"/>。</param>
    /// <returns>解析且校验通过返回 true。</returns>
    public static bool TryDecodeSkillSlots(string? json, out PetSkillSlot[] slots, out string? error)
    {
        slots = Array.Empty<PetSkillSlot>();
        error = null;
        if (string.IsNullOrWhiteSpace(json)) return true;

        JsonDocument? doc = null;
        try
        {
            doc = JsonDocument.Parse(json);
            return DecodeSlots(doc.RootElement, out slots, out error);
        }
        catch (JsonException ex)
        {
            error = "技能槽 JSON 解析失败：" + ex.Message;
            return false;
        }
        finally
        {
            doc?.Dispose();
        }
    }

    private static bool DecodeSlots(JsonElement root, out PetSkillSlot[] slots, out string? error)
    {
        slots = Array.Empty<PetSkillSlot>();
        error = null;
        if (root.ValueKind != JsonValueKind.Array)
        {
            error = "技能槽 JSON 的根必须是数组，实际是 " + root.ValueKind;
            return false;
        }

        var list = new List<PetSkillSlot>(8);
        var index = 0;
        foreach (var item in root.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                error = $"技能槽第 {index} 项不是对象（{item.ValueKind}）";
                return false;
            }

            var skillId = ReadInt32(item, KeySkillId, 0);
            var kindRaw = ReadInt32(item, KeyKind, 0);
            var level = ReadInt32(item, KeyLevel, 1);
            var locked = ReadBoolean(item, KeyLocked, false);

            if (kindRaw < (byte)PetSkillSlotKind.Empty || kindRaw > (byte)PetSkillSlotKind.Locked)
            {
                error = $"技能槽第 {index} 项的 kind={kindRaw} 越界（合法 0–{(byte)PetSkillSlotKind.Locked}）";
                return false;
            }

            var kind = (PetSkillSlotKind)(byte)kindRaw;

            // "0 是空槽"与 Kind.Empty 必须同时成立：只改一个的话，
            // 下游按 skillId 过滤会把"空槽"当成技能、按 kind 过滤会把技能当成空槽。
            if (skillId == 0 && kind != PetSkillSlotKind.Empty)
            {
                error = $"技能槽第 {index} 项 skillId=0 但 kind={kind}（空槽必须成对）";
                return false;
            }
            if (skillId > 0 && kind == PetSkillSlotKind.Empty)
            {
                error = $"技能槽第 {index} 项有技能 {skillId} 却标成 Empty";
                return false;
            }
            if (skillId < 0)
            {
                error = $"技能槽第 {index} 项 skillId={skillId} 为负";
                return false;
            }
            if (level < 1 || level > PetSkillCore.MaxSkillLevel)
            {
                error = $"技能槽第 {index} 项 level={level} 越界（合法 1–{PetSkillCore.MaxSkillLevel}）";
                return false;
            }

            list.Add(new PetSkillSlot { SkillId = skillId, Kind = kind, Level = level, Locked = locked });
            index++;
        }

        slots = list.ToArray();
        return true;
    }

    // ==================== 穿戴清单 ====================

    /// <summary>穿戴清单 → JSON 数组文本（只存引用，不复制装备本体）。</summary>
    public static string EncodeWornGear(PetWornGear[]? worn)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartArray();
            if (worn != null)
            {
                for (var i = 0; i < worn.Length; i++)
                {
                    writer.WriteStartObject();
                    writer.WriteNumber(KeyItemInstanceId, worn[i].ItemInstanceId);
                    writer.WriteNumber(KeySlot, (byte)worn[i].Slot);
                    writer.WriteEndObject();
                }
            }
            writer.WriteEndArray();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>JSON 文本 → 穿戴清单；部位重复视为坏数据（一件装备占两个位会被 <c>Summarize</c> 双算）。</summary>
    public static bool TryDecodeWornGear(string? json, out PetWornGear[] worn, out string? error)
    {
        worn = Array.Empty<PetWornGear>();
        error = null;
        if (string.IsNullOrWhiteSpace(json)) return true;

        JsonDocument? doc = null;
        try
        {
            doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                error = "穿戴清单 JSON 的根必须是数组，实际是 " + doc.RootElement.ValueKind;
                return false;
            }

            var list = new List<PetWornGear>(8);
            var seen = new HashSet<byte>();
            var index = 0;
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    error = $"穿戴清单第 {index} 项不是对象（{item.ValueKind}）";
                    return false;
                }

                var itemId = ReadInt64(item, KeyItemInstanceId, 0);
                var slotRaw = ReadInt32(item, KeySlot, -1);
                if (itemId <= 0)
                {
                    error = $"穿戴清单第 {index} 项 itemInstanceId={itemId} 无效（必须 > 0）";
                    return false;
                }
                if (slotRaw < (byte)PetEquipSlot.Wrist || slotRaw > (byte)PetEquipSlot.Bell)
                {
                    error = $"穿戴清单第 {index} 项 slot={slotRaw} 越界（合法 0–{(byte)PetEquipSlot.Bell}）";
                    return false;
                }
                if (!seen.Add((byte)slotRaw))
                {
                    error = $"穿戴清单第 {index} 项重复占用部位 {slotRaw}";
                    return false;
                }

                list.Add(new PetWornGear { ItemInstanceId = itemId, Slot = (PetEquipSlot)(byte)slotRaw });
                index++;
            }

            worn = list.ToArray();
            return true;
        }
        catch (JsonException ex)
        {
            error = "穿戴清单 JSON 解析失败：" + ex.Message;
            return false;
        }
        finally
        {
            doc?.Dispose();
        }
    }

    // ==================== 临时变异 ====================

    /// <summary>临时变异 → JSON 文本；<c>ExpireTick &lt;= 0</c> 编码成 null（哨兵语义见 <see cref="TimedPetMutation"/>）。</summary>
    public static string? EncodeTimedMutation(in TimedPetMutation mutation)
    {
        if (mutation.ExpireTick <= 0) return null;

        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString(KeyMutationKey, mutation.Key ?? string.Empty);
            writer.WriteNumber(KeyStartTick, mutation.StartTick);
            writer.WriteNumber(KeyExpireTick, mutation.ExpireTick);
            writer.WriteNumber(KeyQualityCapBps, mutation.QualityCapBps);
            writer.WriteString(KeyColorOverride, mutation.ColorOverride ?? string.Empty);
            writer.WriteNumber(KeyExtraSkillSlots, mutation.ExtraSkillSlots);
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>
    /// JSON 文本 → 临时变异。
    /// <para><paramref name="present"/> 为 false 的三种来源要分清：列本身为空（正常）、
    /// <c>expireTick &lt;= 0</c>（"从未生效"哨兵）、解析失败（<paramref name="error"/> 非 null）。
    /// 只有第三种是异常。</para>
    /// </summary>
    public static bool TryDecodeTimedMutation(string? json, out TimedPetMutation mutation,
        out bool present, out string? error)
    {
        mutation = default;
        present = false;
        error = null;
        if (string.IsNullOrWhiteSpace(json)) return true;

        JsonDocument? doc = null;
        try
        {
            doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                error = "临时变异 JSON 的根必须是对象，实际是 " + doc.RootElement.ValueKind;
                return false;
            }

            var root = doc.RootElement;
            var expireTick = ReadInt64(root, KeyExpireTick, 0);
            if (expireTick <= 0)
            {
                // 哨兵：0 或负数表示"这一层从未生效"，不是"第 0 tick 到期"。
                return true;
            }

            var key = ReadString(root, KeyMutationKey);
            if (string.IsNullOrEmpty(key))
            {
                error = "临时变异有 expireTick 却没有 key，无法回指物种表里的那行定义";
                return false;
            }

            mutation = new TimedPetMutation(
                key,
                ReadInt64(root, KeyStartTick, 0),
                expireTick,
                ReadInt32(root, KeyQualityCapBps, 1),
                ReadString(root, KeyColorOverride) ?? string.Empty,
                ReadInt32(root, KeyExtraSkillSlots, 0),
                PetMutationLayer.Temporary);
            present = true;
            return true;
        }
        catch (JsonException ex)
        {
            error = "临时变异 JSON 解析失败：" + ex.Message;
            return false;
        }
        finally
        {
            doc?.Dispose();
        }
    }

    // ==================== 读数助手 ====================

    private static int ReadInt32(JsonElement item, string name, int fallback)
        => item.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n)
            ? n : fallback;

    private static long ReadInt64(JsonElement item, string name, long fallback)
        => item.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n)
            ? n : fallback;

    private static bool ReadBoolean(JsonElement item, string name, bool fallback)
    {
        if (!item.TryGetProperty(name, out var v)) return fallback;
        return v.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => fallback,
        };
    }

    private static string? ReadString(JsonElement item, string name)
        => item.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;
}
