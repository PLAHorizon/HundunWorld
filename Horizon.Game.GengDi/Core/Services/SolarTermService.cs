using System;
using System.Collections.Generic;

namespace Horizon.Game.GengDi.Core.Services
{
    public class SolarTermInfo
    {
        public int Month { get; set; }
        public int Day { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Season { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string DietaryTip { get; set; } = string.Empty;
        public string HealthTip { get; set; } = string.Empty;
        public string RecommendedDish { get; set; } = string.Empty;
        public string DishReason { get; set; } = string.Empty;
        public string Ingredients { get; set; } = string.Empty;
        public string CookingMethod { get; set; } = string.Empty;
        public string Contraindications { get; set; } = string.Empty;
    }

    public static class SolarTermService
    {
        private static readonly SolarTermInfo[] Terms = BuildSolarTerms();

        private static SolarTermInfo[] BuildSolarTerms()
        {
            return new SolarTermInfo[]
            {
                new SolarTermInfo {
                    Month = 1, Day = 5, Name = "小寒", Season = "冬季",
                    Description = "天气寒冷但还未到极点，是一年中最冷的时节之一",
                    DietaryTip = "宜温补，多食羊肉、鸡肉、红枣、桂圆等温性食物",
                    HealthTip = "注意防寒保暖，早睡晚起，适度运动不宜出汗过多",
                    RecommendedDish = "当归生姜羊肉汤",
                    DishReason = "羊肉性温，当归补血，生姜驱寒，适合小寒时节温补驱寒",
                    Ingredients = "羊肉500克、当归15克、生姜30克、红枣6枚、料酒适量",
                    CookingMethod = "1. 羊肉切块焯水去血沫；2. 当归、生姜、红枣洗净；3. 砂锅中加水放入所有材料；4. 大火煮沸后转小火炖2小时；5. 加盐调味即可",
                    Contraindications = "阴虚火旺、实热证者慎用；感冒发热期间不宜；孕妇慎服当归"
                },
                new SolarTermInfo {
                    Month = 1, Day = 20, Name = "大寒", Season = "冬季",
                    Description = "一年中最冷的时期，寒气到达极致",
                    DietaryTip = "宜进补，多食温热食物如牛肉、羊肉、核桃、黑芝麻",
                    HealthTip = "大寒过后阳气开始回升，注意养肾防寒，可适当进行户外活动",
                    RecommendedDish = "八宝饭",
                    DishReason = "糯米温补，八宝食材丰富，大寒时节食之可补气血、暖脾胃",
                    Ingredients = "糯米500克、红豆沙200克、红枣6枚、莲子30克、桂圆30克、葡萄干30克、枸杞20克、猪油30克、白糖适量",
                    CookingMethod = "1. 糯米浸泡2小时后蒸熟；2. 碗底涂猪油，铺入八宝食材；3. 铺上糯米饭压实；4. 放入豆沙馅；5. 再铺糯米饭蒸20分钟；6. 倒扣装盘，淋白糖浆",
                    Contraindications = "糖尿病患者慎食；脾胃虚寒者少食糯米制品；消化不良者减量"
                },
                new SolarTermInfo {
                    Month = 2, Day = 4, Name = "立春", Season = "春季",
                    Description = "春季的开始，万物复苏，阳气开始升发",
                    DietaryTip = "宜清淡，少食油腻辛辣，多食芽菜、韭菜、春笋等时令蔬菜",
                    HealthTip = "春捂秋冻，不宜过早脱去冬衣，注意养肝护阳",
                    RecommendedDish = "春饼",
                    DishReason = "立春吃春饼是传统习俗，包裹时蔬，清爽宜人，顺应春气",
                    Ingredients = "面粉300克、绿豆芽150克、韭菜100克、胡萝卜1根、粉丝50克、鸡蛋3个、香油适量",
                    CookingMethod = "1. 面粉加热水和成面团，醒30分钟；2. 分成小剂子擀成薄饼；3. 平底锅烙熟；4. 绿豆芽、韭菜、胡萝卜丝炒熟；5. 粉丝泡软炒熟；6. 用饼卷各种菜食用",
                    Contraindications = "脾胃虚寒者不宜多食生蔬菜；肠胃不适者菜需炒熟；对鸡蛋过敏者慎食"
                },
                new SolarTermInfo {
                    Month = 2, Day = 19, Name = "雨水", Season = "春季",
                    Description = "降雨开始增多，气温逐渐回升，大地渐呈欣欣向荣之貌",
                    DietaryTip = "宜健脾养胃，多食山药、莲子、红枣、小米等",
                    HealthTip = "注意防湿邪，保持心情舒畅，适合散步、太极等缓和运动",
                    RecommendedDish = "山药莲子粥",
                    DishReason = "山药补脾，莲子养心，雨水时节湿气渐重，食之健脾祛湿",
                    Ingredients = "大米100克、山药150克、莲子50克、红枣6枚、冰糖适量",
                    CookingMethod = "1. 大米淘洗干净浸泡30分钟；2. 山药去皮切小块；3. 莲子去芯洗净；4. 锅中加水煮沸放入大米；5. 煮开后加入山药、莲子、红枣；6. 小火熬至粥稠，加冰糖调味",
                    Contraindications = "便秘者少食莲子；大便干燥者减量；糖尿病患者慎用冰糖"
                },
                new SolarTermInfo {
                    Month = 3, Day = 6, Name = "惊蛰", Season = "春季",
                    Description = "春雷乍动，蛰伏的昆虫开始苏醒，生机盎然",
                    DietaryTip = "宜清淡疏肝，多食芹菜、菠菜、荠菜、梨等",
                    HealthTip = "肝气旺盛，注意保持情绪稳定，早睡早起，多呼吸新鲜空气",
                    RecommendedDish = "荠菜春卷",
                    DishReason = "荠菜清热利湿，惊蛰时节食之顺应春令，清肝明目",
                    Ingredients = "新鲜荠菜300克、猪肉馅100克、春卷皮10张、鸡蛋1个、姜末少许、盐适量",
                    CookingMethod = "1. 荠菜焯水切碎；2. 猪肉馅加姜末、盐、鸡蛋调匀；3. 荠菜与肉馅混合拌匀；4. 春卷皮包入馅料；5. 油锅五成热炸至金黄酥脆",
                    Contraindications = "阴虚体质者少食油炸食品；高血压患者注意控油；对麸质过敏者慎食春卷皮"
                },
                new SolarTermInfo {
                    Month = 3, Day = 21, Name = "春分", Season = "春季",
                    Description = "昼夜等长，春季过半，气候温和宜人",
                    DietaryTip = "宜调和阴阳，多食时令蔬菜、春笋、香椿等",
                    HealthTip = "阴阳平衡之际，注意作息规律，不宜过劳，适合户外踏青",
                    RecommendedDish = "香椿炒蛋",
                    DishReason = "春分时节香椿鲜嫩，性温味苦，与鸡蛋同炒营养均衡",
                    Ingredients = "新鲜香椿芽100克、鸡蛋3个、盐适量、食用油适量",
                    CookingMethod = "1. 香椿芽用开水焯一下（去除亚硝酸盐）；2. 捞出切碎；3. 鸡蛋打散加盐搅匀；4. 将香椿碎拌入蛋液；5. 热锅凉油倒入蛋液；6. 翻炒至凝固即可",
                    Contraindications = "香椿含亚硝酸盐需焯水后食用；阴虚火旺者慎食；对香椿过敏者禁食"
                },
                new SolarTermInfo {
                    Month = 4, Day = 5, Name = "清明", Season = "春季",
                    Description = "天清气明，草木繁茂，是踏青扫墓的好时节",
                    DietaryTip = "宜柔肝养肺，多食菠菜、荠菜、青团、清明菜等",
                    HealthTip = "注意预防呼吸道疾病，多到户外活动，保持心情愉悦",
                    RecommendedDish = "青团",
                    DishReason = "清明传统美食，艾草清香，糯米软糯，应节应景",
                    Ingredients = "糯米粉300克、艾草200克、豆沙馅200克、白糖适量",
                    CookingMethod = "1. 艾草焯水去苦味，加少许碱打成泥；2. 糯米粉加艾草泥和匀成团；3. 分成小剂子包入豆沙馅；4. 搓成圆球状；5. 垫油纸蒸15分钟即可",
                    Contraindications = "艾草性温，实热证者慎食；糯米制品不易消化，胃弱者减量；糖尿病患者注意控糖"
                },
                new SolarTermInfo {
                    Month = 4, Day = 20, Name = "谷雨", Season = "春季",
                    Description = "雨水增多，利于谷类作物生长，春季最后一个节气",
                    DietaryTip = "宜健脾祛湿，多食薏米、赤小豆、冬瓜、玉米等",
                    HealthTip = "湿气较重，注意防潮，适当运动以助气血运行",
                    RecommendedDish = "薏米赤小豆粥",
                    DishReason = "薏米利湿，赤小豆健脾，谷雨时节湿气渐盛，食之祛湿健脾",
                    Ingredients = "薏米50克、赤小豆50克、大米50克、冰糖适量",
                    CookingMethod = "1. 薏米、赤小豆提前浸泡4小时；2. 大米淘洗干净；3. 锅中加水煮沸放入薏米和赤小豆；4. 大火煮沸后转小火煮1小时；5. 加入大米继续煮30分钟；6. 加冰糖调味即可",
                    Contraindications = "孕妇忌食薏米（有滑胎作用）；大便干结者慎用；脾胃虚寒者少食"
                },
                new SolarTermInfo {
                    Month = 5, Day = 6, Name = "立夏", Season = "夏季",
                    Description = "夏季开始，气温明显升高，雷雨增多，农作物进入旺季",
                    DietaryTip = "宜清淡养心，多食莲子、百合、绿豆、冬瓜等",
                    HealthTip = "心气渐旺，注意养心护心，保证充足睡眠，避免午后暴晒",
                    RecommendedDish = "绿豆百合汤",
                    DishReason = "绿豆清热，百合养心，立夏后气温升高，食之清热解暑",
                    Ingredients = "绿豆100克、鲜百合100克、冰糖适量、清水适量",
                    CookingMethod = "1. 绿豆洗净浸泡2小时；2. 鲜百合掰开洗净；3. 锅中加水煮沸放入绿豆；4. 大火煮沸后转小火煮40分钟至绿豆开花；5. 加入百合煮10分钟；6. 加冰糖调味即可",
                    Contraindications = "脾胃虚寒者慎食绿豆；腹泻期间不宜；体质偏寒者减量"
                },
                new SolarTermInfo {
                    Month = 5, Day = 21, Name = "小满", Season = "夏季",
                    Description = "夏熟作物籽粒开始饱满但未成熟，气温升高，雨水充沛",
                    DietaryTip = "宜清热利湿，多食苦瓜、黄瓜、西瓜、番茄等",
                    HealthTip = "注意防暑降温，饮食宜清淡，避免贪凉饮冷",
                    RecommendedDish = "凉拌苦瓜",
                    DishReason = "苦瓜清热解毒，小满时节气温升高，食之降火消暑",
                    Ingredients = "苦瓜1根、大蒜3瓣、小米椒1个、白糖1勺、香醋1勺、盐适量、香油适量",
                    CookingMethod = "1. 苦瓜去瓤切片，用盐腌10分钟后冲洗（去苦味）；2. 焯水1分钟后捞出过凉水；3. 大蒜切末，小米椒切圈；4. 碗中放蒜末、辣椒、白糖、醋、香油调成汁；5. 浇在苦瓜上拌匀即可",
                    Contraindications = "脾胃虚寒者慎食苦瓜；孕妇慎食；低血糖者注意苦瓜可能降低血糖"
                },
                new SolarTermInfo {
                    Month = 6, Day = 6, Name = "芒种", Season = "夏季",
                    Description = "麦类等有芒作物成熟，仲夏时节，气温继续升高",
                    DietaryTip = "宜清热消暑，多食西瓜、绿豆、酸梅、黄瓜等",
                    HealthTip = "农事繁忙，注意劳逸结合，补充水分和电解质",
                    RecommendedDish = "酸梅汤",
                    DishReason = "酸梅生津止渴，芒种时节劳作辛苦，饮之解暑提神",
                    Ingredients = "乌梅30克、山楂20克、陈皮5克、甘草5克、桂花5克、冰糖150克、清水2升",
                    CookingMethod = "1. 乌梅、山楂、陈皮、甘草洗净浸泡30分钟；2. 锅中加水煮沸放入药材；3. 大火煮沸后转小火煮30分钟；4. 过滤掉渣滓；5. 加入冰糖搅拌至融化；6. 放凉后撒入桂花，冷藏后饮用更佳",
                    Contraindications = "胃溃疡患者慎用；胃酸过多者减量；糖尿病患者注意控糖"
                },
                new SolarTermInfo {
                    Month = 6, Day = 21, Name = "夏至", Season = "夏季",
                    Description = "一年中白昼最长之日，阳气最盛，气温最高",
                    DietaryTip = "宜清热生津，多食凉面、绿豆汤、西瓜、莲子等",
                    HealthTip = "昼长夜短，注意午休，饮食清淡，避免过度贪凉",
                    RecommendedDish = "夏至凉面",
                    DishReason = "夏至吃面是传统，面条清凉爽口，顺应夏日养生之道",
                    Ingredients = "面条300克、黄瓜1根、胡萝卜1根、鸡蛋2个、芝麻酱2勺、蒜末适量、醋2勺、生抽1勺、香油适量",
                    CookingMethod = "1. 面条煮熟后过凉水沥干；2. 黄瓜、胡萝卜切丝；3. 鸡蛋摊成蛋皮切丝；4. 芝麻酱加温水调稀，加醋、生抽、蒜末、香油调成酱汁；5. 面条装盘铺上菜丝，浇上酱汁拌匀即可",
                    Contraindications = "脾胃虚寒者不宜多食凉面；面条需用熟水制作，注意卫生；对芝麻过敏者慎食"
                },
                new SolarTermInfo {
                    Month = 7, Day = 7, Name = "小暑", Season = "夏季",
                    Description = "天气开始炎热但未到最热，三伏天即将到来",
                    DietaryTip = "宜清热消暑，多食绿豆、荷叶、冬瓜、西瓜等",
                    HealthTip = "注意防暑降温，避免正午外出，多饮温水",
                    RecommendedDish = "荷叶粥",
                    DishReason = "荷叶清暑利湿，小暑时节食之清热解烦，健脾开胃",
                    Ingredients = "大米100克、鲜荷叶1张、冰糖适量、清水适量",
                    CookingMethod = "1. 大米洗净浸泡30分钟；2. 鲜荷叶洗净剪成小块；3. 锅中加水煮沸放入大米；4. 加入荷叶块同煮；5. 小火熬至粥稠；6. 捞出荷叶，加冰糖调味即可",
                    Contraindications = "体质虚弱者慎用荷叶；低血压患者注意荷叶有降压作用；孕妇慎食"
                },
                new SolarTermInfo {
                    Month = 7, Day = 23, Name = "大暑", Season = "夏季",
                    Description = "一年中最热的时期，高温酷热，雷雨频繁",
                    DietaryTip = "宜清补，多食绿豆、薏米、莲子、银耳等清润之品",
                    HealthTip = "注意防暑降温，保证充足睡眠，适量运动，避免中暑",
                    RecommendedDish = "银耳莲子羹",
                    DishReason = "银耳滋阴，莲子养心，大暑酷暑食之清补润燥",
                    Ingredients = "银耳20克、莲子50克、红枣6枚、冰糖适量、清水适量",
                    CookingMethod = "1. 银耳冷水泡发去根部杂质撕成小朵；2. 莲子去芯洗净；3. 红枣洗净；4. 锅中加水放入银耳大火煮沸；5. 转小火炖1小时后加入莲子、红枣；6. 继续炖30分钟至银耳出胶，加冰糖调味",
                    Contraindications = "风寒咳嗽者慎食银耳；大便溏稀者减量；糖尿病患者注意控糖"
                },
                new SolarTermInfo {
                    Month = 8, Day = 8, Name = "立秋", Season = "秋季",
                    Description = "秋季开始，暑去凉来，但余热未消（秋老虎）",
                    DietaryTip = "宜润肺养阴，多食梨、百合、银耳、蜂蜜等",
                    HealthTip = "注意防暑降温之余逐渐增加润燥食物，不宜立即大补",
                    RecommendedDish = "冰糖炖雪梨",
                    DishReason = "梨润肺生津，冰糖甘润，立秋后燥气渐起，食之润肺",
                    Ingredients = "雪梨2个、冰糖30克、枸杞10克、清水适量",
                    CookingMethod = "1. 雪梨洗净从顶部切开去核；2. 梨心挖空放入冰糖和枸杞；3. 盖上梨盖用牙签固定；4. 放入碗中加少许清水；5. 蒸锅大火蒸30分钟至梨软；6. 取出即可食用",
                    Contraindications = "脾胃虚寒者慎食生梨；腹泻期间不宜；糖尿病患者注意控糖"
                },
                new SolarTermInfo {
                    Month = 8, Day = 23, Name = "处暑", Season = "秋季",
                    Description = "暑气至此而止，天气逐渐转凉，秋高气爽",
                    DietaryTip = "宜滋阴润燥，多食鸭肉、百合、银耳、莲藕等",
                    HealthTip = "注意增减衣物，防秋燥伤肺，保持室内湿度",
                    RecommendedDish = "老鸭汤",
                    DishReason = "鸭肉性凉，处暑后食之滋阴润燥，补充夏季消耗",
                    Ingredients = "老鸭半只（约750克）、山药200克、枸杞15克、生姜5片、料酒适量、盐适量",
                    CookingMethod = "1. 老鸭斩块水去血沫；2. 山药去皮切块；3. 砂锅中加足量水放入鸭块、姜片、料酒；4. 大火煮沸后撇去浮沫；5. 转小火慢炖2小时；6. 加入山药、枸杞继续炖30分钟，加盐调味",
                    Contraindications = "脾胃虚寒者慎食鸭肉；感冒期间不宜进补；高尿酸者注意鸭肉嘌呤较高"
                },
                new SolarTermInfo {
                    Month = 9, Day = 8, Name = "白露", Season = "秋季",
                    Description = "气温下降，露水凝结，秋意渐浓",
                    DietaryTip = "宜润燥养肺，多食梨、蜂蜜、芝麻、核桃等",
                    HealthTip = "注意早晚添衣，防寒气入体，适当增加运动量",
                    RecommendedDish = "桂花糕",
                    DishReason = "白露时节桂花飘香，桂花温中散寒，糕饼滋润养肺",
                    Ingredients = "糯米粉200克、粘米粉100克、糖桂花50克、白糖80克、清水适量",
                    CookingMethod = "1. 糯米粉和粘米粉混合，加白糖拌匀；2. 少量多次加入清水拌成湿润的粉状（握之成团，松之即散）；3. 过筛入抹了油的蒸碗；4. 表面撒糖桂花；5. 大火蒸30分钟；6. 取出切块即可",
                    Contraindications = "糖尿病患者慎食；糯米制品不易消化，胃弱者减量；对桂花过敏者禁食"
                },
                new SolarTermInfo {
                    Month = 9, Day = 23, Name = "秋分", Season = "秋季",
                    Description = "昼夜等长，秋季过半，气温适宜，秋高气爽",
                    DietaryTip = "宜平和调养，多食芝麻、核桃、山药、藕等",
                    HealthTip = "阴阳平衡，注意调养脾胃，保持良好作息",
                    RecommendedDish = "山药排骨汤",
                    DishReason = "山药健脾，排骨补益，秋分时节食之平和调养",
                    Ingredients = "排骨500克、山药300克、枸杞15克、姜片3片、料酒适量、盐适量",
                    CookingMethod = "1. 排骨斩块焯水去血沫；2. 山药去皮切段；3. 砂锅中加水放入排骨、姜片、料酒；4. 大火煮沸后转小火炖1小时；5. 加入山药继续炖30分钟；6. 加入枸杞炖5分钟，加盐调味",
                    Contraindications = "便秘者少食山药；大便干结者减量；高尿酸者注意排骨嘌呤含量"
                },
                new SolarTermInfo {
                    Month = 10, Day = 8, Name = "寒露", Season = "秋季",
                    Description = "露水更凉，气温更低，深秋寒意渐浓",
                    DietaryTip = "宜温补润燥，多食羊肉、牛肉、栗子、柿子等",
                    HealthTip = "注意保暖防寒，特别是足部和腹部，避免寒邪入侵",
                    RecommendedDish = "板栗烧鸡",
                    DishReason = "板栗补肾强筋，鸡肉温中益气，寒露时节食之温补防寒",
                    Ingredients = "鸡腿肉500克、板栗300克、生姜5片、葱段适量、料酒2勺、生抽2勺、老抽1勺、冰糖适量",
                    CookingMethod = "1. 鸡腿肉斩块焯水；2. 板栗去壳（可先煮一下再剥）；3. 锅中加油爆香姜片；4. 放入鸡块翻炒至变色；5. 加入料酒、生抽、老抽、冰糖翻炒上色；6. 加适量水放入板栗，小火焖40分钟至鸡肉酥烂收汁",
                    Contraindications = "实热证者慎食板栗；便秘者减量；糖尿病患者注意控糖；对坚果过敏者慎食"
                },
                new SolarTermInfo {
                    Month = 10, Day = 23, Name = "霜降", Season = "秋季",
                    Description = "天气渐冷，开始有霜，秋季最后一个节气",
                    DietaryTip = "宜温补健脾，多食牛肉、羊肉、白萝卜、柿子等",
                    HealthTip = "注意防寒保暖，早睡早起，适当进补以迎冬季",
                    RecommendedDish = "萝卜炖牛腩",
                    DishReason = "白萝卜消食化痰，牛腩温补，霜降时节食之健脾暖胃",
                    Ingredients = "牛腩500克、白萝卜1根、生姜5片、八角2个、桂皮1小块、料酒2勺、生抽2勺、盐适量",
                    CookingMethod = "1. 牛切块焯水去血沫；2. 白萝卜去皮切滚刀块；3. 锅中加油爆香姜片、八角、桂皮；4. 放入牛腩翻炒；5. 加入料酒、生抽和足量水；6. 大火煮沸后转小火炖2小时；7. 加入萝卜继续炖30分钟至软烂，加盐调味",
                    Contraindications = "脾胃虚寒者少食白萝卜；气虚者慎食；高尿酸者注意牛腩嘌呤含量"
                },
                new SolarTermInfo {
                    Month = 11, Day = 7, Name = "立冬", Season = "冬季",
                    Description = "冬季开始，万物收藏，阳气潜藏",
                    DietaryTip = "宜温补肾阳，多食羊肉、核桃、栗子、黑豆等",
                    HealthTip = "注意防寒保暖，早睡晚起，适度进补，不宜大汗",
                    RecommendedDish = "核桃黑芝麻糊",
                    DishReason = "核桃补肾，黑芝麻养血，立冬后阳气潜藏，食之补肾暖身",
                    Ingredients = "核桃仁50克、黑芝麻100克、糯米50克、冰糖适量、清水适量",
                    CookingMethod = "1. 核桃仁、黑芝麻分别炒香；2. 糯米炒至微黄；3. 将三种材料放入料理机打成粉；4. 锅中加水煮沸，加入粉末搅拌；5. 小火煮至糊状；6. 加冰糖调味即可",
                    Contraindications = "腹泻期间不宜；大便溏稀者慎食；糖尿病患者注意控糖；对坚果过敏者禁食"
                },
                new SolarTermInfo {
                    Month = 11, Day = 22, Name = "小雪", Season = "冬季",
                    Description = "开始降雪但雪量不大，气温继续下降",
                    DietaryTip = "宜温补御寒，多食羊肉、牛肉、红枣、桂圆等",
                    HealthTip = "注意保暖，多晒太阳以补充阳气，适当增加温性食物",
                    RecommendedDish = "红枣桂圆茶",
                    DishReason = "红枣补血，桂圆温中，小雪时节饮之暖身驱寒",
                    Ingredients = "红枣6枚、桂圆肉15克、枸杞10克、红糖适量、清水适量",
                    CookingMethod = "1. 红枣洗净去核；2. 桂圆肉、枸杞洗净；3. 锅中加水放入红枣、桂圆；4. 大火煮沸后转小火煮20分钟；5. 加入枸杞煮5分钟；6. 加红糖搅拌至融化即可饮用",
                    Contraindications = "实热证、阴虚火旺者慎用；糖尿病患者注意控糖；感冒发热期间不宜饮用"
                },
                new SolarTermInfo {
                    Month = 12, Day = 7, Name = "大雪", Season = "冬季",
                    Description = "降雪量增多，气温骤降，寒冬正式到来",
                    DietaryTip = "宜大补气血，多食羊肉、牛肉、人参、鹿茸等",
                    HealthTip = "注意防寒保暖，减少户外活动，室内保持适宜温度",
                    RecommendedDish = "红烧羊肉",
                    DishReason = "羊肉性温，大雪寒冬食之温补气血，驱寒暖身",
                    Ingredients = "羊肉500克、生姜5片、大葱1根、八角2个、桂皮1小块、料酒2勺、生抽2勺、老抽1勺、冰糖适量、干辣椒适量",
                    CookingMethod = "1. 羊肉切块焯水去血沫；2. 锅中加油爆香姜片、葱段、八角、桂皮；3. 放入羊肉翻炒；4. 加入料酒、生抽、老抽、冰糖翻炒上色；5. 加足量水大火煮沸；6. 转小火炖1.5小时至羊肉酥烂收汁",
                    Contraindications = "阴虚火旺、实热证者慎食羊肉；感冒发热期间不宜；高血压患者注意控盐"
                },
                new SolarTermInfo {
                    Month = 12, Day = 22, Name = "冬至", Season = "冬季",
                    Description = "一年中白昼最短、黑夜最长之日，阴极之至，阳气始生",
                    DietaryTip = "宜温补养阳，多食饺子、汤圆、羊肉、年糕等",
                    HealthTip = "冬至一阳生，注意养阳护阳，早睡晚起，不宜过度劳累",
                    RecommendedDish = "冬至饺子",
                    DishReason = "冬至吃饺子是北方传统，馅料丰富，温补御寒，寓意团圆",
                    Ingredients = "面粉500克、猪肉馅300克、韭菜200克、生姜3片、鸡蛋1个、生抽2勺、香油适量、盐适量",
                    CookingMethod = "1. 面粉加水和成面团醒30分钟；2. 猪肉馅加姜末、鸡蛋、生抽、盐、香油调匀；3. 韭菜洗净切碎拌入肉馅；4. 面团擀成饺子皮包入馅料；5. 锅中加水煮沸放入饺子；6. 水沸后加三次冷水，饺子浮起即可捞出",
                    Contraindications = "韭菜含粗纤维，肠胃不适者慎食；对鸡蛋过敏者调整馅料；高血压患者注意控盐"
                }
            };
        }

        public static SolarTermInfo GetCurrentSolarTerm(DateTime? date = null)
        {
            var now = date ?? DateTime.Now.Date;
            var monthDay = now.Month * 100 + now.Day;

            int bestIndex = -1;
            for (int i = 0; i < Terms.Length; i++)
            {
                var term = Terms[i];
                var termDay = term.Month * 100 + term.Day;
                if (monthDay >= termDay)
                    bestIndex = i;
            }

            if (bestIndex >= 0)
                return Terms[bestIndex];

            return Terms[23];
        }

        public static string GetSeasonDescription(string season)
        {
            return season switch
            {
                "春季" => "春暖花开，万物复苏，宜养肝护阳",
                "夏季" => "夏日炎炎，心火旺盛，宜清热解暑",
                "秋季" => "秋高气爽，燥气渐起，宜润肺养阴",
                "冬季" => "寒冬腊月，阳气潜藏，宜温补养肾",
                _ => string.Empty
            };
        }
    }
}
