const { test } = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const collection = JSON.parse(fs.readFileSync(path.resolve(__dirname,
  '../../../postman/collections/4Money.Banking.postman_collection.json'), 'utf8'));
const login = collection.item[0].item[0];
const pre = login.event.find(event => event.listen === 'prerequest').script.exec.join('\n');
const post = login.event.find(event => event.listen === 'test').script.exec.join('\n');

function session(overrides = {}) {
  const environment = new Map(Object.entries({
    baseUrl: 'https://localhost:7278', userLogin: 'synthetic.user', userPassword: 'synthetic"\\password',
    fourMoneyAccessToken: 'stale-access', fourMoneyRefreshToken: 'stale-refresh', ...overrides
  }));
  const state = { failures: [], skipped: false, next: 'unchanged', body: null };
  const pm = {
    environment: { get: key => environment.get(key), set: (key, value) => environment.set(key, value),
      unset: key => environment.delete(key) },
    request: { body: { update: value => { state.body = value; } } },
    execution: { skipRequest: () => { state.skipped = true; }, setNextRequest: value => { state.next = value; } },
    test: (name, callback) => { try { callback(); } catch { state.failures.push(name); } },
    expect: value => ({ to: { equal: expected => assert.equal(value, expected) } })
  };
  return { environment, state, pm };
}

function respond(context, { code = 200, contentType = 'application/json; charset=utf-8', body, malformed = false } = {}) {
  context.pm.response = { code, headers: { get: () => contentType }, json: () => {
    if (malformed) throw new Error('Invalid JSON');
    return body ?? { accessToken: 'synthetic-access-token', tokenType: 'Bearer',
      accessTokenExpiresAt: '2099-01-01T00:00:00Z', refreshToken: 'never-persist-this-refresh-token' };
  } };
  return () => vm.runInNewContext(post, { pm: context.pm });
}

test('login is anonymous, first in folder 10, and subsequent requests inherit its Bearer token', () => {
  assert.equal(login.request.method, 'POST');
  assert.equal(login.request.url, '{{baseUrl}}/api/users/login');
  assert.equal(login.request.auth.type, 'noauth');
  assert.equal(collection.item[0].item[1].name.startsWith('11 '), true);
  assert.equal(collection.auth.bearer[0].value, '{{fourMoneyAccessToken}}');
});

test('successful login escapes credentials and persists only the returned access token', () => {
  const context = session();
  vm.runInNewContext(pre, { pm: context.pm });
  assert.deepEqual(JSON.parse(context.state.body), { login: 'synthetic.user', password: 'synthetic"\\password' });
  assert.equal(context.environment.has('fourMoneyAccessToken'), false);
  respond(context)();
  assert.equal(context.environment.get('fourMoneyAccessToken'), 'synthetic-access-token');
  assert.equal(context.environment.has('fourMoneyRefreshToken'), false);
  assert.deepEqual(context.state.failures, []);
  assert.equal(context.state.next, 'unchanged');
});

for (const key of ['baseUrl', 'userLogin', 'userPassword']) {
  for (const value of ['', '{{unresolved}}']) {
    test(`missing or unresolved ${key} clears stale tokens and skips the request: ${value || 'empty'}`, () => {
      const context = session({ [key]: value });
      vm.runInNewContext(pre, { pm: context.pm });
      assert.equal(context.state.skipped, true);
      assert.equal(context.state.next, null);
      assert.equal(context.environment.has('fourMoneyAccessToken'), false);
      assert.equal(context.environment.has('fourMoneyRefreshToken'), false);
      assert.equal(context.state.body, null);
      assert.equal(context.state.failures.length, 1);
    });
  }
}

for (const response of [{ code: 401 }, { code: 403 }, { code: 500 }, { malformed: true },
  { contentType: 'text/html' }, { body: {} }, { body: [] },
  { body: { accessToken: ' ', tokenType: 'Bearer', accessTokenExpiresAt: '2099-01-01' } },
  { body: { accessToken: 'token', tokenType: 'Other', accessTokenExpiresAt: '2099-01-01' } },
  { body: { accessToken: 'token', tokenType: 'Bearer', accessTokenExpiresAt: 'invalid' } }]) {
  test(`invalid login stops the runner without retaining a token: ${JSON.stringify(response)}`, () => {
    const context = session();
    vm.runInNewContext(pre, { pm: context.pm });
    assert.throws(respond(context, response));
    assert.equal(context.state.next, null);
    assert.equal(context.environment.has('fourMoneyAccessToken'), false);
    assert.equal(context.state.failures.length, 1);
  });
}
