import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import './i18n'
import './index.css'
import App from './App'
import { handleRedirectCallback } from './auth'

const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })

// Finish the OIDC redirect (if any) before the first render so the app starts signed in.
handleRedirectCallback()
  .catch(() => undefined)
  .finally(() => {
    createRoot(document.getElementById('root')!).render(
      <StrictMode>
        <QueryClientProvider client={queryClient}>
          <App />
        </QueryClientProvider>
      </StrictMode>,
    )
  })
