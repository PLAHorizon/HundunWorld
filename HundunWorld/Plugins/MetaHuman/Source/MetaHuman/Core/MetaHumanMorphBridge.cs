using System.Collections.Generic;

namespace MetaHuman
{
    /// <summary>
    /// morph 名字的双左岸查询：.dna 族（<c>head_wm1_blink_L</c>）与 UE 网格族
    /// （<c>head_lod0_mesh__brow_down_L</c>）是**两套同名命中 0/37 的命名族**（实测，文档 §41/§42），
    /// 所以运行期必须两张表都查，而不是换一种头就要换一次代码。
    ///
    /// 这里刻意只做"按顺序查两张表并把结果并起来"这一件事：
    /// 查表规则本身仍然只在 <see cref="MetaHumanMorphGuiMap"/> 里，
    /// 而"哪张表先查"也只在这一个方法里——写回端与动画器共用它，
    /// 免得出现"一处支持双岸、另一处只支持单岸"这种最难查的分裂。
    /// </summary>
    public static class MetaHumanMorphBridge
    {
        /// <summary>
        /// 把模板给的形状名翻译成**这具资产真有的** blendshape 名。
        ///
        /// 关键是"跨族"这一步：单张表的 <see cref="MetaHumanMorphGuiMap.ResolveAssetMorph"/>
        /// 只能从**它自己那张表的左岸名**出发，所以"模板写 .dna 族名、资产是 UE 网格族名"这种组合，
        /// 逐张表各问一遍是问不出来的（合成夹具一测就露馅）。正确通路是：
        /// ① 用任意一张表把想要的名字换成 GUI 控制；
        /// ② 再用每张表把该控制映射成形状名，取第一个"资产里真有"的；
        /// ③ 都失败才退回各表自己的直接通路。
        /// 三条都不中才返回 null（调用方据此记 skipped，绝不猜"看着像"的名字）。
        /// </summary>
        public static string ResolveAssetMorph(string wanted,
                                               ICollection<string> availableAssetMorphs,
                                               MetaHumanMorphGuiMap dnaFamily,
                                               MetaHumanMorphGuiMap ueFamily,
                                               MetaHumanWarpShapeMap warpFamily = null)
        {
            if (string.IsNullOrEmpty(wanted) || availableAssetMorphs == null
                || availableAssetMorphs.Count == 0)
            {
                return null;
            }

            // ⓪ 第三命名族先走自己的对照表：`head_wmN_*` 是 .dna 的形变器名，
            //    它既不是两张 morph→GUI 表的左岸，也不能靠剥前缀硬塞进表里（剥出来的 `blink_L`
            //    不是任何一张表的键，实测 0 命中）。表给的是候选清单，逐个问这具资产有没有——
            //    同一形变在真头上同时挂在 head 与 cartilage 上，各写回端取到自己那根才对。
            if (warpFamily != null)
            {
                List<string> candidates = warpFamily.ShapesForWarp(wanted);
                for (int i = 0; i < candidates.Count; i++)
                {
                    if (availableAssetMorphs.Contains(candidates[i]))
                    {
                        return candidates[i];
                    }
                }
            }

            MetaHumanMorphGuiMap[] maps = { dnaFamily, ueFamily };

            // ① 想要的名字属于哪一族？只要有一张表认得，就能拿到共同的 GUI 控制。
            //    模板里写的是 `head_wm1_blink_L`（形变族带节号），而 dna 表的键是 `blink_L`，
            //    所以先按原名问一遍，问不到再按"剥掉 head_wm<节号>_"的形式问一遍。
            //    剥前缀只当作**取控制键**的兜底，不参与第 ③ 步的直接命中 ——
            //    否则等于允许"看着像"的名字凭空造出一个形状对应，正是本项目反复踩的静默错位。
            string stripped = StripWarpPrefix(wanted);
            string[] probes = stripped == null
                ? new string[] { wanted }
                : new string[] { wanted, stripped };

            string gui = null;
            for (int p = 0; p < probes.Length && gui == null; p++)
            {
                for (int i = 0; i < maps.Length && gui == null; i++)
                {
                    if (maps[i] == null)
                    {
                        continue;
                    }

                    string found;
                    if (maps[i].TryGetGuiControl(probes[p], out found) && !string.IsNullOrEmpty(found))
                    {
                        gui = found;
                    }
                }
            }

            // ② 用这个控制去问每一张表：谁给出的形状名真在资产里，就用谁
            if (gui != null)
            {
                List<string> names = MorphsForGui(gui, dnaFamily, ueFamily);
                for (int i = 0; i < names.Count; i++)
                {
                    if (availableAssetMorphs.Contains(names[i]))
                    {
                        return names[i];
                    }
                }
            }

            // ③ 退回各表自己的通路（同名同族、或表内已配好直连的情况）
            for (int i = 0; i < maps.Length; i++)
            {
                if (maps[i] == null)
                {
                    continue;
                }

                string resolved = maps[i].ResolveAssetMorph(wanted, availableAssetMorphs);
                if (!string.IsNullOrEmpty(resolved) && availableAssetMorphs.Contains(resolved))
                {
                    return resolved;
                }
            }

            return null;
        }

        /// <summary>
        /// 剥掉形变族前缀 `head_wm&lt;节号&gt;_`（模板侧写法 `head_wm1_blink_L` ↔ 表里键 `blink_L`）。
        /// 手工解析而不是正则：插件编译单元里没有 System.Text.RegularExpressions（用了就是 CS1069）。
        /// 不合规的名字返回 null，调用方按"没有这个兜底"处理。
        /// </summary>
        public static string StripWarpPrefix(string name)
        {
            if (string.IsNullOrEmpty(name) || !name.StartsWith("head_wm", System.StringComparison.Ordinal))
            {
                return null;
            }

            int i = "head_wm".Length;
            while (i < name.Length && name[i] >= '0' && name[i] <= '9')
            {
                i++;
            }

            // 必须真的带节号、且后面紧跟下划线，否则不肯剥（`head_wmx_blink` 这种不进这条路）
            if (i == "head_wm".Length || i >= name.Length || name[i] != '_')
            {
                return null;
            }

            string rest = name.Substring(i + 1);
            return rest.Length == 0 ? null : rest;
        }

        /// <summary>
        /// 一个 GUI 控制在**两张表里**能落到哪些形状名（并集、去重、顺序稳定：先 .dna 族后 UE 族）。
        /// 去重是必须的：两张表若给出同一个名字，写两遍会把同一根形状的权重加两次。
        /// </summary>
        public static List<string> MorphsForGui(string guiControlName,
                                                MetaHumanMorphGuiMap dnaFamily,
                                                MetaHumanMorphGuiMap ueFamily)
        {
            List<string> found = new List<string>();
            if (string.IsNullOrEmpty(guiControlName))
            {
                return found;
            }

            MetaHumanMorphGuiMap[] maps = { dnaFamily, ueFamily };
            for (int i = 0; i < maps.Length; i++)
            {
                if (maps[i] == null)
                {
                    continue;
                }

                List<string> names = maps[i].MorphsForGui(guiControlName);
                if (names == null)
                {
                    continue;
                }

                for (int k = 0; k < names.Count; k++)
                {
                    if (!string.IsNullOrEmpty(names[k]) && !found.Contains(names[k]))
                    {
                        found.Add(names[k]);
                    }
                }
            }

            return found;
        }
    }
}
