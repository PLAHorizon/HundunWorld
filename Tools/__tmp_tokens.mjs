
import fs from 'node:fs';
const CN_RE = /[\u4e00-\u9fff][\u4e00-\u9fff0-9A-Za-z·、。！？：；（）%\-]{0,23}/g;
let html = fs.readFileSync('hundun-yy-ui/pages/combat-hud-v2.html','utf8');
let body = html.replace(/<head[\s\S]*?<\/head>/gi,' ').replace(/<script[\s\S]*?<\/script>/gi,' ').replace(/<style[\s\S]*?<\/style>/gi,' ').replace(/<!--[\s\S]*?-->/g,' ').replace(/<title[\s\S]*?<\/title>/gi,' ');
body = body.replace(/<[^>]+>/g,'\n').replace(/&nbsp;/g,' ').replace(/&amp;/g,'&');
const set = new Set();
for (const line of body.split('\n')) { const t = line.trim(); if(!t) continue; for (const m of t.match(CN_RE)||[]) { const tok=m.trim(); if(tok.length>=2) set.add(tok); } }
console.log([...set].join(' | '));
console.log('COUNT', set.size);
