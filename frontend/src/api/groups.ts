import { api, allPages, type Page } from './client'
import { ME_ID } from './identity'
import type {
  CreateGroupInvitationResponse, FileUrlResult, GroupIconPreset, GroupInvitationResponse, GroupMemberResponse, GroupResponse,
  GroupSettlementRequest, GroupSettlementResponse, GroupSettlementSuggestionsResponse, InvitationPreview,
} from './types'
import type { Group, GroupData, Member, MembershipFilter, SettlementInput } from '../mocks/groups'

export const toGroup = (g: GroupResponse): Group => ({
  id: g.id, name: g.name, description: g.description, currencyCode: g.currencyCode, adminUserId: g.adminUserId, icon: g.icon,
  hasCustomIcon: !!g.iconStorageKey, isMember: g.isMember, archived: g.isArchived,
})
export const toMember = (m: GroupMemberResponse): Member => ({
  userId: m.userId, status: m.isMember ? 'active' : 'former', joinedAt: m.joinedAt, totalPaid: m.totalPaid, totalShare: m.totalShare,
  totalSettlementsSent: m.totalSettlementsSent, totalSettlementsReceived: m.totalSettlementsReceived,
})

export type GroupWrite = { name: string; description: string; currencyCode?: string; icon?: string | null; iconFile?: File | null; removeIcon?: boolean }

function groupBody(i: GroupWrite, creating: boolean) {
  const desc = i.description.trim() || undefined
  if (i.iconFile) {
    const f = new FormData() // multipart: the browser sets the boundary
    f.set('name', i.name.trim()); if (creating) f.set('currencyCode', i.currencyCode ?? 'PLN'); if (desc) f.set('description', desc)
    f.set('iconFile', i.iconFile)
    return { form: f }
  }
  const json: Record<string, unknown> = { name: i.name.trim(), description: desc ?? null }
  if (creating) json.currencyCode = i.currencyCode ?? 'PLN'
  if (i.icon) json.icon = i.icon
  if (!creating) json.removeIcon = !!i.removeIcon
  return { json }
}

export const groupsApi = {
  presets: () => api.get<GroupIconPreset[]>('/api/groups/icon-presets'),
  list: async (membership: MembershipFilter) => (await allPages<GroupResponse>('/api/groups', { membership })).map(toGroup),
  get: async (id: string): Promise<GroupData> => {
    const g = toGroup(await api.get<GroupResponse>(`/api/groups/${id}`))
    const members = (await allPages<GroupMemberResponse>(`/api/groups/${id}/members`, { membership: 'all' })).map(toMember)
    // Invitations are listed for administrators only (others would get 403).
    const invitations = g.adminUserId === ME_ID ? await allPages<GroupInvitationResponse>(`/api/groups/${id}/invitations`) : []
    return { ...g, members, invitations }
  },
  activeMembers: async (id: string) => (await allPages<GroupMemberResponse>(`/api/groups/${id}/members`, { membership: 'active' })).map((m) => m.userId),
  iconUrl: (id: string) => api.get<FileUrlResult>(`/api/groups/${id}/icon`),
  create: async (i: GroupWrite) => { const b = groupBody(i, true); return toGroup(b.form ? await api.upload<GroupResponse>('POST', '/api/groups', b.form) : await api.post<GroupResponse>('/api/groups', b.json)) },
  update: async (id: string, i: GroupWrite) => { const b = groupBody(i, false); return toGroup(b.form ? await api.upload<GroupResponse>('PUT', `/api/groups/${id}`, b.form) : await api.put<GroupResponse>(`/api/groups/${id}`, b.json)) },
  archive: (id: string) => api.del(`/api/groups/${id}`),
  transferAdmin: (id: string, userId: string) => api.put<GroupResponse>(`/api/groups/${id}/administrator`, { userId }),
  leave: (id: string) => api.post(`/api/groups/${id}/leave`),
  removeMember: (id: string, userId: string) => api.del(`/api/groups/${id}/members/${userId}`),
  createInvitation: (id: string, b: { maxUses: number; expiresAt: string }) => api.post<CreateGroupInvitationResponse>(`/api/groups/${id}/invitations`, b),
  revokeInvitation: (id: string, invitationId: string) => api.del(`/api/groups/${id}/invitations/${invitationId}`),
  previewInvitation: (token: string) => api.post<InvitationPreview>('/api/group-invitations/preview', { token: token.trim() }),
  acceptInvitation: (token: string) => api.post<GroupMemberResponse>('/api/group-invitations/accept', { token: token.trim() }),
  // Settlements (payments made outside 4Money; bankTransactionId is never sent)
  settlements: (id: string) => allPages<GroupSettlementResponse>(`/api/groups/${id}/settlements`, { descending: true }),
  settlement: (id: string, sid: string) => api.get<GroupSettlementResponse>(`/api/groups/${id}/settlements/${sid}`),
  createSettlement: (id: string, i: SettlementInput) => api.post<GroupSettlementResponse>(`/api/groups/${id}/settlements`, settlementBody(i)),
  updateSettlement: (id: string, sid: string, i: SettlementInput) => api.put<GroupSettlementResponse>(`/api/groups/${id}/settlements/${sid}`, settlementBody(i)),
  deleteSettlement: (id: string, sid: string) => api.del(`/api/groups/${id}/settlements/${sid}`),
  suggestions: (id: string) => api.get<GroupSettlementSuggestionsResponse>(`/api/groups/${id}/settlements/suggestions`),
}

const settlementBody = (i: SettlementInput): GroupSettlementRequest => ({
  fromUserId: i.fromUserId, toUserId: i.toUserId, amount: Math.round(Number(i.amount.replace(',', '.')) * 100) / 100, method: i.method, settledAt: i.settledAt,
})
export type { Page }
