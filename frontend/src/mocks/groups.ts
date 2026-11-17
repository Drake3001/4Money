// UI-side models, labels and helpers for Grouping. Shapes follow the real DTOs (GroupResponse, GroupMemberResponse, ...). No data lives here.
import { ME_ID } from '../api/identity'
import { knownName } from '../api/names'
import { ICON_TOO_LARGE } from '../shared/iconImage'
import type { GroupIconPreset, GroupInvitationResponse, GroupSettlementResponse, GroupSettlementSuggestionsResponse } from '../api/types'

export { ME_ID }
export const mockName = (userId: string) => knownName(userId) ?? 'Użytkownik'
// Names come from authorized member and expense responses; never expose IDs as UI labels.
export const shortId = (id: string) => (id.length > 12 ? `${id.slice(0, 4)}…${id.slice(-4)}` : id)
export const userLabel = (id: string, displayName?: string | null) => {
  const n = displayName?.trim() || knownName(id)
  return id === ME_ID ? (n ? `${n} (Ty)` : 'Ty') : n ?? 'Użytkownik'
}

export type MembershipStatus = 'active' | 'former'
export type MembershipFilter = 'active' | 'former' | 'all'
export type GroupSortBy = 'name' | 'joinedAt'

export type IconPreset = GroupIconPreset
export type Group = {
  id: string; name: string; description: string | null; currencyCode: string; adminUserId: string
  /** Preset key (GroupResponse.icon). */
  icon: string | null
  /** True when the group has a custom uploaded image (GroupResponse.iconStorageKey is set). */
  hasCustomIcon: boolean
  isMember: boolean; archived: boolean
}
export type Member = {
  userId: string; status: MembershipStatus; joinedAt: string
  displayName?: string
  totalPaid: number; totalShare: number; totalSettlementsSent: number; totalSettlementsReceived: number
}
export type Invitation = GroupInvitationResponse
export type GroupData = Group & { members: Member[]; invitations: Invitation[] }

export type PaymentMethod = 1 | 2 | 3
export const PAYMENT_METHODS: { v: PaymentMethod; label: string }[] = [{ v: 1, label: 'Gotówka' }, { v: 2, label: 'Przelew bankowy' }, { v: 3, label: 'Inne' }]
export const paymentLabel = (m: number) => PAYMENT_METHODS.find((x) => x.v === m)?.label ?? 'Inne'
export type Settlement = GroupSettlementResponse
export type SettlementInput = { fromUserId: string; toUserId: string; amount: string; method: PaymentMethod; settledAt: string }
export type SettlementSuggestionsResponse = GroupSettlementSuggestionsResponse
export type SettlementSuggestion = GroupSettlementSuggestionsResponse['suggestions'][number]

export const MAX_DESCRIPTION = 100
export const charCount = (s: string) => [...s].length

export type InvitationState = 'usable' | 'revoked' | 'expired' | 'exhausted'
// isActive alone does not mean an invitation can be used.
export const invitationState = (i: Invitation, now = Date.now()): InvitationState =>
  !i.isActive ? 'revoked' : new Date(i.expiresAt).getTime() <= now ? 'expired' : i.useCount >= i.maxUses ? 'exhausted' : 'usable'

export const ERROR_TEXT: Record<number, string> = {
  400: 'Formularz zawiera nieprawidłowe dane. Sprawdź pola i spróbuj ponownie.',
  403: 'Ta czynność wymaga uprawnień administratora grupy lub nie jest dla Ciebie dostępna.',
  404: 'Nie znaleziono grupy lub zaproszenie jest niedostępne.',
  409: 'Ta operacja jest teraz niemożliwa dla tego członkostwa lub administratora grupy.',
  413: ICON_TOO_LARGE,
  415: 'Wybierz obsługiwany format pliku: JPEG, PNG, WebP, HEIC/HEIF.',
  503: 'Nie udało się zapisać lub pobrać ikony. Spróbuj ponownie za chwilę.',
}
