import { UserManager, WebStorageStateStore } from 'oidc-client-ts'

export const userManager = new UserManager({
  authority: import.meta.env.VITE_OIDC_AUTHORITY ?? 'http://localhost:8180/realms/orderflow',
  client_id: 'orderflow-web',
  redirect_uri: window.location.origin,
  post_logout_redirect_uri: window.location.origin,
  response_type: 'code', // authorization code flow, PKCE is applied by the library
  scope: 'openid profile',
  userStore: new WebStorageStateStore({ store: window.sessionStorage }),
})

/** Completes the redirect from Keycloak, if the URL carries an authorization response. */
export async function handleRedirectCallback(): Promise<void> {
  const params = new URLSearchParams(window.location.search)
  if (params.has('code') && params.has('state')) {
    await userManager.signinRedirectCallback()
    window.history.replaceState({}, document.title, window.location.pathname)
  }
}
