using System;
using System.Text;
using FlaxEngine;
using GameDatabase = global::Game.Database.DatabaseManager;

namespace HundunWorld.Game.UI.Ink.ActionBar
{
    /// <summary>技能栏槽位可承载的内容类型：药品与可直接消耗的物品都归为 Item。</summary>
    public enum ActionBarSlotKind : byte
    {
        Empty = 0,
        Skill = 1,
        Item = 2,
    }

    /// <summary>
    /// 一个槽位绑定的内容。<see cref="RefId"/> 对技能是 SkillId，对物品是 TemplateId
    /// （模板号而非服务端实例号：堆叠被吃掉后实例号会变，模板号不会）。
    /// </summary>
    public struct ActionBarSlotContent
    {
        public static readonly ActionBarSlotContent EmptyContent = default;

        public ActionBarSlotKind Kind;
        public int RefId;

        public bool IsEmpty => Kind == ActionBarSlotKind.Empty;

        public bool SameAs(ActionBarSlotContent other) => Kind == other.Kind && RefId == other.RefId;

        public static ActionBarSlotContent OfSkill(int skillId) =>
            new ActionBarSlotContent { Kind = ActionBarSlotKind.Skill, RefId = skillId };

        public static ActionBarSlotContent OfItem(int templateId) =>
            new ActionBarSlotContent { Kind = ActionBarSlotKind.Item, RefId = templateId };
    }

    /// <summary>
    /// 技能栏布局：6 行 × 10 列共 60 个槽位的运行时模型，按角色落本地库。
    /// <para>
    /// 同一内容只允许出现在一个槽位上：往已占用的槽位放新内容会把旧内容挤到被拖走的槽位（交换），
    /// 这样"拖动换位"与"覆盖绑定"共用一条规则，不会出现两份同一技能的假象。
    /// </para>
    /// </summary>
    public sealed class ActionBarLayout
    {
        public const int Rows = 6;
        public const int Columns = 10;
        public const int SlotCount = Rows * Columns;

        private static ActionBarLayout _instance;

        /// <summary>全局唯一布局实例（技能栏、选择器、拖拽源共享同一份槽位数据）。</summary>
        public static ActionBarLayout Instance => _instance ??= new ActionBarLayout();

        private readonly ActionBarSlotContent[] _slots = new ActionBarSlotContent[SlotCount];

        private ulong _loadedCharacterId;
        private bool _savingEnabled;

        /// <summary>槽位绑定或可见行发生变化。</summary>
        public event Action Changed;

        /// <summary>当前滚轮停靠的行（0 基），仅显示这一行。</summary>
        public int VisibleRow { get; private set; }

        public ActionBarSlotContent Get(int index) =>
            index >= 0 && index < SlotCount ? _slots[index] : ActionBarSlotContent.EmptyContent;

        public int RowOf(int index) => index / Columns;

        public int ColumnOf(int index) => index % Columns;

        public int IndexOf(int row, int column) => row * Columns + column;

        /// <summary>找出某内容当前所在槽位，未绑定返回 -1。</summary>
        public int FindIndex(ActionBarSlotContent content)
        {
            if (content.IsEmpty) return -1;
            for (int i = 0; i < SlotCount; i++)
                if (_slots[i].SameAs(content)) return i;
            return -1;
        }

        public void SetVisibleRow(int row)
        {
            row = ((row % Rows) + Rows) % Rows;
            if (VisibleRow == row) return;
            VisibleRow = row;
            Persist();
        }

        /// <summary>清空槽位（幂等）。</summary>
        public void Clear(int index)
        {
            if (index < 0 || index >= SlotCount) return;
            if (_slots[index].IsEmpty) return;
            _slots[index] = ActionBarSlotContent.EmptyContent;
            NotifyChanged();
        }

        /// <summary>把内容放进槽位；内容原本在别的槽位时两槽交换。</summary>
        public void Assign(int index, ActionBarSlotContent content)
        {
            if (index < 0 || index >= SlotCount) return;
            if (content.IsEmpty)
            {
                Clear(index);
                return;
            }

            int existing = FindIndex(content);
            if (existing == index) return;

            var target = _slots[index];
            _slots[index] = content;
            if (existing >= 0)
                _slots[existing] = target;

            NotifyChanged();
        }

        /// <summary>两个槽位互换内容（拖到已占用槽位时的换位语义）。</summary>
        public void Swap(int a, int b)
        {
            if (a < 0 || a >= SlotCount || b < 0 || b >= SlotCount || a == b) return;
            (_slots[a], _slots[b]) = (_slots[b], _slots[a]);
            NotifyChanged();
        }

        private void NotifyChanged()
        {
            Persist();
            try
            {
                Changed?.Invoke();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ActionBarLayout] Changed 订阅者异常: {ex.Message}");
            }
        }

        // ===================================================================
        // 按角色持久化：DatabaseManager 的配置表只存标量字符串，
        // 所以 60 个槽位编码成 "kind:refId,kind:refId,..." 一行字符串。
        // ===================================================================

        private static string CategoryFor(ulong characterId) => $"ActionBar_{characterId}";

        /// <summary>
        /// 载入该角色的技能栏布局。切换角色时重新载入；同一角色重复调用无副作用。
        /// </summary>
        public void LoadForCharacter(ulong characterId)
        {
            if (characterId == 0UL) return;
            if (_loadedCharacterId == characterId) return;

            _loadedCharacterId = characterId;
            _savingEnabled = false;

            Array.Clear(_slots, 0, _slots.Length);
            VisibleRow = 0;

            try
            {
                string category = CategoryFor(characterId);
                Decode(GameDatabase.GetConfig<string>("slots", category, string.Empty));
                VisibleRow = GameDatabase.GetConfig("row", category, 0);
                if (VisibleRow < 0 || VisibleRow >= Rows) VisibleRow = 0;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ActionBarLayout] 载入角色 {characterId} 技能栏失败: {ex.Message}");
            }

            _savingEnabled = true;
            Changed?.Invoke();
        }

        private void Persist()
        {
            if (!_savingEnabled || _loadedCharacterId == 0UL) return;

            try
            {
                string category = CategoryFor(_loadedCharacterId);
                GameDatabase.SetConfig("slots", Encode(), category);
                GameDatabase.SetConfig("row", VisibleRow, category);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ActionBarLayout] 保存角色 {_loadedCharacterId} 技能栏失败: {ex.Message}");
            }
        }

        private string Encode()
        {
            var sb = new StringBuilder(SlotCount * 6);
            for (int i = 0; i < SlotCount; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append((int)_slots[i].Kind).Append(':').Append(_slots[i].RefId);
            }
            return sb.ToString();
        }

        /// <summary>解析失败（手工改坏存档、版本不符）时逐段跳过，其余槽位照常恢复。</summary>
        private void Decode(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return;

            var parts = raw.Split(',');
            int count = Math.Min(parts.Length, SlotCount);
            for (int i = 0; i < count; i++)
            {
                int colon = parts[i].IndexOf(':');
                if (colon <= 0) continue;
                if (!int.TryParse(parts[i].Substring(0, colon), out int kind)) continue;
                if (!int.TryParse(parts[i].Substring(colon + 1), out int refId)) continue;
                if (kind < (int)ActionBarSlotKind.Empty || kind > (int)ActionBarSlotKind.Item) continue;

                _slots[i] = new ActionBarSlotContent
                {
                    Kind = (ActionBarSlotKind)kind,
                    RefId = refId,
                };
            }
        }
    }
}
