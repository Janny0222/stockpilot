import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import { App } from '@/app/app'
import { Providers } from '@/app/providers'
import { bootstrapSession } from '@/features/auth/session'

// Entry point: side effects only, no components (so hot reload never runs createRoot twice).
void bootstrapSession()

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <Providers>
      <App />
    </Providers>
  </StrictMode>,
)
