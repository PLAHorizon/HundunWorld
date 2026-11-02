using FlaxEngine;
using Horizon.Game.Message.Enums;
using Horizon.Game.Message.Network;
using HundunWorld.Game.Combat.Skills;
using HundunWorld.Game.Network.Handlers;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;

namespace HundunWorld.Game.Network.MessageHandlers
{
    /// <summary>
    /// 服务端属性回执消费方。目前只认领一件事：<b>五行循环节的权威值</b>。
    /// <para>
    /// 施放回执在成功路径上是 <see cref="AttributeUpdateMessage"/>（服务端从不下发成功的
    /// <c>SkillCastMessage</c>，客户端那套"成功才播特效"的分支因此一直没被喂到）。
    /// 循环节要脱离本地预测、按服务端计数显示，就得从这里取。
    /// </para>
    /// <para>
    /// 取不到值时一律<b>保持本地预测不动</b>：属性字典是 <c>Dictionary&lt;string, object&gt;</c>，
    /// 跨线后的装箱形态不稳定，宁可不校正也不要指示器被清零闪一下。
    /// </para>
    /// </summary>
    public class AttributeUpdateResponseHandler : BaseMessageHandler
    {
        public override List<MessageType> MessageTypes =>
            new List<MessageType> { MessageType.AttributeUpdate };

        public override ServiceType ServiceType => ServiceType.Game;

        public AttributeUpdateResponseHandler() : base(MessageType.AttributeUpdate)
        {
        }

        public override Task HandleAsync(HorizonMessagePacket message)
        {
            if (!(message.Body is AttributeUpdateMessage update)) return Task.CompletedTask;

            var changes = update.AttributeChanges;
            if (changes == null || changes.Count == 0) return Task.CompletedTask;

            int? stack = TryGetInt(changes, "LoopStack");
            int? completion = TryGetInt(changes, "LoopCompletionCount");
            bool? completed = TryGetBool(changes, "CycleCompleted");

            // 一个都没有 = 这条回执与循环节无关（普攻/其它属性变更），照常不动。
            if (stack == null && completion == null && completed != true) return Task.CompletedTask;

            int stackValue = stack ?? -1;
            int completionValue = completion ?? -1;
            bool cycleCompleted = completed ?? false;

            Scripting.InvokeOnUpdate(() =>
                WuxingCycleTracker.Instance.SyncFromAuthority(stackValue, completionValue, cycleCompleted));

            return Task.CompletedTask;
        }

        private static int? TryGetInt(Dictionary<string, object> changes, string key)
        {
            if (!changes.TryGetValue(key, out var value) || value == null) return null;
            try
            {
                if (value is int i) return i;
                if (value is long l) return (int)l;
                if (value is float f) return (int)f;
                if (value is double d) return (int)d;
                if (value is bool b) return b ? 1 : 0;
                return int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out var parsed) ? parsed : (int?)null;
            }
            catch
            {
                return null;
            }
        }

        private static bool? TryGetBool(Dictionary<string, object> changes, string key)
        {
            if (!changes.TryGetValue(key, out var value) || value == null) return null;
            try
            {
                if (value is bool b) return b;
                if (value is int i) return i != 0;
                return bool.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), out var parsed)
                    ? parsed : (bool?)null;
            }
            catch
            {
                return null;
            }
        }
    }
}
