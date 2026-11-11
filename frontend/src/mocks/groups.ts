// UI-side models, labels and helpers for Grouping. Shapes follow the real DTOs (GroupResponse, GroupMemberResponse, ...). No data lives here.
import { ME_ID } from '../api/identity'
import { knownName } from '../api/names'
import type { GroupIconPreset, GroupInvitationResponse, GroupSettlementResponse, GroupSettlementSuggestionsResponse } from '../api/types'

export { ME_ID }
export const mockName = (userId: string) => knownName(userId) ?? 'Użytkownik'
// Grouping returns only userId. A name is shown only when another response supplied it; otherwise a readable shortened ID.
export const shortId = (id: string) => (id.length > 12 ? `${id.slice(0, 4)}…${id.slice(-4)}` : id)
export const userLabel = (id: string) => {
  const n = knownName(id)
  return id === ME_ID ? `Ty (${n ?? shortId(id)})` : n ?? shortId(id)
}

export type MembershipStatus = 'active' | 'former'
export type MembershipFilter = 'active' | 'former' | 'all'

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

export const MAX_DESCRIPTION = 255
export const MAX_ICON_BYTES = 5 * 1024 * 1024
export const ICON_TYPES = ['image/png', 'image/jpeg', 'image/webp']
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
  413: 'Plik ikony jest za duży. Maksymalny rozmiar to 5 MiB.',
  415: 'Nieobsługiwany format pliku. Użyj PNG, JPEG lub WebP.',
  503: 'Nie udało się zapisać lub pobrać ikony. Spróbuj ponownie za chwilę.',
}
