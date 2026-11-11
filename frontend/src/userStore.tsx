import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react'
import { UserCtx, type UserApi } from './userContext'
import { api, run, sessionStore, setSessionLostHandler } from './api/client'
import { setMeId } from './api/identity'
import { registerName } from './api/names'
import type { AuthenticationResponse, RegisterUserResponse, UserProfileResponse } from './api/types'
import type { UserProfile } from './mocks/users'

const EMPTY: UserProfile = { id: '', login: '', name: '', surname: '', email: '', phone: null, birthDate: '', icon: null, isEmailConfirmed: false, createdAt: '', lastLoginTime: null }

// User module over the real API (POST/GET/PUT /api/users/...). Tokens live in the api client's session store and are never shown.
export function UserProvider({ children }: { children: ReactNode }) {
  const [profile, setProfile] = useState<UserProfile>(EMPTY)
  const [profileReady, setReady] = useState(false)
  const [signedIn, setSignedIn] = useState(() => !!sessionStore.get())
  const [pendingEmail, setPendingEmail] = useState<string | null>(null)
  const [deactivationRequested, setDeactivationRequested] = useState(false)

  const applyProfile = useCallback((p: UserProfileResponse) => {
    setMeId(p.id); registerName(p.id, `${p.name} ${p.surname}`)
    setProfile(p); setReady(true)
  }, [])
  const loadProfile = useCallback(() => run(() => api.get<UserProfileResponse>('/api/users/profile')).then((r) => { if (r.ok) applyProfile(r.data); return r }), [applyProfile])
  const clear = useCallback(() => { sessionStore.clear(); setMeId(''); setProfile(EMPTY); setReady(false); setSignedIn(false) }, [])

  useEffect(() => { setSessionLostHandler(clear) }, [clear])
  useEffect(() => { if (signedIn && !profileReady) void loadProfile() }, [signedIn]) // eslint-disable-line react-hooks/exhaustive-deps

  const apiObj = useMemo<UserApi>(() => ({
    profile, profileReady, isSignedIn: signedIn, pendingEmail, deactivationRequested,
    login: async (login, password) => {
      const r = await run(() => api.post<AuthenticationResponse>('/api/users/login', { login, password }, { auth: false }))
      if (!r.ok) return r
      sessionStore.set(r.data)
      setSignedIn(true)
      const p = await loadProfile()
      return p.ok ? { ok: true, data: undefined } : p
    },
    logout: async () => {
      await run(() => api.post('/api/users/logout')) // the local session is dropped even if the server cannot be reached
      clear()
    },
    register: (i) => run(async () => { await api.post<RegisterUserResponse>('/api/users/register', i, { auth: false }) }),
    confirmEmail: (token) => run(() => api.post('/api/users/confirm-email', { token }, { auth: false })),
    resendConfirmation: (email) => run(() => api.post('/api/users/resend-confirmation', { email }, { auth: false })),
    updateProfile: (i) => run(async () => {
      const p = await api.put<UserProfileResponse>('/api/users/profile', { login: i.login.trim(), name: i.name.trim(), surname: i.surname.trim(), phone: i.phone.trim() || null, birthDate: i.birthDate, icon: i.icon })
      applyProfile(p)
    }),
    requestEmailChange: (newEmail, currentPassword) => run(async () => { await api.post('/api/users/profile/change-email', { newEmail, currentPassword }); setPendingEmail(newEmail) }),
    confirmEmailChange: (token) => run(async () => {
      await api.post('/api/users/confirm-email-change', { token }, { auth: false })
      setPendingEmail(null)
      clear()
    }),
    forgotPassword: (email) => run(() => api.post('/api/users/forgot-password', { email }, { auth: false })),
    resetPassword: (token, newPassword) => run(async () => {
      await api.post('/api/users/reset-password', { token, newPassword }, { auth: false })
      clear()
    }),
    requestDeactivation: () => run(async () => { await api.post('/api/users/profile/deactivate'); setDeactivationRequested(true) }),
    confirmDeactivation: (token) => run(async () => { await api.post('/api/users/confirm-deactivation', { token }, { auth: false }); setDeactivationRequested(false); clear() }),
  }), [profile, profileReady, signedIn, pendingEmail, deactivationRequested, applyProfile, loadProfile, clear])

  return <UserCtx.Provider value={apiObj}>{children}</UserCtx.Provider>
}
