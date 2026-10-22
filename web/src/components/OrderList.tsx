import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { ApiError, listOrders, listProducts, returnOrder } from '@/api'
import { StatusBadge } from '@/components/StatusBadge'
import { Alert } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { formatMoney } from '@/orderTotal'
import { cn } from '@/lib/utils'

export function OrderList({ selected, onSelect }: { selected: string | null; onSelect: (id: string) => void }) {
  const { t, i18n } = useTranslation()
  const queryClient = useQueryClient()
  const locale = i18n.resolvedLanguage ?? 'en'

  const { data, isPending } = useQuery({
    queryKey: ['orders'],
    queryFn: listOrders,
    // Keep polling while any order is still moving through the saga.
    refetchInterval: (q) => (q.state.data?.some((o) => o.status === 'Submitted') ? 1500 : false),
  })
  const { data: products } = useQuery({ queryKey: ['products', locale], queryFn: () => listProducts(locale) })
  const nameOf = (sku: string) => products?.find((p) => p.sku === sku)?.name ?? sku

  const returning = useMutation({
    mutationFn: returnOrder,
    onSuccess: (_, id) => {
      void queryClient.invalidateQueries({ queryKey: ['orders'] })
      void queryClient.invalidateQueries({ queryKey: ['timeline', id] })
    },
  })

  return (
    <Card>
      <CardHeader>
        <CardTitle>{t('orders.title')}</CardTitle>
      </CardHeader>
      <CardContent>
        {isPending ? (
          <p>{t('orders.loading')}</p>
        ) : !data?.length ? (
          <p className="text-muted-foreground">{t('orders.empty')}</p>
        ) : (
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>{t('orders.created')}</TableHead>
                <TableHead>{t('orders.items')}</TableHead>
                <TableHead>{t('orders.total')}</TableHead>
                <TableHead>{t('orders.status')}</TableHead>
                <TableHead />
              </TableRow>
            </TableHeader>
            <TableBody>
              {data.map((o) => (
                <TableRow
                  key={o.id}
                  data-state={o.id === selected ? 'selected' : undefined}
                  className={cn('cursor-pointer')}
                  onClick={() => onSelect(o.id)}
                >
                  <TableCell>{new Date(o.createdAt).toLocaleString(locale)}</TableCell>
                  <TableCell className="max-w-64 truncate">{o.lines.map((l) => `${nameOf(l.sku)} × ${l.quantity}`).join(', ')}</TableCell>
                  <TableCell>{formatMoney(o.total, o.currency, locale)}</TableCell>
                  <TableCell>
                    <span className="flex flex-wrap items-center gap-1">
                      <StatusBadge status={o.status} />
                      {o.activatedAt && <Badge variant="success" data-testid="activated">{t('orders.activated')}</Badge>}
                    </span>
                  </TableCell>
                  <TableCell>
                    {o.status === 'Shipped' && (
                      <Button
                        size="sm"
                        variant="outline"
                        disabled={returning.isPending}
                        onClick={(e) => {
                          e.stopPropagation()
                          returning.mutate(o.id)
                        }}
                      >
                        {t('orders.return')}
                      </Button>
                    )}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
        {returning.error instanceof ApiError && (
          <Alert variant="destructive" className="mt-3">
            {t(returning.error.key)}
          </Alert>
        )}
      </CardContent>
    </Card>
  )
}
