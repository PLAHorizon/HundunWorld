#!/usr/bin/env node
/**
 * 五行技能树数据生成器。
 *
 * 唯一数据源：scripts/skilltree/wuxing_skill_tree.json（语义字段）
 * 数值全部由 ladder 公式推导，杜绝 91 行手抄不一致。
 *
 * 产出：
 *   1) scripts/sql/011_seed_wuxing_skill_tree.sql   —— 服务端 91 技能模板 + 91 秘籍 + 91 秘籍物品
 *   2) HundunWorld/Content/GameData/Skills/skills.json —— 客户端 SkillConfig 目录（当前该文件缺失，全走硬编码兜底）
 *
 * 用法：node scripts/skilltree/generate.mjs
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(here, '..', '..');
const data = JSON.parse(fs.readFileSync(path.join(here, 'wuxing_skill_tree.json'), 'utf8'));
const L = data.ladder;

/** 客户端 SkillConfig.Type → 服务端 skill_type（见 SkillTemplateEntity 注释：0主动攻击 1主动辅助 2被动增益 3心法 4轻功 5内功 6绝学） */
const SERVER_SKILL_TYPE = { ActiveAttack: 0, Control: 0, Support: 1, Dash: 4, Ultimate: 6 };

function fail(msg) { console.error('✗ ' + msg); process.exitCode = 1; throw new Error(msg); }
function round2(v) { return Math.round(v * 100) / 100; }

/** 技能在阶梯上的序号 k：基础式按 行×阶×境 展平，道阶与混沌接在其后。 */
function kOf(grade, stage) { return 3 * (grade - 1) + (stage - 1); }

function multiplier(k) { return round2(L.multiplierBase * Math.pow(L.multiplierGrowth, k)); }

function energy(grade, stage, k) {
  if (grade === 6) return L.energyDao[stage - 1];
  if (grade === 7) return L.energyHundun;
  return L.energyBase + L.energyPerK * k;
}

function ladderFor(grade, stage, elementKey) {
  const k = grade === 7 ? 18 : kOf(grade, stage);
  const idx = stage - 1;
  return {
    k,
    damageMultiplier: multiplier(k),
    energyCost: energy(grade, stage, k),
    cooldown: L.cooldownByGrade[grade][idx],
    range: L.rangeByGrade[grade][idx],
    aoeRadius: L.aoeByGrade[grade][idx],
    requiredLevel: L.levelByGrade[grade][idx],
    maxLevel: L.maxSkillLevelByGrade[grade],
    maxProficiency: L.proficiencyByGrade[grade],
    comprehension: L.comprehensionByK[k],
    realm: data.grades.find(g => g.grade === grade).realm,
    bookQuality: L.bookQualityByGrade[grade],
    bookType: grade === 7 ? 3 : L.bookTypeByStage[stage],
    learnSuccessRate: L.learnSuccessByGrade[grade],
    learnDuration: L.learnDurationMinByGrade[grade],
    useLimit: L.useLimitByGrade[grade],
    elementKey,
  };
}

/**
 * 14.2 分层矩阵用到的特殊灵材（研读成功时消耗）。物品 id 段 910001-910010，
 * 每个五行域各一粗一炼：粗材 910001..910005（按域），炼材 910006..910010。
 */
const MATERIAL_TIER1 = { 1: 910001, 2: 910002, 3: 910003, 4: 910004, 5: 910005 };
const MATERIAL_TIER2 = { 1: 910006, 2: 910007, 3: 910008, 4: 910009, 5: 910010 };
const MATERIAL_NAMES = {
  910001: ['锐金砂', '金'], 910002: ['青霭露', '木'], 910003: ['玄冰髓', '水'], 910004: ['炎心晶', '火'], 910005: ['厚土精', '土'],
  910006: ['凝金魄', '金'], 910007: ['乙木心', '木'], 910008: ['弱水精', '水'], 910009: ['三昧火砂', '火'], 910010: ['戊土髓', '土'],
};

// ---------------------------------------------------------------------------
// 1. 展平成行
// ---------------------------------------------------------------------------
const rows = [];

for (const group of data.skills) {
  const el = data.elements.find(e => e.key === group.el);
  if (!el) fail(`未知五行 key: ${group.el}`);
  for (const [id, grade, stage, series, name, type, rangeType, effect, extra] of group.rows) {
    rows.push(mkRow({ id, grade, stage, series, name, type, rangeType, effect, extra, element: el }));
  }
}

for (const pair of data.evolutionPairs) {
  const mother = data.elements.find(e => e.key === pair.mother);
  const child = data.elements.find(e => e.key === pair.child);
  for (const [id, stage, name, type, rangeType, effect, extra] of pair.rows) {
    rows.push(mkRow({
      id, grade: 6, stage, series: pair.series, name, type, rangeType, effect, extra,
      element: child,                       // 演化式归属"子"行（被生出来的那一行）
      pair: pair.pair,
      consume: [gradeStageId(mother, 5, stage), gradeStageId(child, 5, stage)],
      loopNeed: pair.loopNeed[stage - 1],
    }));
  }
}

{
  const u = data.ultimate;
  rows.push(mkRow({
    id: u.id, grade: 7, stage: 3, series: u.series, name: u.name, type: u.type, rangeType: u.rangeType,
    effect: u.effect, extra: { needPairsCompleted: u.needPairsCompleted, loopNeed: u.loopNeed },
    element: { key: 'None', cn: '混沌', wuxing: 0, itemElement: 5 },
    aoeOverride: u.aoeRadius, rangeOverride: u.range, comprehensionOverride: u.comprehension,
  }));
}

function gradeStageId(el, grade, stage) { return el.wuxing * 10000 + grade * 100 + stage; }

