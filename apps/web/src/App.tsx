import { lazy, Suspense, useEffect, useState } from 'react'

import { clearStoredAuth, readStoredAuth, storeAuth } from './lib/auth'
import type { LoginResponse } from './types'

const LoginPage = lazy(() => import('./pages/LoginPage'))
const WorkerDashboard = lazy(() => import('./pages/WorkerDashboard'))
const ReviewerDashboard = lazy(() => import('./pages/ReviewerDashboard'))
const DevPage = lazy(() => import('./pages/DevPage'))
const AdminTemplates = lazy(() => import('./pages/AdminTemplates'))
const RagReportPage = lazy(() => import('./pages/RagReportPage'))

/** Hash-based routing: #dev opens the developer scaffold, #rag-report opens RAG report. */
function useHashRoute(): string {
  const [hash, setHash] = useState(() => window.location.hash)
  useEffect(() => {
    const handler = () => setHash(window.location.hash)
    window.addEventListener('hashchange', handler)
    return () => window.removeEventListener('hashchange', handler)
  }, [])
  return hash
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
  const hash = useHashRoute()
  const [auth, setAuth] = useState<LoginResponse | null>(readStoredAuth)

  const handleLogin = (nextAuth: LoginResponse) => {
    storeAuth(nextAuth)
    setAuth(nextAuth)
  }

  const handleLogout = () => {
    clearStoredAuth()
    setAuth(null)
  }

  if (hash === '#dev') {
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
  if (role === 2 || role === 'Admin') {
    return (
      <Suspense fallback={<Spinner />}>
        <AdminTemplates auth={auth} onLogout={handleLogout} />
      </Suspense>
    )
  }

  if (role === 0 || role === 'IntakeWorker') {
    return (
      <Suspense fallback={<Spinner />}>
        <WorkerDashboard auth={auth} onLogout={handleLogout} />
      </Suspense>
    )
  }

  if (hash === '#rag-report') {
    return (
      <Suspense fallback={<Spinner />}>
        <RagReportPage auth={auth} onLogout={handleLogout} />
      </Suspense>
    )
  }

  return (
    <Suspense fallback={<Spinner />}>
      <ReviewerDashboard auth={auth} onLogout={handleLogout} />
    </Suspense>
  )
}
