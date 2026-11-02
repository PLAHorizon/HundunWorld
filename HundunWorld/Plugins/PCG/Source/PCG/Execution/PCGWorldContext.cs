// PCG 插件 - 节点可用的世界查询
// FlaxEngine PCG 插件的一部分。见 docs/PCG_PLUGIN_DESIGN.md §3.5。

using System;
using System.Collections.Generic;
using FlaxEngine;

namespace PCG.Execution
{
    /// <summary>
    /// 一次图运行需要的场景侧查找服务：采样哪片地形，以及如何抵达
    /// 节点引用的 actor（样条、生成的父节点）。
    /// </summary>
    /// <remarks>
    /// 与 <see cref="PCGExecutionContext"/> 分开保存，好让无场景的测试
    /// 能塞进一个替身而不用载入场景，也让节点从不直接去抓全局状态。
    /// </remarks>
    public sealed class PCGWorldContext
    {
        private readonly List<Terrain> _terrains = new List<Terrain>();

        /// <summary>可采样的地形 actor，按场景顺序。</summary>
        public IReadOnlyList<Terrain> Terrains => _terrains;

        /// <summary>
        /// 按 id 解析 actor 引用。由调用方填好；节点用它做样条与父节点的
        /// 查找，而不是自己去扫关卡。
        /// </summary>
        public Func<Guid, Actor> ActorResolver { get; set; }

        /// <summary>设置时，覆盖 <see cref="ActorResolver"/> 按 actor 名字做的查找。</summary>
        public Func<string, Actor> ActorNameResolver { get; set; }

        /// <summary>
        /// 对场景的物理发一条射线；这次运行没有物理可撞时返回 null。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 用注入而不是直接调用，理由与 <see cref="ActorResolver"/> 相同：引擎的
        /// <c>Physics.RayCast</c> 需要已载入的场景，所以自己去抓它的节点没法离屏测——
        /// 而命中周围的那套算术（点落哪儿、怎么倾斜、什么都没撞到时怎么办）
        /// 恰是最值得用测试钉死的那部分。真实运行装上引擎的调用；测试装上一份
        /// 自己造的平面。
        /// </para>
        /// <para>
        /// 参数是射线起点、射线方向（期望调用方已归一化）、最大距离、层级掩码，以及
        /// 触发器碰撞体算不算。返回 null 意味着"什么都没撞到"。
        /// </para>
        /// </remarks>
        public Func<Vector3, Vector3, float, uint, bool, RayCastHit?> RayCaster { get; set; }

        /// <summary>这次运行有射线投射器可用时为真。</summary>
        public bool HasRayCaster => RayCaster != null;

        /// <summary>
        /// 把场景里的 actor 写进调用方拥有的列表，并返回它写入了多少个；这次运行
        /// 没有关卡可枚举时返回 null。
        /// </summary>
        /// <remarks>
        /// 注入的理由与射线投射器相同：枚举关卡需要引擎的场景，而节点
        /// 对结果施加的匹配规则才是值得测的那部分。真实运行装上关卡的一份快照；
        /// 测试装上它自己编的一份列表。每次调用重新填满而不是只交一次，因为
        /// 来源才是那个决定该怎样缓存的人——一次分区的运行会每格问一次。
        /// </remarks>
        public Func<List<PCGActorInfo>, int> ActorSource { get; set; }

        /// <summary>这次运行能枚举场景 actor 时为真。</summary>
        public bool HasActorSource => ActorSource != null;

        /// <summary>
        /// 某个世界 XZ 位置上的地形高度；一无所知的地方返回 null——那是一条路径规划于其上的场。
        /// </summary>
        /// <remarks>
        /// 注入的理由与射线投射器相同：规划一条路需要地形，而
        /// 决定路往哪儿走的那些规则才是值得测的部分。一次运行装上引擎的高度查询；测试
        /// 装上一个平面或一座能手算出来的山丘。
        /// </remarks>
        public Func<float, float, float?> HeightSource { get; set; }

        /// <summary>这次运行能回答高度查询时为真。</summary>
        public bool HasHeightSource => HeightSource != null;

        /// <summary>
        /// 通过 <see cref="RayCaster"/> 发一条射线。
        /// </summary>
        /// <returns>没装投射器、或什么都没撞到时为假。</returns>
        public bool TryRayCast(
            Vector3 origin, Vector3 direction, float maxDistance, uint layerMask, bool hitTriggers,
            out RayCastHit hit)
        {
            hit = default;

            var caster = RayCaster;

            if (caster == null)
                return false;

            var result = caster(origin, direction, maxDistance, layerMask, hitTriggers);

            if (result == null)
                return false;

            hit = result.Value;
            return true;
        }

        /// <summary>有地形可供采样时为真。</summary>
        public bool HasTerrain => _terrains.Count > 0;

        /// <summary>登记一个供采样的地形。重复的忽略。</summary>
        public void AddTerrain(Terrain terrain)
        {
            if (terrain != null && !_terrains.Contains(terrain))
                _terrains.Add(terrain);
        }

        /// <summary>移除所有已登记的地形。</summary>
        public void ClearTerrains()
        {
            _terrains.Clear();
        }

        /// <summary>
        /// 返回世界边界在 XZ 平面上包住给定点的那个地形，没有则返回 null。
        /// </summary>
        public Terrain FindTerrainAt(Vector3 worldPosition)
        {
            for (int i = 0; i < _terrains.Count; i++)
            {
                var terrain = _terrains[i];
                if (terrain == null || !terrain.IsActiveInHierarchy)
                    continue;

                var box = terrain.Box;
                if (worldPosition.X >= box.Minimum.X && worldPosition.X <= box.Maximum.X &&
                    worldPosition.Z >= box.Minimum.Z && worldPosition.Z <= box.Maximum.Z)
                {
                    return terrain;
                }
            }

            // 退回唯一登记的那个地形；单地形场景是常见情形，而
            // 对一个 patch 只建了一部分的地形来说，边界可能是偏保守的。
            return _terrains.Count == 1 ? _terrains[0] : null;
        }

        /// <summary>从已载入的场景里填好地形列表。</summary>
        /// <remarks>
        /// 必须在主线程调用；actor 的枚举不是线程安全的。
        /// </remarks>
        public void CollectTerrainsFromScene()
        {
            _terrains.Clear();

            var terrains = Level.GetActors<Terrain>();
            if (terrains != null)
            {
                for (int i = 0; i < terrains.Length; i++)
                    AddTerrain(terrains[i]);
            }
        }

        /// <summary>用配置好的解析器按 id 解析一个 actor。</summary>
        public Actor ResolveActor(Guid id)
        {
            if (id == Guid.Empty)
                return null;

            return ActorResolver?.Invoke(id);
        }

        /// <summary>用配置好的解析器按名字解析一个 actor。</summary>
        public Actor ResolveActor(string name)
        {
            if (string.IsNullOrEmpty(name))
                return null;

            return ActorNameResolver?.Invoke(name);
        }
    }
}
