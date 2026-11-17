import { createContext, useContext } from 'react'
import type { Res } from './api/client'
import type { InvitationPreview, GroupSettlementSuggestionsResponse as SettlementSuggestionsResponse } from './api/types'
import type { Group, GroupData, GroupSortBy, IconPreset, Invitation, Member, MembershipFilter, MembershipStatus, Settlement, SettlementInput } from './mocks/groups'

export type { Res }
export type GroupInput = { name: string; description: string; icon: string | null; iconFile: File | null; removeIcon: boolean }
export type GroupListItem = { group: Group; myStatus: MembershipStatus }

// Every operation goes to the real Grouping API; reads are Promises and `rev` changes after each successful write so views reload.
export type GroupsApi = {
  iconPresets: IconPreset[]
  rev: number
  fetchList: (filter: MembershipFilter, descending?: boolean, sortBy?: GroupSortBy) => Promise<GroupListItem[]>
  fetchGroup: (id: string) => Promise<GroupData>
  fetchActiveMembers: (id: string) => Promise<string[]>
  fetchIconUrl: (id: string) => Promise<string | null>
  create: (i: GroupInput) => Promise<Res<Group>>
  update: (id: string, i: GroupInput) => Promise<Res<Group>>
  archive: (id: string) => Promise<Res>
  leave: (id: string) => Promise<Res>
  removeMember: (id: string, userId: string) => Promise<Res>
  transferAdmin: (id: string, userId: string) => Promise<Res>
  createInvitation: (id: string, i: { maxUses: number; expiresAt: string }) => Promise<Res<{ invitation: Invitation; token: string }>>
  revokeInvitation: (id: string, invitationId: string) => Promise<Res>
  previewInvitation: (token: string) => Promise<Res<InvitationPreview>>
  acceptInvitation: (token: string) => Promise<Res<Member & { groupId: string }>>
  fetchSettlements: (groupId: string) => Promise<Settlement[]>
  fetchSuggestions: (groupId: string) => Promise<SettlementSuggestionsResponse>
  createSettlement: (groupId: string, i: SettlementInput) => Promise<Res<Settlement>>
  updateSettlement: (groupId: string, id: string, i: SettlementInput) => Promise<Res<Settlement>>
  deleteSettlement: (groupId: string, id: string) => Promise<Res>
}
export const GroupsCtx = createContext<GroupsApi>(null as never)
export const useGroups = () => useContext(GroupsCtx)
