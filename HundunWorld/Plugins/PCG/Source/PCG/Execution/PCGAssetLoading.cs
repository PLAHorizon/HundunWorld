// PCG 插件 - 生成器节点的资产解析
// 属于 FlaxEngine PCG 插件。见 docs/PCG_PLUGIN_DESIGN.md §5.1.9。

using System;
using FlaxEngine;

namespace PCG.Execution
{
    /// <summary>
    /// 按 Guid 把图里写下的资产取出来。
    /// </summary>
    /// <remarks>
    /// 这些 Guid 来自设计师在描述里写的模型名、或编辑器资产选择器，而"取不到"以前只有一种含义：
    /// 这个资产不存在、或者类型不对。**实测还有第二种**：引擎的 <c>Content.GetAsset</c> 只翻
    /// **已经载入**的资产池（其 C++ 声明的返回值写的就是 "The found asset or null if not loaded"），
    /// 于是一个完好、存在、只是还没人碰过的模型，在本会话第一次生成时会被读成"没加载"。
    /// 同一个 Cone 的 Guid 在同一次会话里先是被植被节点报 "is not loaded or is not a Model"，
    /// 几十分钟后被另一个图的静态模型节点正常取用——差别只是那之前有没有别人加载过它。
    /// 所以这里保留快路径，取不到时按 Guid 请求一次真加载。
    /// 不用泛型 <c>Content.Load&lt;T&gt;(id)</c>：它的实现是 <c>(T)LoadAsync(...)</c>，
    /// 类型不符会抛 InvalidCastException，而这里要的是"交回 null、由节点报自己那句配置错误"。
    /// </remarks>
    public static class PCGAssetLoading
    {
        /// <summary>等待加载的上限（毫秒）。生成必须在一次图运行里做完，但不能把一个坏资产变成无限期卡顿。</summary>
        public const int LoadTimeoutMilliseconds = 5000;

        /// <summary>取这个 Guid 对应的资产；不存在、类型不符、或超时给不出，都是 null。</summary>
        public static T Resolve<T>(Guid id) where T : Asset
        {
            if (id == Guid.Empty)
                return null;

            var loaded = Content.GetAsset(id) as T;
            if (loaded != null)
                return loaded;

            // 非泛型重载：拿到的是原生实例，类型判断留给下面的 as。
            var requested = Content.LoadAsync(id);

            if (requested == null)
                return null;

            // `WaitForLoaded` 返回的是"能不能加载"的反面：true = 失败或被取消。
            if (requested.WaitForLoaded(LoadTimeoutMilliseconds))
                return null;

            return requested as T;
        }
    }
}
