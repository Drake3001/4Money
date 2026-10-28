// Local fixture derived from the saved Tink requests and TinkDataHttpTests.
// Deliberately models only the read/grant flow used by the production worker.
const http = require('node:http');
const crypto = require('node:crypto');
const key = 'banking-refresh.state';
const scenarios = ['success', 'partial', 'transient', 'blocked', 'empty', 'invalid'];
const accountIds = ['aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa', 'bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb'];
function seed(scenario) {
  return { scenario, seededAt: new Date().toISOString(), released: false, blockedEntered: false,
    accountPages: 0, transactionCalls: { a: 0, b: 0 }, transactionPages: { a: 0, b: 0 },
    transientSent: false, grants: {}, tokens: {} };
}
function send(res, status, body, headers = {}) {
  res.writeHead(status, { 'Content-Type': 'application/json', ...headers });
  res.end(JSON.stringify(body));
}
async function body(req) {
  if (req.body !== undefined) {
    if (typeof req.body === 'string') return req.headers['content-type']?.includes('json') ? JSON.parse(req.body) : Object.fromEntries(new URLSearchParams(req.body));
    return req.body;
  }
  const chunks = [];
  for await (const chunk of req) chunks.push(chunk);
  const raw = Buffer.concat(chunks).toString('utf8');
  return req.headers['content-type']?.includes('json') ? JSON.parse(raw || '{}') : Object.fromEntries(new URLSearchParams(raw));
}
http.createServer(async (req, res) => {
  try {
    const url = new URL(req.url, 'http://localhost');
    let state = await pm.state.get(key) || seed('success');
    if (req.method === 'POST' && url.pathname === '/__control/reset') {
      const input = await body(req);
      if (!scenarios.includes(input.scenario)) return send(res, 400, { error: 'unknown_scenario' });
      if (state.blockedEntered && !state.released) return send(res, 409, { error: 'release_before_reset' });
      state = seed(input.scenario);
      await pm.state.set(key, state);
      return send(res, 200, { scenario: state.scenario });
    }
    if (req.method === 'POST' && url.pathname === '/__control/release') {
      state.released = true;
      await pm.state.set(key, state);
      return send(res, 200, { released: true });
    }
    if (req.method === 'GET' && url.pathname === '/__control/state') {
      // Never return tokens, grants, client secrets or actual request headers.
      const { scenario, blockedEntered, released, firstAccount, accountPages, transactionCalls, transactionPages } = state;
      return send(res, 200, { scenario, blockedEntered, released, firstAccount, accountPages, transactionCalls, transactionPages });
    }
    if (req.method === 'POST' && url.pathname === '/api/v1/oauth/token') {
      const input = await body(req);
      if (input.client_id !== 'refresh-fixture' || input.client_secret !== 'synthetic-local-secret') return send(res, 401, { error: 'invalid_client' });
      if (input.grant_type === 'client_credentials' && input.scope === 'authorization:grant')
        return send(res, 200, { access_token: 'synthetic-client-token', expires_in: 1800 });
      const grant = state.grants[input.code];
      if (input.grant_type !== 'authorization_code' || !grant) return send(res, 400, { error: 'invalid_grant' });
      delete state.grants[input.code];
      const token = crypto.randomUUID();
      state.tokens[token] = grant;
      await pm.state.set(key, state);
      return send(res, 200, { access_token: token, expires_in: 1800 });
    }
    if (req.method === 'POST' && url.pathname === '/api/v1/oauth/authorization-grant') {
      const input = await body(req);
      if (req.headers.authorization !== 'Bearer synthetic-client-token') return send(res, 401, { error: 'unauthorized' });
      if (input.user_id !== 'refresh-fixture-owner' || input.scope !== 'accounts:read,transactions:read') return send(res, 403, { error: 'forbidden' });
      const code = crypto.randomUUID();
      state.grants[code] = { user: input.user_id, scope: input.scope };
      await pm.state.set(key, state);
      return send(res, 200, { code });
    }
    if (req.method === 'GET' && ['/data/v2/accounts', '/data/v2/transactions'].includes(url.pathname)) {
      const token = String(req.headers.authorization || '').replace(/^Bearer /, '');
      if (!state.tokens[token]) return send(res, 401, { error: 'unauthorized' });
      if (url.searchParams.get('pageSize') !== '100') return send(res, 400, { error: 'invalid_page_size' });
      if (url.pathname === '/data/v2/accounts') {
        state.accountPages++;
        await pm.state.set(key, state);
        if (state.scenario === 'invalid') return send(res, 200, { unexpected: [] });
        if (state.scenario === 'empty') return send(res, 200, { accounts: [], nextPageToken: '' });
        const page = url.searchParams.get('pageToken');
        if (page && page !== 'accounts-next +/&') return send(res, 400, { error: 'invalid_page' });
        const index = page ? 1 : 0;
        return send(res, 200, { accounts: [{ id: accountIds[index], name: `Refresh TEST ${index ? 'B' : 'A'}`, type: 'CHECKING',
          identifiers: { financialInstitution: { accountNumber: 'TEST ** 0001' } },
          balances: { booked: { amount: { currencyCode: 'PLN', value: { unscaledValue: '10000', scale: '2' } } } } }],
          nextPageToken: page ? '' : 'accounts-next +/&' });
      }
      const index = accountIds.indexOf(url.searchParams.get('accountIdIn'));
      if (index < 0 || url.searchParams.get('statusIn') !== 'BOOKED' ||
          !/^\d{4}-\d{2}-\d{2}$/.test(url.searchParams.get('bookedDateGte') || '') ||
          !/^\d{4}-\d{2}-\d{2}$/.test(url.searchParams.get('bookedDateLte') || '')) return send(res, 400, { error: 'invalid_filters' });
      const label = index ? 'b' : 'a';
      state.firstAccount ??= label;
      state.transactionCalls[label]++;
      if (index && state.scenario === 'partial') {
        await pm.state.set(key, state);
        return send(res, 403, { error: 'forbidden' });
      }
      if (index && state.scenario === 'transient' && !state.transientSent) {
        state.transientSent = true;
        await pm.state.set(key, state);
        return send(res, 429, { error: 'rate_limit' }, { 'Retry-After': '2' });
      }
      if (label !== state.firstAccount && state.scenario === 'blocked' && !state.released) {
        state.blockedEntered = true;
        await pm.state.set(key, state);
        // Control requests remain available while the backend request is waiting.
        while (!(await pm.state.get(key)).released) {
          if (res.destroyed) return;
          await new Promise(resolve => setTimeout(resolve, 100));
        }
        state = await pm.state.get(key);
      }
      const page = url.searchParams.get('pageToken');
      if (page && page !== 'transactions-next +/&') return send(res, 400, { error: 'invalid_page' });
      const day = offset => new Date(Date.parse(state.seededAt) - offset * 86400000).toISOString().slice(0, 10);
      const items = [1, 2, 45].map((offset, n) => ({ id: `${label}${String(n + 1).padStart(31, '0')}`, accountId: accountIds[index],
        status: 'BOOKED', amount: { currencyCode: 'PLN', value: { unscaledValue: n ? '77' : '-1234', scale: n ? '-1' : '2' } },
        dates: { booked: day(offset) }, descriptions: { original: `Synthetic ${label} ${n + 1}` } }))
        .filter(tx => tx.dates.booked >= url.searchParams.get('bookedDateGte') && tx.dates.booked <= url.searchParams.get('bookedDateLte'));
      state.transactionPages[label]++;
      await pm.state.set(key, state);
      const position = page ? 1 : 0;
      return send(res, 200, { transactions: items.slice(position, position + 1), nextPageToken: !page && items.length > 1 ? 'transactions-next +/&' : '' });
    }
    return send(res, 501, { error: 'endpoint_not_modelled' });
  } catch (_) { send(res, 500, { error: 'fixture_error' }); }
}).listen(Number(process.env.PORT || 4901), '127.0.0.1');
