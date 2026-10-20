import { useEffect, useState } from 'react'
import type { User } from 'oidc-client-ts'
import { useTranslation } from 'react-i18next'
import { userManager } from './auth'
import { LOCALES, type Locale } from './i18n'
import { OrderForm } from './components/OrderForm'
import { OrderList } from './components/OrderList'
import { Timeline } from './components/Timeline'
import './App.css'

export default function App() {
  const { t, i18n } = useTranslation()
  const [user, setUser] = useState<User | null>(null)
  const [loaded, setLoaded] = useState(false)
  const [selected, setSelected] = useState<string | null>(null)

  useEffect(() => {
    void userManager.getUser().then((u) => {
      setUser(u && !u.expired ? u : null)
      setLoaded(true)
    })
  }, [])

  const locale = (i18n.resolvedLanguage ?? 'en') as Locale

  return (
    <div className="app">
      <header className="topbar">
        <h1>{t('app.title')}</h1>
        <div className="topbar-right">
          <label>
            <span className="visually-hidden">{t('app.language')}</span>
            <select value={locale} onChange={(e) => void i18n.changeLanguage(e.target.value)}>
              {LOCALES.map((l) => (
                <option key={l} value={l}>
                  {l.toUpperCase()}
                </option>
              ))}
            </select>
          </label>
          {user && (
            <>
              <span>{t('auth.signedInAs', { name: user.profile.preferred_username ?? user.profile.sub })}</span>
              <button onClick={() => void userManager.signoutRedirect()}>{t('auth.signOut')}</button>
            </>
          )}
        </div>
      </header>

      {!loaded ? null : !user ? (
        <section className="card welcome">
          <p>{t('auth.welcome')}</p>
          <button className="primary" onClick={() => void userManager.signinRedirect()}>
            {t('auth.signIn')}
          </button>
        </section>
      ) : (
        <main className="grid">
          <OrderForm onCreated={setSelected} />
          <OrderList selected={selected} onSelect={setSelected} />
          <Timeline orderId={selected} />
        </main>
      )}
    </div>
  )
}
