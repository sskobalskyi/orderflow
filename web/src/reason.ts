/**
 * Saga reasons arrive from the services as English text. Map the known ones to translation keys so the
 * timeline reads in the user's language; anything unknown is shown as received.
 */
export function parseReason(reason: string): { key: string; values: Record<string, string> } | null {
  const declined = /^Card declined: amount exceeds (\d+(?:\.\d+)?)$/.exec(reason)
  if (declined) return { key: 'reasons.cardDeclined', values: { limit: declined[1] } }

  const stock = /^Insufficient stock for: (.+)$/.exec(reason)
  if (stock) return { key: 'reasons.insufficientStock', values: { skus: stock[1] } }

  return null
}
