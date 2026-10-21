import { describe, expect, it } from 'vitest'
import { parseReason } from './reason'

describe('parseReason', () => {
  it('recognises a declined card and extracts the limit', () => {
    expect(parseReason('Card declined: amount exceeds 1000')).toEqual({ key: 'reasons.cardDeclined', values: { limit: '1000' } })
  })

  it('recognises insufficient stock and extracts the SKUs', () => {
    expect(parseReason('Insufficient stock for: A, C')).toEqual({ key: 'reasons.insufficientStock', values: { skus: 'A, C' } })
  })

  it('returns null for unknown reasons', () => {
    expect(parseReason('Something else')).toBeNull()
  })
})
