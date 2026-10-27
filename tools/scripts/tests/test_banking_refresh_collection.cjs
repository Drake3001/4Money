const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const collection = JSON.parse(fs.readFileSync(path.resolve(__dirname, '../../../postman/4Money.Banking.postman_collection.json')));
const requests = collection.item.flatMap(folder => folder.item || []);
const find = name => requests.find(request => request.name === name);
const start = find('41 — Zleć refresh danych użytkownika');
const poll = find('42 — Oczekuj na refresh');
const finish = find('43 — Potwierdź wynik refreshu');
const id = '11111111-1111-4111-8111-111111111111';
const accountId = '22222222-2222-4222-8222-222222222222';
function body(status = 'SUCCEEDED') {
  const done = !['QUEUED','RUNNING'].includes(status);
  return { refreshId:id, status, createdAt:'2026-10-25T10:00:00Z', startedAt:'2026-10-25T10:00:01Z',
    completedAt:done ? '2026-10-25T10:00:03Z' : null, totalAccounts:1, successfulAccounts:done ? 1 : 0, failedAccounts:0,
    failureCode:null, accounts:[{ accountId, status:done ? 'SUCCEEDED' : 'QUEUED', addedTransactions:0, updatedTransactions:0,
      lastSuccessfulSyncAt:done ? '2026-10-25T10:00:02Z' : null, failureCode:null }] };
}
function context(overrides = {}, local = {}) {
  const environment = new Map(Object.entries({ baseUrl:'https://localhost:7444', fourMoneyAccessToken:'synthetic-token',
    bankRefreshId:id, bankRefreshExpectedStatus:'SUCCEEDED', bankRefreshMockBaseUrl:'http://127.0.0.1:4900/tink', ...overrides }));
  const variables = new Map(Object.entries(local));
  const result = { failures:[], next:'unchanged', skipped:false, environment, variables };
  result.pm = { environment:{ get:k=>environment.get(k), set:(k,v)=>environment.set(k,v), unset:k=>environment.delete(k) },
    variables:{ get:k=>variables.get(k), set:(k,v)=>variables.set(k,v), replaceIn:()=>id }, info:{ requestId:poll.id },
    execution:{ setNextRequest:v=>result.next=v, skipRequest:()=>result.skipped=true },
    test:(name,fn)=>{ try { fn(); } catch { result.failures.push(name); } },
    expect:v=>({ to:{ equal:expected=>assert.equal(v,expected) } }) };
  return result;
}
function run(request, phase, context, document, code = 200) {
  context.pm.response = { code, headers:{ get:name=>({ 'Content-Type':'application/json', 'Cache-Control':'no-store', Location:'/api/banking/refreshes/'+id }[name]) }, json:()=>document };
  try { vm.runInNewContext(request.event.find(e=>e.listen===phase).script.exec.join('\n'), { pm:context.pm }); } catch (error) { context.error=error; }
  return context;
}
test('all scripts parse and workflow request IDs are unique', ()=> {
  const ids=requests.filter(r=>r.id).map(r=>r.id);assert.equal(ids.length,new Set(ids).size);
  for(const request of requests) for(const event of request.event || []) new vm.Script(event.script.exec.join('\n'));
});
for(const status of ['QUEUED','RUNNING']) test('polls nonterminal '+status,()=> {
  const c=run(poll,'test',context(),body(status));assert.deepEqual(c.failures,[]);assert.equal(c.next,poll.id);
});
test('terminal status advances to a stable request ID',()=> {
  const c=run(poll,'test',context(),body());assert.deepEqual(c.failures,[]);assert.equal(c.next,finish.id);
});
test('QUEUED after RUNNING is allowed during retry',()=> {
  const c=context();run(poll,'test',c,body('RUNNING'));run(poll,'test',c,body('QUEUED'));assert.deepEqual(c.failures,[]);
});
test('start stores ID only after a valid 202 and clears stale state first',()=> {
  const c=run(start,'prerequest',context());assert.equal(c.environment.has('bankRefreshId'),false);
  run(start,'test',c,{refreshId:id,status:'QUEUED',createdAt:'2026-10-25T10:00:00Z'},202);assert.equal(c.environment.get('bankRefreshId'),id);assert.deepEqual(c.failures,[]);
});
for(const invalid of [{refreshId:'------------------------------------',status:'QUEUED'}, {refreshId:id,status:'FAILED'}, {refreshId:'00000000-0000-0000-0000-000000000000',status:'QUEUED'}]) test('bad start cannot retain ID '+invalid.status+invalid.refreshId,()=> {
  const c=run(start,'prerequest',context());run(start,'test',c,invalid,202);assert.ok(c.failures.length);assert.equal(c.next,null);assert.equal(c.environment.has('bankRefreshId'),false);
});
for(const code of [401,403,404,429,503]) test('HTTP '+code+' stops rather than polling',()=> {
  const c=run(poll,'test',context(),{},code);assert.ok(c.failures.length);assert.equal(c.next,null);
});
const invalidBodies = [b=>b.totalAccounts++, b=>b.accounts[0].addedTransactions=-1, b=>b.accounts[0].updatedTransactions=0.5,
  b=>b.accounts.push({...b.accounts[0]}), b=>b.accounts[0].lastSuccessfulSyncAt=null, b=>b.completedAt=null,
  b=>b.startedAt='wrong', b=>b.accounts[0].leaseId='private', b=>b.accounts[0].nested={tinkAccountId:'private'},
  b=>b.accounts[0].status='UNKNOWN', b=>b.refreshId='foreign', b=>{b.accounts[0].status='FAILED';b.failedAccounts=1;b.successfulAccounts=0;},
  b=>{b.createdAt='2026-10-25T10:10:00Z';}];
invalidBodies.forEach((mutate,index)=>test('invalid result '+index+' stops flow',()=> {
  const value=body();mutate(value);const c=run(poll,'test',context(),value);assert.ok(c.failures.length);assert.equal(c.next,null);
}));
for(const [local,label] of [[{'refresh.deadline':String(Date.now()-1),'refresh.polls':'0'},'deadline'],[{'refresh.deadline':String(Date.now()+100000),'refresh.polls':'1200'},'poll count']]) test('bounded by '+label,()=> {
  const c=run(poll,'prerequest',context({},local));assert.equal(c.skipped,true);assert.equal(c.next,null);assert.ok(c.failures.length);
});
test('manual first GET initializes a bounded polling context',()=> {
  const c=run(poll,'prerequest',context());assert.equal(c.skipped,false);assert.equal(c.variables.get('refresh.polls'),'1');
});
for(const values of [{fourMoneyAccessToken:''},{bankRefreshId:'{{missing}}'},{bankRefreshTimeoutSeconds:'0'},{bankRefreshMaxPolls:'1.5'}]) test('invalid precondition '+JSON.stringify(values),()=> {
  const c=run(poll,'prerequest',context(values));assert.equal(c.skipped,true);assert.ok(c.failures.length);
});
test('incomplete task never passes final success assertion',()=> {
  const c=run(finish,'test',context(),body('RUNNING'));assert.ok(c.failures.length);assert.equal(c.next,null);
});
test('mock controls cannot target an external host',()=> {
  const request=find('70.0 — Atrapa partial');const c=run(request,'prerequest',context({bankRefreshMockBaseUrl:'http://example.com:4900/tink'}));assert.equal(c.skipped,true);
});
test('control requests never inherit owner JWT',()=> {
  for(const request of requests.filter(r=>String(r.request.url).includes('__control')))assert.equal(request.request.auth.type,'noauth');
});
