import { type FormEvent, useState } from 'react'

import { api, ApiError, userMessage } from '../api'
import { storeAuth } from '../lib/auth'
import type { LoginResponse } from '../types'

const FOCUS_RING =
  'focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-sky-700 focus-visible:ring-offset-2 focus-visible:ring-offset-white'

const inputStyle =
  'w-full rounded border border-slate-300 bg-white px-3 py-2.5 text-sm text-slate-900 outline-none placeholder:text-slate-400 transition focus:border-sky-600'

const inputInteractive = `${inputStyle} ${FOCUS_RING}`

const DEMO_CREDENTIALS = [
  { label: 'Sunrise Reviewer', email: 'reviewer@sunrise.example', tenant: 'tenant-a' },
  { label: 'Sunrise Worker', email: 'worker@sunrise.example', tenant: 'tenant-a' },
  { label: 'Lakewood Reviewer', email: 'reviewer@lakewood.example', tenant: 'tenant-b' },
  { label: 'Lakewood Worker', email: 'worker@lakewood.example', tenant: 'tenant-b' },
] as const

type Props = {
  onLogin: (auth: LoginResponse) => void
  sessionExpiredMessage?: string | null
}

export default function LoginPage({ onLogin, sessionExpiredMessage }: Props) {
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [showReviewerHint, setShowReviewerHint] = useState(false)

  const handleSubmit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    setBusy(true)
    setError(null)

    try {
      const auth = await api.login({ email: email.trim(), password })
      storeAuth(auth)
      onLogin(auth)
    } catch (err) {
      if (err instanceof ApiError && err.status === 401) {
        setError('Incorrect email or password. Please try again.')
      } else if (err instanceof ApiError && err.status >= 500) {
        setError('The sign-in service is temporarily unavailable. Please try again later.')
      } else {
        setError(userMessage(err, 'Sign in failed. Please try again.'))
      }
    } finally {
      setBusy(false)
    }
  }

  const useDemoCredentials = (demoEmail: string) => {
    setEmail(demoEmail)
    setPassword('password')
    setShowReviewerHint(false)
  }

  return (
    <div className="flex min-h-screen flex-col items-center justify-center bg-slate-50 px-4 py-12">
      <a
        href="#main"
        className="sr-only focus:not-sr-only focus:fixed focus:left-4 focus:top-4 focus:z-50 focus:bg-slate-900 focus:px-3 focus:py-2 focus:text-sm focus:font-medium focus:text-white"
      >
        Skip to main content
      </a>

      <a
        href="#"
        onClick={(e) => {
          e.preventDefault()
          setShowReviewerHint(true)
        }}
        className="fixed right-3 top-3 z-40 rounded-full border border-slate-300 bg-white px-3 py-1.5 text-sm text-slate-700 shadow-sm transition hover:bg-slate-50 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-sky-700 focus-visible:ring-offset-2 focus-visible:ring-offset-white"
        aria-label="Are you a reviewer"
      >
        Are you a reviewer, click here…
      </a>

      <main id="main" className="w-full max-w-sm">
        <header className="mb-8 text-center">
          <p className="text-xs font-semibold uppercase tracking-widest text-slate-500">TraverseLite</p>
          <h1 className="mt-2 text-2xl font-semibold text-slate-900">Sign in</h1>
          <p className="mt-1 text-sm text-slate-600">Use your work email and password.</p>
        </header>

        {sessionExpiredMessage ? (
          <p role="alert" aria-live="assertive" className="mb-4 rounded border border-amber-200 bg-amber-50 px-3 py-2 text-sm text-amber-800">
            {sessionExpiredMessage}
          </p>
        ) : null}

        <div className="rounded-lg border border-slate-200 bg-white p-6 shadow-sm">
          <form onSubmit={handleSubmit} noValidate className="space-y-5">
            <div className="space-y-1.5">
              <label htmlFor="login-email" className="block text-sm font-medium text-slate-700">
                Email
                <span className="sr-only"> (required)</span>
              </label>
              <input
                id="login-email"
                type="email"
                autoComplete="email"
                required
                aria-required="true"
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                className={inputInteractive}
                placeholder="you@organization.example"
                disabled={busy}
              />
            </div>

            <div className="space-y-1.5">
              <label htmlFor="login-password" className="block text-sm font-medium text-slate-700">
                Password
                <span className="sr-only"> (required)</span>
              </label>
              <input
                id="login-password"
                type="password"
                autoComplete="current-password"
                required
                aria-required="true"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                className={inputInteractive}
                disabled={busy}
              />
            </div>

            {error ? (
              <p role="alert" aria-live="assertive" className="rounded border border-rose-200 bg-rose-50 px-3 py-2 text-sm text-rose-700">
                {error}
              </p>
            ) : null}

            <button
              type="submit"
              disabled={busy || !email.trim() || !password}
              className={`w-full rounded border border-sky-700 bg-sky-700 px-4 py-2.5 text-sm font-semibold text-white transition hover:bg-sky-800 disabled:cursor-not-allowed disabled:opacity-60 ${FOCUS_RING}`}
            >
              {busy ? 'Signing in…' : 'Sign in'}
            </button>
          </form>
        </div>
      </main>

      {showReviewerHint ? (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 p-4" role="presentation">
          <div className="w-full max-w-md rounded-lg border border-slate-200 bg-white p-4 shadow-lg">
            <header className="mb-3 flex items-start justify-between gap-4">
              <div>
                <h2 className="text-base font-semibold text-slate-900">Are you reviewing Muness's Submission?</h2>
                <p className="mt-1 text-sm text-slate-600">Pick a user below to pre-fill credentials (password is always <span className="font-semibold">password</span>).</p>
              </div>
              <button
                type="button"
                onClick={() => setShowReviewerHint(false)}
                className={`shrink-0 rounded px-2 py-1 text-sm text-slate-700 hover:bg-slate-50 ${FOCUS_RING}`}
                aria-label="Close reviewer hint"
              >
                Close
              </button>
            </header>

            <div className="space-y-2">
              {DEMO_CREDENTIALS.map((cred) => (
                <button
                  key={cred.email}
                  type="button"
                  onClick={() => useDemoCredentials(cred.email)}
                  className={`flex w-full items-center justify-between rounded border border-slate-200 px-3 py-2 text-left text-sm hover:bg-slate-50 ${FOCUS_RING}`}
                >
                  <span className="font-medium text-slate-900">{cred.label}</span>
                  <span className="text-slate-500">{cred.email} / {cred.tenant}</span>
                </button>
              ))}
            </div>

            <div className="mt-4 flex items-center justify-between">
              <a
                href="#rag-report"
                className={`text-sm font-medium text-sky-700 hover:underline ${FOCUS_RING}`}
              >
                Open RAG Report
              </a>
              <button type="button" onClick={() => setShowReviewerHint(false)} className="rounded border border-slate-300 px-3 py-1.5 text-sm">
                Not now
              </button>
            </div>
          </div>
        </div>
      ) : null}
    </div>
  )
}
