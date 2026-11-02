using System.Collections.Generic;

namespace MetaHuman
{
    /// <summary>
    /// 骨骼名别名表：把模板用的 UE 骨骼名翻译成资产里真存在的名字。
    ///
    /// 为什么要这张表（实测，文档 §44）：身体模板 24 个骨骼目标全是 UE 那套
    /// （`pelvis / spine_01 / clavicle_l / hand_l / foot_l / neck_01 / calf_knee_l`），
    /// 而项目现有角色模型（Character_1 / Character_2）是 Unity Humanoid 命名
    /// （`Hips / Spine / LeftShoulder / LeftHand / LeftFoot / LeftUpLeg …`），
    /// **同名命中 0/24** ⇒ "玩家调身体滑杆"在现成资产上本来一条都写不出来。
    ///
    /// 为什么这张表能救身体、救不了脸：UE Mannequin ↔ Unity Humanoid 是业界标准的一对一配对，
    /// 每对都是同一根骨骼、只是名字不同；而脸上那批 `FACIAL_*` 目标在 Humanoid 骨架上
    /// **根本没有对应结构**（§39.1：那两具模型没有面部骨骼）。
    /// 所以这里只做可证为"同一根骨骼换了个名字"的配对，
    /// 不拿"位置差不多""看着像"来配对——那会把形变写到错的骨头上还全绿。
    /// </summary>
    public static class MetaHumanBoneAliases
    {
        /// <summary>模板骨骼名 → 资产侧可接受的别名（按顺序试，先命中的赢）。</summary>
        /// <remarks>
        /// 右岸名字的来源已换成**资产实测**：`skeleton_rename`（dryRun）的 `plan[].from`
        /// 就是这具资产现有的节点名（`tableRowsNotFoundInSkeleton: []` ⇒ 那 52 行全部真在骨架里），
        /// 它们是 Mixamo 的 `DEF-*` 族，而不是我上一轮误以为的 Unity Humanoid 名
        /// （`Hips / LeftArm …` 出现在 `introducedNameCollisions` 里，那是"改名之后会有的名字"，
        /// 把"改完会怎样"读成"现在是什么"就是这个文件上一版的错）。
        /// 所以每个键的第一候选都是**已证存在**的 `DEF-*`；Humanoid 名留作第二候选，
        /// 它只在别的资产真用那套名字时才生效（别名按构造不可能凭空造出一根骨头）。
        /// 刻意**不配**的：`calf_knee_*`（Mixamo 没有独立膝骨）、`index_metacarpal_*`（同上，没有掌骨）、
        /// `neck_02`（这具资产没有第二根颈骨）—— 宁可少一条别名，也不拿"位置差不多"配。
        /// </remarks>
        private static readonly Dictionary<string, string[]> Map =
            new Dictionary<string, string[]>(System.StringComparer.Ordinal)
        {
            { "pelvis", new string[] { "DEF-spine", "Hips" } },
            { "spine_01", new string[] { "DEF-spine.001", "Spine" } },
            { "spine_02", new string[] { "DEF-spine.002", "Spine1" } },
            { "spine_03", new string[] { "DEF-spine.003", "Chest" } },
            { "neck_01", new string[] { "DEF-spine.005", "Neck" } },
            { "head", new string[] { "DEF-spine.006", "Head" } },
            { "clavicle_l", new string[] { "DEF-shoulder.L", "LeftShoulder" } },
            { "clavicle_r", new string[] { "DEF-shoulder.R", "RightShoulder" } },
            { "upperarm_l", new string[] { "DEF-upper_arm.L", "LeftArm" } },
            { "upperarm_r", new string[] { "DEF-upper_arm.R", "RightArm" } },
            { "lowerarm_l", new string[] { "DEF-forearm.L", "LeftForeArm" } },
            { "lowerarm_r", new string[] { "DEF-forearm.R", "RightForeArm" } },
            { "hand_l", new string[] { "DEF-hand.L", "LeftHand" } },
            { "hand_r", new string[] { "DEF-hand.R", "RightHand" } },
            { "thumb_01_l", new string[] { "DEF-thumb.01.L", "LeftHandThumb1" } },
            { "thumb_01_r", new string[] { "DEF-thumb.01.R", "RightHandThumb1" } },
            { "index_01_l", new string[] { "DEF-f_index.01.L", "LeftHandIndex1" } },
            { "index_01_r", new string[] { "DEF-f_index.01.R", "RightHandIndex1" } },
            { "middle_01_l", new string[] { "DEF-f_middle.01.L", "LeftHandMiddle1" } },
            { "middle_01_r", new string[] { "DEF-f_middle.01.R", "RightHandMiddle1" } },
            { "ring_01_l", new string[] { "DEF-f_ring.01.L", "LeftHandRing1" } },
            { "ring_01_r", new string[] { "DEF-f_ring.01.R", "RightHandRing1" } },
            { "pinky_01_l", new string[] { "DEF-f_pinky.01.L", "LeftHandPinky1" } },
            { "pinky_01_r", new string[] { "DEF-f_pinky.01.R", "RightHandPinky1" } },
            { "thigh_l", new string[] { "DEF-thigh.L", "LeftUpLeg" } },
            { "thigh_r", new string[] { "DEF-thigh.R", "RightUpLeg" } },
            { "calf_l", new string[] { "DEF-shin.L", "LeftLeg" } },
            { "calf_r", new string[] { "DEF-shin.R", "RightLeg" } },
            { "foot_l", new string[] { "DEF-foot.L", "LeftFoot" } },
            { "foot_r", new string[] { "DEF-foot.R", "RightFoot" } },
            { "ball_l", new string[] { "DEF-toe.L", "LeftToeBase" } },
            { "ball_r", new string[] { "DEF-toe.R", "RightToeBase" } },
            // 手指二、三节。上一版只登记了每指第一节，于是真机上别名档的命中数正好等于表行数
            // （Ada 身体 342 节点：tierAlias=31 + tierExact=2 = 33），表就是天花板。
            // Character_1 侧的 `DEF-f_*.02/03.L/R`、`DEF-thumb.02/03.L/R` 都在盘上同名存在，
            // 不补这 20 行等于把 MetaHuman 手部一半的可对齐骨骼静默留在门外。
            { "index_02_l", new string[] { "DEF-f_index.02.L", "LeftHandIndex2" } },
            { "index_02_r", new string[] { "DEF-f_index.02.R", "RightHandIndex2" } },
            { "index_03_l", new string[] { "DEF-f_index.03.L", "LeftHandIndex3" } },
            { "index_03_r", new string[] { "DEF-f_index.03.R", "RightHandIndex3" } },
            { "middle_02_l", new string[] { "DEF-f_middle.02.L", "LeftHandMiddle2" } },
            { "middle_02_r", new string[] { "DEF-f_middle.02.R", "RightHandMiddle2" } },
            { "middle_03_l", new string[] { "DEF-f_middle.03.L", "LeftHandMiddle3" } },
            { "middle_03_r", new string[] { "DEF-f_middle.03.R", "RightHandMiddle3" } },
            { "ring_02_l", new string[] { "DEF-f_ring.02.L", "LeftHandRing2" } },
            { "ring_02_r", new string[] { "DEF-f_ring.02.R", "RightHandRing2" } },
            { "ring_03_l", new string[] { "DEF-f_ring.03.L", "LeftHandRing3" } },
            { "ring_03_r", new string[] { "DEF-f_ring.03.R", "RightHandRing3" } },
            { "pinky_02_l", new string[] { "DEF-f_pinky.02.L", "LeftHandPinky2" } },
            { "pinky_02_r", new string[] { "DEF-f_pinky.02.R", "RightHandPinky2" } },
            { "pinky_03_l", new string[] { "DEF-f_pinky.03.L", "LeftHandPinky3" } },
            { "pinky_03_r", new string[] { "DEF-f_pinky.03.R", "RightHandPinky3" } },
            { "thumb_02_l", new string[] { "DEF-thumb.02.L", "LeftHandThumb2" } },
            { "thumb_02_r", new string[] { "DEF-thumb.02.R", "RightHandThumb2" } },
            { "thumb_03_l", new string[] { "DEF-thumb.03.L", "LeftHandThumb3" } },
            { "thumb_03_r", new string[] { "DEF-thumb.03.R", "RightHandThumb3" } },
        };

