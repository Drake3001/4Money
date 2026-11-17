// Display names learned from API responses that carry them (UserProfileResponse, expense payerDisplayName / userDisplayName).
// Authorized group member responses also provide displayName for current and former members.
const names = new Map<string, string>()
export const registerName = (id: string, name: string) => { if (id && name.trim()) names.set(id, name.trim()) }
export const knownName = (id: string) => names.get(id)
