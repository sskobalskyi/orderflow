import { useEffect } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { getTimeline } from '../api'
import { parseReason } from '../reason'

const TERMINAL = new Set(['Confirmed', 'Rejected'])
const FAILED = new Set(['StockRejected', 'PaymentFailed', 'Rejected'])

export function Timeline({ orderId }: { orderId: string | null }) {
  const { t, i18n } = useTranslation()
  const reasonText = (detail: string) => {
    const parsed = parseReason(detail)
    return parsed ? t(parsed.key, parsed.values) : detail
  }
  const queryClient = useQueryClient()
  const { data } = useQuery({
    queryKey: ['timeline', orderId],
    queryFn: () => getTimeline(orderId!),
    enabled: orderId !== null,
    refetchInterval: (q) => (q.state.data?.some((s) => TERMINAL.has(s.step)) ? false : 1000),
  })

  // The order list reads a different endpoint, so refresh it the moment the saga reaches its outcome.
  const finished = data?.some((s) => TERMINAL.has(s.step)) ?? false
  useEffect(() => {
    if (finished) void queryClient.invalidateQueries({ queryKey: ['orders'] })
  }, [finished, queryClient])

  return (
    <section className="card timeline">
      <h2>{t('timeline.title')}</h2>
      {!orderId ? (
        <p>{t('orders.select')}</p>
      ) : (
        <ol>
          {data?.map((s) => (
            <li key={s.step} className={FAILED.has(s.step) ? 'failed' : 'ok'}>
              <strong>{t(`timeline.steps.${s.step}`, { defaultValue: s.step })}</strong>
              <time>{new Date(s.at).toLocaleTimeString(i18n.language)}</time>
              {s.detail && <small>{reasonText(s.detail)}</small>}
            </li>
          ))}
        </ol>
      )}
    </section>
  )
}
