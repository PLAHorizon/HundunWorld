using FlaxEngine;

namespace HundunWorld.Game.UI.Ink.Pages.CharacterCreation
{
    /// <summary>
    /// 一次创角流程的跨页状态。
    /// 页面由 <see cref="InkPageRouter"/> 的工厂每次导航时新建（旧页会被销毁），
    /// 所以性别 / 面容预设 / 姓名这类跨步骤数据必须放在页面之外。
    /// </summary>
    public sealed class CcSession
    {
        /// <summary>当前创角会话。</summary>
        public static readonly CcSession Current = new CcSession();

        /// <summary>0 = 男，1 = 女。</summary>
        public int Gender;

        /// <summary>面容风格分类：0 = 风雅，1 = 写实。</summary>
        public int StyleCategory;

        /// <summary>选中的面容预设下标，-1 表示未选。</summary>
        public int FacePresetIndex = -1;

        /// <summary>选中的衣装 / 体型下标，写入 <c>AppearanceInfo.Clothing</c>。</summary>
        public int BodyIndex;

        /// <summary>发色 / 瞳色 / 肤色下标，分别写入 <c>AppearanceInfo.HairColor / EyeColor / SkinColor</c>。</summary>
        public int HairColorIndex;
        public int EyeColorIndex;
        public int SkinColorIndex;

        /// <summary>发型下标（hair.style），写入 <c>AppearanceInfo.HairStyle</c>；渲染以 look 里的值为准。</summary>
        public int HairStyleIndex;

        /// <summary>角色名。</summary>
        public string CharacterName = string.Empty;

        /// <summary>
        /// 整段创角共用的那一具捏脸角色（数值会话 + 可回溯历史都挂在它身上）。
        /// <para>
        /// 归属必须在这里，不能挂在页面或预览的叶 Actor 上：本类上面写着「页面每次导航都新建」，
        /// 而 2026-10-18 的实测回执正是 —— 面板 #1/#2/#3 各绑一具全新对象（steps=1 全丢），
        /// 同一页内组件也会在预览重建 Actor 后变成孤儿（Flax 的 <c>Object.==</c> 比原生指针，
        /// 于是 <c>_character == null</c> 为真、每次拖动被静默退回）。
        /// </para>
        /// </summary>
        public MetaHuman.Runtime.MetaHumanCharacter Sculpt;

        /// <summary><see cref="Sculpt"/> 的常驻宿主：不属于任何页面，退出创角流程才销毁。</summary>
        public Actor SculptHost;

        /// <summary>
        /// 重置为新一次创角（从选角页点「创建角色」进入时调用）。
        /// </summary>
        public void Reset()
        {
            Gender = 0;
            StyleCategory = 0;
            FacePresetIndex = -1;
            BodyIndex = 0;
            HairColorIndex = 0;
            EyeColorIndex = 0;
            SkinColorIndex = 0;
            HairStyleIndex = 0;
            CharacterName = string.Empty;

            // 新一段创角要一张新脸：宿主连同其上的组件一起销毁，下一次进捏脸页会重建。
            // 判活要用 Flax 的 ==（比原生指针）：上一页销毁时可能已经把它带走了。
            // 销毁走静态的 FlaxEngine.Object.Destroy（Actor 上没有实例版 Destroy()），
            // 且这里必须写全限定名：本文件同时 using System，裸 Object 会撞 System.Object。
            if (SculptHost != null)
            {
                FlaxEngine.Object.Destroy(SculptHost);
            }

            SculptHost = null;
            Sculpt = null;
        }
    }
}
