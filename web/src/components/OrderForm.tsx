import { useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { ApiError, submitOrder } from '../api'
import { isValidLine, orderTotal } from '../orderTotal'
import type { OrderLine } from '../types'

const emptyLine = (): OrderLine => ({ sku: 'A', quantity: 1, unitPrice: 10 })

export function OrderForm({ onCreated }: { onCreated: (id: string) => void }) {
  const { t } = useTranslation()
  const queryClient = useQueryClient()
  const [lines, setLines] = useState<OrderLine[]>([emptyLine()])

  const mutation = useMutation({
    mutationFn: submitOrder,
    onSuccess: (order) => {
      void queryClient.invalidateQueries({ queryKey: ['orders'] })
      onCreated(order.id)
    },
  })

  const update = (i: number, patch: Partial<OrderLine>) =>
    setLines((ls) => ls.map((l, idx) => (idx === i ? { ...l, ...patch } : l)))

  return (
    <section className="card">
      <h2>{t('form.title')}</h2>
      <form
        onSubmit={(e) => {
          e.preventDefault()
          mutation.mutate(lines)
        }}
      >
        {lines.map((line, i) => (
          <div className="line" key={i}>
            <label>
              {t('form.sku')}
              <input value={line.sku} onChange={(e) => update(i, { sku: e.target.value })} />
            </label>
            <label>
              {t('form.quantity')}
              <input
                type="number"
                min={1}
                value={line.quantity}
                onChange={(e) => update(i, { quantity: Number(e.target.value) })}
              />
            </label>
            <label>
              {t('form.unitPrice')}
              <input
                type="number"
                min={0}
                step="0.01"
                value={line.unitPrice}
                onChange={(e) => update(i, { unitPrice: Number(e.target.value) })}
              />
            </label>
            {lines.length > 1 && (
              <button type="button" onClick={() => setLines((ls) => ls.filter((_, idx) => idx !== i))}>
                {t('form.removeLine')}
              </button>
            )}
          </div>
        ))}
        <button type="button" onClick={() => setLines((ls) => [...ls, emptyLine()])}>
          {t('form.addLine')}
        </button>
        <p className="total">{t('form.total', { total: orderTotal(lines).toFixed(2) })}</p>
        <button className="primary" disabled={mutation.isPending || !lines.every(isValidLine)}>
          {mutation.isPending ? t('form.submitting') : t('form.submit')}
        </button>
        {mutation.error instanceof ApiError && (
          <p role="alert" className="error">
            {t(mutation.error.key)}
          </p>
        )}
        <p className="hint">{t('form.hint')}</p>
      </form>
    </section>
  )
}
