import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react'
import { GroupsCtx, type GroupsApi } from './groupsContext'
import { run } from './api/client'
import { groupsApi, toMember } from './api/groups'
import { useUser } from './userContext'
import type { IconPreset } from './mocks/groups'

// Grouping module over the real API. Nothing is cached as truth: views fetch, and `rev` triggers a reload after every successful write.
export function GroupsProvider({ children }: { children: ReactNode }) {
  const { isSignedIn } = useUser()
  const [iconPresets, setPresets] = useState<IconPreset[]>([])
  const [rev, setRev] = useState(0)
  const bump = useCallback(() => setRev((n) => n + 1), [])

  useEffect(() => {
    if (!isSignedIn) { setPresets([]); return }
    void run(() => groupsApi.presets()).then((r) => { if (r.ok) setPresets(r.data) })
  }, [isSignedIn])

  const api = useMemo<GroupsApi>(() => {
    // Runs a write and refreshes dependants only when the backend confirmed success.
    const write = <T,>(fn: () => Promise<T>) => run(fn).then((r) => { if (r.ok) bump(); return r })
    return {
      iconPresets, rev,
      fetchList: async (filter) => (await groupsApi.list(filter)).map((group) => ({ group, myStatus: group.isMember ? 'active' as const : 'former' as const })),
      fetchGroup: groupsApi.get,
      fetchActiveMembers: groupsApi.activeMembers,
      fetchIconUrl: async (id) => { try { return (await groupsApi.iconUrl(id)).url } catch { return null } },
      create: (i) => write(() => groupsApi.create({ ...i, currencyCode: 'PLN' })),
      update: (id, i) => write(() => groupsApi.update(id, i)),
      archive: (id) => write(() => groupsApi.archive(id)),
      leave: (id) => write(() => groupsApi.leave(id)),
      removeMember: (id, userId) => write(() => groupsApi.removeMember(id, userId)),
      transferAdmin: (id, userId) => write(async () => { await groupsApi.transferAdmin(id, userId) }),
      createInvitation: (id, i) => write(() => groupsApi.createInvitation(id, i)),
      revokeInvitation: (id, invId) => write(() => groupsApi.revokeInvitation(id, invId)),
      previewInvitation: (token) => run(() => groupsApi.previewInvitation(token)),
      acceptInvitation: (token) => write(async () => { const m = await groupsApi.acceptInvitation(token); return { ...toMember(m), groupId: m.groupId } }),
      fetchSettlements: groupsApi.settlements,
      fetchSuggestions: groupsApi.suggestions,
      createSettlement: (gid, i) => write(() => groupsApi.createSettlement(gid, i)),
      updateSettlement: (gid, id, i) => write(() => groupsApi.updateSettlement(gid, id, i)),
      deleteSettlement: (gid, id) => write(() => groupsApi.deleteSettlement(gid, id)),
    }
  }, [iconPresets, rev, bump])

  return <GroupsCtx.Provider value={api}>{children}</GroupsCtx.Provider>
}
