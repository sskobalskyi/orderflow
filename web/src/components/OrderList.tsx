import { useQuery } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { listOrders } from '../api'

export function OrderList({ selected, onSelect }: { selected: string | null; onSelect: (id: string) => void }) {
  const { t, i18n } = useTranslation()
  const { data, isPending } = useQuery({
    queryKey: ['orders'],
    queryFn: listOrders,
    // Keep polling while any order is still moving through the saga.
    refetchInterval: (q) => (q.state.data?.some((o) => o.status === 'Submitted') ? 1500 : false),
  })

  return (
    <section className="card">
      <h2>{t('orders.title')}</h2>
      {isPending ? (
        <p>{t('orders.loading')}</p>
      ) : !data?.length ? (
        <p>{t('orders.empty')}</p>
      ) : (
        <table>
          <thead>
            <tr>
              <th>{t('orders.created')}</th>
              <th>{t('orders.total')}</th>
              <th>{t('orders.status')}</th>
            </tr>
          </thead>
          <tbody>
            {data.map((o) => (
              <tr key={o.id} className={o.id === selected ? 'selected' : ''} onClick={() => onSelect(o.id)}>
                <td>{new Date(o.createdAt).toLocaleString(i18n.language)}</td>
                <td>{o.total.toFixed(2)}</td>
                <td>
                  <span className={`badge ${o.status}`}>{t(`status.${o.status}`)}</span>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </section>
  )
}
