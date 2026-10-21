import type { User } from 'oidc-client-ts'

/** Reads the realm roles Keycloak puts in the access token (`roles` claim). The gateway enforces them; this only shows or hides admin UI. */
export function rolesOf(user: User | null): string[] {
  if (!user) return []
  try {
    const payload = user.access_token.split('.')[1].replace(/-/g, '+').replace(/_/g, '/')
    const claims = JSON.parse(atob(payload)) as { roles?: string[] }
    return claims.roles ?? []
  } catch {
    return []
  }
}

export const isAdmin = (user: User | null) => rolesOf(user).includes('admin')
