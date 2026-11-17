import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react'
import { UserCtx, type UserApi } from './userContext'
import { api, run, sessionStore, setSessionLostHandler } from './api/client'
import { setMeId } from './api/identity'
import { registerName } from './api/names'
import { usersApi } from './api/users'
import type { AuthenticationResponse, GoogleRegistrationResponse, RegisterUserResponse, UserProfileResponse } from './api/types'
import type { UserProfile } from './mocks/users'

const EMPTY: UserProfile = { id: '', login: '', name: '', surname: '', email: '', phone: null, birthDate: '', icon: null, iconStorageKey: null, hasPassword: false, isGoogleLinked: false, isEmailConfirmed: false, createdAt: '', lastLoginTime: null }

// User module over the real API (POST/GET/PUT /api/users/...). Tokens live in the api client's session store and are never shown.
export function UserProvider({ children }: { children: ReactNode }) {
  const [profile, setProfile] = useState<UserProfile>(EMPTY)
  const [profileReady, setReady] = useState(false)
  const [signedIn, setSignedIn] = useState(() => !!sessionStore.get())
  const [pendingEmail, setPendingEmail] = useState<string | null>(null)
  const [deactivationRequestedFor, setDeactivationRequestedFor] = useState<string | null>(null)
  const deactivationRequested = !!profile.id && deactivationRequestedFor === profile.id
  const [profileImageUrl, setProfileImageUrl] = useState<string | null>(null)

  useEffect(() => {
    let cancelled = false
    let timer: ReturnType<typeof setTimeout>
    setProfileImageUrl(null)
    if (!signedIn || !(profile.icon || profile.iconStorageKey)) return
    const load = async () => {
      const r = await run(usersApi.iconUrl)
      if (cancelled) return
      setProfileImageUrl(r.ok ? r.data.url : null)
      // Refresh signed URLs before expiry; retry temporary storage errors.
      const delay = r.ok ? Math.max(1000, Date.parse(r.data.expiresAt) - Date.now() - 30_000) : 60_000
      timer = setTimeout(load, Number.isFinite(delay) ? delay : 60_000)
    }
    void load()
    return () => { cancelled = true; clearTimeout(timer) }
  }, [profile, signedIn])

  const applyProfile = useCallback((p: UserProfileResponse) => {
    setMeId(p.id); registerName(p.id, `${p.name} ${p.surname}`)
    setProfile(p); setReady(true)
  }, [])
  const loadProfile = useCallback(() => run(() => api.get<UserProfileResponse>('/api/users/profile')).then((r) => { if (r.ok) applyProfile(r.data); return r }), [applyProfile])
  const clear = useCallback(() => { sessionStore.clear(); setMeId(''); setProfile(EMPTY); setReady(false); setSignedIn(false); setPendingEmail(null); setDeactivationRequestedFor(null) }, [])

  useEffect(() => { setSessionLostHandler(clear) }, [clear])
  useEffect(() => { if (signedIn && !profileReady) void loadProfile() }, [signedIn]) // eslint-disable-line react-hooks/exhaustive-deps

  const apiObj = useMemo<UserApi>(() => ({
    profile, profileImageUrl, profileReady, isSignedIn: signedIn, pendingEmail, deactivationRequested,
    login: async (login, password) => {
      const r = await run(() => api.post<AuthenticationResponse>('/api/users/login', { login, password }, { auth: false }))
      if (!r.ok) return r
      setDeactivationRequestedFor(null); setPendingEmail(null)
      sessionStore.set(r.data)
      setSignedIn(true)
      const p = await loadProfile()
      return p.ok ? { ok: true, data: undefined } : p
    },
    logout: async () => {
      await run(() => api.post('/api/users/logout')) // the local session is dropped even if the server cannot be reached
      clear()
    },
    googleLogin: async (idToken) => {
      const r = await run(() => api.post<AuthenticationResponse>('/api/users/google/login', { idToken }, { auth: false }))
      if (!r.ok) return r
      setDeactivationRequestedFor(null); setPendingEmail(null)
      sessionStore.set(r.data); setSignedIn(true)
      const p = await loadProfile()
      return p.ok ? { ok: true, data: undefined } : p
    },
    googleRegister: async (input) => {
      const r = await run(() => api.post<GoogleRegistrationResponse>('/api/users/google/register', input, { auth: false }))
      if (!r.ok) return r
      if (r.data.authentication) {
        setDeactivationRequestedFor(null); setPendingEmail(null)
        sessionStore.set(r.data.authentication); setSignedIn(true)
        const p = await loadProfile()
        if (!p.ok) return p
      }
      return r
    },
    register: (i) => run(async () => { await api.post<RegisterUserResponse>('/api/users/register', i, { auth: false }) }),
    confirmEmail: (token) => run(() => api.post('/api/users/confirm-email', { token }, { auth: false })),
    resendConfirmation: (email) => run(() => api.post('/api/users/resend-confirmation', { email }, { auth: false })),
    updateProfile: (i) => run(async () => {
      const p = await usersApi.updateProfile(i)
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
    requestDeactivation: () => run(async () => { await api.post('/api/users/profile/deactivate'); setDeactivationRequestedFor(profile.id) }),
    confirmDeactivation: (token) => run(async () => { await api.post('/api/users/confirm-deactivation', { token }, { auth: false }); clear() }),
  }), [profile, profileImageUrl, profileReady, signedIn, pendingEmail, deactivationRequested, applyProfile, loadProfile, clear])

  return <UserCtx.Provider value={apiObj}>{children}</UserCtx.Provider>
}
