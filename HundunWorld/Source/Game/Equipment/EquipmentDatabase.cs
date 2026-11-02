using FlaxEngine;
using Game.Character.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HundunWorld.Game.Equipment
{
    /// <summary>
    /// 装备/配饰/武器数据库
    /// </summary>
    public static class EquipmentDatabase
    {
        /// <summary>默认身体装备ID</summary>
        public static int DefaultBodyId => 10001;

        /// <summary>默认长剑ID</summary>
        public static int DefaultLongswordId => 20001;

        /// <summary>默认头巾ID</summary>
        public static int DefaultHeadScarfId => 30001;

        /// <summary>默认项链ID</summary>
        public static int DefaultNecklaceId => 10002;

        /// <summary>默认护肩ID</summary>
        public static int DefaultShoulderGuardId => 10003;

        /// <summary>默认披风ID</summary>
        public static int DefaultCloakId => 10004;

        /// <summary>默认腰带ID</summary>
        public static int DefaultBeltId => 10005;

        /// <summary>默认长裤ID</summary>
        public static int DefaultLeggingsId => 10006;

        /// <summary>默认布鞋ID</summary>
        public static int DefaultShoesId => 10007;

        /// <summary>默认短剑ID</summary>
        public static int DefaultDaggerId => 10008;

        /// <summary>默认右戒指ID</summary>
        public static int DefaultRightRingId => 10009;

        /// <summary>默认左戒指ID</summary>
        public static int DefaultLeftRingId => 10010;

        /// <summary>默认右手腕ID</summary>
        public static int DefaultRightWristId => 10011;

        /// <summary>默认左手腕ID</summary>
        public static int DefaultLeftWristId => 10012;

        /// <summary>默认面具ID</summary>
        public static int DefaultMaskId => 10013;

        /// <summary>粗布小包ID</summary>
        public static int SmallClothBagId => 2001;

        /// <summary>皮革中包ID</summary>
        public static int MediumLeatherBagId => 2002;

        /// <summary>丝绸大包ID</summary>
        public static int LargeSilkBagId => 2003;

        /// <summary>龙皮巨包ID</summary>
        public static int HugeDragonBagId => 2004;

        /// <summary>外观装备号段起点（90001 起，与战斗装备 10001/20001/30001 分开）。</summary>
        public const int AppearanceIdBase = 91000;

        /// <summary>男性外观「玄墨劲装」</summary>
        public static int AppearanceXuanmoId => 91001;

        /// <summary>男性外观「玄铁重铠」</summary>
        public static int AppearanceXuantieId => 91002;

        /// <summary>女性外观「流云霓裳」</summary>
        public static int AppearanceLiuyunId => 91003;

        /// <summary>女性外观「素雪罗裙」</summary>
        public static int AppearanceSuxueId => 91004;

        /// <summary>
        /// 外观装备当前没有独立的时装网格资产（工程内唯一候选 <c>skm_uefn_mannequin.flax</c>
        /// 实测 0 顶点且骨架与 Character_1/2 不同族，不能用）。因此现阶段用
        /// <see cref="EquipmentData.BodyScale"/> 改变整体身形轮廓；美术补了正式时装后，
        /// 只需把对应外观的 <see cref="EquipmentData.BodyModelPath"/> 指向新资产
        /// （加载与切换链路已就绪），运行时逻辑不用改。
        /// </summary>

        /// <summary>
        /// 身体模型资产路径 —— **这一层只有路径是可靠键，不要再往代码里写 GUID**。
        /// <para>
        /// 2026-09-27：<c>skm_uefn_mannequin.flax</c> 已被 FBX 重导入产物 <c>Character_1</c>（男）/
        /// <c>Character_2</c>（女）取代，但代码仍指旧路径与旧 GUID ⇒ 创角/选角离屏预览拿不到模型
        /// （也就拿不到骨骼基准姿态，待机动画同时消失）。
        /// </para>
        /// <para>
        /// 2026-10-16 把这里的 <c>MaleBodyModelGuid</c> / <c>FemaleBodyModelGuid</c> **删掉**了。理由不是洁癖：
        /// 同一天内 <c>Character_1.flax</c> 的头两度变脸（<c>afa7789e-…</c> 悬空 → 现读
        /// <c>528b2144-…</c> → 当天晚些时候重导后又变成 <c>216f8857-…</c>），
        /// 而 <c>HundunWorldGame.LoadAssetFullyLoaded</c> 是"**GUID 优先、失败才回退路径**"，
        /// 于是每次重导都要白等一次超时 + 满屏 <c>Cannot find asset</c>，严重时角色直接没身体。
        /// 唯一消费者 <c>HundunWorldGame.SkinnedModelGuid</c> 已改填 <c>Guid.Empty</c>（= 走路径）。
        /// ⇒ 纪律：**GUID 是易碎快照，不是标识**；要现读就用工具现读，别落成常量。见任务 #56 与方案 §171.9。
        /// </para>
        /// </summary>
        public const string MaleBodyModelPath = "Content/Character/Models/Character_1.flax";

        /// <summary>女性身体模型资产路径（同上：只认路径）</summary>
        public const string FemaleBodyModelPath = "Content/Character/Models/Character_2.flax";

        /// <summary>
        /// MetaHuman 头部模型资产路径（创角捏脸预览用）。
        /// <para>
        /// **为什么预览里要单独挂一具头**：头与身体是**两套骨架**
        /// （身体 = UE mannequin 系 Character_1/2；头 = MetaHuman FACIAL 系），
        /// 不能把脸当子网格蒙皮到身体上，只能另起一个 AnimatedModel。
        /// 在此之前创角预览**只有身体**，于是"头部 120 项"滑杆没有可见对象
        /// （用户实测截图 + 代码核实，见方案文档 §170）。
        /// </para>
        /// <para>
        /// 这具资产的 **slot 0 已指认 <c>Content/Character/Materials/MH_Head.flax</c>**
        /// （见方案文档 §168 续三），所以捏脸的形状/妆色改动能在这具头上直接看见。
        /// </para>
        /// <para>
        /// <b>2026-10-17 换成"模板形状的可捏脸头"</b>（<c>Ada_FaceMesh_conformhead.flax</c>）：
        /// 它就是把 <c>Ada_FaceMesh_slotpacked.glb</c> 皮肤层的**基础顶点**换成模板头
        /// （conform topology v130）的形状，骨架 669 骨 876 节点、9 个材质槽、
        /// <b>821 个形状通道全部原样</b>（引擎 <c>model_inspect</c> 写后复检逐项相等）。
        /// 为什么只换顶点不换 <c>head.flax</c> 那具静态网格：模板头的源 FBX 里
        /// <c>BlendShape/Deformer/Skin/Cluster</c> 全 0，而 Flax 只有
        /// <c>AnimatedModel.SetBlendShapeWeight</c>（静态 <c>Model</c> 没有运行时形状 API）
        /// ⇒ 换上去就永远捏不了（方案文档 §172 的三条实测）。
        /// 换形的收益是**与模板身体同族 ⇒ 颈缝只剩 0.26mm 级贴合误差**，
        /// 而不是 Ada 那截比模板窄一整圈的颈根。
        /// </para>
        /// <para>
        /// 回退：把这一行改回 <c>.../Ada_FaceMesh_slotpacked.flax</c> 即可（旧资产与
        /// <c>Tools/_backup_Ada_FaceMesh_conformhead_20261017.flax</c> 都还在盘上）；
        /// 生成与复核：<c>Tools/_head_template_patch.py</c> / <c>_head_template_verify.py</c>
        /// （六项判据，含"退化三角数必须与源相同"）。
        /// </para>
        /// <para>
        /// 2026-10-19：改指向 <c>conformhead4</c>。UE 导出带 12 路蒙皮影响，Flax 顶点格式只有 4 路；
        /// 旧资产按第一组取权（和≈0.43）再归一化，面部骨有效权重只剩约 27%，滑杆满量程只动一两个像素。
        /// 新资产由 <c>Tools/_head_bake4.py</c> 把 12 路合并成 top-4 并归一化（每个顶点权重和恰为 255），
        /// morph 821 条全部保留。回退：把这一行改回 <c>conformhead.flax</c> 即可。
        /// </para>
        /// </summary>
        public const string HeadModelPath = "Content/Character/Models/head_mh2/Ada_FaceMesh_conformhead4.flax";

        /// <summary>
        /// 世界角色头发：UE 毛发卡片（Hair_S_Coil_Cards）以 Scale=100 静态导入（米→厘米），
        /// 几何包围盒与头资产同空间（y 142..163cm），直接挂到头 Actor 下、本地零变换即可对齐。
        /// 材质 MH_Hair：Mask=RootUVSeedCoverage.a（Flax 无 Masked 混合模式，走 Opaque+Mask）。
        /// </summary>
        public const string HairCardsModelPath = "Content/Character/Models/mhhair/Hair_S_Coil_Cards.flax";

        /// <summary>
        /// MetaHuman 基础模板**身体**（2026-10-16 导入的 conform topology v130）。
        /// <para>
        /// 为什么需要它：原先预览的身体是 <see cref="MaleBodyModelPath"/>（一具自带脑袋的完整角色），
        /// MetaHuman 的头装上去会和它自带的头重叠 ⇒ 脸被罩住（方案文档 §170 的目视堵点）。
        /// 这具模板身体是**脖子以下**：引擎实测包围盒 y ∈ [-0.08, 142.35]（cm），**到颈顶就没了**，
        /// 所以它不会遮挡 MetaHuman 的脸。
        /// </para>
        /// <para>
        /// 类型是 <c>FlaxEngine.Model</c>（静态网格、**无骨架无蒙皮**，源 FBX 里 Skin=0、Deformer=0），
        /// 因此预览里要用 <c>StaticModel</c> 承载，不能塞进 AnimatedModel。
        /// </para>
        /// <para>
        /// 2026-10-19：原 <c>body.flax</c>（来自已不存在的 FBX 源）在 12:30 的重导后变成不可用，
        /// 加载即报 "Unsupported mesh version 0"，预览的肩颈其实来自头部资产自身。
        /// 现改指向 <c>TemplateBody4.flax</c>（由 <c>Output/mhbaked/Ada_BodyMesh4.glb</c> 以 Type=Model 静态导入）。
        /// 回退：改回 <c>body.flax</c> 即可。
        /// </para>
        /// </summary>
        public const string TemplateBodyModelPath = "Content/Character/MetaHuman_Conform_Topology/TemplateBody4.flax";

        /// <summary>
        /// MetaHuman 基础模板**头部**（与 <see cref="TemplateBodyModelPath"/> 同代、同度量系）。
        /// <para>
        /// 它现在的用途只是**参照物**：预览真正用的头是 <see cref="HeadModelPath"/>
        /// （那具的形状就是从这里逐点搬过去的）。留着这具是因为它是"无缝"的唯一判据来源
        /// —— 与身体同族同次导出，颈圈天然对得上。
        /// </para>
        /// <para>
        /// <b>别把它当可捏脸的头</b>（2026-10-17 三条实测）：这具是 <c>FlaxEngine.Model</c>，
        /// 源 FBX 里 <c>BlendShape/BlendShapeChannel/Deformer/Skin/Cluster</c> 全 0，
        /// 而 Flax 只有 <c>AnimatedModel.SetBlendShapeWeight</c>（静态 Model 没有运行时形状 API）
        /// ⇒ 重导成任何类型都变不出可写通道，换上去"头部 120 项"必然无效果（方案文档 §172）。
        /// </para>
        /// <para>
        /// 顺带纠正一条旧推断：原先写的"Ada 脸比 v130 矮 12.8cm ⇒ 同一变换即严丝合缝"
        /// 是拿包围盒猜的；逐点 ICP 实测量到的是 <see cref="AdaToTemplateScale"/> 倍加
        /// <see cref="AdaToTemplateOffset"/> 平移（残差中位 0.125cm）。
        /// </para>
        /// </summary>
        public const string TemplateHeadModelPath = "Content/Character/MetaHuman_Conform_Topology/head.flax";

        /// <summary>
        /// MetaHuman 身体的**蒙皮版**（Ada_BodyPreview4：32,334 顶点、130.8cm 到颈、躯干齐全）。
        /// <para>
        /// 为什么换掉旧的 Ada_BodyMesh4：UE 里 <c>f_med_nrw_body</c> 只导出了四肢壳（实测顶点全在
        /// |x|&gt;0.05 或 y&lt;0.2，中间没有躯干）；真正的完整身体在
        /// <c>/Game/MetaHumans/Common/Common/f_med_nrw_body_preview</c>，由
        /// <c>Tools/metahuman_body_export_preview.py</c> 导出、<c>Tools/_head_bake4.py</c> 压到 4 路影响。
        /// 骨骼名是 MetaHuman 的 <c>pelvis/spine_01..05/neck_01..02/clavicle_l...</c>，正是身体参数的目标骨。
        /// </para>
        /// </summary>
        public const string TemplateSkinnedBodyModelPath = "Content/Character/MetaHuman_Conform_Topology/Ada_BodyPreview4.flax";

        /// <summary>
        /// 身体页签是否显示**蒙皮**身体。2026-10-19 打开：完整身体 Ada_BodyPreview4 已压到 4 路影响，
        /// 身体 30 项骨骼参数需要它才动得起来（静态模板身体承载 0/598）。
        /// 目视不合格就把这里改回 false（身体页签会退回按性别取的完整角色 Character_1/2），
        /// 只影响身体页签的可见性，不影响写回链路。
        /// </summary>
        public const bool UseSkinnedBodyForPreview = true;

        /// <summary>
        /// 模板自带那颗头的顶点高度（cm）。引擎实测 <c>head.flax</c> bounds.maximum.y = 171.9799。
        /// <para>
        /// 现在只有一个用途：**取景**（<c>CharacterPreview3D.TryAutoFrame</c>）。模板身体只到颈顶 142.35，
        /// 而各页的 <c>FramingHeightFrac</c>（0.78 = 头肩）是按"到**头顶**的整身高"标定的，
        /// 直接拿 142 去算会把取景中心落到胸口、把头顶切掉。
        /// </para>
        /// <para>
        /// 曾经这里还配过一个 <c>AdaFaceCrownY=159.14</c> 与"+12.84cm 顶点对齐"补偿，
        /// 已删 —— 两具资产同在 MetaHuman 度量系里，同一变换即天然同框，那个补偿反而造成了头肩缝隙。
        /// 教训见方案文档 §171.11：**这类快照常数既易随重导失效，也会把一次误判固化成"标定"。**
        /// </para>
        /// </summary>
        public const float TemplateHeadCrownY = 171.98f;

        /// <summary>
        /// Ada 脸 → 模板度量系的**尺度**标定。
        /// <para>
        /// 来源：<c>Tools/_head_align_icp.py</c> 的 ICP（等比缩放 + 平移，逐点最近邻，
        /// 每轮只用最接近的 90% 对应参与拟合）。终值残差中位 <b>0.125cm</b>、
        /// p90 0.370cm、86.4% 的 Ada 顶点落在 0.3cm 内 ⇒ 两具确实是同族同拓扑，
        /// 但**不是同一个尺度**（1.096 倍）。
        /// </para>
        /// <para>
        /// 它推翻了我原先那句"两具同在 MetaHuman 度量系 ⇒ 同一变换即无缝"：
        /// 那是拿"都用厘米、高度都约 170"推断的，而逐点对应才是量尺。
        /// 注意别拿包围盒逐轴比例当证据（实测 x=1.171 / y=1.070 / z=1.198 互不相等），
        /// 因为两具各自包含的部位不同（Ada 脸的网格有 <b>4760 个</b>模板控制点没人认领）。
        /// </para>
        /// </summary>
        public const float AdaToTemplateScale = 1.096062f;

        /// <summary>
        /// Ada 脸 → 模板度量系的**平移**标定（cm，模板头所在的那一侧）。
        /// 与 <see cref="AdaToTemplateScale"/> 同一次 ICP 的产物，只能成对改。
        /// </summary>
        public static readonly Vector3 AdaToTemplateOffset = new Vector3(0.00602f, -2.52212f, 0.54173f);

        /// <summary>性别 → 身体模型路径。约定与 <c>CharacterInfo.Gender</c> 一致：0 = 男，1 = 女。</summary>
        public static string GetBodyModelPath(int gender)
        {
            return gender == 1 ? FemaleBodyModelPath : MaleBodyModelPath;
        }

        /// <summary>按性别缓存已加载的身体模型（下标 = 性别）。索引 0/1 与 <see cref="GetBodyModelPath"/> 对应。</summary>
        private static readonly SkinnedModel[] BodyModelCache = new SkinnedModel[2];

        /// <summary>按资产路径缓存外观装备的全身模型（外观换装与性别无关，按路径复用）。</summary>
        private static readonly Dictionary<string, SkinnedModel> AppearanceBodyModelCache =
            new Dictionary<string, SkinnedModel>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 非阻塞请求任意路径的身体模型：已缓存则复用同一个句柄，未缓存则发起一次异步加载。
        /// 外观装备的模型不走性别缓存，避免"男/女共用同一条路径"时互相覆盖。
        /// </summary>
        public static SkinnedModel RequestBodyModelByPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;

            if (AppearanceBodyModelCache.TryGetValue(path, out var cached) && cached != null && !cached.LastLoadFailed)
                return cached;

            var asset = Content.LoadAsync<SkinnedModel>(MetaHuman.Runtime.MetaHumanAssetUtil.ResolveProjectPath(path));
            if (asset == null)
            {
                Debug.LogError($"[EquipmentDatabase] 外观身体模型请求失败: {path}");
                return null;
            }

            AppearanceBodyModelCache[path] = asset;
            return asset;
        }

        /// <summary>
        /// 阻塞获取任意路径的身体模型（最长等待 <paramref name="timeoutMs"/> 毫秒）。
        /// 只应在"换装必须立刻生效"的路径调用；UI 交互请先 <see cref="RequestBodyModelByPath"/> 预加载。
        /// </summary>
        public static SkinnedModel GetBodyModelByPath(string path, double timeoutMs = 5000.0)
        {
            var asset = RequestBodyModelByPath(path);
            if (asset == null) return null;

            if (!asset.IsLoaded)
                asset.WaitForLoaded(timeoutMs);

            if (!asset.IsLoaded)
            {
                Debug.LogError($"[EquipmentDatabase] 外观身体模型加载失败或超时: {path}");
                return null;
            }

            return asset;
        }

        /// <summary>
        /// 按需加载并缓存指定性别的身体模型：**只按路径**。
        /// <para>
        /// <b>会阻塞游戏线程</b>（<c>WaitForLoaded</c> 最长 30 秒），只适合"进世界前必须拿到资产"
        /// 的一次性路径。UI 交互里（选角列表换人）请改用 <see cref="RequestBodyModel"/>，
        /// 否则一次点击就把主线程钉住，同时资产与渲染在同帧交错，表现是模型发糊并叠出虚影。
        /// </para>
        /// </summary>
        /// <param name="gender">0 = 男，1 = 女</param>
        public static SkinnedModel GetBodyModel(int gender)
        {
            int slot = gender == 1 ? 1 : 0;

            var cached = BodyModelCache[slot];
            if (cached != null)
            {
                if (cached.IsLoaded) return cached;
                cached.WaitForLoaded(30000.0);
                if (cached.IsLoaded) return cached;
            }

            var path = GetBodyModelPath(gender);
            var asset = Content.LoadAsync<SkinnedModel>(MetaHuman.Runtime.MetaHumanAssetUtil.ResolveProjectPath(path));
            if (asset != null && asset.WaitForLoaded(30000.0) && asset.IsLoaded)
            {
                BodyModelCache[slot] = asset;
                return asset;
            }

            // 不再按 GUID 兜底：GUID 会随重导入变掉，而注册表里的旧 ID 会让引擎去开这个文件、
            // 发现头对不上后报 "Cannot find asset"，白等一次超时（2026-10-16 实测）。
            Debug.LogError($"[EquipmentDatabase] 无法加载身体模型（性别 {gender}）: {path}");
            return null;
        }

        /// <summary>
        /// 非阻塞请求身体模型：已加载就直接返回；未加载则发起一次异步加载并立刻返回句柄，
        /// 由调用方自行轮询 <see cref="Asset.IsLoaded"/>。
        /// <para>
        /// 幂等要点：同一性别最多只有一个在途请求——重复调用只会拿到<b>同一个</b>句柄，
        /// 不会再发第二次 <c>LoadAsync</c>。缺了这条，快速连点选角列表就会并发多个加载，
        /// 资产/骨骼数据在各帧分别就绪，渲染端表现为叠出虚影 + 停在低 mip 的糊面。
        /// </para>
        /// </summary>
        public static SkinnedModel RequestBodyModel(int gender)
        {
            int slot = gender == 1 ? 1 : 0;

            var cached = BodyModelCache[slot];
            if (cached != null && !cached.LastLoadFailed)
                return cached; // 已加载或仍在途，都复用同一个请求

            var path = GetBodyModelPath(gender);
            var asset = Content.LoadAsync<SkinnedModel>(MetaHuman.Runtime.MetaHumanAssetUtil.ResolveProjectPath(path));

            if (asset == null)
            {
                Debug.LogError($"[EquipmentDatabase] 身体模型请求失败（性别 {gender}）: {path}");
                return null;
            }

            BodyModelCache[slot] = asset;
            return asset;
        }

        /// <summary>
        /// 加载并缓存默认（男性）身体模型。
        /// </summary>
        public static SkinnedModel GetDefaultBodyModel()
        {
            return GetBodyModel(0);
        }

        /// <summary>
        /// 默认衣服
        /// </summary>
        public static readonly EquipmentData DefaultBody = new EquipmentData
        {
            Id = 10001,
            Name = "默认衣服",
            IconPath = "Content/Icons/Equipment/DefaultBody.flax",
            Type = EquipmentType.Body,
            Slot = EquipmentSlot.Body,
            // BodyModel 延迟加载：避免在类型静态初始化时访问 Content 系统
            BodyModel = null,
            AttachmentBoneName = null,
            Description = "新手初始衣物，提供基础防护。",
            BaseStats = new Dictionary<string, float>
            {
                { "Defense", 10f },
                { "HP", 50f }
            },
            WuxingBonus = new Dictionary<WuxingElement, int>
            {
                { WuxingElement.Earth, 20 }
            },
            // 该行五行战斗属性：与服务端物品模板 base_attributes 的同名键对齐，两端必须算出同一个数
            WuxingDefense = new Dictionary<WuxingElement, int>
            {
                { WuxingElement.Earth, 20 }
            },
            RequiredLevel = 1,
            Quality = 1,
            ItemLevel = 5
        };

        /// <summary>
        /// 默认长剑
        /// </summary>
        public static readonly EquipmentData DefaultLongsword = new EquipmentData
        {
            Id = 20001,
            Name = "默认长剑",
            IconPath = "Content/Icons/Equipment/DefaultLongsword.flax",
            Type = EquipmentType.Weapon,
            Slot = EquipmentSlot.RightHand,
            StaticMesh = null,
            AttachmentBoneName = "RightHand",
            Description = "新手练习用长剑，锋锐不足但足以防身。",
            BaseStats = new Dictionary<string, float>
            {
                { "Attack", 15f }
            },
            WuxingBonus = new Dictionary<WuxingElement, int>
            {
                { WuxingElement.Metal, 30 }
            },
            // 该行五行战斗属性：与服务端物品模板 base_attributes 的同名键对齐，两端必须算出同一个数
            WuxingAttack = new Dictionary<WuxingElement, int>
            {
                { WuxingElement.Metal, 30 }
            },
            RequiredLevel = 1,
            Quality = 2,
            ItemLevel = 8
        };

        /// <summary>
        /// 默认头巾
        /// </summary>
        public static readonly EquipmentData DefaultHeadScarf = new EquipmentData
        {
            Id = 30001,
            Name = "默认头巾",
            IconPath = "Content/Icons/Equipment/DefaultHeadScarf.flax",
            Type = EquipmentType.Accessory,
            Slot = EquipmentSlot.Head,
            StaticMesh = null,
            AttachmentBoneName = "Head",
            Description = "朴素头巾，可略微提升内力恢复。",
            BaseStats = new Dictionary<string, float>
            {
                { "MP", 30f },
                { "Defense", 5f }
            },
            WuxingBonus = new Dictionary<WuxingElement, int>
            {
                { WuxingElement.Wood, 15 }
            },
            // 该行五行战斗属性：与服务端物品模板 base_attributes 的同名键对齐，两端必须算出同一个数
            WuxingPenetration = new Dictionary<WuxingElement, int>
            {
                { WuxingElement.Wood, 15 }
            },
            RequiredLevel = 1,
            Quality = 1,
            ItemLevel = 5
        };

        /// <summary>
        /// 默认项链
        /// </summary>
        public static readonly EquipmentData DefaultNecklace = new EquipmentData
        {
            Id = 10002,
            Name = "新手项链",
            IconPath = "Content/Icons/Equipment/DefaultNecklace.flax",
            Type = EquipmentType.Accessory,
            Slot = EquipmentSlot.Neck,
            StaticMesh = null,
            AttachmentBoneName = null,
            Description = "朴素的木质项链，可略微提升内力恢复。",
            BaseStats = new Dictionary<string, float>
            {
                { "MP", 20f }
            },
            WuxingBonus = new Dictionary<WuxingElement, int>
            {
                { WuxingElement.Wood, 10 }
            },
            // 该行五行战斗属性：与服务端物品模板 base_attributes 的同名键对齐，两端必须算出同一个数
            WuxingPenetration = new Dictionary<WuxingElement, int>
            {
                { WuxingElement.Wood, 10 }
            },
            RequiredLevel = 1,
            Quality = 1,
            ItemLevel = 5
        };

        /// <summary>
        /// 默认护肩
        /// </summary>
        public static readonly EquipmentData DefaultShoulderGuard = new EquipmentData
        {
            Id = 10003,
            Name = "新手护肩",
            IconPath = "Content/Icons/Equipment/DefaultShoulderGuard.flax",
            Type = EquipmentType.Accessory,
            Slot = EquipmentSlot.Shoulder,
            StaticMesh = null,
            AttachmentBoneName = null,
            Description = "皮质护肩，能缓冲部分冲击。",
            BaseStats = new Dictionary<string, float>
            {
                { "Defense", 5f },
                { "HP", 20f }
            },
            WuxingBonus = new Dictionary<WuxingElement, int>
            {
                { WuxingElement.Earth, 10 }
            },
            // 该行五行战斗属性：与服务端物品模板 base_attributes 的同名键对齐，两端必须算出同一个数
            WuxingPenetration = new Dictionary<WuxingElement, int>
            {
                { WuxingElement.Earth, 10 }
            },
            RequiredLevel = 1,
            Quality = 1,
            ItemLevel = 5
        };

        /// <summary>
        /// 默认披风
        /// </summary>
        public static readonly EquipmentData DefaultCloak = new EquipmentData
        {
            Id = 10004,
            Name = "新手披风",
            IconPath = "Content/Icons/Equipment/DefaultCloak.flax",
            Type = EquipmentType.Accessory,
            Slot = EquipmentSlot.Back,
            StaticMesh = null,
            AttachmentBoneName = null,
            Description = "粗糙的亚麻披风，可遮挡风尘。",
            BaseStats = new Dictionary<string, float>
            {
                { "Defense", 3f }
            },
            WuxingBonus = new Dictionary<WuxingElement, int>
            {
                { WuxingElement.Water, 10 }
            },
            // 该行五行战斗属性：与服务端物品模板 base_attributes 的同名键对齐，两端必须算出同一个数
            WuxingPenetration = new Dictionary<WuxingElement, int>
            {
                { WuxingElement.Water, 10 }
            },
            RequiredLevel = 1,
            Quality = 1,
            ItemLevel = 5
        };

        /// <summary>
        /// 默认腰带
        /// </summary>
        public static readonly EquipmentData DefaultBelt = new EquipmentData
        {
            Id = 10005,
            Name = "新手腰带",
            IconPath = "Content/Icons/Equipment/DefaultBelt.flax",
            Type = EquipmentType.Accessory,
            Slot = EquipmentSlot.Waist,
            StaticMesh = null,
            AttachmentBoneName = null,
            Description = "结实的布腰带，可悬挂随身物品。",
            BaseStats = new Dictionary<string, float>
            {
                { "HP", 30f }
            },
            WuxingBonus = new Dictionary<WuxingElement, int>
            {
                { WuxingElement.Earth, 10 }
            },
            // 该行五行战斗属性：与服务端物品模板 base_attributes 的同名键对齐，两端必须算出同一个数
            WuxingPenetration = new Dictionary<WuxingElement, int>
            {
                { WuxingElement.Earth, 10 }
            },
            RequiredLevel = 1,
            Quality = 1,
            ItemLevel = 5
        };

        /// <summary>
        /// 默认长裤
        /// </summary>
        public static readonly EquipmentData DefaultLeggings = new EquipmentData
        {
            Id = 10006,
            Name = "新手长裤",
            IconPath = "Content/Icons/Equipment/DefaultLeggings.flax",
            Type = EquipmentType.Body,
            Slot = EquipmentSlot.Legs,
            BodyModel = null,
            AttachmentBoneName = null,
            Description = "耐穿的布裤，适合长途跋涉。",
            BaseStats = new Dictionary<string, float>
            {
                { "Defense", 6f },
                { "HP", 20f }
            },
            WuxingBonus = new Dictionary<WuxingElement, int>
            {
                { WuxingElement.Earth, 15 }
            },
            // 该行五行战斗属性：与服务端物品模板 base_attributes 的同名键对齐，两端必须算出同一个数
            WuxingDefense = new Dictionary<WuxingElement, int>
            {
                { WuxingElement.Earth, 15 }
            },
            RequiredLevel = 1,
            Quality = 1,
            ItemLevel = 5
        };

        /// <summary>
        /// 默认布鞋
        /// </summary>
        public static readonly EquipmentData DefaultShoes = new EquipmentData
        {
            Id = 10007,
            Name = "新手布鞋",
            IconPath = "Content/Icons/Equipment/DefaultShoes.flax",
            Type = EquipmentType.Body,
            Slot = EquipmentSlot.Feet,
            BodyModel = null,
            AttachmentBoneName = null,
            Description = "轻便的布鞋，行走时悄无声息。",
            BaseStats = new Dictionary<string, float>
            {
                { "Defense", 4f }
            },
            WuxingBonus = new Dictionary<WuxingElement, int>
            {
                { WuxingElement.Water, 10 }
            },
            // 该行五行战斗属性：与服务端物品模板 base_attributes 的同名键对齐，两端必须算出同一个数
            WuxingDefense = new Dictionary<WuxingElement, int>
            {
                { WuxingElement.Water, 10 }
            },
            RequiredLevel = 1,
            Quality = 1,
            ItemLevel = 5
        };

        /// <summary>
        /// 默认短剑
        /// </summary>
        public static readonly EquipmentData DefaultDagger = new EquipmentData
        {
            Id = 10008,
            Name = "新手短剑",
            IconPath = "Content/Icons/Equipment/DefaultDagger.flax",
            Type = EquipmentType.Weapon,
            Slot = EquipmentSlot.LeftHand,
            StaticMesh = null,
            AttachmentBoneName = "LeftHand",
            Description = "新手练习用短剑，灵活轻巧。",
            BaseStats = new Dictionary<string, float>
            {
                { "Attack", 10f }
            },
            WuxingBonus = new Dictionary<WuxingElement, int>
            {
                { WuxingElement.Metal, 20 }
            },
            // 该行五行战斗属性：与服务端物品模板 base_attributes 的同名键对齐，两端必须算出同一个数
            WuxingAttack = new Dictionary<WuxingElement, int>
            {
                { WuxingElement.Metal, 20 }
            },
            RequiredLevel = 1,
            Quality = 2,
            ItemLevel = 8
        };

        /// <summary>
        /// 默认右戒指
        /// </summary>
        public static readonly EquipmentData DefaultRightRing = new EquipmentData
        {
            Id = 10009,
            Name = "新手右戒指",
            IconPath = "Content/Icons/Equipment/DefaultRightRing.flax",
            Type = EquipmentType.Accessory,
            Slot = EquipmentSlot.RightRing,
            StaticMesh = null,
            AttachmentBoneName = null,
            Description = "铁环戒指，微微提升力量。",
            BaseStats = new Dictionary<string, float>
            {
                { "Attack", 3f }
            },
            WuxingBonus = new Dictionary<WuxingElement, int>
            {
                { WuxingElement.Metal, 10 }
            },
            // 该行五行战斗属性：与服务端物品模板 base_attributes 的同名键对齐，两端必须算出同一个数
            WuxingPenetration = new Dictionary<WuxingElement, int>
            {
                { WuxingElement.Metal, 10 }
            },
            RequiredLevel = 1,
            Quality = 1,
            ItemLevel = 5
        };

        /// <summary>
        /// 默认左戒指
        /// </summary>
        public static readonly EquipmentData DefaultLeftRing = new EquipmentData
        {
            Id = 10010,
            Name = "新手左戒指",
            IconPath = "Content/Icons/Equipment/DefaultLeftRing.flax",
            Type = EquipmentType.Accessory,
            Slot = EquipmentSlot.LeftRing,
            StaticMesh = null,
            AttachmentBoneName = null,
            Description = "木质戒指，蕴含一丝自然灵气。",
            BaseStats = new Dictionary<string, float>
            {
                { "MP", 15f }
            },
            WuxingBonus = new Dictionary<WuxingElement, int>
            {
                { WuxingElement.Wood, 10 }
            },
            // 该行五行战斗属性：与服务端物品模板 base_attributes 的同名键对齐，两端必须算出同一个数
            WuxingPenetration = new Dictionary<WuxingElement, int>
            {
                { WuxingElement.Wood, 10 }
            },
            RequiredLevel = 1,
            Quality = 1,
            ItemLevel = 5
        };

        /// <summary>
        /// 默认右手腕
        /// </summary>
        public static readonly EquipmentData DefaultRightWrist = new EquipmentData
        {
            Id = 10011,
            Name = "新手右手腕",
            IconPath = "Content/Icons/Equipment/DefaultRightWrist.flax",
            Type = EquipmentType.Accessory,
            Slot = EquipmentSlot.RightWrist,
            StaticMesh = null,
            AttachmentBoneName = null,
            Description = "皮质护腕，保护右手腕关节。",
            BaseStats = new Dictionary<string, float>
            {
                { "Defense", 4f }
            },
            WuxingBonus = new Dictionary<WuxingElement, int>
            {
                { WuxingElement.Earth, 10 }
            },
            // 该行五行战斗属性：与服务端物品模板 base_attributes 的同名键对齐，两端必须算出同一个数
            WuxingPenetration = new Dictionary<WuxingElement, int>
            {
                { WuxingElement.Earth, 10 }
            },
            RequiredLevel = 1,
            Quality = 1,
            ItemLevel = 5
        };

        /// <summary>
        /// 默认左手腕
        /// </summary>
        public static readonly EquipmentData DefaultLeftWrist = new EquipmentData
        {
            Id = 10012,
            Name = "新手左手腕",
            IconPath = "Content/Icons/Equipment/DefaultLeftWrist.flax",
            Type = EquipmentType.Accessory,
            Slot = EquipmentSlot.LeftWrist,
            StaticMesh = null,
            AttachmentBoneName = null,
            Description = "布质护腕，便于左手灵活施法。",
            BaseStats = new Dictionary<string, float>
            {
                { "MP", 10f }
            },
            WuxingBonus = new Dictionary<WuxingElement, int>
            {
                { WuxingElement.Water, 10 }
            },
            // 该行五行战斗属性：与服务端物品模板 base_attributes 的同名键对齐，两端必须算出同一个数
            WuxingPenetration = new Dictionary<WuxingElement, int>
            {
                { WuxingElement.Water, 10 }
            },
            RequiredLevel = 1,
            Quality = 1,
            ItemLevel = 5
        };

        /// <summary>
        /// 默认面具
        /// </summary>
        public static readonly EquipmentData DefaultMask = new EquipmentData
        {
            Id = 10013,
            Name = "新手面具",
            IconPath = "Content/Icons/Equipment/DefaultMask.flax",
            Type = EquipmentType.Accessory,
            Slot = EquipmentSlot.Face,
            StaticMesh = null,
            AttachmentBoneName = "Head",
            Description = "木制面具，可威慑弱小的野兽。",
            BaseStats = new Dictionary<string, float>
            {
                { "Attack", 2f },
                { "Defense", 2f }
            },
            WuxingBonus = new Dictionary<WuxingElement, int>
            {
                { WuxingElement.Fire, 10 }
            },
            // 该行五行战斗属性：与服务端物品模板 base_attributes 的同名键对齐，两端必须算出同一个数
            WuxingPenetration = new Dictionary<WuxingElement, int>
            {
                { WuxingElement.Fire, 10 }
            },
            RequiredLevel = 1,
            Quality = 1,
            ItemLevel = 5
        };

        /// <summary>
        /// 粗布小包
        /// </summary>
        public static readonly EquipmentData SmallClothBag = new EquipmentData
        {
            Id = 2001,
            Name = "粗布小包",
            IconPath = "Content/Icons/Equipment/SmallClothBag.flax",
            Type = EquipmentType.Bag,
            Slot = EquipmentSlot.Back,
            StaticMesh = null,
            AttachmentBoneName = null,
            Description = "粗布缝制的小型背包，可扩展 6 个格子。",
            BaseStats = new Dictionary<string, float>
            {
                { "HP", 10f }
            },
            WuxingBonus = new Dictionary<WuxingElement, int>
            {
                { WuxingElement.Earth, 5 }
            },
            RequiredLevel = 1,
            Quality = 0,
            ItemLevel = 5,
            ExtraSlots = 6
        };

        /// <summary>
        /// 皮革中包
        /// </summary>
        public static readonly EquipmentData MediumLeatherBag = new EquipmentData
        {
            Id = 2002,
            Name = "皮革中包",
            IconPath = "Content/Icons/Equipment/MediumLeatherBag.flax",
            Type = EquipmentType.Bag,
            Slot = EquipmentSlot.Back,
            StaticMesh = null,
            AttachmentBoneName = null,
            Description = "坚韧皮革缝制的中型背包，可扩展 12 个格子。",
            BaseStats = new Dictionary<string, float>
            {
                { "HP", 20f },
                { "Defense", 3f }
            },
            WuxingBonus = new Dictionary<WuxingElement, int>
            {
                { WuxingElement.Earth, 10 }
            },
            RequiredLevel = 10,
            Quality = 1,
            ItemLevel = 15,
            ExtraSlots = 12
        };

        /// <summary>
        /// 丝绸大包
        /// </summary>
        public static readonly EquipmentData LargeSilkBag = new EquipmentData
        {
            Id = 2003,
            Name = "丝绸大包",
            IconPath = "Content/Icons/Equipment/LargeSilkBag.flax",
            Type = EquipmentType.Bag,
            Slot = EquipmentSlot.Back,
            StaticMesh = null,
            AttachmentBoneName = null,
            Description = "丝绸织就的大型背包，轻便而结实，可扩展 18 个格子。",
            BaseStats = new Dictionary<string, float>
            {
                { "HP", 30f },
                { "Defense", 6f }
            },
            WuxingBonus = new Dictionary<WuxingElement, int>
            {
                { WuxingElement.Wood, 15 }
            },
            RequiredLevel = 30,
            Quality = 2,
            ItemLevel = 35,
            ExtraSlots = 18
        };

        /// <summary>
        /// 龙皮巨包
        /// </summary>
        public static readonly EquipmentData HugeDragonBag = new EquipmentData
        {
            Id = 2004,
            Name = "龙皮巨包",
            IconPath = "Content/Icons/Equipment/HugeDragonBag.flax",
            Type = EquipmentType.Bag,
            Slot = EquipmentSlot.Back,
            StaticMesh = null,
            AttachmentBoneName = null,
            Description = "以龙皮缝制的巨型背包，蕴含龙气，可扩展 20 个格子。",
            BaseStats = new Dictionary<string, float>
            {
                { "HP", 50f },
                { "Defense", 10f }
            },
            WuxingBonus = new Dictionary<WuxingElement, int>
            {
                { WuxingElement.Fire, 20 }
            },
            RequiredLevel = 50,
            Quality = 3,
            ItemLevel = 60,
            ExtraSlots = 20
        };

        /// <summary>
        /// 男性外观「玄墨劲装」：收窄身形，表现轻装。
        /// <para>
        /// 外观装备独立占用 <see cref="EquipmentSlot.Appearance"/>，与战斗装备（身体/配饰/武器）
        /// 互不挤占；穿上后由 <c>CharacterEquipmentManager</c> 按
        /// <see cref="EquipmentData.BodyScale"/> 重设整套身形，脱下则恢复默认比例。
        /// </para>
        /// </summary>
        public static readonly EquipmentData AppearanceXuanmo = new EquipmentData
        {
            Id = 91001,
            Name = "玄墨劲装",
            IconPath = "Content/Icons/Equipment/DefaultBody.flax",
            Type = EquipmentType.Appearance,
            Slot = EquipmentSlot.Appearance,
            RequiredGender = 0,
            BodyScale = new Vector3(0.94f, 0.99f, 0.94f),
            Description = "整套外观：玄墨劲装，身形轻盈利落。仅改变角色外形，不提供战斗属性。",
            RequiredLevel = 1,
            Quality = 2,
            ItemLevel = 1
        };

        /// <summary>男性外观「玄铁重铠」：放大身形，表现重甲。</summary>
        public static readonly EquipmentData AppearanceXuantie = new EquipmentData
        {
            Id = 91002,
            Name = "玄铁重铠",
            IconPath = "Content/Icons/Equipment/DefaultBody.flax",
            Type = EquipmentType.Appearance,
            Slot = EquipmentSlot.Appearance,
            RequiredGender = 0,
            BodyScale = new Vector3(1.12f, 1.04f, 1.12f),
            Description = "整套外观：玄铁重铠，身披重甲格外魁梧。仅改变角色外形，不提供战斗属性。",
            RequiredLevel = 1,
            Quality = 3,
            ItemLevel = 1
        };

        /// <summary>女性外观「流云霓裳」，规则同 <see cref="AppearanceXuanmo"/>。</summary>
        public static readonly EquipmentData AppearanceLiuyun = new EquipmentData
        {
            Id = 91003,
            Name = "流云霓裳",
            IconPath = "Content/Icons/Equipment/DefaultBody.flax",
            Type = EquipmentType.Appearance,
            Slot = EquipmentSlot.Appearance,
            RequiredGender = 1,
            BodyScale = new Vector3(0.95f, 0.98f, 0.95f),
            Description = "整套外观：流云霓裳，衣袂随身形轻展。仅改变角色外形，不提供战斗属性。",
            RequiredLevel = 1,
            Quality = 2,
            ItemLevel = 1
        };

        /// <summary>女性外观「素雪罗裙」：略高挑的裙装轮廓。</summary>
        public static readonly EquipmentData AppearanceSuxue = new EquipmentData
        {
            Id = 91004,
            Name = "素雪罗裙",
            IconPath = "Content/Icons/Equipment/DefaultBody.flax",
            Type = EquipmentType.Appearance,
            Slot = EquipmentSlot.Appearance,
            RequiredGender = 1,
            BodyScale = new Vector3(1.06f, 1.03f, 1.06f),
            Description = "整套外观：素雪罗裙，身形高挑。仅改变角色外形，不提供战斗属性。",
            RequiredLevel = 1,
            Quality = 3,
            ItemLevel = 1
        };

        /// <summary>所有外观装备（性别过滤由调用方按 <see cref="EquipmentData.RequiredGender"/> 处理）。</summary>
        private static readonly EquipmentData[] AppearanceEquipments =
        {
            AppearanceXuanmo, AppearanceXuantie, AppearanceLiuyun, AppearanceSuxue
        };

        /// <summary>
        /// 获取指定性别可用的外观装备。<paramref name="gender"/> 为 -1 时返回全部。
        /// </summary>
        public static List<EquipmentData> GetAppearanceEquipments(int gender = -1)
        {
            var result = new List<EquipmentData>();
            foreach (var item in AppearanceEquipments)
            {
                if (gender >= 0 && item.RequiredGender >= 0 && item.RequiredGender != gender)
                    continue;
                result.Add(item);
            }
            return result;
        }

        /// <summary>
        /// 根据装备ID获取装备数据
        /// </summary>
        /// <param name="id">装备ID</param>
        /// <returns>装备数据，未找到则返回 null</returns>
        public static EquipmentData GetEquipment(int id)
        {
            return id switch
            {
                10001 => DefaultBody,
                10002 => DefaultNecklace,
                10003 => DefaultShoulderGuard,
                10004 => DefaultCloak,
                10005 => DefaultBelt,
                10006 => DefaultLeggings,
                10007 => DefaultShoes,
                10008 => DefaultDagger,
                10009 => DefaultRightRing,
                10010 => DefaultLeftRing,
                10011 => DefaultRightWrist,
                10012 => DefaultLeftWrist,
                10013 => DefaultMask,
                2001 => SmallClothBag,
                2002 => MediumLeatherBag,
                2003 => LargeSilkBag,
                2004 => HugeDragonBag,
                20001 => DefaultLongsword,
                30001 => DefaultHeadScarf,
                91001 => AppearanceXuanmo,
                91002 => AppearanceXuantie,
                91003 => AppearanceLiuyun,
                91004 => AppearanceSuxue,
                _ => null
            };
        }

        /// <summary>
        /// 获取所有装备数据
        /// </summary>
        /// <returns>所有装备列表</returns>
        public static List<EquipmentData> GetAllEquipments()
        {
            return new List<EquipmentData>
            {
                DefaultBody,
                DefaultNecklace,
                DefaultShoulderGuard,
                DefaultCloak,
                DefaultBelt,
                DefaultLeggings,
                DefaultShoes,
                DefaultDagger,
                DefaultRightRing,
                DefaultLeftRing,
                DefaultRightWrist,
                DefaultLeftWrist,
                DefaultMask,
                DefaultLongsword,
                DefaultHeadScarf,
                SmallClothBag,
                MediumLeatherBag,
                LargeSilkBag,
                HugeDragonBag,
                AppearanceXuanmo,
                AppearanceXuantie,
                AppearanceLiuyun,
                AppearanceSuxue
            };
        }

        /// <summary>
        /// 根据装备槽位获取装备数据
        /// </summary>
        /// <param name="slot">装备槽位</param>
        /// <returns>该槽位下的装备列表</returns>
        public static List<EquipmentData> GetEquipmentsBySlot(EquipmentSlot slot)
        {
            return GetAllEquipments().Where(e => e.Slot == slot).ToList();
        }
    }
}
