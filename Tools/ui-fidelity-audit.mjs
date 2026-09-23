#!/usr/bin/env node
// UI 设计落地保真度审计：把 hundun-yy-ui 设计稿与 Flax 客户端 Ink 页面实现逐页比对。
// 产出：Tools/reports/ui-fidelity-audit.json + .md
// 用法：node Tools/ui-fidelity-audit.mjs
import fs from 'node:fs';
import path from 'node:path';

const ROOT = 'C:/Works/GitHubProjects/HundunWorld';
const DESIGN_PAGES = path.join(ROOT, 'hundun-yy-ui/pages');
const UI_DIR = path.join(ROOT, 'HundunWorld/Source/Game/UI');
const ROUTER = path.join(UI_DIR, 'Ink/InkPageRouter.cs');
const MAIN = path.join(UI_DIR, 'MainUIManager.cs');
const OUT_DIR = path.join(ROOT, 'Tools/reports');
const readUtf8 = (p) => fs.readFileSync(p, 'utf8');

// ---------- 1. InkPageDomIds 常量表 ----------
const routerSrc = readUtf8(ROUTER);
const domConst = {};
for (const m of routerSrc.matchAll(/public const string (\w+)\s*=\s*"([^"]*)"/g)) domConst[m[1]] = m[2];

// ---------- 2. MainUIManager 注册表 + 工厂返回类型 ----------
const mainSrc = readUtf8(MAIN);
const registrations = [];
for (const m of mainSrc.matchAll(/_inkPageRouter\.RegisterPage\(InkPageDomIds\.(\w+),\s*\(\)\s*=>\s*Create(\w+)\(\)\s*\)\s*;?([^\n]*)/g)) {
  registrations.push({ domConst: m[1], domId: domConst[m[1]] ?? null, factory: 'Create' + m[2], note: (m[3] || '').replace(/^\/\/\s*/, '').trim() });
}

const csFiles = [];
(function walk(dir) {
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    const p = path.join(dir, e.name);
    if (e.isDirectory()) walk(p); else if (e.name.endsWith('.cs')) csFiles.push(p);
  }
})(UI_DIR);

const cache = new Map();
const fileText = (p) => { if (!cache.has(p)) cache.set(p, readUtf8(p)); return cache.get(p); };

const factoryReturn = {};
for (const f of csFiles) {
  const src = fileText(f);
  for (const m of src.matchAll(/(?:private|public|internal|protected)[\w\s<>,\[\]\.]*?\s([\w\.]+)\s+Create(\w+)\s*\(\s*\)\s*(?:=>|\{)/g)) {
    const name = 'Create' + m[2];
    const shortType = m[1].split('.').pop();
    if (!factoryReturn[name]) factoryReturn[name] = { type: shortType, file: path.relative(ROOT, f).replace(/\\/g, '/') };
  }
}

const typeFile = {};
for (const f of csFiles) {
  const src = fileText(f);
  const rel = path.relative(ROOT, f).replace(/\\/g, '/');
  for (const m of src.matchAll(/(?:public|internal)\s+(?:sealed\s+|abstract\s+|partial\s+)*class\s+(\w+)/g)) if (!typeFile[m[1]]) typeFile[m[1]] = rel;
}

// ---------- 3. 设计稿可见文案抽取 ----------
const CN_RE = /[\u4e00-\u9fff][\u4e00-\u9fff0-9A-Za-z·、。！？：；（）%\-]{0,23}/g;
function designTokens(html) {
  let body = html
    .replace(/<head[\s\S]*?<\/head>/gi, ' ')
    .replace(/<script[\s\S]*?<\/script>/gi, ' ')
    .replace(/<style[\s\S]*?<\/style>/gi, ' ')
    .replace(/<!--[\s\S]*?-->/g, ' ')
    .replace(/<title[\s\S]*?<\/title>/gi, ' ');
  body = body.replace(/<[^>]+>/g, '\n').replace(/&nbsp;/g, ' ').replace(/&amp;/g, '&');
  const set = new Set();
  for (const line of body.split('\n')) {
    const t = line.trim(); if (!t) continue;
    for (const m of t.match(CN_RE) || []) { const tok = m.trim(); if (tok.length >= 2) set.add(tok); }
  }
  return set;
}

function csStringLiterals(src) {
  const noBlock = src.replace(/\/\*[\s\S]*?\*\//g, ' ');
  const noLine = noBlock.split('\n').map((l) => l.replace(/\/\/.*$/, '')).join('\n');
  const out = [];
  for (const m of noLine.matchAll(/"((?:[^"\\]|\\.)*)"/g)) out.push(m[1]);
  return out.join('\u0001');
}

// ---------- 3b. 全局字符串池（判断"设计稿文案是否在客户端任意处落地"）----------
const ALIAS = { 'menu-char-attributes-v2': 'nav-character-v2', 'shop-rare-items': 'nav-shop-rare', 'menu-shop': 'nav-shop', 'menu-char-attributes': 'menu-char-attributes' };
const globalLits = [];
const SRC_ROOT = path.join(ROOT, 'HundunWorld/Source');
(function walkAll(dir) {
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    const p = path.join(dir, e.name);
    if (e.isDirectory()) {
      if (e.name === 'obj' || e.name === 'bin') continue;
      walkAll(p);
    } else if (e.name.endsWith('.cs')) globalLits.push(csStringLiterals(readUtf8(p)));
  }
})(SRC_ROOT);
const GLOBAL_POOL = globalLits.join('\u0001');

