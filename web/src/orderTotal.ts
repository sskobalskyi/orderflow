export interface Priced {
  quantity: number
  unitPrice: number
}

export const orderTotal = (lines: Priced[]) => lines.reduce((sum, l) => sum + l.quantity * l.unitPrice, 0)

/** A cart quantity is submittable when it is a positive whole number. */
export const isValidQuantity = (quantity: number) => Number.isInteger(quantity) && quantity > 0

export const formatMoney = (amount: number, currency: string, locale: string) =>
  new Intl.NumberFormat(locale, { style: 'currency', currency }).format(amount)
