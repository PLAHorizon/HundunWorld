
const fs=require('fs');
const CN_RE = /[\u4e00-\u9fff][\u4e00-\u9fff0-9A-Za-z·、。！？：；（）%\-]{0,23}/g;
function designTokens(html){let body=html.replace(/<script[\s\S]*?<\/script>/gi,' ').replace(/<style[\s\S]*?<\/style>/gi,' ').replace(/<!--[\s\S]*?-->/g,' ');body=body.replace(/<[^>]+>/g,'\n').replace(/&nbsp;/g,' ').replace(/&amp;/g,'&');const set=new Set();for(const line of body.split('\n')){const t=line.trim();if(!t)continue;for(const m of t.match(CN_RE)||[]){const tok=m.trim();if(tok.length>=2)set.add(tok);}}return set;}
for(const f of ['acupoint.html','death-screen.html']){const s=designTokens(fs.readFileSync('hundun-yy-ui/pages/'+f,'utf8'));console.log('==',f,s.size);console.log([...s].map(x=>JSON.stringify(x)).join('\n'));}
