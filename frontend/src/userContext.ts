import { createContext, useContext } from 'react'
import type { Res } from './api/client'
import type { ProfileInput, RegisterInput, UserProfile } from './mocks/users'

export type UserRes<T = void> = Res<T>

export type UserApi = {
  profile: UserProfile
  /** False until GET /api/users/profile has answered for the current session. */
  profileReady: boolean
  isSignedIn: boolean
  /** UI memory only: a change-email / deactivation request was sent in this browser session. */
  pendingEmail: string | null
  deactivationRequested: boolean
  login: (login: string, password: string) => Promise<UserRes>
  logout: () => Promise<void>
  register: (i: RegisterInput) => Promise<UserRes>
  confirmEmail: (token: string) => Promise<UserRes>
  resendConfirmation: (email: string) => Promise<UserRes>
  updateProfile: (i: ProfileInput) => Promise<UserRes>
  requestEmailChange: (newEmail: string, password: string) => Promise<UserRes>
  confirmEmailChange: (token: string) => Promise<UserRes>
  forgotPassword: (email: string) => Promise<UserRes>
  resetPassword: (token: string, password: string) => Promise<UserRes>
  requestDeactivation: () => Promise<UserRes>
  confirmDeactivation: (token: string) => Promise<UserRes>
}
export const UserCtx = createContext<UserApi>(null as never)
export const useUser = () => useContext(UserCtx)
