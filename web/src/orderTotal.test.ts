import { describe, expect, it } from 'vitest'
import { isValidLine, orderTotal } from './orderTotal'

describe('orderTotal', () => {
  it('sums quantity times price', () => {
    expect(
      orderTotal([
        { sku: 'A', quantity: 2, unitPrice: 10 },
        { sku: 'B', quantity: 1, unitPrice: 5.5 },
      ]),
    ).toBe(25.5)
  })
})

describe('isValidLine', () => {
  it.each([
    [{ sku: '', quantity: 1, unitPrice: 1 }, false],
    [{ sku: 'A', quantity: 0, unitPrice: 1 }, false],
    [{ sku: 'A', quantity: 1.5, unitPrice: 1 }, false],
    [{ sku: 'A', quantity: 1, unitPrice: -1 }, false],
    [{ sku: 'A', quantity: 1, unitPrice: 0 }, true],
  ])('%j -> %s', (line, expected) => {
    expect(isValidLine(line)).toBe(expected)
  })
})
