import assert from 'node:assert/strict'
import { after, before, test } from 'node:test'
import { createServer } from 'vite'
import { createElement } from 'react'
import { renderToString } from 'react-dom/server'

let vite, usersApi, calls
const originalFetch = globalThis.fetch
const originalStorage = globalThis.localStorage
const profile = { login: ' test-user ', name: ' Jan ', surname: ' Kowalski ', phone: '', birthDate: '2000-01-01' }

before(async () => {
  globalThis.localStorage = { getItem: () => JSON.stringify({ accessToken: 'synthetic-test-token' }) }
  globalThis.fetch = async (url, options) => { calls.push({ url, ...options }); return Response.json({}) }
  vite = await createServer({ logLevel: 'error' })
  ;({ usersApi } = await vite.ssrLoadModule('/src/api/users.ts'))
})
after(async () => {
  await vite?.close()
  globalThis.fetch = originalFetch
  globalThis.localStorage = originalStorage
})

test('profile edits preserve the image; preset selection and removal are explicit', async () => {
  calls = []
  await usersApi.updateProfile(profile)
  await usersApi.updateProfile({ ...profile, icon: 'icons/presets/users/test.png' })
  await usersApi.updateProfile({ ...profile, removeIcon: true })
  assert.deepEqual(JSON.parse(calls[0].body), { login: 'test-user', name: 'Jan', surname: 'Kowalski', phone: null, birthDate: '2000-01-01' })
  assert.equal(JSON.parse(calls[1].body).icon, 'icons/presets/users/test.png')
  assert.equal(JSON.parse(calls[2].body).removeIcon, true)
  assert.equal(JSON.parse(calls[2].body).icon, undefined)
})

test('custom profile images use authenticated multipart with all required profile fields', async () => {
  calls = []
  const file = new File(['synthetic-image'], 'test.png', { type: 'image/png' })
  await usersApi.updateProfile({ ...profile, iconFile: file })
  const call = calls[0]
  assert.equal(call.url, '/api/users/profile')
  assert.equal(call.method, 'PUT')
  assert.equal(call.headers.Authorization, 'Bearer synthetic-test-token')
  assert.equal(call.headers['Content-Type'], undefined)
  assert.equal(call.body.get('iconFile'), file)
  assert.equal(call.body.get('login'), 'test-user')
  assert.equal(call.body.get('birthDate'), '2000-01-01')
  assert.equal(call.body.has('icon'), false)
  assert.equal(call.body.has('removeIcon'), false)
  assert.equal(call.body.has('iconStorageKey'), false)
})

test('avatar reads use the signed URL and preset endpoints', async () => {
  calls = []
  await usersApi.presets()
  await usersApi.iconUrl()
  assert.deepEqual(calls.map(c => c.url), ['/api/users/profile/icon-presets', '/api/users/profile/icon'])
})

test('avatars show initials without an image and render a supplied signed URL', async () => {
  const { ProfileIcon } = await vite.ssrLoadModule('/src/views/ProfileIcon.tsx')
  const initials = renderToString(createElement(ProfileIcon, { name: ' Łukasz ', surname: ' Żak ' }))
  assert.match(initials, /ŁŻ/)
  assert.doesNotMatch(initials, /<img/)
  const image = renderToString(createElement(ProfileIcon, { name: 'Jan', surname: 'Kowalski', url: 'https://storage.example.test/signed.png' }))
  assert.match(image, /<img[^>]+src="https:\/\/storage.example.test\/signed.png"/)
})

test('profile offers password and avatar settings, with setup wording for Google-only users', async () => {
  const { UserCtx } = await vite.ssrLoadModule('/src/userContext.ts')
  const { AppCtx } = await vite.ssrLoadModule('/src/context.ts')
  const { default: Profile } = await vite.ssrLoadModule('/src/views/Profile.tsx')
  const render = (hasPassword) => renderToString(createElement(AppCtx.Provider, { value: {} },
    createElement(UserCtx.Provider, { value: { profile: { ...profile, hasPassword, email: 'test@example.test', createdAt: '2026-11-12', lastLoginTime: null } } }, createElement(Profile))))
  assert.match(render(true), /Zmień hasło/)
  assert.match(render(true), /Zmień ikonę profilu/)
  assert.match(render(false), /Ustaw hasło/)
})
