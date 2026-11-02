using FlaxEngine;

namespace HundunWorld.Game.Character
{
    /// <summary>
    /// 让世界玩家预制体里的 MetaHuman 头跟随同预制体内的 Ada 身体。
    /// 只在 OnStart 调一次 SetMasterPoseModel（同骨架、按骨名匹配）；
    /// 不做逐帧矩阵合成——运行时换模型/逐帧贴骨已验证会把编辑器断言钉死。
    /// </summary>
    public class WorldHeadMasterPose : Script
    {
        public override void OnStart()
        {
            if (Actor is not AnimatedModel head || Actor.Parent == null)
                return;

            AnimatedModel body = Actor.Parent.FindActor<AnimatedModel>();
            if (body == null || ReferenceEquals(body, head))
            {
                Debug.LogWarning("[WorldHeadMasterPose] 未找到身体 AnimatedModel，头保持独立");
                return;
            }

            head.SetMasterPoseModel(body);
            Debug.Log($"[WorldHeadMasterPose] 头已跟随身体: head={head.Name}, body={body.Name}");
        }
    }
}
