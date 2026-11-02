using System;
using System.Collections.Generic;
using System.Text;
using FlaxEngine;
using MetaHuman;
using MetaHuman.Runtime;

namespace HundunWorld.Game.Character
{
    /// <summary>
    /// 合成远端探针：在运行时用 **与网络远端完全相同** 的 RemotePlayerActor.ApplyMetaHumanLook
    /// 造一具远端角色并套一份内存里的 look。存在的理由：验证远端瞳色/发型渲染链时不一定要
    /// 第二台客户端，但必须走同一条代码路径——在 Game 程序集里实现可避免工具侧反射跨域
    /// 拿到不同装配的 MetaHumanCharacter（实测会导致 GetScript 返回 null、Session 为空）。
    /// </summary>
    public static class MetaHumanRemoteProbe
    {
        public static string Synthesize(
            float x, float y, float z,
            int hairStyle, int hairColor, float eyeColor01, ulong characterId,
            out string diagnostics)
        {
            var sb = new StringBuilder();
            try
            {
                var scene = HundunWorldGame.FindGameWorldScene();
                if (scene == null)
                {
                    diagnostics = "找不到玩法场景（FindGameWorldScene 返回 null）";
                    return null;
                }

                var actor = new EmptyActor
                {
                    Name = $"RemoteProbe_{characterId}",
                    Position = new Vector3(x, y, z),
                };
                Level.SpawnActor(actor, scene);

                var body = actor.AddChild<AnimatedModel>();
                body.Name = "Body";
                body.PerBoneMotionBlur = false;
                var model = Content.Load<SkinnedModel>(MetaHuman.Runtime.MetaHumanAssetUtil.ResolveProjectPath(Equipment.EquipmentDatabase.TemplateSkinnedBodyModelPath), 10000.0);
                if (model == null || !model.IsLoaded)
                {
                    diagnostics = "身体模型未就绪: " + Equipment.EquipmentDatabase.TemplateSkinnedBodyModelPath;
                    return null;
                }

                body.SkinnedModel = model;

                var rpa = actor.AddScript<RemotePlayerActor>();
                rpa.EntityId = characterId;
                rpa.PlayerName = "RemoteProbe";

                var look = new MetaHumanLook
                {
                    // 成对装载时 Session 的模板 Id 是组合串（实测：只写 face_archetype 会被拒）。
                    TemplateId = "metahuman.face_archetype+metahuman.body_base",
                    Values = new Dictionary<string, float>
                    {
                        { "hair.style", hairStyle },
                        { "hair.color", hairColor / 5f },
                        { "eye.iris_color", eyeColor01 },
                    },
                };
                // 快照校验和是随数据一起走的（ValidateAgainst 会核对）；手工构造的 look 必须自己补。
                look.Checksum = MetaHumanLook.ComputeChecksum(look.Values);

                bool applied = rpa.ApplyMetaHumanLook(look, characterId, hairColor);
                sb.Append("applied=").Append(applied);
                sb.Append(" bodyPath=").Append(body.SkinnedModel?.Path ?? "<null>");
                sb.Append(" hairAsset=").Append(Content.Load<Model>(MetaHuman.Runtime.MetaHumanAssetUtil.ResolveProjectPath(Equipment.EquipmentDatabase.HairCardsModelPath), 10000.0) != null);

                // 远端 locomotion：真实远端由 FlaxActorSyncSystem→EnsureMetaHumanBody 挂同款图并加驱动；
                // 探针手动补齐同一条链（身体此时已被 ApplyMetaHumanLook 接管为 Ada 身体）。
                var graph = HundunWorldGame.LoadAssetFullyLoaded<AnimationGraph>(Guid.Empty, "Content/Character/Models/Animation Graph.flax");
                if (graph != null && graph.IsLoaded)
                {
                    body.AnimationGraph = graph;
                    body.UpdateMode = AnimatedModel.AnimationUpdateMode.Auto;
                    body.IsActive = true;
                    MetaHumanClipFrameFix.Attach(body);
                }
                HundunWorldGame.EnsureLocomotionDriver(actor);
                sb.Append(" graph=").Append(body.AnimationGraph != null ? body.AnimationGraph.Path : "<null>");

                AnimatedModel head = null;
                StaticModel hair = null;
                for (int i = 0; i < actor.ChildrenCount; i++)
                {
                    var child = actor.GetChild(i);
                    if (child.Name == "RemoteHead_" + characterId) head = child as AnimatedModel;
                }

                // 头发是 **头的子节点**（RemotePlayerActor 里 head.AddChild），不是 Actor 的直接子级。
                if (head != null)
                {
                    for (int i = 0; i < head.ChildrenCount; i++)
                    {
                        if (head.GetChild(i).Name == "RemoteHair_" + characterId)
                            hair = head.GetChild(i) as StaticModel;
                    }
                }

                sb.Append(" head=").Append(head != null);
                sb.Append(" hair=").Append(hair != null);
                sb.Append(" hairModel=").Append(hair?.Model?.Path ?? "<none>");

                var mh = actor.GetScript<MetaHumanCharacter>();
                sb.Append(" mh=").Append(mh != null);
                if (mh != null)
                {
                    sb.Append(" hairStyle=").Append(mh.HairStyle);
                    sb.Append(" session=").Append(mh.Session != null);
                    // 诊断：直接从同一实例再 Load 一次，用来区分"内部拿到的实例不同"与"look 本身被拒"。
                    sb.Append(" directLoad=").Append(mh.LoadLook(look));
                }

                diagnostics = sb.ToString();
                return actor.Name;
            }
            catch (Exception ex)
            {
                diagnostics = "异常: " + ex;
                return null;
            }
        }
    }
}
