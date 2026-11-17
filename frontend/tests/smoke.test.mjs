import assert from 'node:assert/strict'
import { createServer as createHttpServer } from 'node:http'
import { after, before, test } from 'node:test'
import { createServer } from 'vite'
import { createElement } from 'react'
import { renderToString } from 'react-dom/server'

let backend, vite, origin, received
const originalTarget = process.env.API_PROXY_TARGET

function setupWindow(location) {
  globalThis.window = {
    location,
    matchMedia: (media) => ({
      media,
      matches: false,
      addEventListener() {},
      removeEventListener() {},
    }),
  }
  globalThis.localStorage = { getItem: () => null }
}

before(async () => {
  backend = createHttpServer(async (req, res) => {
    let body = ''
    for await (const chunk of req) body += chunk
    received = { method: req.method, url: req.url, authorization: req.headers.authorization, body }
    res.writeHead(401, { 'Content-Type': 'application/problem+json' })
    res.end(JSON.stringify({ title: 'Synthetic unauthorized response', status: 401 }))
  })
  await new Promise((resolve) => backend.listen(0, '127.0.0.1', resolve))
  process.env.API_PROXY_TARGET = `http://127.0.0.1:${backend.address().port}`
  vite = await createServer({ server: { host: '127.0.0.1', port: 0 }, logLevel: 'error' })
  await vite.listen()
  origin = `http://127.0.0.1:${vite.httpServer.address().port}`
})

after(async () => {
  await vite?.close()
  if (backend) await new Promise((resolve) => backend.close(resolve))
  if (originalTarget === undefined) delete process.env.API_PROXY_TARGET
  else process.env.API_PROXY_TARGET = originalTarget
  delete globalThis.window
  delete globalThis.localStorage
})

test('app renders the login screen with the complete provider tree', async () => {
  setupWindow({ search: '', hash: '' })
  const { default: App } = await vite.ssrLoadModule('/src/App.tsx')
  const html = renderToString(createElement(App))
  assert.match(html, /Logowanie/)
  assert.match(html, /Zaloguj/)
})

test('Vite serves the app and forwards API bodies, authorization and error responses', async () => {
  const page = await fetch(origin)
  assert.equal(page.status, 200)
  assert.match(await page.text(), /\/src\/main.tsx/)
  const body = JSON.stringify({ login: 'synthetic-user', password: 'synthetic-test-only' })
  const response = await fetch(`${origin}/api/users/login?smoke=1`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', Authorization: 'Bearer synthetic-test-only' },
    body,
  })
  assert.equal(response.status, 401)
  assert.equal((await response.json()).title, 'Synthetic unauthorized response')
  assert.deepEqual(received, {
    method: 'POST', url: '/api/users/login?smoke=1', authorization: 'Bearer synthetic-test-only', body,
  })
})

test('email links select public action pages and never render the one-time token', async () => {
  const { default: App } = await vite.ssrLoadModule('/src/App.tsx')
  const titles = {
    'confirm-email': 'Potwierdzenie adresu e-mail',
    'confirm-email-change': 'Potwierdzenie zmiany adresu e-mail',
    'confirm-deactivation': 'Dezaktywacja konta',
    reset: 'Ustaw nowe hasło',
  }
  for (const [action, title] of Object.entries(titles)) {
    setupWindow({ search: `?action=${action}`, hash: '#token=synthetic-email-secret' })
    const html = renderToString(createElement(App))
    assert.ok(html.includes(title), action)
    assert.ok(!html.includes('synthetic-email-secret'))
    assert.ok(!html.includes('Token z wiadomości'))
    assert.ok(!html.includes('Link jest niekompletny'))
  }
})

test('missing email token disables reset and routing preserves invitations', async () => {
  const { default: App } = await vite.ssrLoadModule('/src/App.tsx')
  setupWindow({ search: '?action=reset', hash: '' })
  const html = renderToString(createElement(App))
  assert.ok(html.includes('Link jest niekompletny'))
  assert.match(html, /<button[^>]*disabled=""[^>]*>Zapisz hasło/)
  const { initialRoute } = await vite.ssrLoadModule('/src/emailLinks.ts')
  assert.deepEqual(initialRoute({ search: '?token=invitation', hash: '' }), { name: 'invite', id: 'invitation' })
  assert.deepEqual(initialRoute({ search: '?action=reset', hash: '#token=a%2Bb%2Fc%3D' }), { name: 'reset', id: 'a+b/c=' })
})

test('opening a confirmation twice shares one POST, but retries and distinct operations are independent', async () => {
  const { confirmEmailLink } = await vite.ssrLoadModule('/src/emailLinks.ts')
  let calls = 0
  const run = async (token) => { assert.equal(token, 'single-use-token'); calls++; return { ok: true, data: undefined } }
  const first = confirmEmailLink('confirm-email', 'single-use-token', run)
  const second = confirmEmailLink('confirm-email', 'single-use-token', run)
  assert.equal(first, second)
  await Promise.all([first, second])
  assert.equal(calls, 1)
  await confirmEmailLink('confirm-email-change', 'single-use-token', run)
  assert.equal(calls, 2)
  await confirmEmailLink('confirm-email-change', 'single-use-token', run, true)
  assert.equal(calls, 3)
})
