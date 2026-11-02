namespace HundunWorld.Game.Combat
{
    /// <summary>
    /// 纯函数层：玩家此刻<b>能不能</b>对"当前选中的东西"下打断／嘲讽这道指令，
    /// 以及不能的时候该念哪一句人话。
    /// <para>
    /// <b>为什么单独一个文件、而且不许引用引擎类型</b>：这条判据以前只长在 HUD 的按钮显隐里
    /// （或者根本没有——服务端两条受理口 <c>ReportInterruptById</c> / <c>ReportTauntById</c>
    /// 长期零调用方，玩家打不到怪也踢不断条子）。把它做成纯函数之后，
    /// 离线闸门可以把它原样编进测试工程逐点真跑；只比常量的闸门证明不了这条式子
    /// （与插件 <c>MonsterPreviewMath</c> 同一先例）。</para>
    /// <para>
    /// <b>只管客户端真知道的事</b>：距离够不够（服务端 <c>MonsterTauntRange</c> 那一档）
    /// 与"是不是尸体"都由服务端拒并各自计数，这里不复制那两条判据——
    /// 同一条规则两处各写一遍，迟早有一处漏改，而漏改的那一处会表现成"客户端说能按，按下去没反应"。</para>
    /// </summary>
    public static class MonsterCombatActionRules
    {
        /// <summary>打断用的键（与 <c>WorldSelectionService</c> 的轮询同一处定义，闸门按文本对账）。</summary>
        public const string InterruptKeyLabel = "X";

        /// <summary>嘲讽用的键。</summary>
        public const string TauntKeyLabel = "T";

        /// <summary>
        /// 嘲讽：只对"还活着、还可选中的怪"给按，且它<b>此刻不是正在回位</b>。
        /// <para>玩家（PVP 那一层还没接）与采集点/物品都不受理，按钮灰着比"按下去什么也没发生"诚实。</para>
        /// <para><c>unattackable</c> 与伤害那一手共用：服务端在回位整段路上既拒伤害也拒嘲讽
        /// （<c>MonsterAiGate.CanBeForcedIntoCombat</c>），客户端拿到的又是同一个状态位，
        /// 那就不能出现"技能栏灰了而嘲讽按钮还亮着"——按下去被拒第二次就成了玩家眼里的 bug。</para>
        /// </summary>
        public static bool CanTaunt(bool hasSelection, bool isMonster, bool selectable, bool unattackable)
            => hasSelection && isMonster && selectable && !unattackable;

        /// <summary>
        /// 打断：嘲讽的四个前提之外，还要"它正在读条"且"那一条标了可打断"。
        /// <para>后者是服务端起手事件带下来的旗标（<c>MonsterCastPayload.Interruptible</c>），
        /// 不是客户端猜的——所以"这条踢不动"是配置事实，不是玩家操作失误。</para>
        /// </summary>
        public static bool CanInterrupt(bool hasSelection, bool isMonster, bool selectable,
                                        bool casting, bool interruptible, bool unattackable)
            => CanTaunt(hasSelection, isMonster, selectable, unattackable) && casting && interruptible;

        /// <summary>不能嘲讽时念哪句（可用时返回空串）。顺序即优先级：先说"没选中"，再说"选错东西"。</summary>
        public static string TauntHint(bool hasSelection, bool isMonster, bool selectable, bool unattackable)
        {
            if (!hasSelection) return "没有选中目标";
            if (!isMonster) return "嘲讽只对怪物有效";
            if (!selectable) return "目标已经倒下了";
            if (unattackable) return "它正在跑回原位，拉不动";
            return string.Empty;
        }

        /// <summary>不能打断时念哪句（可用时返回空串）。</summary>
        public static string InterruptHint(bool hasSelection, bool isMonster, bool selectable,
                                           bool casting, bool interruptible, bool unattackable)
        {
            var baseHint = TauntHint(hasSelection, isMonster, selectable, unattackable);
            if (baseHint.Length > 0) return baseHint;
            if (!casting) return "它现在没在读条";
            if (!interruptible) return "这条读条不可打断";
            return string.Empty;
        }

        /// <summary>
        /// 伤害这一手（普攻／技能）能不能落在此刻选中的目标上。
        /// <para><c>unattackable</c> 是<b>服务端随状态位下发的既成事实</b>（那只怪正在回位——
        /// AzerothCore 的 <code>Creature::IsEvadingAttacks()</code> 挂在<b>受击方</b>那侧，
        /// 判据主人是 <c>Horizon.Game.Sim.Monsters.MonsterAiGate.BlocksIncomingDamage</c>），
        /// 不是客户端自己再算一遍的规则。权威拒伤始终在服务端：这里挡一下只为让玩家看见一句人话，
        /// 而不是"按了技能什么反应都没有、还以为卡了"。</para>
        /// </summary>
        public static bool CanDamage(bool hasSelection, bool selectable, bool unattackable)
            => hasSelection && selectable && !unattackable;

        /// <summary>不能出手时念哪句（可用时返回空串）。顺序即优先级。</summary>
        public static string DamageHint(bool hasSelection, bool selectable, bool unattackable)
        {
            if (!hasSelection) return "没有选中目标";
            if (!selectable) return "目标已经倒下了";
            if (unattackable) return "它正在跑回原位，这一下打不到";
            return string.Empty;
        }
    }
}
