import { useState } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { useTranslation } from 'react-i18next'
import { ApiError, listProducts, submitOrder } from '@/api'
import { Alert } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardFooter, CardHeader, CardTitle } from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { formatMoney, isValidQuantity, orderTotal } from '@/orderTotal'

export function Shop({ onOrdered }: { onOrdered: (orderId: string) => void }) {
  const { t, i18n } = useTranslation()
  const queryClient = useQueryClient()
  const locale = i18n.resolvedLanguage ?? 'en'
  const [quantities, setQuantities] = useState<Record<string, number>>({})

  const { data: products } = useQuery({ queryKey: ['products', locale], queryFn: () => listProducts(locale) })

  const chosen = (products ?? []).filter((p) => isValidQuantity(quantities[p.sku] ?? 0))
  const total = orderTotal(chosen.map((p) => ({ quantity: quantities[p.sku], unitPrice: p.price })))
  const currency = chosen[0]?.currency ?? 'USD'

  const mutation = useMutation({
    mutationFn: () => submitOrder(chosen.map((p) => ({ sku: p.sku, quantity: quantities[p.sku] }))),
    onSuccess: (order) => {
      setQuantities({})
      void queryClient.invalidateQueries({ queryKey: ['orders'] })
      onOrdered(order.id)
    },
  })

  return (
    <div className="grid gap-4 lg:grid-cols-[2fr_1fr]">
      <section aria-labelledby="shop-title" className="grid content-start gap-4 sm:grid-cols-2">
        <h2 id="shop-title" className="sr-only">
          {t('shop.title')}
        </h2>
        {products?.length === 0 && <p className="text-muted-foreground">{t('shop.empty')}</p>}
        {products?.map((p) => (
          <Card key={p.sku} data-sku={p.sku}>
            <CardHeader>
              <CardTitle>{p.name}</CardTitle>
              <CardDescription>{p.description}</CardDescription>
            </CardHeader>
            <CardContent className="text-lg font-semibold">{formatMoney(p.price, p.currency, locale)}</CardContent>
            <CardFooter className="gap-2">
              <Label htmlFor={`qty-${p.sku}`} className="sr-only">
                {t('shop.quantity', { name: p.name })}
              </Label>
              <Input
                id={`qty-${p.sku}`}
                type="number"
                min={0}
                className="w-24"
                value={quantities[p.sku] ?? ''}
                placeholder="0"
                onChange={(e) => setQuantities((q) => ({ ...q, [p.sku]: Number(e.target.value) }))}
              />
            </CardFooter>
          </Card>
        ))}
      </section>

      <Card className="h-fit">
        <CardHeader>
          <CardTitle>{t('shop.cart')}</CardTitle>
        </CardHeader>
        <CardContent className="grid gap-3">
          {chosen.length === 0 ? (
            <p className="text-sm text-muted-foreground">{t('shop.cartEmpty')}</p>
          ) : (
            <ul className="grid gap-1 text-sm">
              {chosen.map((p) => (
                <li key={p.sku} className="flex justify-between gap-2">
                  <span>
                    {p.name} × {quantities[p.sku]}
                  </span>
                  <span>{formatMoney(p.price * quantities[p.sku], p.currency, locale)}</span>
                </li>
              ))}
            </ul>
          )}
          <p className="font-semibold">{t('shop.total', { total: formatMoney(total, currency, locale) })}</p>
          <Button disabled={mutation.isPending || chosen.length === 0} onClick={() => mutation.mutate()}>
            {mutation.isPending ? t('shop.submitting') : t('shop.submit')}
          </Button>
          {mutation.error instanceof ApiError && <Alert variant="destructive">{t(mutation.error.key)}</Alert>}
          <p className="text-xs text-muted-foreground">{t('shop.hint')}</p>
        </CardContent>
      </Card>
    </div>
  )
}
