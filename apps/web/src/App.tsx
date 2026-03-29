import { lazy, Suspense, useEffect, useState } from 'react'

import { clearStoredAuth, readStoredAuth, storeAuth } from './lib/auth'
import { parseReviewHash } from './lib/hashRoute'
import { setUnauthorizedHandler } from './api'
import type { LoginResponse } from './types'

const LoginPage = lazy(() => import('./pages/LoginPage'))
const WorkerDashboard = lazy(() => import('./pages/WorkerDashboard'))
const ReviewerDashboard = lazy(() => import('./pages/ReviewerDashboard'))
const DevPage = lazy(() => import('./pages/DevPage'))
const AdminTemplates = lazy(() => import('./pages/AdminTemplates'))
const RagReportPage = lazy(() => import('./pages/RagReportPage'))

/** Hash-based routing: listens for hash changes and re-renders. */
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
        Loading&hellip;
      </p>
    </div>
  )
}

export default function App() {
  const hash = useHashRoute()
  const [auth, setAuth] = useState<LoginResponse | null>(readStoredAuth)
  const [sessionExpiredMsg, setSessionExpiredMsg] = useState<string | null>(null)

  const handleLogin = (nextAuth: LoginResponse) => {
    storeAuth(nextAuth)
    setSessionExpiredMsg(null)
    setAuth(nextAuth)
  }

  const handleLogout = () => {
    clearStoredAuth()
    setAuth(null)
  }

  // Register the global 401 handler once on mount. The handler is stable:
  // it only calls React state setters which are guaranteed stable by React.
  useEffect(() => {
    setUnauthorizedHandler(() => {
      clearStoredAuth()
      setSessionExpiredMsg('Your session has expired. Please sign in again.')
      setAuth(null)
    })
  }, [])

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
        <LoginPage onLogin={handleLogin} sessionExpiredMessage={sessionExpiredMsg} />
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

  // RAG report is reviewer-only
  if (hash === '#rag-report' && (role === 1 || role === 'Reviewer')) {
    return (
      <Suspense fallback={<Spinner />}>
        <RagReportPage auth={auth} onLogout={handleLogout} />
      </Suspense>
    )
  }

  // All other reviewer routes: parse the hash into structured state.
  // Handles #review/*, #doc/{uuid} (legacy), #submissions (legacy),
  // and the empty hash (default queue view).
  if (role === 1 || role === 'Reviewer') {
    const route = parseReviewHash(hash)
    return (
      <Suspense fallback={<Spinner />}>
        <ReviewerDashboard
          auth={auth}
          onLogout={handleLogout}
          initialRoute={route}
        />
      </Suspense>
    )
  }

  // Fallback for unknown roles
  return (
    <Suspense fallback={<Spinner />}>
      <ReviewerDashboard auth={auth} onLogout={handleLogout} />
    </Suspense>
  )
}
