import { useQuery } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { getTimeline } from '../api'

const TERMINAL = new Set(['Confirmed', 'Rejected'])
const FAILED = new Set(['StockRejected', 'PaymentFailed', 'Rejected'])

export function Timeline({ orderId }: { orderId: string | null }) {
  const { t, i18n } = useTranslation()
  const { data } = useQuery({
    queryKey: ['timeline', orderId],
    queryFn: () => getTimeline(orderId!),
    enabled: orderId !== null,
    refetchInterval: (q) => (q.state.data?.some((s) => TERMINAL.has(s.step)) ? false : 1000),
  })

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
              {s.detail && <small>{s.detail}</small>}
            </li>
          ))}
        </ol>
      )}
    </section>
  )
}
