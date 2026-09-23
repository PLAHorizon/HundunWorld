
import fs from 'node:fs';
const CN_RE = /[\u4e00-\u9fff][\u4e00-\u9fff0-9A-Za-z·、。！？：；（）%\-]{0,23}/g;
const html = fs.readFileSync('C:/Works/GitHubProjects/HundunWorld/hundun-yy-ui/pages/menu-mail.html','utf8');
let body = html.replace(/<script[\s\S]*?<\/script>/gi,' ').replace(/<style[\s\S]*?<\/style>/gi,' ').replace(/<!--[\s\S]*?-->/g,' ');
body = body.replace(/<[^>]+>/g,'\n').replace(/&nbsp;/g,' ').replace(/&amp;/g,'&');
const set=new Set();
for(const line of body.split('\n')){const t=line.trim(); if(!t) continue; for(const m of t.match(CN_RE)||[]){const tok=m.trim(); if(tok.length>=2) set.add(tok);}}
const arr=[...set].sort((a,b)=>b.length-a.length);
console.log('COUNT', arr.length);
console.log(JSON.stringify(arr,null,0));