function mkRow(o) {
  const lad = ladderFor(o.grade, o.stage, o.element.key);
  if (o.rangeOverride !== undefined) lad.range = o.rangeOverride;
  if (o.aoeOverride !== undefined) lad.aoeRadius = o.aoeOverride;
  if (o.comprehensionOverride !== undefined) lad.comprehension = o.comprehensionOverride;

  const single = o.rangeType === 'Single' || o.rangeType === 'Self';
  const maxTargets = single ? 1 : o.rangeType === 'Line' ? (o.extra?.pierce ?? 3) : (o.extra?.toAllies ? 5 : 8);
  const aoe = single || o.rangeType === 'Line' ? 0 : (o.rangeType === 'Sector' ? lad.aoeRadius * 0.6 : lad.aoeRadius);

  // 前置链：同阶内 初→中→极；跨阶由"上一阶极境"引下（白阶初式无前置）。
  let preSkillId = 0;
  if (o.grade === 6) {
    preSkillId = o.stage === 1 ? 0 : o.id - 1;                 // 道阶三境自链
  } else if (o.grade === 7) {
    preSkillId = 0;
  } else if (o.stage > 1) {
    preSkillId = o.id - 1;
  } else if (o.grade > 1) {
    preSkillId = gradeStageId(o.element, o.grade - 1, 3);
  }

  return {
    ...o,
    lad,
    maxTargets,
    aoeRadius: round2(aoe),
    aoeAngle: o.rangeType === 'Sector' ? 90 : 0,
    preSkillId,
    castTime: castTimeOf(o.type),
  };
}

function castTimeOf(type) {
  return type === 'Ultimate' ? 1.0 : type === 'Support' ? 0.6 : 0.3;
}

// ---------------------------------------------------------------------------
// 2. 自检
// ---------------------------------------------------------------------------
const expectedBase = 5 * 5 * 3, expectedDao = 5 * 3;
const baseRows = rows.filter(r => r.grade <= 5);
if (baseRows.length !== expectedBase) fail(`基础式应为 ${expectedBase}，实得 ${baseRows.length}`);
if (rows.filter(r => r.grade === 6).length !== expectedDao) fail(`道阶应为 ${expectedDao}，实得 ${rows.filter(r => r.grade === 6).length}`);
if (rows.length !== 91) fail(`总数应为 91，实得 ${rows.length}`);

const ids = rows.map(r => r.id);
if (new Set(ids).size !== ids.length) fail('存在重复技能 id');

for (const el of data.elements) {
  for (let g = 1; g <= 5; g++) {
    for (let s = 1; s <= 3; s++) {
      const want = gradeStageId(el, g, s);
      if (!rows.some(r => r.id === want)) fail(`缺行：${el.cn} 阶${g} 境${s}（id ${want}）`);
    }
  }
}
for (const r of rows) {
  if (r.preSkillId && !rows.some(x => x.id === r.preSkillId)) fail(`前置 id 不存在：${r.id} → ${r.preSkillId}`);
  for (const c of (r.consume || [])) if (!rows.some(x => x.id === c)) fail(`演化消耗的 id 不存在：${r.id} → ${c}`);
}
if (process.exitCode) throw new Error('校验未通过');

// ---------------------------------------------------------------------------
// 3. 客户端 skills.json
// ---------------------------------------------------------------------------
const clientEntries = rows.map(r => ({
  id: r.id,
  name: displayName(r),
  description: r.effect,
  icon: `Content/Icons/Skills/${r.element.key.toLowerCase()}_${r.grade}${r.stage}.flax`,
  element: r.element.key,
  type: r.type,
  damageMultiplier: r.lad.damageMultiplier,
  energyCost: r.lad.energyCost,
  cooldown: r.lad.cooldown,
  range: r.lad.range,
  castTime: r.castTime,
  rangeType: r.rangeType,
  aoeRadius: r.aoeRadius,
  aoeAngle: r.aoeAngle,
  requiredLevel: r.lad.requiredLevel,
  maxLevel: r.lad.maxLevel,
  critRate: round2(0.05 + 0.002 * r.lad.k),
  critMultiplier: round2(1.5 + 0.02 * r.grade),
  comboNextId: 0,
  comboWindow: 0.8,
  comboIndex: 0,
  effects: effectsOf(r),
  damagePerLevel: round2(0.10 + 0.01 * r.grade),
  canMoveWhileCasting: r.type === 'Support' && r.rangeType === 'Self',
  knockbackForce: r.extra?.knockback ?? 0,
  // 阶梯语义：SkillDatabase/技能树页按这几键画格子与判门槛
  grade: r.grade,
  gradeStage: r.stage,
  seriesId: `${r.element.key}_${r.grade}`,
  preSkillId: r.preSkillId,
  comprehensionRequire: r.lad.comprehension,
  // 演化式回指被它融合掉的两式：客户端据此判断"哪些式已被封存"，不必再抄一份相生对照表。
  pair: r.pair ?? 0,
  evolveFrom: r.consume ?? [],
  // §6.2 的两条计数门槛。技能树页要提前把"周天圆满 3/12"显示出来，
  // 否则玩家只在读书被拒的那一刻才知道缺什么，白跑一趟秘境。
  loopNeed: r.loopNeed ?? r.extra?.loopNeed ?? 0,
  needPairsCompleted: r.extra?.needPairsCompleted ?? 0,
  // §14 的动态门槛原样带上（好感域 / 材料清单 / 基础属性）。技能树页要预先告诉玩家
  // "金域好感 12/40、缺锐金砂×6、体魄 20/36"，而不是等他读卷被拒那一刻才知道缺什么；
  // 判定仍在服务端，这份只是显示用的同一份数据，不是第二次抄写。
  learnCost: JSON.parse(learnCostOf(r)),
}));

