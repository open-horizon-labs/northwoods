import { Component, type ErrorInfo, type ReactNode } from 'react'

interface ErrorBoundaryProps {
  children: ReactNode
}

interface ErrorBoundaryState {
  hasError: boolean
  message?: string
}

export default class ErrorBoundary extends Component<ErrorBoundaryProps, ErrorBoundaryState> {
  state: ErrorBoundaryState = {
    hasError: false,
  }

  static getDerivedStateFromError(error: Error): ErrorBoundaryState {
    // Detect stale chunk errors after a deploy — auto-reload once
    if (ErrorBoundary.isChunkLoadError(error) && !sessionStorage.getItem('chunk_reload')) {
      sessionStorage.setItem('chunk_reload', '1')
      window.location.reload()
      return { hasError: true, message: 'Updating…' }
    }

    return {
      hasError: true,
      message: (error.message && !error.message.includes('<') && error.message.length < 200)
        ? error.message
        : 'An unexpected error occurred.',
    }
  }

  private static isChunkLoadError(error: Error): boolean {
    const msg = error.message?.toLowerCase() ?? ''
    return msg.includes('failed to fetch dynamically imported module')
      || msg.includes('importing a module script failed')
      || msg.includes('loading chunk')
  }

  override componentDidCatch(error: Error, info: ErrorInfo) {
    console.error('Northwoods UI error boundary caught:', error, info)
  }

  reset = () => {
    sessionStorage.removeItem('chunk_reload')
    this.setState({ hasError: false, message: undefined })
  }

  override render() {
    if (!this.state.hasError) {
      return this.props.children
    }

    return (
      <main className="min-h-screen bg-slate-50 text-slate-900">
        <div className="mx-auto flex min-h-screen w-full max-w-3xl items-center justify-center px-4 py-8">
          <div className="w-full rounded-2xl border border-red-200 bg-white p-6">
            <h1 className="text-lg font-semibold text-slate-900">Something went wrong</h1>
            <p className="mt-2 text-sm text-slate-600">
              The review console hit an unexpected error. Use the button below to reset the interface.
            </p>
            <p className="mt-3 rounded-lg bg-red-50 px-3 py-2 text-xs text-red-700" role="status">
              {this.state.message}
            </p>
            <button
              type="button"
              onClick={() => { sessionStorage.removeItem('chunk_reload'); window.location.reload() }}
              className="mt-4 inline-flex min-h-11 items-center justify-center rounded-lg border border-slate-300 bg-white px-4 py-2.5 text-sm font-semibold text-slate-900 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-sky-700 focus-visible:ring-offset-2 focus-visible:ring-offset-slate-50"
            >
              Reload app
            </button>
          </div>
        </div>
      </main>
    )
  }
}
