import { type FormEvent, useState } from 'react'

import { api } from '../api'
import type { LoginResponse } from '../types'

const FOCUS_RING =
  'focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-sky-700 focus-visible:ring-offset-2 focus-visible:ring-offset-white'

const inputStyle =
  'w-full rounded border border-slate-300 bg-white px-3 py-2.5 text-sm text-slate-900 outline-none placeholder:text-slate-400 transition focus:border-sky-600'

const inputInteractive = `${inputStyle} ${FOCUS_RING}`

type Props = {
  onLogin: (auth: LoginResponse) => void
}

const AUTH_STORAGE_KEY = 'northwoods:auth'

export default function LoginPage({ onLogin }: Props) {
  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  const handleSubmit = async (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    setBusy(true)
    setError(null)

    try {
      const auth = await api.login({ email: email.trim(), password })
      try {
        localStorage.setItem(AUTH_STORAGE_KEY, JSON.stringify(auth))
      } catch {
        // best-effort
      }
      onLogin(auth)
    } catch (err) {
      setError(
        err instanceof Error && err.message
          ? err.message.includes('401') || err.message.toLowerCase().includes('unauthorized')
            ? 'Incorrect email or password. Please try again.'
            : err.message
          : 'Sign in failed. Please try again.',
      )
    } finally {
      setBusy(false)
    }
  }

  return (
    <div className="flex min-h-screen flex-col items-center justify-center bg-slate-50 px-4 py-12">
      <a
        href="#main"
        className="sr-only focus:not-sr-only focus:fixed focus:left-4 focus:top-4 focus:z-50 focus:bg-slate-900 focus:px-3 focus:py-2 focus:text-sm focus:font-medium focus:text-white"
      >
        Skip to main content
      </a>

      <main id="main" className="w-full max-w-sm">
        <header className="mb-8 text-center">
          <p className="text-xs font-semibold uppercase tracking-widest text-slate-500">Northwoods</p>
          <h1 className="mt-2 text-2xl font-semibold text-slate-900">Sign in</h1>
          <p className="mt-1 text-sm text-slate-600">Use your work email and password.</p>
        </header>

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
              {busy ? 'Signing in\u2026' : 'Sign in'}
            </button>
          </form>
        </div>
      </main>
    </div>
  )
}