function effectsOf(r) {
  const out = [];
  const x = r.extra || {};
  if (x.pen) out.push({ type: 'Penetration', duration: 0, value: x.pen, chance: 1 });
  if (x.dotRatio) out.push({ type: 'DamageOverTime', duration: x.dotSec ?? 4, value: x.dotRatio, chance: 1 });
  if (x.slowPct) out.push({ type: 'Slow', duration: x.slowSec ?? 3, value: x.slowPct, chance: 1 });
  if (x.stunSec) out.push({ type: 'Stun', duration: x.stunSec, value: 0, chance: 1 });
  if (x.rootSec) out.push({ type: 'Root', duration: x.rootSec, value: 0, chance: 1 });
  if (x.shieldRatio) out.push({ type: 'Shield', duration: 6, value: x.shieldRatio, chance: 1 });
  if (x.healPct) out.push({ type: 'Heal', duration: 0, value: x.healPct, chance: 1 });
  return out;
}

const skillsJsonPath = path.join(repoRoot, 'HundunWorld', 'Content', 'GameData', 'Skills', 'skills.json');
fs.mkdirSync(path.dirname(skillsJsonPath), { recursive: true });
// SkillDatabase.TryLoadFromJson 反序列化的是 { "Skills": [...] } 这个对象根。
// 两层都不能少：裸数组根会让 Flax 启动时的 JSON 资产探测（rapidjson FindMember 断言 IsObject）崩编辑器；
// 而包错键名会让解析拿到空表并静默退回 26 条硬编码。
fs.writeFileSync(skillsJsonPath, JSON.stringify({ Skills: clientEntries }, null, 2) + '\n', 'utf8');

// ---------------------------------------------------------------------------
// 4. 服务端种子 SQL
// ---------------------------------------------------------------------------
const q = s => `N'${String(s ?? '').replace(/'/g, "''")}'`;

/**
 * 从既有可执行种子脚本里抓出真实列清单，避免手写 VALUES 与列数错位
 * （006/007 是线上跑通过的样本，直接拿它们的列序当权威）。
 */
function parseColumns(sqlFile, table) {
  const text = fs.readFileSync(path.join(repoRoot, 'scripts', 'sql', sqlFile), 'utf8');
  const m = text.match(new RegExp(`INSERT INTO\\s+(?:dbo\\.)?${table}\\s*\\(([^)]*)\\)\\s*VALUES`, 'i'));
  if (!m) fail(`无法从 ${sqlFile} 解析 ${table} 的列清单`);
  return m[1].split(',').map(s => s.trim()).filter(Boolean);
}

const ITEM_BASE_COLS = parseColumns('006_seed_equipment_item_templates.sql', 'Game_HunduShijie_ItemTemplate');
const SKILL_BASE_COLS = parseColumns('007_seed_skill_templates.sql', 'Game_HunduShijie_SkillTemplate');

// 007 当年跳过了可空的前置列；本树全靠前置链表达阶/境递进，必须补上。
// 另四列来自 A1 迁移新增：脚本引用它们，迁移没先落库就会整批失败（不是静默降级）。
const SKILL_EXTRA_COLS = ['pre_skill_id', 'pre_skill_level', 'advance_skill_id', 'skill_grade', 'grade_stage', 'series_id', 'damage_multiplier'];
const SKILL_COLS = [...SKILL_BASE_COLS, ...SKILL_EXTRA_COLS.filter(c => !SKILL_BASE_COLS.includes(c))];
const BOOK_COLS = ['book_name', 'description', 'skill_id', 'quality', 'book_type', 'level_require', 'comprehension_require',
  'learn_duration', 'learn_success_rate', 'max_skill_level', 'use_limit', 'extra_effects', 'source_info', 'create_time',
  'IsValid', 'GameId', 'AreaId', 'ServerId', 'GameUserId'];

/** 按列名取值：values 里没有的列直接报错，杜绝漏列/错序。 */
function buildInsert(table, cols, values) {
  const missing = cols.filter(c => !(c in values));
  if (missing.length) fail(`${table} 缺少列值：${missing.join(', ')}`);
  const ordered = cols.map(c => values[c]);
  return `INSERT INTO ${table} (${cols.join(', ')}) VALUES (${ordered.join(', ')})`;
}

