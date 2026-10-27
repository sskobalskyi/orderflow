import { useEffect, useState } from 'react'
import type { User } from 'oidc-client-ts'
import { useTranslation } from 'react-i18next'
import { userManager } from '@/auth'
import { AdminOrders } from '@/components/AdminOrders'
import { OrderList } from '@/components/OrderList'
import { Shop } from '@/components/Shop'
import { Timeline } from '@/components/Timeline'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { LOCALES, type Locale } from '@/i18n'
import { branding } from '@/config'
import { isAdmin } from '@/roles'

export default function App() {
  const { t, i18n } = useTranslation()
  const [user, setUser] = useState<User | null>(null)
  const [loaded, setLoaded] = useState(false)
  const [tab, setTab] = useState('shop')
  const [selected, setSelected] = useState<string | null>(null)

  useEffect(() => {
    void userManager.getUser().then((u) => {
      setUser(u && !u.expired ? u : null)
      setLoaded(true)
    })
  }, [])

  const locale = (i18n.resolvedLanguage ?? 'en') as Locale

  return (
    <div className="mx-auto grid max-w-6xl gap-6 p-4">
      <header className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex flex-wrap items-baseline gap-x-4 gap-y-1">
          <h1 className="text-2xl font-bold">{branding.shopName}</h1>
          {branding.homeUrl && (
            <a href={branding.homeUrl} className="text-sm text-primary underline-offset-4 hover:underline" data-testid="home-link">
              {t('app.backHome')}
            </a>
          )}
        </div>
        <div className="flex flex-wrap items-center gap-3 text-sm">
          <Select value={locale} onValueChange={(l) => void i18n.changeLanguage(l)}>
            <SelectTrigger aria-label={t('app.language')} className="w-20">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {LOCALES.map((l) => (
                <SelectItem key={l} value={l}>
                  {l.toUpperCase()}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
          {user && (
            <>
              <span>{t('auth.signedInAs', { name: user.profile.preferred_username ?? user.profile.sub })}</span>
              <Button variant="outline" size="sm" onClick={() => void userManager.signoutRedirect()}>
                {t('auth.signOut')}
              </Button>
            </>
          )}
        </div>
      </header>

      {!loaded ? null : !user ? (
        <Card>
          <CardContent className="grid justify-items-center gap-4 py-6">
            <p>{t('auth.welcome')}</p>
            <Button onClick={() => void userManager.signinRedirect()}>{t('auth.signIn')}</Button>
          </CardContent>
        </Card>
      ) : (
        <Tabs value={tab} onValueChange={setTab}>
          <TabsList>
            <TabsTrigger value="shop">{t('tabs.shop')}</TabsTrigger>
            <TabsTrigger value="orders">{t('tabs.orders')}</TabsTrigger>
            {isAdmin(user) && <TabsTrigger value="admin">{t('tabs.admin')}</TabsTrigger>}
          </TabsList>
          <TabsContent value="shop">
            <Shop
              onOrdered={(id) => {
                setSelected(id)
                setTab('orders')
              }}
            />
          </TabsContent>
          <TabsContent value="orders" className="grid gap-4">
            <OrderList selected={selected} onSelect={setSelected} />
            <Timeline orderId={selected} />
          </TabsContent>
          {isAdmin(user) && (
            <TabsContent value="admin">
              <AdminOrders />
            </TabsContent>
          )}
        </Tabs>
      )}
    </div>
  )
}
