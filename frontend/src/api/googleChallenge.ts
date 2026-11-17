import { api, run, type Res } from './client'
import type { GoogleChallengeResponse } from './types'

let pending: Promise<Res<GoogleChallengeResponse>> | null = null

// StrictMode repeats effect setup. Share the in-flight request so a second
// Set-Cookie response cannot replace the cookie belonging to the SDK's nonce.
export function requestGoogleChallenge(): Promise<Res<GoogleChallengeResponse>> {
  pending ??= run(() => api.post<GoogleChallengeResponse>('/api/users/google/challenge', undefined, { auth: false }))
    .finally(() => { pending = null })
  return pending
}
