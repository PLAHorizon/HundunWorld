using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Horizon.Core.Abstract.Enums
{
    /// <summary>
    /// 处方单检查项
    /// </summary>
    public enum PrescritpionItem
    {
        /// <summary>
        /// 戴镜史
        /// </summary>
        [Description("戴镜史")]
        PHWG = 0,
        /// <summary>
        /// 屈光检查
        /// </summary>
        [Description("屈光检查")]
        RefractiveExamination = 1,
        /// <summary>
        /// 双眼视功能检查
        /// </summary>
        [Description("双眼视功能检查")]
        BinocularVisual = 2,
        /// <summary>
        /// 角膜接触镜复查
        /// </summary>
        [Description("角膜接触镜复查")]
        CClensReExamination = 6,
        /// <summary>
        /// 检查结论
        /// </summary>
        [Description("检查结论")]
        CheckConclusion = 5,
        /// <summary>
        /// 接触镜评估
        /// </summary>
        [Description("接触镜评估")]
        ClensEvaluation = 4,
        /// <summary>
        /// 相关检查，特殊功能检查
        /// </summary>
        [Description("相关检查")]
        SpecialCheck = 3,
    }
    /// <summary>
    /// 处方状态
    /// </summary>
    public enum PrescriptionStatus
    {
        /// <summary>
        /// 完成
        /// </summary>
        [Description("完成")]
        Complete = 0,
        /// <summary>
        /// 进行中
        /// </summary>
        [Description("进行中")]
        Process = 1,
        /// <summary>
        /// 未完成
        /// </summary>
        [Description("未完成")]
        Unfinished = 2,
        /// <summary>
        /// 未开始
        /// </summary>
        [Description("未开始")]
        Create = -1,
    }

    /// <summary>
    /// 双眼视功能检查常规项类型
    /// </summary>
    public enum BVCNormalType
    {
        /// <summary>
        /// 远
        /// </summary>
        [Description("远")]
        Far = 0,
        /// <summary>
        /// 近
        /// </summary>
        [Description("近")] Near = 1,
        /// <summary>
        /// 中
        /// </summary>
        [Description("中")] Middle = 2,
        /// <summary>
        /// 手机端通用形式数据
        /// </summary>
        [Description("全部")] All = 3,
    }

    /// <summary>
    /// 镜片材质
    /// </summary>
    public enum LensMaterial
    {
        /// <summary>
        /// 老花渐进
        /// </summary>
        [Description("老花渐进")]
        ProgressivePresbyopia = 0,
        /// <summary>
        /// 青少年渐进
        /// </summary>
        [Description("青少年渐进")]
        YoungstersadvanceGradually = 1,
        /// <summary>
        /// 非球面树脂
        /// </summary>
        [Description("非球面树脂")]
        AsphericSurface = 2,
        /// <summary>
        /// 抗辐射眼镜片
        /// </summary>
        [Description("抗辐射眼镜片")]
        RadioresistanceRadiationHardening = 3,
        /// <summary>
        /// 染色镜片
        /// </summary>
        [Description("染色镜片")]
        TlongedLenses = 4,
        /// <summary>
        /// 变色镜片
        /// </summary>
        [Description("变色镜片")]
        PhotochromicLens = 5,
        /// <summary>
        /// 普通树脂
        /// </summary>
        [Description("普通树脂")]
        OrdinaryResin = 6,
        /// <summary>
        /// 高折玻璃
        /// </summary>
        [Description("高折玻璃")]
        HighBrokenGlass = 7,
        /// <summary>
        /// 玻璃片
        /// </summary>
        [Description("玻璃片")]
        GlassSheet = 8,
        /// <summary>
        /// 树脂偏光镜片（茶/灰）
        /// </summary>
        [Description("树脂偏光镜片（茶/灰）")]
        ResinPolarizingLenses = 9,
        /// <summary>
        /// 树脂抗疲劳镜片
        /// </summary>
        [Description("树脂抗疲劳镜片")]
        ResinAntiFatigueLens = 10,
        /// <summary>
        /// 双光片
        /// </summary>
        [Description("双光片")]
        DoubleLightFilm = 11
    }

    /// <summary>
    /// 镜片类型
    /// </summary>
    public enum LensType
    {
        /// <summary>
        /// 老花渐进
        /// </summary>
        [Description("老花渐进")]
        ProgressivePresbyopia = 0,
        /// <summary>
        /// 青少年渐进
        /// </summary>
        [Description("青少年渐进")]
        YoungstersadvanceGradually = 1,
        /// <summary>
        /// 远用框架
        /// </summary>
        [Description("远用框架")]
        RemoteFrame = 2,
        /// <summary>
        /// 近用框架
        /// </summary>
        [Description("近用框架")]
        NearFrame = 3,
        /// <summary>
        /// 双光框架
        /// </summary>
        [Description("双光框架")]
        TwoBeamFrame = 4
    }

    /// <summary>
    /// 镜片颜色
    /// </summary>
    public enum LensColor
    {
        /// <summary>
        /// 变茶
        /// </summary>
        /// 
        [Description("变茶")]
        ChangeOfTea = 0,
        /// <summary>
        /// 变灰
        /// </summary>
        [Description("变灰")]
        Gray = 1,
        /// <summary>
        /// 染色
        /// </summary>
        [Description("染色")]
        Dyeing = 2,
        /// <summary>
        /// 树脂偏光镜片（茶/灰
        /// </summary>
        [Description("树脂偏光镜片（茶/灰）")]
        ResinPolarizingLenses = 3,
        /// <summary>
        /// 树脂抗疲劳镜片
        /// </summary>
        [Description("树脂抗疲劳镜片")]
        ResinAntiFatigueLens = 4
    }

    /// <summary>
    /// 隐形材质
    /// </summary>
    public enum StealthMaterial
    {
        /// <summary>
        /// 硅弹镜软镜
        /// </summary>
        [Description("硅弹镜软镜")]
        SiliconMirrorFlexibleMirror = 0,
        /// <summary>
        /// 透气性硬性接触镜
        /// </summary>
        [Description("透气性硬性接触镜")]
        GasPermeableRigidContactLens = 1,
        /// <summary>
        /// 水凝胶软镜
        /// </summary>
        [Description("水凝胶软镜")]
        HydrogelSoftMirror = 2,
        /// <summary>
        /// 硅水凝胶软镜
        /// </summary>
        [Description("硅水凝胶软镜")]
        SiliconeHydrogelSoftMirror = 3

    }
    /// <summary>
    /// 隐形产品
    /// </summary>
    public enum InvisibleProducts
    {
        /// <summary>
        /// 强生
        /// </summary>
        [Description("强生")]
        Johnson = 0,
        /// <summary>
        /// 视康
        /// </summary>
        [Description("视康")]
        CIBAVision = 1,
        /// <summary>
        /// 卫康
        /// </summary>
        [Description("卫康")]
        WeiKang = 2,
        /// <summary>
        /// 博士伦
        /// </summary>
        [Description("博士伦")]
        BauschLomb = 3,
        /// <summary>
        /// 海昌
        /// </summary>
        [Description("海昌")]
        Haichang = 4,
        /// <summary>
        /// 富士伦
        /// </summary>
        [Description("富士伦")]
        FujiLun = 5,
        /// <summary>
        /// 菲士康
        /// </summary>
        [Description("菲士康")]
        FreshKon = 6,
        /// <summary>
        /// 艾爵
        /// </summary>
        [Description("艾爵")]
        Igel = 7,
        /// <summary>
        /// 晶视佳
        /// </summary>
        [Description("晶视佳")]
        CrystallineVision = 8
    }
    /// <summary>
    /// 隐形镜片颜色
    /// </summary>
    public enum ContactLensColor
    {
        /// <summary>
        /// 彩色
        /// </summary>
        [Description("彩色")]
        colour = 0,
        /// <summary>
        /// 无色
        /// </summary>
        [Description("无色")]
        Colourless = 1,
        /// <summary>
        /// 浅蓝色
        /// </summary>
        [Description("浅蓝色")]
        Wathet = 2,
        /// <summary>
        /// 淡绿色
        /// </summary>
        [Description("淡绿色")]
        PaleGreen = 3,
        /// <summary>
        /// 黑色
        /// </summary>
        [Description("黑色")]
        Black = 4,
        /// <summary>
        /// 棕色
        /// </summary>
        [Description("棕色")]
        Brown = 5
    }
    /// <summary>
    /// 左眼右眼基底
    /// </summary>
    public enum BaseODOS
    {
        /// <summary>
        /// 内
        /// </summary>
        [Description("内")]
        Within = 0,
        /// <summary>
        /// 外
        /// </summary>
        [Description("外")]
        Abroad = 1,
        /// <summary>
        /// 上
        /// </summary>
        [Description("上")]
        Upper = 2,
        /// <summary>
        /// 下
        /// </summary>
        [Description("下")]
        Lower = 3
    }
    /// <summary>
    /// 原配镜类型
    /// </summary>
    public enum EnumTheOriginalTypeOfGlasses
    {
        /// <summary>
        /// 无
        /// </summary>
        [Description("无")]
        Nothing = 0,
        /// <summary>
        /// 框架
        /// </summary>
        [Description("框架")]
        Frame = 1,
        /// <summary>
        /// 隐形眼镜
        /// </summary>
        [Description("隐形眼镜")]
        ContactLenses = 2
    }

    /// <summary>
    /// 主导眼
    /// </summary>
    public enum FloorManager
    {
        /// <summary>
        /// 左
        /// </summary>
        [Description("左")]
        Left = 0,
        /// <summary>
        /// 右
        /// </summary>
        [Description("右")]
        Right = 1,
        /// <summary>
        /// 交替
        /// </summary>
        [Description("交替")]
        Alternate = 2
    }

    /// <summary>
    /// 双眼平衡方法
    /// </summary>
    public enum BinocularBalanceMethod
    {
        /// <summary>
        /// 棱镜法
        /// </summary>
        [Description("棱镜法")]
        PrismMethod = 0,
        /// <summary>
        /// 偏振片法
        /// </summary>
        [Description("偏振片法")]
        PolarizerMethod = 1
    }

    /// <summary>
    /// 双眼终点红/绿试验
    /// </summary>
    public enum DoubleEyeRedGreenTest
    {
        /// <summary>
        /// 红大于绿
        /// </summary>
        [Description("红大于绿")]
        RedIsGreaterThanGreen = 0,
        /// <summary>
        /// 红等于绿
        /// </summary>
        [Description("红等于绿")]
        RedEqualsGreen = 1,
        /// <summary>
        /// 红小于绿
        /// </summary>
        [Description("红小于绿")]
        RedIsLessThanGreen = 2
    }
    /// <summary>
    ///交替遮盖试验
    /// </summary>
    public enum AlternateCoveringTest
    {
        /// <summary>
        ///正位
        /// </summary>
        [Description("正位")]
        Normotopia = 0,
        /// <summary>
        /// 微外动
        /// </summary>
        [Description("微外动")]
        MicroExternalMotion = 1,
        /// <summary>
        /// 微内动
        /// </summary>
        [Description("微内动")]
        MicrolongernalMovement = 2,
        /// <summary>
        /// 明显内动
        /// </summary>
        [Description("明显内动")]
        ApparentlongernalMotion = 3,
        /// <summary>
        /// 明显外动
        /// </summary>
        [Description("明显外动")]
        ApparentExternalMotion = 4,
        /// <summary>
        /// 在斜位不能回正
        /// </summary>
        [Description("在斜位不能回正")]
        ItCanNBReversedInObliquePosition = 5
    }

    /// <summary>
    ///WORTH 4 DOT 远
    /// </summary>
    public enum WORTHDOTFar
    {
        /// <summary>
        /// 2dots
        /// </summary>
        [Description("2dots")]
        TwoDots = 0,
        /// <summary>
        /// A196
        /// </summary>
        [Description("A196")]
        AOneHundredAndNinetysix = 1,
        /// <summary>
        /// 4dots
        /// </summary>
        [Description("4dots")]
        FourDots = 2,
        /// <summary>
        /// 5dots
        /// </summary>
        [Description("5dots")]
        FiveDots = 3
    }
    /// <summary>
    ///远:隐斜
    /// </summary>
    public enum DistalOblique
    {
        /// <summary>
        /// Bi
        /// </summary>
        [Description("BI")]
        BI = 0,
        /// <summary>
        /// Bo
        /// </summary>
        [Description("BO")]
        BO = 1
    }
    /// <summary>
    ///近:隐斜
    /// </summary>
    public enum NearOblique
    {
        /// <summary>
        /// Bu
        /// </summary>
        [Description("BU")]
        BU = 0,
        /// <summary>
        /// Bo
        /// </summary>
        [Description("BD")]
        BD = 1
    }
    /// <summary>
    /// 立体视
    /// </summary>
    public enum Stereopsis
    {
        /// <summary>
        /// 有
        /// </summary>
        [Description("有")]
        OK = 0,
        /// <summary>
        /// 无
        /// </summary>
        [Description("无")]
        Not = 1
    }
    /// <summary>
    ///调节灵敏度/ 双面镜度数
    /// </summary>
    public enum DoubleMirrorNumber
    {
        /// <summary>
        /// ±1.00
        /// </summary>
        [Description("±1.00")]
        One = 0,
        /// <summary>
        /// ±1.50
        /// </summary>
        [Description("±1.50")]
        OnePolongFivezero = 1,
        /// <summary>
        /// ±2.00
        /// </summary>
        [Description("±2.00")]
        Two = 3,
        /// <summary>
        /// ±2.50
        /// </summary>
        [Description("±2.50")]
        TwoPolongFivezero = 4
    }
    /// <summary>
    ///视标
    /// </summary>
    public enum AsTheStandard
    {
        /// <summary>
        /// 20/30
        /// </summary>
        [Description("20/30")]
        TwentyThirty = 0,
        /// <summary>
        /// 20/40
        /// </summary>
        [Description("20/40")]
        TwentyForty = 1,
        /// <summary>
        /// 20/50
        /// </summary>
        [Description("20/50")]
        TwentyFifty = 2
    }
    /// <summary>
    ///AddODOS
    /// </summary>
    public enum AddODOS
    {
        /// <summary>
        /// 0
        /// </summary>
        [Description("0")]
        Zero = 0,
        /// <summary>
        /// +0.25
        /// </summary>
        [Description("+0.25")]
        ZeroPolongTwoFive = 1,
        /// <summary>
        /// +0.50
        /// </summary>
        [Description("+0.50")]
        ZeroPolongFiveZero = 2,
        /// <summary>
        /// +0.75
        /// </summary>
        [Description("+0.75")]
        ZeroPolongSevenFive = 3,
        /// <summary>
        /// +1.00
        /// </summary>
        [Description("+1.00")]
        AddOne = 4,
        /// <summary>
        /// +1.25
        /// </summary>
        [Description("+1.25")]
        OnePolongTwoFive = 5,
        /// <summary>
        /// +1.50
        /// </summary>
        [Description("+1.50")]
        OnePolongFive = 6,
        /// <summary>
        /// +1.75
        /// </summary>
        [Description("+1.75")]
        OnePolongSevenFive = 7,
        /// <summary>
        /// +2.00
        /// </summary>
        [Description("+2.00")]
        AddTwo = 8,
        /// <summary>
        /// +2.25
        /// </summary>
        [Description("+2.25")]
        TwoPolongTwoFive = 9,
        /// <summary>
        /// +2.50
        /// </summary>
        [Description("+2.50")]
        TwoPolongFiveZero = 10,
        /// <summary>
        /// +2.75
        /// </summary>
        [Description("+2.75")]
        TwoPolongSevenFive = 11,
        /// <summary>
        /// +3.00
        /// </summary>
        [Description("+3.00")]
        AddThree = 12,
        /// <summary>
        /// +3.25
        /// </summary>
        [Description("+3.25")]
        ThreePolongTwoFive = 13,
        /// <summary>
        /// +3.50
        /// </summary>
        [Description("+3.50")]
        ThreePolongFiveZero = 14,
        /// <summary>
        /// +3.75
        /// </summary>
        [Description("+3.75")]
        ThreePolongSevenFive = 15,
        /// <summary>
        /// +4.00
        /// </summary>
        [Description("+4.00")]
        AddFour = 16
    }
    /// <summary>
    ///不等像视
    /// </summary>
    public enum UnequalImage
    {
        /// <summary>
        /// 无
        /// </summary>
        [Description("无")]
        UnequalNothing = 0,
        /// <summary>
        /// 有
        /// </summary>
        [Description("有")]
        Yes = 1
    }

    /// <summary>
    ///选择类型
    /// </summary>
    public enum SelectionType
    {
        /// <summary>
        /// 是
        /// </summary>
        [Description("是")]
        SelectionYes = 0,
        /// <summary>
        /// 否
        /// </summary>
        [Description("否")]
        No = 1
    }


    /// <summary>
    ///检影验光选择类型
    /// </summary>
    public enum RetinoscopySelectionType
    {
        /// <summary>
        /// 快速散瞳
        /// </summary>
        [Description("快速散瞳")]
        RapidMydriasis = 0,
        /// <summary>
        /// 慢速散瞳
        /// </summary>
        [Description("慢速散瞳")]
        SlowDilation = 1,
        /// <summary>
        /// 显然验光
        /// </summary>
        [Description("显然验光")]
        ManifestOptometry = 2
    }

    /// <summary>
    ///配镜需求
    /// </summary>
    public enum NeedGlasses
    {
        /// <summary>
        /// 看黑板
        /// </summary>
        [Description("看黑板")]
        LookAtTheBlackboard = 0,
        /// <summary>
        /// 开车
        /// </summary>
        [Description("开车")]
        DriveACar = 1,
        /// <summary>
        /// 看报纸
        /// </summary>
        [Description("看报纸")]
        ReadTheNewspaper = 2,
        /// <summary>
        /// 针线活
        /// </summary>
        [Description("针线活")]
        Needlework = 3,
        /// <summary>
        /// 看电视
        /// </summary>
        [Description("看电视")]
        WatchTV = 4,
        /// <summary>
        /// 弹琴
        /// </summary>
        [Description("弹琴")]
        PlayThePiano = 5,
        /// <summary>
        /// 体验
        /// </summary>
        [Description("体验")]
        Experience = 6,
        /// <summary>
        /// 看眼病
        /// </summary>
        [Description("看眼病")]
        EyeDiseases = 7,
        /// <summary>
        /// 近视控制
        /// </summary>
        [Description("近视控制")]
        MyopiaControl = 8,
        /// <summary>
        /// 其他
        /// </summary>
        [Description("其他")]
        Other = 9
    }

    /// <summary>
    ///泪膜检测 分级
    /// </summary>
    public enum Classification
    {
        /// <summary>
        /// Ⅱ
        /// </summary>
        [Description("Ⅱ")]
        Polysaccharides = 0,
        /// <summary>
        /// Ⅲ
        /// </summary>
        [Description("Ⅲ")]
        Competition = 1,
        /// <summary>
        /// Ⅳ
        /// </summary>
        [Description("Ⅳ")]
        Competitions = 2,
        /// <summary>
        /// Ⅴ
        /// </summary>
        [Description("Ⅴ")]
        FiveⅤ = 3
    }
    /// <summary>
    ///定位
    /// </summary>
    public enum Location
    {
        /// <summary>
        /// 中心
        /// </summary>
        [Description("中心")]
        core = 0,
        /// <summary>
        /// 上偏
        /// </summary>
        [Description("上偏")]
        OnTheSide = 1,
        /// <summary>
        /// 下偏
        /// </summary>
        [Description("下偏")]
        Partial = 2,
        /// <summary>
        /// 鼻侧偏
        /// </summary>
        [Description("鼻侧偏")]
        LateralNasalDeviation = 3,
        /// <summary>
        /// 颞侧偏
        /// </summary>
        [Description("颞侧偏")]
        TemporalDeviation = 4
    }
    /// <summary>
    ///活动度
    /// </summary>
    public enum ActivityDegree
    {
        /// <summary>
        /// 小于1mm
        /// </summary>
        [Description("小于1mm")]
        LessThan = 0,
        /// <summary>
        /// 1mm~2mm
        /// </summary>
        [Description("1mm~2mm")]
        BeEqualTo = 1,
        /// <summary>
        /// 大于2mmm
        /// </summary>
        [Description("大于2mmm")]
        GreaterThan = 2
    }

    /// <summary>
    ///覆盖
    /// </summary>
    public enum Cover
    {
        /// <summary>
        /// 全覆盖
        /// </summary>
        [Description("全覆盖")]
        FullCoverage = 0,
        /// <summary>
        /// 暴露角膜缘
        /// </summary>
        [Description("暴露角膜缘")]
        ExposedLimbus = 1
    }

    /// <summary>
    ///建议
    /// </summary>
    public enum Proposal
    {
        /// <summary>
        /// 可接受
        /// </summary>
        [Description("可接受")]
        Acceptable = 0,
        /// <summary>
        /// 尚可
        /// </summary>
        [Description("尚可")]
        Fair = 1,
        /// <summary>
        /// 不接受
        /// </summary>
        [Description("不接受")]
        NotAccepted = 2
    }
    /// <summary>
    ///近视眼和远视眼
    /// </summary>
    public enum MyopicEye
    {
        /// <summary>
        /// 轴性
        /// </summary>
        [Description("轴性")]
        Axis = 0,
        /// <summary>
        /// 曲率性
        /// </summary>
        [Description("曲率性")]
        Curvature = 1,
        /// <summary>
        /// 屈光指数性
        /// </summary>
        [Description("屈光指数性")]
        RefractiveIndex = 2
    }
    /// <summary>
    ///散光
    /// </summary>
    public enum Astigmatism
    {
        /// <summary>
        /// 顺规性
        /// </summary>
        [Description("顺规性")]
        Compliance = 0,
        /// <summary>
        /// 逆规性
        /// </summary>
        [Description("逆规性")]
        ReverseRegularity = 1,
        /// <summary>
        /// 斜向
        /// </summary>
        [Description("斜向")]
        Oblique = 2,
        /// <summary>
        /// 不规则性
        /// </summary>
        [Description("不规则性")]
        Irregularity = 3
    }
    /// <summary>
    ///处方类型
    /// </summary>
    public enum PrescriptionType
    {
        /// <summary>
        /// 远用
        /// </summary>
        [Description("远用")]
        Far = 0,
        /// <summary>
        /// 近用
        /// </summary>
        [Description("近用")]
        Distance = 1,
        /// <summary>
        /// 渐进/双光
        /// </summary>
        [Description("渐进/双光")]
        ProgressiveDualLight = 2,
        /// <summary>
        /// 隐形
        /// </summary>
        [Description("隐形")]
        Invisible = 3,
        /// <summary>
        /// 中用
        /// </summary>
        [Description("中用")]
        OfUse = 4,
        /// <summary>
        /// 角膜塑形
        /// </summary>
        [Description("角膜塑形")]
        Orthokeratology = 5,
        /// <summary>
        /// 视觉训练
        /// </summary>
        [Description("视觉训练")]
        VisualTraining = 6
    }
    /// <summary>
    ///中距离
    /// </summary>
    public enum MiddleDistance
    {
        /// <summary>
        /// 打孔
        /// </summary>
        [Description("打孔")]
        Punch = 0,
        /// <summary>
        /// 拉丝
        /// </summary>
        [Description("拉丝")]
        WireDrawing = 1,
        /// <summary>
        /// 板材
        /// </summary>
        [Description("板材")]
        Board = 2,
        /// <summary>
        /// 框高
        /// </summary>
        [Description("框高")]
        HighFrame = 3
    }
    /// <summary>
    ///处理方式
    /// </summary>
    public enum TreatmentMode
    {
        /// <summary>
        /// 足矫
        /// </summary>
        [Description("足矫")]
        FullCorrection = 0,
        /// <summary>
        /// 欠矫
        /// </summary>
        [Description("欠矫")]
        Undercorrection = 1,
        /// <summary>
        /// 过矫
        /// </summary>
        [Description("过矫")]
        Overcorrection = 2,
        /// <summary>
        /// 附加棱镜
        /// </summary>
        [Description("附加棱镜")]
        AdditionalPrism = 3,
        /// <summary>
        /// 调整
        /// </summary>
        [Description("调整")]
        Adjustment = 4,
        /// <summary>
        /// 平衡
        /// </summary>
        [Description("平衡")]
        Balance = 5,
        /// <summary>
        /// 医嘱
        /// </summary>
        [Description("医嘱")]
        Doctor = 6,
        /// <summary>
        /// 全矫
        /// </summary>
        [Description("全矫")]
        AllCorrection = 7,
        [Description("患者要求减度")]
        PatientRequestReduction = 8
    }

    /// <summary>
    ///隐形处理方式
    /// </summary>
    public enum ContactTreatmentMode
    {
        /// <summary>
        /// 足矫
        /// </summary>
        [Description("足矫")]
        FullCorrection = 0,
        /// <summary>
        /// 欠矫
        /// </summary>
        [Description("欠矫")]
        Undercorrection = 1,
        /// <summary>
        /// 过矫
        /// </summary>
        [Description("过矫")]
        Overcorrection = 2,
    }
    /// <summary>
    ///戴镜方式
    /// </summary>
    public enum WearingGlasses
    {
        /// <summary>
        /// 日戴
        /// </summary>
        [Description("日戴")]
        DailyWear = 0,
        /// <summary>
        /// 夜戴
        /// </summary>
        [Description("夜戴")]
        NightWear = 1,
        /// <summary>
        /// 连续配戴
        /// </summary>
        [Description("连续配戴")]
        ContinuousWear = 2
    }
    /// <summary>
    ///ODOS角膜
    /// </summary>
    public enum Corneal
    {
        /// <summary>
        /// 正常
        /// </summary>
        [Description("正常")]
        Normal = 0,
        /// <summary>
        /// 上皮缺损
        /// </summary>
        [Description("上皮缺损")]
        EpithelialDefect = 1,
        /// <summary>
        /// 云翳
        /// </summary>
        [Description("云翳")]
        Clouds = 2,
        /// <summary>
        /// 白斑
        /// </summary>
        [Description("白斑")]
        Leukoplakia = 3,
        /// <summary>
        /// 浸润
        /// </summary>
        [Description("浸润")]
        Infiltration = 4,
        /// <summary>
        /// 瘢痕
        /// </summary>
        [Description("瘢痕")]
        Scar = 5,
        /// <summary>
        /// 新生血管
        /// </summary>
        [Description("新生血管")]
        Neovascularization = 6,
        /// <summary>
        /// 异物
        /// </summary>
        [Description("异物")]
        ForeignBody = 7,
        /// <summary>
        /// 水肿
        /// </summary>
        [Description("水肿")]
        Edema = 8,
        /// <summary>
        /// 上皮染色
        /// </summary>
        [Description("上皮染色")]
        EpithelialStaining = 9,
        /// <summary>
        /// 透明
        /// </summary>
        [Description("透明")]
        Transparent = 10,
        /// <summary>
        /// 屈光力偏高
        /// </summary>
        [Description("屈光力偏高")]
        HighRefractivePower = 11,
        /// <summary>
        /// 规则指数异常
        /// </summary>
        [Description("规则指数异常")]
        RuleIndexAnomaly = 12
    }
    /// <summary>
    ///ODOS结膜
    /// </summary>
    public enum Conjunctiva
    {
        /// <summary>
        /// 正常
        /// </summary>
        [Description("正常")]
        ConjunctivaNormal = 0,
        /// <summary>
        /// 乳头增生
        /// </summary>
        [Description("乳头增生")]
        PapillaryHyperplasia = 1,
        /// <summary>
        /// 滤泡
        /// </summary>
        [Description("滤泡")]
        Follicular = 2,
        /// <summary>
        /// 结石
        /// </summary>
        [Description("结石")]
        stone = 3,
        /// <summary>
        /// 水肿
        /// </summary>
        [Description("水肿")]
        ConjunctivaEdema = 4,
        /// <summary>
        /// 睑板瘢痕
        /// </summary>
        [Description("睑板瘢痕")]
        TarsalScar = 5
    }
    /// <summary>
    ///接触镜评估下的选项
    /// </summary>
    public enum ContactLensEvaluationOption
    {
        /// <summary>
        /// 试戴
        /// </summary>
        [Description("试戴")]
        Try = 0,
        /// <summary>
        /// 顾客要求不试戴
        /// </summary>
        [Description("顾客要求不试戴")]
        TheCustomerAskedNotToTry = 1

    }

    /// <summary>
    ///增加试戴选项
    /// </summary>
    public enum IncreaseTrialWearOptions
    {
        /// <summary>
        /// 软镜
        /// </summary>
        [Description("软镜")]
        SoftLens = 0,
        /// <summary>
        /// RGP
        /// </summary>
        [Description("RGP")]
        RGP = 1,
        /// <summary>
        /// 角膜塑形镜
        /// </summary>
        [Description("角膜塑形镜")]
        Mct = 2

    }
    /// <summary>
    ///双眼视功能评估
    /// </summary>
    public enum BinocularVisualFunctionAssessment
    {
        /// <summary>
        /// 正常
        /// </summary>
        [Description("正常")]
        BinocularNormal = 0,
        /// <summary>
        /// 调节不足
        /// </summary>
        [Description("调节不足")]
        InadequateAccommodation = 1,
        /// <summary>
        /// 调节过度
        /// </summary>
        [Description("调节过度")]
        OverRegulation = 2,
        /// <summary>
        /// 调节灵敏度异常
        /// </summary>
        [Description("调节灵敏度异常")]
        AbnormalAccommodationSensitivity = 3,
        /// <summary>
        /// 集合过度
        /// </summary>
        [Description("集合过度")]
        OverSet = 4,
        /// <summary>
        /// 集合不足
        /// </summary>
        [Description("集合不足")]
        AggregationInsufficiency = 5,
        /// <summary>
        /// 内隐斜
        /// </summary>
        [Description("内隐斜")]
        ImplicitOblique = 6,
        /// <summary>
        /// 外隐斜
        /// </summary>
        [Description("外隐斜")]
        Skew = 7,
        /// <summary>
        /// 眼球运动训练
        /// </summary>
        [Description("眼球运动训练")]
        EyeMovementTraining = 8,
        /// <summary>
        /// 其他
        /// </summary>
        [Description("其他")]
        BinocularOther = 9
    }
    /// <summary>
    /// 双眼视功能 原因
    /// </summary>
    public enum BinocularVisualFunctionReason
    {
        /// <summary>
        /// 年龄过小
        /// </summary>
        [Description("年龄过小")]
        AgeMin = 0,
        /// <summary>
        /// 配合不好
        /// </summary>
        [Description("配合不好")]
        NotCoordination = 1,
        /// <summary>
        /// 斜视
        /// </summary>
        [Description("斜视")]
        Strabismus = 2,
        /// <summary>
        /// 弱视
        /// </summary>
        [Description("弱视")]
        Amblyopia = 3,
        /// <summary>
        /// 低视力
        /// </summary>
        [Description("低视力")]
        LowVision = 4,

        /// <summary>
        /// 其它
        /// </summary>
        [Description("其它")]
        Orther = 5,
    }
    /// <summary>
    ///框架眼镜
    /// </summary>
    public enum FrameGlasses
    {
        /// <summary>
        /// 非球面
        /// </summary>
        [Description("非球面")]
        AsphericSurface = 0,
        /// <summary>
        /// 球面
        /// </summary>
        [Description("球面")]
        Sphere = 1,
        /// <summary>
        /// 球面渐进多焦点
        /// </summary>
        [Description("球面渐进多焦点")]
        SphericalProgressiveFocusing = 2,
        /// <summary>
        /// 抗疲劳
        /// </summary>
        [Description("抗疲劳")]
        ResistFatigue = 3,
        /// <summary>
        /// 变色片
        /// </summary>
        [Description("变色片")]
        Chameleon = 4
    }
    /// <summary>
    ///角膜接触镜
    /// </summary>
    public enum ContactLensType
    {
        /// <summary>
        /// OK
        /// </summary>
        [Description("OK")]
        OK = 0,
        /// <summary>
        /// RPG
        /// </summary>
        [Description("RPG")]
        ContactRPG = 1,
        /// <summary>
        /// 散光
        /// </summary>
        [Description("散光")]
        Astigmatism = 2,
        /// <summary>
        /// 传统型
        /// </summary>
        [Description("传统型")]
        TraditionalType = 3,
        /// <summary>
        /// 抛弃型
        /// </summary>
        [Description("抛弃型")]
        AbandonType = 4
    }
    /// <summary>
    ///视觉训练
    /// </summary>
    public enum VisualTraining
    {
        /// <summary>
        /// 调节训练
        /// </summary>
        [Description("调节训练")]
        ConditioningTraining = 0,
        /// <summary>
        /// 融像训练
        /// </summary>
        [Description("融像训练")]
        ImageryTraining = 1,
        /// <summary>
        /// 精细训练
        /// </summary>
        [Description("精细训练")]
        FineTraining = 2,
        /// <summary>
        /// 视认知训练
        /// </summary>
        [Description("视认知训练")]
        VisualCognitiveTraining = 3,
        /// <summary>
        /// 加强散开训练
        /// </summary>
        [Description("加强散开训练")]
        ReinforcementTraining = 4,
        /// <summary>
        /// 加强集合训练
        /// </summary>
        [Description("加强集合训练")]
        longensiveTraining = 5,
        /// <summary>
        /// 加强调节放松训练
        /// </summary>
        [Description("加强调节放松训练")]
        StrengthenTheRegulationrelaxationtraining = 6
    }
    /// <summary>
    ///复诊建议下的选项
    /// </summary>
    public enum ReferralOptions
    {
        /// <summary>
        /// 正常
        /// </summary>
        [Description("正常")]
        ReferralNormal = 0,
        /// <summary>
        /// 停戴
        /// </summary>
        [Description("停戴")]
        StopWearing = 1,
        /// <summary>
        /// 更换镜片
        /// </summary>
        [Description("更换镜片")]
        ReplaceLenses = 2
    }
    /// <summary>
    ///复诊建议下的的选择复查
    /// </summary>
    public enum SelectiveReview
    {
        /// <summary>
        /// 一周复查
        /// </summary>
        [Description("一周复查")]
        Aweeksreview = 0,
        /// <summary>
        /// 二周复查
        /// </summary>
        [Description("二周复查")]
        ReviewForTwoWeeks = 1,
        /// <summary>
        /// 三周复查
        /// </summary>
        [Description("三周复查")]
        ReviewForThreeWeeks = 2,
        /// <summary>
        /// 一月复查
        /// </summary>
        [Description("一月复查")]
        MonthlyReview = 3,
        /// <summary>
        /// 三月复查
        /// </summary>
        [Description("三月复查")]
        MarchReview = 4,
        /// <summary>
        /// 半年复查
        /// </summary>
        [Description("半年复查")]
        SemiAnnualReview = 5
    }


    /// <summary>
    /// 处方单打印枚举
    /// </summary>
    public enum PrescriptionPrlong
    {
        /// <summary>
        /// 双眼视功能
        /// </summary>
        [Description("双眼视功能")]
        BinocularVision = 0,
        /// <summary>
        /// 双眼平衡
        /// </summary>
        [Description("双眼平衡")]
        BinocularBalance = 1,
        /// <summary>
        /// 角膜接触镜
        /// </summary>
        [Description("角膜接触镜")]
        ContactLens = 2,
        /// <summary>
        /// 角膜内皮细胞计数
        /// </summary>
        [Description("角膜内皮细胞计数")]
        CornealEndothelialCell = 3,
        /// <summary>
        /// 角膜地形图
        /// </summary>
        [Description("角膜地形图")]
        CornealTopography = 4,
        /// <summary>
        /// 建议矫正方案
        /// </summary>
        [Description("建议矫正方案")]
        Correct = 5,
        /// <summary>
        /// 眼部健康检查
        /// </summary>
        [Description("眼部健康检查")]
        EyeHealthCheck = 6,
        /// <summary>
        /// 眼睛健康评估
        /// </summary>
        [Description("眼睛健康评估")]
        EyeHealthEvaluation = 7,
        /// <summary>
        /// 接触镜评估
        /// </summary>
        [Description("接触镜评估")]
        LensEvaluation = 8,
        /// <summary>
        /// 泪膜检查
        /// </summary>
        [Description("泪膜检查")]
        TearFilm = 9,
        /// <summary>
        /// 试镜
        /// </summary>
        [Description("试镜")]
        TestGlasses = 10,
        /// <summary>
        /// 双眼视功能检查结论 常规项 远
        /// </summary>
        [Description("双眼视功能检查结论 常规项 远")]
        BinocularVisionCN_F = 11,
        /// <summary>
        /// 双眼视功能检查结论 常规项 近
        /// </summary>
        [Description("双眼视功能检查结论 常规项 近")]
        BinocularVisionCN_N = 12,
        /// <summary>
        /// 双眼视功能检查结论 常规项 远
        /// </summary>
        [Description("双眼视功能检查结论 常规项 中")]
        BinocularVisionCN_M = 13,
        /// <summary>
        /// 双眼视功能检查结论 隐形眼镜
        /// </summary>
        [Description("双眼视功能检查结论 隐形眼镜")]
        BinocularVisionCCL = 14,
        /// <summary>
        /// 双眼视功能检查结论 渐进/双光
        /// </summary>
        [Description("双眼视功能检查结论 渐进/双光")]
        BinocularVisionCPDL = 15,
        /// <summary>
        /// 双眼视功能检查结论 角膜塑形
        /// </summary>
        [Description("双眼视功能检查结论 角膜塑形")]
        BinocularVisionO = 16,
        /// <summary>
        /// 双眼视功能检查结论 视觉训练
        /// </summary>
        [Description("双眼视功能检查结论 视觉训练")]
        BinocularVisionVT = 17,
        /// <summary>
        /// 旧镜信息
        /// </summary>
        [Description("旧镜信息")]
        OldMirrorInfo = 18,
        /// <summary>
        /// 既往戴镜史
        /// </summary>
        [Description("既往戴镜史")]
        PHWGlasses = 19,
        /// <summary>
        /// 追加度数
        /// </summary>
        [Description("追加度数")]
        AdditionalDegree = 20,
        /// <summary>
        /// 屈光检查
        /// </summary>
        [Description("屈光检查")]
        Refraction = 21,
        /// <summary>
        /// 检影验光
        /// </summary>
        [Description("检影验光")]
        Retinoscopy = 22,
        /// <summary>
        /// 复诊建议
        /// </summary>
        [Description("复诊建议")]
        ReturnVisit = 23,
        /// <summary>
        /// 特殊功能检查
        /// </summary>
        [Description("特殊功能检查")]
        SpecialFunction = 24,
        /// <summary>
        /// 主观屈光检查
        /// </summary>
        [Description("主观屈光检查")]
        SubjectiveRefraction = 25,
        /// <summary>
        /// 头部信息
        /// </summary>
        [Description("头部信息")]
        Head = 999,
        /// <summary>
        /// 所有
        /// </summary>
        [Description("所有")]
        ALL = -1,
    }
}
