import { userManager } from './auth'
import type { Order, OrderLine, TimelineStep } from './types'

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

export const listOrders = () => request<Order[]>('/api/orders')
export const getTimeline = (id: string) => request<TimelineStep[]>(`/api/orders/${id}/timeline`)
export const submitOrder = (lines: OrderLine[]) =>
  request<Order>('/api/orders', {
    method: 'POST',
    headers: { 'Idempotency-Key': crypto.randomUUID() },
    body: JSON.stringify({ lines }),
  })
