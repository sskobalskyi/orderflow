export type OrderStatus = 'Submitted' | 'Confirmed' | 'Rejected' | 'Shipped' | 'Returned' | 'Refunded'

export interface OrderLine {
  sku: string
  quantity: number
  unitPrice: number
}

export interface Order {
  id: string
  status: OrderStatus
  rejectionReason: string | null
  currency: string
  createdAt: string
  /** Set when an outside system reported the goods of a shipped order in use. */
  activatedAt?: string | null
  lines: OrderLine[]
  total: number
}

export interface Product {
  sku: string
  name: string
  description: string
  price: number
  currency: string
}

export interface TimelineStep {
  step: string
  detail: string | null
  at: string
}
