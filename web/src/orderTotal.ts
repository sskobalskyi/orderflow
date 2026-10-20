import type { OrderLine } from './types'

export const orderTotal = (lines: OrderLine[]) =>
  lines.reduce((sum, l) => sum + l.quantity * l.unitPrice, 0)

/** A line is submittable when it has a SKU, a positive whole quantity and a non-negative price. */
export const isValidLine = (l: OrderLine) =>
  l.sku.trim().length > 0 && Number.isInteger(l.quantity) && l.quantity > 0 && l.unitPrice >= 0
