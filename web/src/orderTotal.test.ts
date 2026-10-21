import { describe, expect, it } from 'vitest'
import { formatMoney, isValidQuantity, orderTotal } from './orderTotal'

describe('orderTotal', () => {
  it('sums quantity times price', () => {
    expect(
      orderTotal([
        { quantity: 2, unitPrice: 10 },
        { quantity: 1, unitPrice: 5.5 },
      ]),
    ).toBe(25.5)
  })

  it('is zero for an empty cart', () => {
    expect(orderTotal([])).toBe(0)
  })
})

describe('isValidQuantity', () => {
  it.each([
    [0, false],
    [-1, false],
    [1.5, false],
    [1, true],
    [12, true],
  ])('%s -> %s', (quantity, expected) => {
    expect(isValidQuantity(quantity)).toBe(expected)
  })
})

describe('formatMoney', () => {
  it('formats with the currency', () => {
    expect(formatMoney(25, 'USD', 'en')).toBe('$25.00')
  })
})
