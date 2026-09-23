
import fs from 'node:fs';
const ROOT='C:/Works/GitHubProjects/HundunWorld';
const html=fs.readFileSync(ROOT+'/hundun-yy-ui/pages/menu-team.html','utf8');
const CN_RE = /[\u4e00-\u9fff][\u4e00-\u9fff0-9A-Za-z·、。！？：；（）%\-]{0,23}/g;
let body = html.replace(/<head[\s\S]*?<\/head>/gi,' ').replace(/<script[\s\S]*?<\/script>/gi,' ').replace(/<style[\s\S]*?<\/style>/gi,' ').replace(/<!--[\s\S]*?-->/g,' ').replace(/<title[\s\S]*?<\/title>/gi,' ');
body = body.replace(/<[^>]+>/g,'\n').replace(/&nbsp;/g,' ').replace(/&amp;/g,'&');
const set=new Set();
for (const line of body.split('\n')) { const t=line.trim(); if(!t) continue; for (const m of t.match(CN_RE)||[]) { const tok=m.trim(); if(tok.length>=2) set.add(tok); } }
console.log([...set].join(' | '));
console.log('count', set.size);
