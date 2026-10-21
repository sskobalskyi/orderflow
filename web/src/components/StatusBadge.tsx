import { useTranslation } from 'react-i18next'
import { Badge } from '@/components/ui/badge'
import type { OrderStatus } from '@/types'

const VARIANT = {
  Submitted: 'secondary',
  Confirmed: 'success',
  Shipped: 'success',
  Rejected: 'destructive',
  Returned: 'outline',
  Refunded: 'outline',
} as const

export function StatusBadge({ status }: { status: OrderStatus }) {
  const { t } = useTranslation()
  return (
    <Badge variant={VARIANT[status]} data-status={status}>
      {t(`status.${status}`)}
    </Badge>
  )
}
