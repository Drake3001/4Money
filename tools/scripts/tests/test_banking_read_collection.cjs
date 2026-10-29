const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const collection = JSON.parse(fs.readFileSync(path.join(__dirname, '../../../postman/collections/4Money.Banking.postman_collection.json'), 'utf8'));
const folder = collection.item.find(x => x.name.startsWith('80 —'));
const id = '10000000-0000-0000-0000-000000000010';
function run(request, phase, body, values = {}, status = 200) {
  const env = new Map(Object.entries({ baseUrl:'http://localhost:5283', fourMoneyAccessToken:'synthetic', ...values }));
  const result = { failures:[], next:undefined, skipped:false, env };
  const pm = { environment:{ get:k=>env.get(k), set:(k,v)=>env.set(k,v), unset:k=>env.delete(k) },
    execution:{ setNextRequest:v=>{result.next=v;}, skipRequest:()=>{result.skipped=true;} },
    response:{ code:status, json:()=>body, headers:{ get:k=>({'Content-Type':'application/json','Cache-Control':'no-store'}[k]) } },
    test:(name, fn)=>{try { fn(); } catch {result.failures.push(name);}},
    expect:v=>({to:{equal:expected=>assert.equal(v,expected)}}) };
  try { vm.runInNewContext(request.event.find(x=>x.listen===phase).script.exec.join('\n'), {pm}); }
  catch { result.threw = true; }
  return result;
}
test('four read requests inherit auth and have valid scripts and examples', () => {
  assert.equal(folder.item.length, 4);
  assert.equal(collection.auth.type, 'bearer');
  for (const request of folder.item) {
    assert.equal(request.request.method, 'GET');
    assert.equal(request.auth, undefined);
    for (const event of request.event) new vm.Script(event.script.exec.join('\n'));
    const body=JSON.parse(request.response[0].body);
    const variable=request.name.startsWith('82')?'bankReadAccountId':'bankReadTransactionId';
    const selected=body.id;
    const result=run(request,'test',body,{[variable]:selected});
    assert.deepEqual(result.failures, []);
    assert.equal(result.next, null);
  }
});
for (const index of [0,2]) test('empty page clears stale detail ID '+index, () => {
  const request=folder.item[index], variable=index===0?'bankReadAccountId':'bankReadTransactionId';
  const before=run(request,'prerequest',{}, {[variable]:id});
  assert.equal(before.env.has(variable), false);
  const body={items:[],totalCount:0,pageNumber:1,pageSize:20,totalPages:0,hasPreviousPage:false,hasNextPage:false};
  const after=run(request,'test',body);
  assert.deepEqual(after.failures, []);
  assert.equal(after.env.has(variable), false);
});
for (const index of [1,3]) test('detail requires ID and verifies returned ID '+index, () => {
  const request=folder.item[index];
  assert.equal(run(request,'prerequest',{}).skipped, true);
  const body=JSON.parse(request.response[0].body);
  const variable=index===1?'bankReadAccountId':'bankReadTransactionId';
  assert.ok(run(request,'test',body,{[variable]:id+'x'}).failures.length);
});
for (const amount of ['-123.45','0.00','9999999999999999.99']) test('exact decimal text accepted '+amount, () => {
  const request=folder.item[3], body=JSON.parse(request.response[0].body);
  body.amount=amount;
  assert.deepEqual(run(request,'test',body,{bankReadTransactionId:body.id}).failures, []);
});
for (const amount of [-123.45,0,null,'1.2','1e3','NaN']) test('invalid money rejected '+amount, () => {
  const request=folder.item[3], body=JSON.parse(request.response[0].body);
  body.amount=amount;
  assert.ok(run(request,'test',body,{bankReadTransactionId:body.id}).failures.length);
});
for (const field of ['tinkUserId','credentialsId','tinkTransactionId']) test('private fields rejected '+field, () => {
  const request=folder.item[3], body=JSON.parse(request.response[0].body);
  body[field]='private';
  assert.ok(run(request,'test',body,{bankReadTransactionId:body.id}).failures.length);
});
test('HTTP failure never stores a detail ID', () => {
  const result=run(folder.item[0],'test',{}, {}, 503);
  assert.ok(result.failures.length);
  assert.equal(result.env.has('bankReadAccountId'), false);
  assert.equal(result.next, null);
});