/** 006 里那套物品默认值，按列名回填；秘籍行只覆盖我们真正在意的字段。 */
function itemValues(r) {
  const lad = r.lad;
  const itemId = 800000 + r.id;
  const itemName = `${r.series}·${r.name}${lad.bookType === 1 ? '残卷' : lad.bookType === 3 ? '玉简' : '秘籍'}`;
  const useEffect = JSON.stringify({ kind: 'learn_skill', skillId: r.id, successRate: lad.learnSuccessRate, durationMin: lad.learnDuration });
  // §14.2 的获取渠道也写成数据：白黄=任务+该域杂兵掉落，玄地=该域精英，天=世界 BOSS，
  // 道阶不是拾取物（演化的产物），混沌只在世界 BOSS 那一条路上。
  // 权威字段是 via（数组，能表达"一本书既能任务拿也能刷怪掉"）；下面那个 source_type 是单值粗标签，
  // 掉落装配不看它——曾经看过，结果白黄 30 本初级书全被挡在掉落表外，"刷怪获得初级技能书"静默为空。
  const acquisition = {
    1: { via: ['quest', 'loot'], dropRate: 0.08 },
    2: { via: ['quest', 'loot'], dropRate: 0.05 },
    3: { via: ['loot'], dropRate: 0.03 },
    4: { via: ['loot'], dropRate: 0.02 },
    5: { via: ['worldBoss'], dropRate: 0.01 },
    6: { via: ['evolve'], dropRate: 0 },
    7: { via: ['worldBoss'], dropRate: 0.005 },
  }[r.grade];
  const sourceDetail = JSON.stringify({
    ...acquisition,
    domain: r.element.wuxing,          // 掉它/交它的怪属于哪一五行域（E5 装配掉落表用）
    skillId: r.id,
  });
  return {
    item_id: itemId,
    item_name: q(itemName),
    description: q(`${r.element.cn}行·${r.grade}阶${r.stage}境秘籍：${r.effect}`),
    item_type: 6,                          // 6=秘籍（ItemTemplateEntity 注释）
    sub_type: 0,
    base_quality: lad.bookQuality,
    rarity: Math.min(5, r.grade),
    element: r.element.itemElement,        // 装备侧五行编号：0金1木2水3火4土5无
    material_grade: 0,
    level_require: lad.requiredLevel,
    realm_require: lad.realm,
    profession_limit: q(''),               // §14.5：习得不看职业/门派，这列恒空，也不参与任何判定
    max_stack: 1,
    drop_rate: acquisition.dropRate,
    source_type: acquisition.via.includes('quest') ? 1 : 2,   // 1=任务产出 2=战斗产出
    source_detail: q(sourceDetail),
    base_attributes: q(''),
    random_attributes: q(''),
    random_attr_count: q(''),
    gem_slot_rates: q(''),
    synthesis_recipe: q(''),
    inherit_rate: '1.00',
    element_bonus: '1.00',
    element_penalty: 0,
    bind_type: 0,
    sell_price: 0,
    buy_price: 0,
    icon_path: q(`Content/Icons/Books/book_${r.grade}${r.stage}.flax`),
    model_path: q(''),
    use_effect: q(useEffect),
    can_trade: 1,
    can_destroy: 0,
    is_unique: 0,
    valid_days: 0,
    create_time: 'GETUTCDATE()',
    update_time: 'GETUTCDATE()',
    IsValid: 1,
    GameId: 0,
    AreaId: 0,
    ServerId: 0,
    GameUserId: "'00000000-0000-0000-0000-000000000000'",
  };
}


/**
 * 这一式的动态门槛（§14.2）。矩阵按"阶给种类、境给数量"生成，不逐行手抄：
 * 白黄无额外条件（书就是任务与刷怪给的）；玄阶看该域好感；地阶看材料；
 * 天阶材料+好感；道阶再加基础属性（连同 §6.2 的树完整性）；混沌要五域好感均达标。
 * 数值都是内容参数，改这里就能调，不用动服务端判定。
 */
function gatesFor(r) {
  const g = r.grade, s = r.stage, domain = r.element.wuxing;
  const scale = 0.6 + 0.2 * s;                       // 初 0.8 / 中 1.0 / 极 1.2
  const gates = {};
  const materials = [];

  const favorMin = (base) => base + 5 * (s - 1);
  const attr = (base) => Math.round(base * scale);

  if (g === 3) {
    gates.favorNeed = { domain, min: favorMin(20) };
  } else if (g === 4) {
    materials.push({ itemId: MATERIAL_TIER1[domain], count: Math.ceil(4 * scale) });
  } else if (g === 5) {
    materials.push({ itemId: MATERIAL_TIER1[domain], count: Math.ceil(8 * scale) });
    materials.push({ itemId: MATERIAL_TIER2[domain], count: Math.ceil(3 * scale) });
    gates.favorNeed = { domain, min: favorMin(55) };
  } else if (g === 6) {
    materials.push({ itemId: MATERIAL_TIER1[domain], count: Math.ceil(12 * scale) });
    materials.push({ itemId: MATERIAL_TIER2[domain], count: Math.ceil(5 * scale) });
    gates.favorNeed = { domain, min: favorMin(75) };
    gates.attrNeed = { strength: attr(24), agility: attr(24), health: attr(36), intelligence: attr(48) };
  } else if (g === 7) {
    // 混沌终式跨全域：不存在"只混金水两家就能悟"的路径
    for (const d of [1, 2, 3, 4, 5]) {
      materials.push({ itemId: MATERIAL_TIER2[d], count: Math.ceil(4 * scale) });
    }
    gates.favorNeed = { allDomains: true, min: favorMin(100) };
    gates.attrNeed = { strength: attr(48), agility: attr(48), health: attr(72), intelligence: attr(96) };
  }

  if (materials.length) gates.materials = materials;
  return gates;
}

function learnCostOf(r) {
  const gates = gatesFor(r);
  if (r.grade === 6) {
    return JSON.stringify({
      pair: r.pair, consume: r.consume, loopNeed: r.loopNeed,
      affinityNeed: r.lad.comprehension, bookItemId: 800000 + r.id, ...gates,
    });
  }
  if (r.grade === 7) {
    return JSON.stringify({
      needPairsCompleted: r.extra.needPairsCompleted, loopNeed: r.extra.loopNeed,
      affinityNeed: r.lad.comprehension, bookItemId: 800000 + r.id, ...gates,
    });
  }
  return JSON.stringify({
    grade: r.grade, gradeStage: r.stage,
    affinityNeed: r.lad.comprehension, proficiencyNeed: r.lad.maxProficiency,
    bookItemId: 800000 + r.id, ...gates,
  });
}

function bookValues(r) {
  const lad = r.lad;
  const bookName = `${r.series}·${r.name}${lad.bookType === 1 ? '残卷' : lad.bookType === 3 ? '传承玉简' : '秘籍'}`;
  const src = r.grade === 6 ? '相生演化（不可拾取）' : r.grade === 7 ? '世界BOSS唯一掉落' : r.grade === 1 ? '门派入门传授/补读' : '秘境·世界事件·坊市';
  return {
    book_name: q(bookName),
    description: q(`习得《${r.name}》（${r.grade}阶·${r.stage}境）：${r.effect}`),
    skill_id: r.id,
    quality: lad.bookQuality,
    book_type: lad.bookType,
    level_require: lad.requiredLevel,
    comprehension_require: lad.comprehension,
    learn_duration: lad.learnDuration,
    learn_success_rate: lad.learnSuccessRate,
    max_skill_level: lad.maxLevel,
    use_limit: lad.useLimit,
    extra_effects: q(JSON.stringify({ grade: r.grade, gradeStage: r.stage, itemId: 800000 + r.id })),
    source_info: q(src),
    create_time: 'GETUTCDATE()',
    IsValid: 1,
    GameId: 0,
    AreaId: 0,
    ServerId: 0,
    GameUserId: "'00000000-0000-0000-0000-000000000000'",
  };
}

