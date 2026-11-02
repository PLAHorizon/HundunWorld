using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MonsterEditor
{
    /// <summary>
    /// 台词组的纯计算（面板侧镜像）。
    /// <para>
    /// 插件运行在 Flax 脚本域里，引用不到 <c>Horizon.MonsterCli</c> / <c>Horizon.Game.Sim</c>，
    /// 所以"下一个可用的文本号""这一组现在有哪几句"只能在这里自己算一遍。
    /// 真正的**判定**不在这一份里：组怎么挑（按 <c>(Seq, SayId)</c> 稳定序用 tick 盐）在服务端
    /// <c>MonsterContent.ResolveSayId</c>；这里只做作者侧的两个小算术，并由
    /// <c>MonsterEditorContractTests</c> 逐值对账（镜像算错的表现是"面板说加了第 3 句而文件里只有 2 句"）。
    /// </para>
    /// </summary>
    public static class MonsterTextGroupMath
    {
        /// <summary>文本源里的一行（只带面板做算术要用到的字段）。</summary>
        public readonly struct Row
        {
            public readonly int SayId;
            public readonly int Group;
            public readonly int Seq;
            public readonly string Text;

            public Row(int sayId, int group, int seq, string text)
            {
                SayId = sayId;
                Group = group;
                Seq = seq;
                Text = text ?? string.Empty;
            }
        }

        /// <summary>
        /// 这只怪下一个没被占用的文本号 = 现有最大号 + 1（一只怪都没有时从 1 起）。
        /// <para>为什么不用"行数 + 1"：号会被删（删除后号会空出来），拿行数加一必然撞号，
        /// 而撞号的表现是 <c>set-text</c> 把已有的那一句**覆盖**掉——策划以为加了一句，其实改了一句。</para>
        /// </summary>
        public static int NextSayId(IReadOnlyList<Row> rows)
        {
            var max = 0;
            if (rows != null)
            {
                for (var i = 0; i < rows.Count; i++)
                {
                    if (rows[i].SayId > max) max = rows[i].SayId;
                }
            }
            return max + 1;
        }

        /// <summary>
        /// 死亡台词所在的组号（镜像服务端 <c>MonsterContent.DeathSayGroup</c>）。
        /// <para>面板引用不到 <c>Horizon.Game.Sim</c>，所以这是**第二份字面量**；
        /// 两份相等由 <c>MonsterEditorContractTests</c> 直接对账（都拿得到类型就能断言，不靠注释）。
        /// 死亡台词没有指针，靠"落在这一组"被运行期挑中。</para>
        /// </summary>
        public const int DeathGroup = 2;

        /// <summary>
        /// 应援台词所在的组号（镜像服务端 <c>MonsterContent.AssistSayGroup</c>）：
        /// 被同族求援叫起来的那一拍念一句。与 <see cref="DeathGroup"/> 同理，两份相等由契约测试对账。
        /// </summary>
        public const int AssistGroup = 3;

        /// <summary>
        /// 某一组里"会被挑中"的那一号（组内按 <c>(Seq, SayId)</c> 稳定序的第一条）；没有那条组回 0。
        /// <para>面板用它决定"临终台词那一格现在编辑的是哪一号"：编辑已有行而不是每次新起一号，
        /// 否则同一只怪点两次保存就会长出第二条死亡台词（而运行期按组随机会挑到旧的）。</para>
        /// </summary>
        public static int SayIdOfGroup(IReadOnlyList<Row> rows, int group)
        {
            if (rows == null || group <= 0) return 0;
            var best = 0;
            var bestSeq = int.MaxValue;
            for (var i = 0; i < rows.Count; i++)
            {
                if (rows[i].Group != group || rows[i].SayId <= 0) continue;
                if (rows[i].Seq < bestSeq || (rows[i].Seq == bestSeq && rows[i].SayId < best))
                {
                    bestSeq = rows[i].Seq;
                    best = rows[i].SayId;
                }
            }
            return best;
        }

        /// <summary>这一号所在的组（0 = 不成组；号不存在也回 0）。</summary>
        public static int GroupOf(IReadOnlyList<Row> rows, int sayId)
        {
            if (rows == null || sayId <= 0) return 0;
            for (var i = 0; i < rows.Count; i++)
            {
                if (rows[i].SayId == sayId) return rows[i].Group;
            }
            return 0;
        }

        /// <summary>
        /// 组的现状（给面板上那行提示用）：按 <c>(Seq, SayId)</c> 排好后逐句点名。
        /// 组 0 或组里没有行时返回"未成组"一类的话——**不能返回空串**，
        /// 空串在界面上与"这个控件没接上"同形。
        /// </summary>
        public static string DescribeGroup(IReadOnlyList<Row> rows, int group)
        {
            if (rows == null || group <= 0) return "台词组：未成组（组号 0 只有这一句）";
            var members = new List<Row>();
            for (var i = 0; i < rows.Count; i++)
            {
                if (rows[i].Group == group) members.Add(rows[i]);
            }
            if (members.Count == 0) return "台词组 " + Num(group) + "：还没有句子";
            members.Sort(static (a, b) =>
            {
                var bySeq = a.Seq.CompareTo(b.Seq);
                return bySeq != 0 ? bySeq : a.SayId.CompareTo(b.SayId);
            });
            var sb = new StringBuilder();
            sb.Append("台词组 ").Append(Num(group)).Append("：");
            for (var i = 0; i < members.Count; i++)
            {
                if (i > 0) sb.Append(" / ");
                sb.Append('第').Append(Num(i + 1)).Append("句 #").Append(Num(members[i].SayId));
            }
            sb.Append("（共 ").Append(Num(members.Count)).Append(" 句，每轮进战挑一句）");
            return sb.ToString();
        }

        private static string Num(int value) => value.ToString(CultureInfo.InvariantCulture);
    }
}
