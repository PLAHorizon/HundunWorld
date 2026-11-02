using System;
using FlaxEngine;

namespace HundunWorld.Game.Character
{
    /// <summary>
    /// 把 UE 的眉毛卡片挂到 MetaHuman 头下面。
    ///
    /// 空间：眉毛 GLB 与头发卡片来自同一个 UE 导出空间（米、Y 朝上、Z 向前），
    /// 导入资产时 Scale=100 已换成厘米，且原点就是头模型原点 —— 因此和头发一样
    /// 用零变换、单位缩放直接挂到头部 AnimatedModel 下即可。
    /// </summary>
    public static class MetaHumanEyebrowBinder
    {
        public const string EyebrowModelPath = "Content/Character/Models/mhhair/Eyebrows_Cards.flax";

        /// <summary>
        /// 眉毛覆盖率掩码：源 Attribute 图的 R 通道（约 3% 像素非零）重新打包到 RGBA 四通道，
        /// 以便复用 MH_Hair_Cutout（它按 MaskTex.W 取掩码）。
        /// </summary>
        public const string EyebrowMaskPath = "Content/Character/Skin/Eyebrows_Coverage_W.flax";

        public const string CutoutMaterialPath = "Content/Character/Materials/MH_Hair_Cutout.flax";

        /// <summary>
        /// 总开关。2026-10-23：几何对位（眉卡与脸面 z 范围重叠）、材质/掩码链路已就绪，
        /// 但本机 PIE 从 19:20 起无法进入播放模式（sim_play 超时、scenes=0），
        /// “是否与眼位对齐、是否与贴图眉毛重影”只能目视判定。未目视前默认关闭，
        /// 避免把未验证的视觉改动带进创角预览与世界。
        /// </summary>
        public const bool Enabled = true;

        /// <summary>
        /// 眉色：直接跟随发色会得到"金发金眉"（实测 2026-10-23 预览截图过亮）。
        /// 取发色压暗后的同色相（约 35%），得到自然的深棕眉；黑发仍接近黑。
        /// </summary>
        public static Color DarkenForBrows(Color hair)
        {
            return new Color(hair.R * 0.35f, hair.G * 0.32f, hair.B * 0.30f, 1f);
        }

        /// <summary>挂眉毛卡片；失败返回 null 并留下一条可归因的日志，不静默。</summary>
        public static StaticModel Attach(Actor head, Color color, string name, out MaterialInstance instance)
        {
            instance = null;
            if (!Enabled)
            {
                return null;
            }

            if (head == null)
            {
                Debug.LogWarning("[MetaHumanEyebrowBinder] 头部为空，跳过眉毛挂载");
                return null;
            }

            var model = Content.Load<Model>(MetaHuman.Runtime.MetaHumanAssetUtil.ResolveProjectPath(EyebrowModelPath), 10000.0);
            if (model == null || !model.IsLoaded)
            {
                Debug.LogWarning("[MetaHumanEyebrowBinder] 眉毛模型未就绪: " + EyebrowModelPath);
                return null;
            }

            var brows = head.AddChild<StaticModel>();
            brows.Name = name;
            brows.Model = model;
            brows.LocalPosition = Vector3.Zero;
            brows.LocalScale = Vector3.One;

            var baseMaterial = Content.Load<MaterialBase>(MetaHuman.Runtime.MetaHumanAssetUtil.ResolveProjectPath(CutoutMaterialPath), 10000.0);
            if (baseMaterial == null || !baseMaterial.IsLoaded)
            {
                Debug.LogWarning("[MetaHumanEyebrowBinder] 抠图材质未就绪: " + CutoutMaterialPath);
                return brows;
            }

            instance = baseMaterial.CreateVirtualInstance() as MaterialInstance;
            if (instance == null)
            {
                Debug.LogWarning("[MetaHumanEyebrowBinder] 抠图材质无法创建实例: " + CutoutMaterialPath);
                return brows;
            }

            Color browColor = DarkenForBrows(color);
            instance.SetParameterValue("HairColor", browColor);
            var mask = Content.Load<Texture>(MetaHuman.Runtime.MetaHumanAssetUtil.ResolveProjectPath(EyebrowMaskPath), 10000.0);
            if (mask != null && mask.IsLoaded)
            {
                instance.SetParameterValue("MaskTex", mask);
            }
            else
            {
                Debug.LogWarning("[MetaHumanEyebrowBinder] 眉毛掩码未就绪: " + EyebrowMaskPath);
            }

            // 资产有两个材质槽（导入时生成了 Material 0/1），逐个槽都指到同一个实例，
            // 否则没指到的槽会用默认白模。
            int slots = brows.MaterialSlots != null ? brows.MaterialSlots.Length : 0;
            for (int i = 0; i < slots; i++)
            {
                brows.SetMaterial(i, instance);
            }

            Debug.Log($"[MetaHumanEyebrowBinder] 眉毛已挂载: {name} slots={slots} color=({color.R:F2},{color.G:F2},{color.B:F2})");
            return brows;
        }
    }
}
