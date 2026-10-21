import { useEffect } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { getTimeline } from '@/api'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { parseReason } from '@/reason'
import { cn } from '@/lib/utils'

// The saga is over at these steps; shipping, returns and refunds can still follow, so keep a slow poll after.
const SAGA_DONE = new Set(['Confirmed', 'Rejected'])
const FAILED = new Set(['StockRejected', 'PaymentFailed', 'Rejected', 'Returned'])

export function Timeline({ orderId }: { orderId: string | null }) {
  const { t, i18n } = useTranslation()
  const queryClient = useQueryClient()
  const reasonText = (detail: string) => {
    const parsed = parseReason(detail)
    return parsed ? t(parsed.key, parsed.values) : detail
  }

  const { data } = useQuery({
    queryKey: ['timeline', orderId],
    queryFn: () => getTimeline(orderId!),
    enabled: orderId !== null,
    refetchInterval: (q) => (q.state.data?.some((s) => SAGA_DONE.has(s.step)) ? 4000 : 1000),
  })

  // The order list reads a different endpoint, so refresh it whenever the timeline gains a step.
  const steps = data?.length ?? 0
  useEffect(() => {
    if (steps > 0) void queryClient.invalidateQueries({ queryKey: ['orders'] })
  }, [steps, queryClient])

  return (
    <Card data-testid="timeline">
      <CardHeader>
        <CardTitle>{t('timeline.title')}</CardTitle>
      </CardHeader>
      <CardContent>
        {!orderId ? (
          <p className="text-muted-foreground">{t('orders.select')}</p>
        ) : (
          <ol className="ml-2 border-l-2 border-border">
            {data?.map((s) => (
              <li
                key={s.step}
                data-failed={FAILED.has(s.step)}
                className={cn(
                  'relative grid gap-0.5 py-1 pb-3 pl-4 before:absolute before:top-2.5 before:-left-[7px] before:size-3 before:rounded-full before:bg-success before:content-[""]',
                  FAILED.has(s.step) && 'before:bg-destructive',
                )}
              >
                <strong>{t(`timeline.steps.${s.step}`, { defaultValue: s.step })}</strong>
                <time className="text-xs text-muted-foreground">{new Date(s.at).toLocaleTimeString(i18n.language)}</time>
                {s.detail && <small className="text-xs text-muted-foreground">{reasonText(s.detail)}</small>}
              </li>
            ))}
          </ol>
        )}
      </CardContent>
    </Card>
  )
}