const out = [];
out.push(`-- 011_seed_wuxing_skill_tree.sql
-- 五行技能树全套数据：91 技能模板 + 91 秘籍 + 91 秘籍物品。
-- 由 scripts/skilltree/generate.mjs 从 scripts/skilltree/wuxing_skill_tree.json 生成，请勿手改。
-- 设计口径见 docs/SKILL_WUXING_TREE_DESIGN.md（D1=75式独立、D2=演化封存源技、D3=接入倍率且基座降到0.25、D4=保留五行强制兼修）。
--
-- ⚠ 依赖 A1 迁移先落库：本脚本引用 skill_grade / grade_stage / series_id / damage_multiplier 四列，
--   以及 CharacterSkillEntity 的 is_retired / retired_into_skill_id。迁移未应用就跑本脚本会整批失败（不是静默降级）。
--   顺序：dotnet ef migrations add → migrations script --idempotent + sqlcmd 应用 → 再跑本脚本 → 重启 Silo。
--
-- 约定：
--   · damage_multiplier 为 §3.2 阶梯值（服务端结算接入在 A5，接入前 CombatGrain 仍按 AttackPower×${L.baseDamageFactor} 结算）。
--   · energy/cooldown/cast_time 公式列写常数（常数是最简单的公式），与客户端 skills.json 同值。
--   · skill_attack / skill_penetration 一律 0：破甲与五行附加攻击属数值设计，本脚本不编造，只把系数写进 skill_effects JSON。
--   · 演化关系放在道阶行的 learn_cost JSON（consume=融合掉的两个源技）。源技可同时出现在两段弧上，
--     因此不能用单值列 advance_skill_id 表达，现有列只能标 can_advance=1。
--   · 技能 wuxing_element 用战斗侧编号（1金2木3水4火5土）；秘籍物品 element 用装备侧编号（0金1木2水3火4土5无）——两套序不同，转换见 data.elements。item_type=6 即秘籍。
--   · 白阶 15 式由门派传授（learn_source=0），仍登记完整秘籍供补读，成功率 100%。
--   · 可重复执行：技能与物品按 id 存在则 UPDATE，秘籍按 skill_id 存在则 UPDATE。

SET NOCOUNT ON;
SET IDENTITY_INSERT dbo.Game_HunduShijie_SkillTemplate ON;
`);

/** 显示名：混沌终式不带"行·系"前缀，避免"混沌·混沌·混沌一炁"这种叠字。 */
function displayName(r) {
  return r.element.key === 'None' ? r.name : `${r.element.cn}·${r.series}·${r.name}`;
}

function skillValues(r) {
  const lad = r.lad;
  const effects = q(JSON.stringify({ ...(r.extra || {}), rangeType: r.rangeType, aoeRadius: r.aoeRadius, maxTargets: r.maxTargets, grade: r.grade, gradeStage: r.stage }));
  return {
    skill_id: r.id,
    skill_name: q(displayName(r)),
    description: q(r.effect),
    skill_type: SERVER_SKILL_TYPE[r.type],
    skill_school: 0,                 // 不编造门派归属
    sect_id: 0,
    profession_limit: q(''),
    max_level: lad.maxLevel,
    learn_level_require: lad.requiredLevel,
    learn_realm_require: lad.realm,
    pre_skill_id: r.preSkillId || 'NULL',
    pre_skill_level: r.preSkillId ? 3 : 0,
    learn_cost: q(learnCostOf(r)),
    comprehension_require: lad.comprehension,
    energy_cost_formula: q(String(lad.energyCost)),
    cooldown_formula: q(String(lad.cooldown)),
    cast_time_formula: q(String(r.castTime)),
    attack_range: lad.range,
    target_type: 0,                  // 作用形状以 skill_effects/客户端 skills.json 为准，不猜枚举语义
    effect_range: r.aoeRadius,
    max_targets: r.maxTargets,
    skill_effects: effects,
    level_effects: q(''),
    combo_skill_ids: q(''),
    can_create: 0,
    can_advance: r.grade === 5 ? 1 : 0,
    advance_skill_id: 0,
    icon_path: q(`Content/Icons/Skills/${r.element.key.toLowerCase()}_${r.grade}${r.stage}.flax`),
    effect_id: 0,
    sound_id: 0,
    create_time: 'GETUTCDATE()',
    update_time: 'GETUTCDATE()',
    IsValid: 1,
    GameId: 0,
    AreaId: 0,
    ServerId: 0,
    GameUserId: "'00000000-0000-0000-0000-000000000000'",
    wuxing_element: r.element.wuxing,
    skill_attack: 0,
    skill_penetration: 0,
    skill_grade: r.grade,
    grade_stage: r.stage,
    series_id: q(`${r.element.key}_${r.grade}`),
    damage_multiplier: lad.damageMultiplier,
  };
}

/** 幂等 UPDATE 只刷真正会变的字段；create/IsValid/审计位留给首次插入。 */
const SKILL_UPDATE_COLS = ['skill_name', 'description', 'skill_type', 'max_level', 'learn_level_require', 'learn_realm_require',
  'pre_skill_id', 'pre_skill_level', 'learn_cost', 'comprehension_require', 'energy_cost_formula', 'cooldown_formula', 'cast_time_formula',
  'attack_range', 'effect_range', 'max_targets', 'skill_effects', 'can_advance', 'icon_path', 'wuxing_element',
  'skill_grade', 'grade_stage', 'series_id', 'damage_multiplier'];