// ---------- 4. 逐页比对 ----------
const designFiles = fs.readdirSync(DESIGN_PAGES).filter((f) => f.endsWith('.html')).sort();
const registeredDomIds = new Set(registrations.map((r) => r.domId).filter(Boolean));
const rows = [];
for (const file of designFiles) {
  const stem = file.replace(/\.html$/, '');
  const html = readUtf8(path.join(DESIGN_PAGES, file));
  const tokens = designTokens(html);
  const candidates = [ALIAS[stem], stem, 'nav-' + stem, 'menu-' + stem, 'nav-' + stem.replace(/^menu-/, '')].filter(Boolean);
  const domId = candidates.find((c) => registeredDomIds.has(c)) || null;
  const reg = domId ? registrations.find((r) => r.domId === domId) : null;
  const fr = reg ? factoryReturn[reg.factory] : null;
  const typeName = fr ? fr.type : null;
  const implFile = typeName && typeFile[typeName] ? typeFile[typeName] : (fr ? fr.file : null);
  let missing = []; let covered = 0; const globalMissing = [];
  if (implFile && fs.existsSync(path.join(ROOT, implFile))) {
    const lits = csStringLiterals(readUtf8(path.join(ROOT, implFile)));
    for (const tok of tokens) { if (lits.includes(tok)) covered++; else missing.push(tok); }
  }
  let globalCovered = 0;
  for (const tok of tokens) { if (GLOBAL_POOL.includes(tok)) globalCovered++; else globalMissing.push(tok); }
  rows.push({
    design: file, domId, factory: reg ? reg.factory : null, impl: implFile,
    designTokens: tokens.size, covered,
    coverage: tokens.size ? +(covered / tokens.size).toFixed(3) : null,
    globalCovered,
    globalCoverage: tokens.size ? +(globalCovered / tokens.size).toFixed(3) : null,
    missing: missing.sort((a, b) => b.length - a.length),
    globalMissing: globalMissing.sort((a, b) => b.length - a.length),
    note: reg ? reg.note : '',
  });
}

// ---------- 5. 输出 ----------
fs.mkdirSync(OUT_DIR, { recursive: true });
const summary = {
  generatedAt: new Date().toISOString(),
  designPageCount: designFiles.length,
  registeredCount: registeredDomIds.size,
  unmapped: rows.filter((r) => !r.domId).map((r) => r.design),
  noImpl: rows.filter((r) => r.domId && !r.impl).map((r) => r.design),
};
fs.writeFileSync(path.join(OUT_DIR, 'ui-fidelity-audit.json'), JSON.stringify({ summary, rows }, null, 2), 'utf8');

const P = [];
P.push('# UI 设计落地保真度审计', '');
P.push('生成时间：' + summary.generatedAt);
P.push('设计稿页面：' + summary.designPageCount + ' ｜ 已注册 dom-id：' + summary.registeredCount);
P.push('未映射设计稿：' + (summary.unmapped.join(', ') || '无'));
P.push('无实现文件：' + (summary.noImpl.join(', ') || '无'), '');
P.push('| 设计稿 | dom-id | 实现文件 | 本页文案覆盖 | 全局文案覆盖 | 全局缺失 |');
P.push('| --- | --- | --- | --- | --- | --- |');
for (const r of rows.slice().sort((a, b) => (a.globalCoverage ?? -1) - (b.globalCoverage ?? -1))) {
  P.push('| ' + r.design + ' | ' + (r.domId || '—') + ' | ' + (r.impl || '—') + ' | '
    + (r.coverage === null ? '—' : (r.coverage * 100).toFixed(1) + '%') + ' | '
    + (r.globalCoverage === null ? '—' : (r.globalCoverage * 100).toFixed(1) + '%') + ' | '
    + r.globalMissing.length + ' |');
}
P.push('', '## 全局缺失文案（客户端源码中完全找不到，每页前 40 条）', '');
for (const r of rows.slice().sort((a, b) => b.globalMissing.length - a.globalMissing.length)) {
  if (!r.globalMissing.length) continue;
  P.push('### ' + r.design + '（全局覆盖 ' + (r.globalCoverage * 100).toFixed(1) + '%，缺失 ' + r.globalMissing.length + '/' + r.designTokens + '）');
  P.push(r.globalMissing.slice(0, 40).map((t) => '`' + t + '`').join(' · '), '');
}
fs.writeFileSync(path.join(OUT_DIR, 'ui-fidelity-audit.md'), P.join('\n'), 'utf8');

console.log(JSON.stringify({ summary, all: rows.map((r) => ({ design: r.design, domId: r.domId, impl: r.impl, cov: r.coverage, gcov: r.globalCoverage, t: r.designTokens, gmiss: r.globalMissing.length })) }, null, 0));
