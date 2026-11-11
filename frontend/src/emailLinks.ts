import type { Route } from './context'
import type { UserRes } from './userContext'

export const EMAIL_ACTIONS = ['confirm-email', 'confirm-email-change', 'confirm-deactivation', 'reset'] as const

export function initialRoute(location: Pick<Location, 'search' | 'hash'>): Route {
  const query = new URLSearchParams(location.search)
  const action = query.get('action')
  if (EMAIL_ACTIONS.some((name) => name === action)) {
    const token = new URLSearchParams(location.hash?.replace(/^#/, '')).get('token') ?? ''
    return { name: action as typeof EMAIL_ACTIONS[number], id: token }
  }
  const invitation = query.get('token')
  return invitation ? { name: 'invite', id: invitation } : { name: 'dashboard' }
}

export function clearEmailLink() {
  const url = new URL(window.location.href)
  url.searchParams.delete('action')
  url.hash = ''
  window.history.replaceState(null, '', url.pathname + url.search)
}

// A single in-flight request also covers React StrictMode's repeated effect setup.
// Only the most recently opened link is retained, in memory for this page lifetime.
let lastConfirmation: { action: string; token: string; result: Promise<UserRes> } | undefined
export function confirmEmailLink(action: string, token: string, run: (token: string) => Promise<UserRes>, retry = false): Promise<UserRes> {
  if (!retry && lastConfirmation?.action === action && lastConfirmation.token === token) return lastConfirmation.result
  const result = Promise.resolve().then(() => run(token))
  lastConfirmation = { action, token, result }
  return result
}