for (const r of rows) {
  const v = skillValues(r);
  const sets = SKILL_UPDATE_COLS.map(c => `${c} = ${v[c]}`).join(', ');
  const lad = r.lad;
  out.push(`-- ${r.id} ${r.element.cn}·${r.series}·${r.name}｜${r.grade}阶·${r.stage}境｜倍率${lad.damageMultiplier} 耗气${lad.energyCost} CD${lad.cooldown}s 射程${lad.range}${r.aoeRadius ? ` AOE${r.aoeRadius}` : ''}｜前置${r.preSkillId || '无'}
IF EXISTS (SELECT 1 FROM Game_HunduShijie_SkillTemplate WHERE skill_id = ${r.id})
    UPDATE Game_HunduShijie_SkillTemplate SET ${sets}, update_time = GETUTCDATE() WHERE skill_id = ${r.id};
ELSE
    ${buildInsert('Game_HunduShijie_SkillTemplate', SKILL_COLS, v)};
`);
}

out.push(`
SET IDENTITY_INSERT dbo.Game_HunduShijie_SkillTemplate OFF;

-- ===== 91 本秘籍（Game_HunduShijie_SkillBook，book_id 为自增，按 skill_id 幂等） =====
`);
for (const r of rows) {
  const lad = r.lad;
  const bookName = `${r.series}·${r.name}${lad.bookType === 1 ? '残卷' : lad.bookType === 3 ? '传承玉简' : '秘籍'}`;
  const src = r.grade === 6 ? '相生演化（不可拾取）' : r.grade === 7 ? '世界BOSS唯一掉落' : r.grade === 1 ? '门派入门传授/补读' : '秘境·世界事件·坊市';
  out.push(`-- 秘籍 ${r.id} → ${q(bookName)} 品质${lad.bookQuality} 类型${lad.bookType} 成功率${lad.learnSuccessRate} 时长${lad.learnDuration}min 限用${lad.useLimit}
IF EXISTS (SELECT 1 FROM Game_HunduShijie_SkillBook WHERE skill_id = ${r.id})
    UPDATE Game_HunduShijie_SkillBook SET book_name = ${q(bookName)}, description = ${q(`习得《${r.name}》（${r.grade}阶·${r.stage}境）：${r.effect}`)},
        quality = ${lad.bookQuality}, book_type = ${lad.bookType}, level_require = ${lad.requiredLevel}, comprehension_require = ${lad.comprehension},
        learn_duration = ${lad.learnDuration}, learn_success_rate = ${lad.learnSuccessRate}, max_skill_level = ${lad.maxLevel},
        use_limit = ${lad.useLimit}, extra_effects = ${q(JSON.stringify({ grade: r.grade, gradeStage: r.stage, itemId: 800000 + r.id }))}, source_info = ${q(src)} WHERE skill_id = ${r.id};
ELSE
    ${buildInsert('Game_HunduShijie_SkillBook', BOOK_COLS, bookValues(r))}
`);
}

out.push(`
-- ===== 91 件秘籍物品（item_type=6 秘籍，item_id = 800000 + 技能 id） =====
-- IDENTITY_INSERT 一个会话只允许一张表为 ON，所以它必须开在这里（技能段已 OFF），
-- 不能和文件头那行并列——并列会在第一条 SET 之后整批失败，一条都写不进去。
SET IDENTITY_INSERT dbo.Game_HunduShijie_ItemTemplate ON;
`);
for (const r of rows) {
  const lad = r.lad;
  const v = itemValues(r);
  const itemName = `${r.series}·${r.name}${lad.bookType === 1 ? '残卷' : lad.bookType === 3 ? '玉简' : '秘籍'}`;
  const itemId = 800000 + r.id;
  out.push(`-- 物品 ${itemId} → ${itemName}（教技能 ${r.id}；element 走装备侧编号 ${r.element.itemElement}=${r.element.cn}）
IF EXISTS (SELECT 1 FROM Game_HunduShijie_ItemTemplate WHERE item_id = ${itemId})
    UPDATE Game_HunduShijie_ItemTemplate SET item_name = ${v.item_name}, description = ${v.description}, item_type = 6, base_quality = ${v.base_quality},
        rarity = ${v.rarity}, element = ${v.element}, level_require = ${v.level_require}, realm_require = ${v.realm_require},
        max_stack = ${v.max_stack}, drop_rate = ${v.drop_rate}, source_type = ${v.source_type}, source_detail = ${v.source_detail},
        use_effect = ${v.use_effect}, icon_path = ${v.icon_path}, update_time = GETUTCDATE() WHERE item_id = ${itemId};
ELSE
    ${buildInsert('Game_HunduShijie_ItemTemplate', ITEM_BASE_COLS, v)};
`);
}

// §14.2 的"特殊材料"本身也得先有模板行，否则门槛引用的是一个不存在的物品，
// 玩家永远凑不齐、服务端也永远查不到——这类"门槛指向空对象"的错最难从报错里看出来。
// 这 10 行必须留在上面 ItemTemplate 的 IDENTITY_INSERT 开窗之内（一张会话只允许一表为 ON）。
// 好感度的来源不能只有任务：再加一类"五行信物"消耗品，用掉换该域好感。
// 它同时是 §14.2 玄阶门槛的产出物（该域怪掉 / 该域任务给），所以中级技能的解锁路径
// 是"与该域的往来"，而不是"某条隐藏计数器"。
const TOKEN_ITEMS = {
  1: [910011, '金行信物'], 2: [910012, '木行信物'], 3: [910013, '水行信物'],
  4: [910014, '火行信物'], 5: [910015, '土行信物'],
};
const TOKEN_FAVOR = 10;

