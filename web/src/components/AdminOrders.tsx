import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { adminCancelReturn, adminListOrders, adminReceiveReturn, adminRefund, adminShip, ApiError } from '@/api'
import { StatusBadge } from '@/components/StatusBadge'
import { Alert } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { formatMoney } from '@/orderTotal'

const REFUNDABLE = new Set(['Confirmed', 'Shipped', 'Returned'])

export function AdminOrders() {
  const { t, i18n } = useTranslation()
  const queryClient = useQueryClient()
  const locale = i18n.resolvedLanguage ?? 'en'

  const { data } = useQuery({ queryKey: ['admin-orders'], queryFn: adminListOrders, refetchInterval: 3000 })
  const refresh = () => void queryClient.invalidateQueries({ queryKey: ['admin-orders'] })
  const ship = useMutation({ mutationFn: adminShip, onSuccess: refresh })
  const refund = useMutation({ mutationFn: adminRefund, onSuccess: refresh })
  const receive = useMutation({ mutationFn: adminReceiveReturn, onSuccess: refresh })
  const undoReturn = useMutation({ mutationFn: adminCancelReturn, onSuccess: refresh })
  const failure = ship.error ?? refund.error ?? receive.error ?? undoReturn.error
  const error = failure instanceof ApiError ? failure : null

  return (
    <Card>
      <CardHeader>
        <CardTitle>{t('admin.title')}</CardTitle>
      </CardHeader>
      <CardContent>
        {!data?.length ? (
          <p className="text-muted-foreground">{t('admin.empty')}</p>
        ) : (
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>{t('orders.created')}</TableHead>
                <TableHead>{t('admin.customer')}</TableHead>
                <TableHead>{t('orders.total')}</TableHead>
                <TableHead>{t('orders.status')}</TableHead>
                <TableHead />
              </TableRow>
            </TableHeader>
            <TableBody>
              {data.map((o) => (
                <TableRow key={o.id} data-order={o.id}>
                  <TableCell>{new Date(o.createdAt).toLocaleString(locale)}</TableCell>
                  <TableCell className="font-mono text-xs">{(o as unknown as { customerId: string }).customerId.slice(0, 8)}</TableCell>
                  <TableCell>{formatMoney(o.total, o.currency, locale)}</TableCell>
                  <TableCell>
                    <StatusBadge status={o.status} />
                  </TableCell>
                  <TableCell className="flex gap-2">
                    {o.status === 'Confirmed' && (
                      <Button size="sm" onClick={() => ship.mutate(o.id)} disabled={ship.isPending}>
                        {t('admin.ship')}
                      </Button>
                    )}
                    {o.status === 'Returned' && !o.returnReceivedAt && (
                      <>
                        <Button size="sm" onClick={() => receive.mutate(o.id)} disabled={receive.isPending}>
                          {t('admin.receiveReturn')}
                        </Button>
                        <Button size="sm" variant="outline" onClick={() => undoReturn.mutate(o.id)} disabled={undoReturn.isPending}>
                          {t('admin.cancelReturn')}
                        </Button>
                      </>
                    )}
                    {REFUNDABLE.has(o.status) && (
                      <Button size="sm" variant="outline" onClick={() => refund.mutate(o.id)} disabled={refund.isPending}>
                        {t('admin.refund')}
                      </Button>
                    )}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
        {error && (
          <Alert variant="destructive" className="mt-3">
            {t(error.key)}
          </Alert>
        )}
      </CardContent>
    </Card>
  )
}
