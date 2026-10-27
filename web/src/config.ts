/**
 * Build-time branding, so a deployment can present the shop under its own name and link back to its main app.
 * Both are optional: without them the UI says "Orderflow" and shows no link.
 */
export interface Branding {
  shopName: string
  homeUrl: string | null
}

/** Only http(s) links are accepted, so a misconfigured value can never become a `javascript:` URL. */
export function readBranding(env: { VITE_SHOP_NAME?: string; VITE_HOME_URL?: string }): Branding {
  const shopName = env.VITE_SHOP_NAME?.trim() || 'Orderflow'
  const raw = env.VITE_HOME_URL?.trim()
  let homeUrl: string | null = null
  if (raw) {
    try {
      const url = new URL(raw)
      if (url.protocol === 'http:' || url.protocol === 'https:') homeUrl = url.toString()
    } catch {
      homeUrl = null
    }
  }
  return { shopName, homeUrl }
}

export const branding = readBranding({
  VITE_SHOP_NAME: import.meta.env.VITE_SHOP_NAME,
  VITE_HOME_URL: import.meta.env.VITE_HOME_URL,
})