out.push(`
-- ===== 5 件五行信物（item_type=2 消耗品：用掉换该域好感 §14.2） =====
`);
for (const [domain, [tokenId, tokenName]] of Object.entries(TOKEN_ITEMS)) {
  const cn = { 1: '金', 2: '木', 3: '水', 4: '火', 5: '土' }[domain];
  const v = {
    item_id: tokenId,
    item_name: q(tokenName),
    description: q(`${cn}行信物：使用后与${cn}域的用意相通（该域好感 +${TOKEN_FAVOR}）`),
    item_type: 2,                        // 2=消耗品：客户端 IsUsable 只认这一类，才能装栏与直接使用
    sub_type: 0,
    base_quality: 1,
    rarity: 2,
    element: Number(domain) - 1,
    material_grade: 0,
    level_require: 1,
    realm_require: 0,
    profession_limit: q(''),
    max_stack: 99,
    drop_rate: 0.10,
    source_type: 2,
    source_detail: q(JSON.stringify({ via: ['loot', 'quest'], domain: Number(domain) })),
    base_attributes: q(''),
    random_attributes: q(''),
    random_attr_count: q(''),
    gem_slot_rates: q(''),
    synthesis_recipe: q(''),
    inherit_rate: '1.00',
    element_bonus: '1.00',
    element_penalty: 0,
    bind_type: 0,
    sell_price: 30,
    buy_price: 0,
    icon_path: q(`Content/Icons/Materials/token_${domain}.flax`),
    model_path: q(''),
    // 服务端 InventoryGrain 认这个结构；一次一件，用掉即消耗，所以成本本身就是防刷的闸门
    use_effect: q(JSON.stringify({ kind: 'favor', domain: Number(domain), delta: TOKEN_FAVOR })),
    can_trade: 1,
    can_destroy: 1,
    is_unique: 0,
    valid_days: 0,
    create_time: 'GETUTCDATE()',
    update_time: 'GETUTCDATE()',
    IsValid: 1,
    GameId: 0,
    AreaId: 0,
    ServerId: 0,
    GameUserId: "'00000000-0000-0000-0000-000000000000'",
  };
  out.push(`-- 信物 ${tokenId} ${tokenName}（${cn}域好感 +${TOKEN_FAVOR}）
IF EXISTS (SELECT 1 FROM Game_HunduShijie_ItemTemplate WHERE item_id = ${tokenId})
    UPDATE Game_HunduShijie_ItemTemplate SET item_name = ${v.item_name}, description = ${v.description}, item_type = 2, base_quality = ${v.base_quality}, rarity = ${v.rarity}, element = ${v.element}, max_stack = ${v.max_stack}, drop_rate = ${v.drop_rate}, source_type = ${v.source_type}, source_detail = ${v.source_detail}, use_effect = ${v.use_effect}, icon_path = ${v.icon_path}, update_time = GETUTCDATE() WHERE item_id = ${tokenId};
ELSE
    ${buildInsert('Game_HunduShijie_ItemTemplate', ITEM_BASE_COLS, v)};
`);
}

out.push(`
-- ===== 10 种五行灵材（item_type=3 材料；地阶以上研读时消耗） =====
`);
for (const [domain, cn] of [[1, '金'], [2, '木'], [3, '水'], [4, '火'], [5, '土']]) {
  for (const tier of [1, 2]) {
    const id = tier === 1 ? MATERIAL_TIER1[domain] : MATERIAL_TIER2[domain];
    const [name] = MATERIAL_NAMES[id];
    const v = {
      item_id: id,
      item_name: q(name),
      description: q(`${cn}行灵材：研读${tier === 2 ? '高阶' : '本行'}武学所需的特殊材料`),
      item_type: 3,                        // 3=材料（与客户端 ItemKind.Material 同序）
      sub_type: 0,
      base_quality: tier === 1 ? 1 : 3,
      rarity: tier === 1 ? 2 : 4,
      element: domain - 1,                 // 装备侧五行编号：0金1木2水3火4土
      material_grade: tier,
      level_require: 1,
      realm_require: 0,
      profession_limit: q(''),
      max_stack: 99,                       // 与秘籍不同：材料必须能堆
      drop_rate: tier === 1 ? 0.12 : 0.04,
      source_type: 2,
      source_detail: q(JSON.stringify({ via: ['loot', 'gather'], domain })),
      base_attributes: q(''),
      random_attributes: q(''),
      random_attr_count: q(''),
      gem_slot_rates: q(''),
      synthesis_recipe: q(''),
      inherit_rate: '1.00',
      element_bonus: '1.00',
      element_penalty: 0,
      bind_type: 0,
      sell_price: tier === 1 ? 20 : 120,
      buy_price: 0,
      icon_path: q(`Content/Icons/Materials/wuxing_${domain}_${tier}.flax`),
      model_path: q(''),
      use_effect: q(''),
      can_trade: 1,
      can_destroy: 1,
      is_unique: 0,
      valid_days: 0,
      create_time: 'GETUTCDATE()',
      update_time: 'GETUTCDATE()',
      IsValid: 1,
      GameId: 0,
      AreaId: 0,
      ServerId: 0,
      GameUserId: "'00000000-0000-0000-0000-000000000000'",
    };
    out.push(`-- 灵材 ${id} ${name}（${cn}行 ${tier} 级）
IF EXISTS (SELECT 1 FROM Game_HunduShijie_ItemTemplate WHERE item_id = ${id})
    UPDATE Game_HunduShijie_ItemTemplate SET item_name = ${v.item_name}, description = ${v.description}, item_type = 3, base_quality = ${v.base_quality}, rarity = ${v.rarity}, element = ${v.element}, max_stack = ${v.max_stack}, drop_rate = ${v.drop_rate}, source_type = ${v.source_type}, source_detail = ${v.source_detail}, icon_path = ${v.icon_path}, update_time = GETUTCDATE() WHERE item_id = ${id};
ELSE
    ${buildInsert('Game_HunduShijie_ItemTemplate', ITEM_BASE_COLS, v)};
`);
  }
}

