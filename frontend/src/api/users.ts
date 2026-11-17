import { api } from './client'
import type { FileUrlResult, UserProfileResponse } from './types'
import type { ProfileInput } from '../mocks/users'

export type UserIconPreset = FileUrlResult & { key: string }

export const usersApi = {
  presets: () => api.get<UserIconPreset[]>('/api/users/profile/icon-presets'),
  iconUrl: () => api.get<FileUrlResult>('/api/users/profile/icon'),
  updateProfile: (i: ProfileInput) => {
    const fields = { login: i.login.trim(), name: i.name.trim(), surname: i.surname.trim(), phone: i.phone.trim() || null, birthDate: i.birthDate }
    if (i.iconFile) {
      const form = new FormData()
      Object.entries(fields).forEach(([key, value]) => { if (value !== null) form.set(key, value) })
      form.set('iconFile', i.iconFile)
      return api.upload<UserProfileResponse>('PUT', '/api/users/profile', form)
    }
    return api.put<UserProfileResponse>('/api/users/profile', { ...fields, ...(i.removeIcon ? { removeIcon: true } : i.icon ? { icon: i.icon } : {}) })
  },
}
