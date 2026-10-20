export type OrderStatus = 'Submitted' | 'Confirmed' | 'Rejected'

export interface OrderLine {
  sku: string
  quantity: number
  unitPrice: number
}

export interface Order {
  id: string
  status: OrderStatus
  rejectionReason: string | null
  createdAt: string
  lines: OrderLine[]
  total: number
}

export interface TimelineStep {
  step: string
  detail: string | null
  at: string
}
