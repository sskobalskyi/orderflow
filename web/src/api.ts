import { userManager } from './auth'
import type { Order, Product, TimelineStep } from './types'

const API = import.meta.env.VITE_API_URL ?? 'http://localhost:8080'

/** An API failure carrying a translation key for the UI. */
export class ApiError extends Error {
  readonly key: string

  constructor(status: number, code?: string) {
    const key = code
      ? `errors.${code}`
      : status === 429
        ? 'errors.rateLimited'
        : status === 401
          ? 'errors.unauthorized'
          : status === 403
            ? 'errors.forbidden'
            : 'errors.generic'
    super(key)
    this.key = key
  }
}

async function request<T>(path: string, init: RequestInit = {}): Promise<T> {
  const user = await userManager.getUser()
  const res = await fetch(`${API}${path}`, {
    ...init,
    headers: {
      'content-type': 'application/json',
      ...(user ? { authorization: `Bearer ${user.access_token}` } : {}),
      ...init.headers,
    },
  })
  if (!res.ok) {
    const problem = (await res.json().catch(() => ({}))) as { code?: string }
    throw new ApiError(res.status, problem.code)
  }
  return (await res.json()) as T
}

export const listProducts = (lang: string) => request<Product[]>(`/api/products?lang=${encodeURIComponent(lang)}`)

export const listOrders = () => request<Order[]>('/api/orders')
export const getTimeline = (id: string) => request<TimelineStep[]>(`/api/orders/${id}/timeline`)
export const returnOrder = (id: string) => request<Order>(`/api/orders/${id}/return`, { method: 'POST' })

export const submitOrder = (lines: { sku: string; quantity: number }[]) =>
  request<Order>('/api/orders', {
    method: 'POST',
    headers: { 'Idempotency-Key': crypto.randomUUID() },
    body: JSON.stringify({ lines }),
  })

export const adminListOrders = () => request<Order[]>('/api/admin/orders')
export const adminShip = (id: string) => request<Order>(`/api/admin/orders/${id}/ship`, { method: 'POST' })
export const adminRefund = (id: string) =>
  request<{ id: string }>(`/api/admin/orders/${id}/refund`, { method: 'POST', body: JSON.stringify({ reason: 'admin refund' }) })