        /// <summary>
        /// 取某个模板骨骼名的别名清单（没有别名返回空数组）。
        /// 运行期要的是这个而不是 <see cref="Resolve"/>：写回端手上只有
        /// <c>SkinnedModel.FindNode(name)</c> 这个"问一具具体资产"的入口，
        /// 拿不到（也不该拿）整副骨架的名字集合，所以由调用方逐个探。
        /// </summary>
        /// <summary>
        /// 反查：给一个**资产侧**关节名（`DEF-spine`、`Hips` 这类），返回它在模板/UE 名字族里的归属名。
        /// 存在的理由：别名表原本只有"UE 名 → 资产名"这一个方向，于是绑动画时
        /// target=MetaHuman 骨架、source=Mixamo 骨架能命中，反过来（target 是 DEF-* 族）完全不触发
        /// —— 实测反向只剩 2 条精确同名，写进资产的重绑表基本没用。
        /// 顺序扫描是刻意的：表只有 32 条，而建反向索引会引入"谁先注册"的第二份状态；
        /// 一名一主的约束由 <see cref="Validate"/> 守着，所以这里返回的是唯一主人。
        /// </summary>
        public static string TemplateNameForAsset(string assetName)
        {
            if (string.IsNullOrEmpty(assetName))
            {
                return null;
            }

            foreach (KeyValuePair<string, string[]> entry in Map)
            {
                string[] aliases = entry.Value;
                for (int i = 0; i < aliases.Length; i++)
                {
                    if (string.Equals(aliases[i], assetName, System.StringComparison.Ordinal))
                    {
                        return entry.Key;
                    }
                }
            }

            return null;
        }

