import { useMemo } from 'react'
import { useGroups, type GroupsApi } from '../groupsContext'

// Settlement service used by the group UI; thin wrapper over the real Grouping API.
// Records payments made OUTSIDE 4Money; it never initiates a bank transfer.
export type SettlementService = {
  /** POST /api/groups/{groupId}/settlements */
  createSettlement: GroupsApi['createSettlement']
  /** PUT /api/groups/{groupId}/settlements/{settlementId} */
  updateSettlement: GroupsApi['updateSettlement']
  /** DELETE /api/groups/{groupId}/settlements/{settlementId} */
  deleteSettlement: GroupsApi['deleteSettlement']
}

export function useSettlementService(): SettlementService {
  const groups = useGroups()
  return useMemo<SettlementService>(() => ({
    createSettlement: groups.createSettlement,
    updateSettlement: groups.updateSettlement,
    deleteSettlement: groups.deleteSettlement,
  }), [groups])
}
