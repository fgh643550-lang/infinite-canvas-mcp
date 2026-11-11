const {test, after} = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const {spawn} = require('node:child_process');
const readline = require('node:readline');
const root = path.resolve(__dirname, '..');
const releaseRoot = process.env.CANVAS_TEST_ROOT || path.join(root,'发布');
const data = path.join(__dirname, 'ai-run-' + Date.now());
fs.mkdirSync(data, {recursive:true});
let bridge, connection, seq=0, second;
const pending = new Map();
function rpc(method, params={}, target=bridge) {
  const id=++seq;
  return new Promise((resolve,reject)=>{
    const timer=setTimeout(()=>{pending.delete(id);reject(Error('RPC timeout: '+method));},20000);
    pending.set(id, obj=>{clearTimeout(timer);obj.error?reject(Error(JSON.stringify(obj.error))):resolve(obj.result);});
    target.stdin.write(JSON.stringify({jsonrpc:'2.0',id,method,params})+'\n');
  });
}
async function call(name, args={}) {
  const r=await rpc('tools/call',{name,arguments:args});
  if(r.isError) throw Error(r.content[0].text);
  return JSON.parse(r.content[0].text);
}
after(()=>{
  if(bridge) bridge.stdin.end();
  if(second) second.stdin.end();
  if(connection) try{process.kill(connection.pid);}catch{}
});
test('MCP and HTTP operate one real WPF document', {timeout:120000}, async t=>{
  const exe=path.join(releaseRoot,'plugin','InfiniteCanvas.Mcp.exe');
  assert.ok(fs.existsSync(exe), 'compiled MCP plugin must exist');
  bridge=spawn(exe,['--data-root',data],{windowsHide:true,stdio:['pipe','pipe','pipe']});
  let stderr='';bridge.stderr.on('data',d=>stderr+=d);
  readline.createInterface({input:bridge.stdout}).on('line',line=>{
    const obj=JSON.parse(line);pending.get(obj.id)?.(obj);pending.delete(obj.id);
  });
  await t.test('handshake and tool discovery',async()=>{
    const init=await rpc('initialize',{protocolVersion:'2025-11-25',capabilities:{},clientInfo:{name:'integration',version:'1'}});
    assert.equal(init.protocolVersion,'2025-11-25');
    bridge.stdin.write(JSON.stringify({jsonrpc:'2.0',method:'notifications/initialized'})+'\n');
    const tools=await rpc('tools/list');assert.ok(tools.tools.some(t=>t.name==='canvas_apply'));
  });
  // Fail fast before dependent scenarios if the real service cannot launch.
  await call('canvas_get');
  await t.test('live state and HTTP authentication',async()=>{
    const state=await call('canvas_get');assert.equal(state.document.Items.length,0);
    connection=JSON.parse(fs.readFileSync(path.join(data,'ai-connection.json'),'utf8').replace(/^\uFEFF/,''));
    assert.equal((await fetch(connection.url+'/health')).status,401);
    assert.equal((await fetch(connection.url+'/health',{headers:{Authorization:'Bearer '+connection.token,Origin:'https://example.com'}})).status,403);
    const health=await fetch(connection.url+'/health',{headers:{Authorization:'Bearer '+connection.token}});
    assert.equal(health.status,200);
  });
  let revision;
  await t.test('batch add, Chinese text and connectors',async()=>{
    const state=await call('canvas_get');revision=state.revision;
    await call('canvas_apply',{expected_revision:revision,operations:[
      {op:'add_text',id:'title',text:'AI 接入验证\n第二行',x:20,y:20,width:400,height:150,font_size:32},
      {op:'add_text',id:'note',text:'同一张实时画布',x:500,y:250},
      {op:'connect',id:'arrow',start_id:'title',end_id:'note'}]});
    const s=await call('canvas_get');assert.equal(s.document.Items.length,2);assert.equal(s.document.Arrows.length,1);
    assert.equal(s.document.Items[0].Text,'AI 接入验证\n第二行');
    await assert.rejects(call('canvas_apply',{expected_revision:revision,operations:[{op:'delete',id:'title'}]}),/revision|版本/i);
  });
  await t.test('invalid batch is atomic and creates no undo checkpoint',async()=>{
    const before=await call('canvas_get');
    await assert.rejects(call('canvas_apply',{operations:[{op:'update',id:'title',text:'不能提交'},{op:'connect',start_id:'missing',end_id:'title'}]}));
    assert.equal((await call('canvas_get')).revision,before.revision);
    await call('canvas_undo');assert.equal((await call('canvas_get')).document.Items.length,0);
    await call('canvas_redo');assert.equal((await call('canvas_get')).document.Items.length,2);
  });
  await t.test('typos and unknown operation fields cannot silently edit content',async()=>{
    const before=await call('canvas_get');
    await assert.rejects(call('canvas_apply',{operations:[{op:'update',id:'title',fontSize:90}]}),/field|参数|property/i);
    await assert.rejects(call('canvas_apply',{operationz:[],operations:[{op:'delete',id:'note'}]}));
    assert.equal((await call('canvas_get')).revision,before.revision);
  });
  await t.test('a second independent MCP client shares the running canvas',async()=>{
    second=spawn(exe,['--data-root',data],{windowsHide:true,stdio:['pipe','pipe','pipe']});
    second.stderr.on('data',d=>stderr+=d);
    readline.createInterface({input:second.stdout}).on('line',line=>{const obj=JSON.parse(line);pending.get(obj.id)?.(obj);pending.delete(obj.id);});
    await rpc('initialize',{protocolVersion:'2025-03-26',capabilities:{},clientInfo:{name:'second',version:'1'}},second);
    second.stdin.write(JSON.stringify({jsonrpc:'2.0',method:'notifications/initialized'})+'\n');
    const response=await rpc('tools/call',{name:'canvas_get',arguments:{}},second);
    assert.equal(JSON.parse(response.content[0].text).document.Items[0].Text,'AI 接入验证\n第二行');
    assert.equal(JSON.parse(fs.readFileSync(path.join(data,'ai-connection.json'),'utf8')).pid,connection.pid);
  });
  await t.test('dirty switch rejected, safe archive save/reopen and traversal rejected',async()=>{
    await assert.rejects(call('canvas_new',{name:'不允许丢失'}),/unsaved|未保存/i);
    await call('canvas_save',{filename:'AI验证.canvas'});
    await assert.rejects(call('canvas_save',{filename:'../outside.canvas'}));
    await call('canvas_new',{name:'空白'});
    await call('canvas_open',{filename:'AI验证.canvas'});
    assert.equal((await call('canvas_get')).document.Items.length,2);
    assert.ok((await call('canvas_list')).archives.some(a=>a.filename==='AI验证.canvas'));
  });
  await t.test('image validation and embedded persistence',async()=>{
    await assert.rejects(call('canvas_apply',{operations:[{op:'add_image',data_base64:'bm90IGFuIGltYWdl'}]}));
    // Real 1x1 PNG fixture, expanded to a visible card by explicit dimensions.
    const png='iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aF9sAAAAASUVORK5CYII=';
    await call('canvas_apply',{operations:[{op:'add_image',id:'img',data_base64:png,x:50,y:300,width:120,height:120}]});
    await call('canvas_save',{filename:'含图.canvas'});
    await call('canvas_new',{name:'另一个'});await call('canvas_open',{filename:'含图.canvas'});
    assert.equal((await call('canvas_get')).document.Items.length,3);
  });
  await t.test('delete cascades connectors and can undo; render actual board',async()=>{
    await call('canvas_apply',{operations:[{op:'delete',id:'note'}]});
    assert.equal((await call('canvas_get')).document.Arrows.length,0);
    await call('canvas_undo');assert.equal((await call('canvas_get')).document.Arrows.length,1);
    await call('canvas_view',{fit:true});
    const r=await rpc('tools/call',{name:'canvas_preview',arguments:{}});
    const img=r.content.find(c=>c.type==='image');assert.ok(img);assert.equal(img.mimeType,'image/png');
    const bytes=Buffer.from(img.data,'base64');assert.ok(bytes.length>5000);
    fs.writeFileSync(path.join(data,'preview.png'),bytes);
  });
  await t.test('invalid tool and protocol errors preserve connection',async()=>{
    await assert.rejects(call('does_not_exist'));
    await assert.rejects(rpc('unsupported/method'));
    assert.deepEqual(await rpc('ping'),{});
    assert.equal(stderr,'');
  });
  console.log('ARTIFACTS '+data);
});
test('AI startup serves requests with recoverable drafts present', {timeout:10000}, async()=>{
  const recoveryData=path.join(data,'startup-recovery');
  const drafts=path.join(recoveryData,'自动恢复');fs.mkdirSync(drafts,{recursive:true});
  fs.copyFileSync(path.join(data,'存档','含图.canvas'),path.join(drafts,'pending.canvas'));
  const proc=spawn(path.join(releaseRoot,'无限画布.exe'),['--ai','--data-root',recoveryData],{windowsHide:true,stdio:'ignore'});
  try {
    const endpoint=path.join(recoveryData,'ai-connection.json');
    for(let i=0;i<60&&!fs.existsSync(endpoint);i++)await new Promise(r=>setTimeout(r,50));
    assert.ok(fs.existsSync(endpoint),'draft recovery must not block AI service startup');
    const c=JSON.parse(fs.readFileSync(endpoint,'utf8'));
    const r=await fetch(c.url+'/health',{headers:{Authorization:'Bearer '+c.token}});assert.equal(r.status,200);
    assert.ok(fs.existsSync(path.join(drafts,'pending.canvas')),'recoverable draft must remain available');
  } finally {proc.kill();}
});
