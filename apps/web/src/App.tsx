import { lazy, Suspense, useEffect, useState } from 'react'

import { clearStoredAuth, readStoredAuth, storeAuth } from './lib/auth'
import type { LoginResponse } from './types'

const LoginPage = lazy(() => import('./pages/LoginPage'))
const WorkerDashboard = lazy(() => import('./pages/WorkerDashboard'))
const ReviewerDashboard = lazy(() => import('./pages/ReviewerDashboard'))
const DevPage = lazy(() => import('./pages/DevPage'))

/** Hash-based routing: #dev opens the developer scaffold. */
function useIsDevRoute(): boolean {
  const [isDev, setIsDev] = useState(() => window.location.hash === '#dev')
  useEffect(() => {
    const handler = () => setIsDev(window.location.hash === '#dev')
    window.addEventListener('hashchange', handler)
    return () => window.removeEventListener('hashchange', handler)
  }, [])
  return isDev
}

function Spinner() {
  return (
    <div className="flex min-h-screen items-center justify-center bg-slate-50">
      <p className="text-sm text-slate-500" role="status" aria-live="polite">
        Loading\u2026
      </p>
    </div>
  )
}

export default function App() {
  const isDevRoute = useIsDevRoute()
  const [auth, setAuth] = useState<LoginResponse | null>(readStoredAuth)

  const handleLogin = (nextAuth: LoginResponse) => {
    storeAuth(nextAuth)
    setAuth(nextAuth)
  }

  const handleLogout = () => {
    clearStoredAuth()
    setAuth(null)
  }

  if (isDevRoute) {
    return (
      <Suspense fallback={<Spinner />}>
        <DevPage />
      </Suspense>
    )
  }

  if (!auth) {
    return (
      <Suspense fallback={<Spinner />}>
        <LoginPage onLogin={handleLogin} />
      </Suspense>
    )
  }

  const role = auth.role
  if (role === 0 || role === 'IntakeWorker') {
    return (
      <Suspense fallback={<Spinner />}>
        <WorkerDashboard auth={auth} onLogout={handleLogout} />
      </Suspense>
    )
  }

  return (
    <Suspense fallback={<Spinner />}>
      <ReviewerDashboard auth={auth} onLogout={handleLogout} />
    </Suspense>
  )
}
