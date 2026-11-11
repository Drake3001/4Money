// Id of the signed-in user (from GET /api/users/profile). ES live binding: importers always read the current value.
export let ME_ID = ''
export const setMeId = (id: string) => { ME_ID = id }