        public static string[] AliasesOf(string templateBoneName)
        {
            if (string.IsNullOrEmpty(templateBoneName))
            {
                return NoAliases;
            }

            string[] aliases;
            return Map.TryGetValue(templateBoneName, out aliases) && aliases != null ? aliases : NoAliases;
        }

        private static readonly string[] NoAliases = new string[0];

        /// <summary>这张表登记的配对条数（断言用它防空转）。</summary>
        public static int Count
        {
            get { return Map.Count; }
        }

        /// <summary>模板声明的骨骼名在资产里找不到时，问一句"它有没有别名，且别名真在资产里"。</summary>
        public static string Resolve(string templateBoneName, ICollection<string> availableAssetBones)
        {
            if (string.IsNullOrEmpty(templateBoneName) || availableAssetBones == null)
            {
                return null;
            }

            string[] aliases;
            if (!Map.TryGetValue(templateBoneName, out aliases))
            {
                return null;
            }

            for (int i = 0; i < aliases.Length; i++)
            {
                if (!string.IsNullOrEmpty(aliases[i]) && availableAssetBones.Contains(aliases[i]))
                {
                    return aliases[i];
                }
            }

            return null;
        }

        /// <summary>
        /// 作者门：别名表最容易出的两种错——左右写反、同一资产名被两根模板骨骼抢。
        /// 这两种都不会让运行期报错，只会让形变歪到别的骨头上，所以必须在这里拦。
        /// </summary>
        public static List<string> Validate()
        {
            List<string> problems = new List<string>();
            Dictionary<string, string> taken = new Dictionary<string, string>(System.StringComparer.Ordinal);

            foreach (KeyValuePair<string, string[]> kv in Map)
            {
                string templateName = kv.Key;
                if (kv.Value == null || kv.Value.Length == 0)
                {
                    problems.Add(templateName + " 没有任何别名");
                    continue;
                }

                for (int i = 0; i < kv.Value.Length; i++)
                {
                    string alias = kv.Value[i];
                    if (string.IsNullOrEmpty(alias))
                    {
                        problems.Add(templateName + " 有空别名");
                        continue;
                    }

                    string owner;
                    if (taken.TryGetValue(alias, out owner))
                    {
                        problems.Add("别名 " + alias + " 同时被 " + owner + " 与 " + templateName + " 使用");
                    }
                    else
                    {
                        taken[alias] = templateName;
                    }

                    // 左右必须一致：`*_l` 配 Right* 是最难查的一类错（对称骨架上看起来"也是动了"）
                    bool templateLeft = templateName.EndsWith("_l", System.StringComparison.Ordinal);
                    bool templateRight = templateName.EndsWith("_r", System.StringComparison.Ordinal);
                    bool aliasLeft = alias.StartsWith("Left", System.StringComparison.Ordinal);
                    bool aliasRight = alias.StartsWith("Right", System.StringComparison.Ordinal);
                    if (templateLeft && aliasRight)
                    {
                        problems.Add(templateName + " 是左侧却配了右侧别名 " + alias);
                    }

                    if (templateRight && aliasLeft)
                    {
                        problems.Add(templateName + " 是右侧却配了左侧别名 " + alias);
                    }
                }
            }

            return problems;
        }

        /// <summary>表里所有模板侧骨骼名（给普查/覆盖率用）。</summary>
        public static ICollection<string> TemplateNames()
        {
            return Map.Keys;
        }
    }
}