out.push(`
SET IDENTITY_INSERT dbo.Game_HunduShijie_ItemTemplate OFF;

PRINT N'[011] 五行技能树：技能 ${rows.length} 条、秘籍 ${rows.length} 条、秘籍物品 ${rows.length} 条已写入';
`);

// 门槛引用到的每一样灵材都必须真的被写成物品行：引用一个不存在的 item_id，
// 现场表现是"玩家永远凑不齐材料"，从任何报错里都看不出来。
{
  const materialIds = new Set(Object.keys(MATERIAL_NAMES).map(Number));
  let gateRows = 0, favorRows = 0, attrRows = 0;
  for (const r of rows) {
    const gates = gatesFor(r);
    if (gates.materials) {
      gateRows++;
      for (const m of gates.materials) {
        if (!materialIds.has(m.itemId)) fail(`技能 ${r.id} 的门槛引用了不存在的灵材 ${m.itemId}`);
        if (!(m.count > 0)) fail(`技能 ${r.id} 的灵材 ${m.itemId} 数量不是正数：${m.count}`);
      }
    }
    if (gates.favorNeed) {
      favorRows++;
      if (!gates.favorNeed.allDomains && ![1, 2, 3, 4, 5].includes(gates.favorNeed.domain))
        fail(`技能 ${r.id} 的好感域非法：${gates.favorNeed.domain}`);
      if (!(gates.favorNeed.min > 0)) fail(`技能 ${r.id} 的好感门槛不是正数`);
    }
    if (gates.attrNeed) attrRows++;
    // §14.5：白黄不许带材料——那两阶的书是任务与刷怪给的，不该再叠一层消耗
    if (r.grade <= 2 && gates.materials) fail(`白/黄阶不该带材料门槛：技能 ${r.id}`);
  }
  // 每个可能被门槛要求到的域都必须有信物可换，否则"刷好感"在某些域上根本没有出路
  const favorDomains = new Set(rows.map(r => gatesFor(r).favorNeed?.domain).filter(d => d));
  for (const d of favorDomains) {
    if (!TOKEN_ITEMS[d]) fail(`第 ${d} 域有好感门槛，却没有对应的五行信物`);
  }
  console.log(`  动态门槛：材料 ${gateRows} 式 / 好感 ${favorRows} 式 / 基础属性 ${attrRows} 式 / 信物 ${Object.keys(TOKEN_ITEMS).length} 件`);
}

// 自检：同一会话只允许一张表的 IDENTITY_INSERT 为 ON。并列两行会在第二条 SET 处整批失败，
// 而 sqlcmd 只在 -b 下返回非零——不加这道检查，下次改列序又可能"看起来跑过了其实一行没写"。
{
  let open = null;
  for (const chunk of out) {
    for (const line of String(chunk).split('\n')) {
      const m = line.match(/SET IDENTITY_INSERT (dbo\.[\w]+) (ON|OFF);/);
      if (!m) continue;
      if (m[2] === 'ON') {
        if (open) throw new Error(`IDENTITY_INSERT 冲突：${open} 仍为 ON 就开了 ${m[1]}`);
        open = m[1];
      } else {
        open = null;
      }
    }
  }
  if (open) throw new Error(`IDENTITY_INSERT 未收尾：${open} 到脚本结束仍是 ON`);
}

// 自检：UPDATE 分支必须写全"产出来源"四列。库里这 91 件秘籍早就存在（007 种子），
// 走的是 UPDATE 而非 INSERT——少写 drop_rate/source_type 时，种子照样 exit 0、
// 客户端目录照样对得上，只有掉落表会静默为空（本次就是这么发现的）。
{
  const sql = out.join('\n');
  const updates = [...sql.matchAll(/UPDATE Game_HunduShijie_ItemTemplate SET ([\s\S]*?) WHERE item_id = (\d+);/g)];
  for (const [, body, id] of updates) {
    for (const col of ['max_stack', 'drop_rate', 'source_type', 'source_detail']) {
      if (!new RegExp(`${col} \\S`).test(body)) {
        throw new Error(`物品 ${id} 的 UPDATE 分支没有写 ${col}：已存在的行会保留旧值，产出链路会静默断掉`);
      }
    }
  }
  if (updates.length === 0) throw new Error('没匹配到任何 ItemTemplate 的 UPDATE 分支，检查语句模板是否改了写法');
}

const sqlPath = path.join(repoRoot, 'scripts', 'sql', '011_seed_wuxing_skill_tree.sql');
fs.writeFileSync(sqlPath, out.join('\n'), 'utf8');

// ---------------------------------------------------------------------------
// 5. 摘要
// ---------------------------------------------------------------------------
const byGrade = {};
for (const r of rows) byGrade[r.grade] = (byGrade[r.grade] || 0) + 1;
console.log(`✓ 校验通过：共 ${rows.length} 式（基础 ${baseRows.length} + 道阶 ${expectedDao} + 混沌 1）`);
console.log(`  阶分布：${Object.entries(byGrade).map(([g, n]) => `${g}阶=${n}`).join(' ')}`);
console.log(`  倍率区间：${rows[0].lad.damageMultiplier} … ${rows[rows.length - 1].lad.damageMultiplier}`);
console.log(`✓ 已生成 ${path.relative(repoRoot, sqlPath)}`);
console.log(`✓ 已生成 ${path.relative(repoRoot, skillsJsonPath)}`);
