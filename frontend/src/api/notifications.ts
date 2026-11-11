import { api, type Page } from './client'
import type { NotificationResponse } from './types'

export const notifications = {
  list: (isRead: boolean | undefined, pageNumber: number, pageSize: number) =>
    api.get<Page<NotificationResponse>>('/api/notifications', { pageNumber, pageSize, isRead }),
  markRead: (id: string) => api.put(`/api/notifications/${id}/read`),
  markAllRead: () => api.put('/api/notifications/read-all'),
}
