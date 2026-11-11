// Display names learned from API responses that carry them (UserProfileResponse, expense payerDisplayName / userDisplayName).
// Grouping returns only userIds, so a name is shown only when some response has provided it.
const names = new Map<string, string>()
export const registerName = (id: string, name: string) => { if (id && name.trim()) names.set(id, name.trim()) }
export const knownName = (id: string) => names.get(id)
