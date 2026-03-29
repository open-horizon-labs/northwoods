/**
 * Hash-based deep linking utilities.
 *
 * URL scheme (fragment only -- no server changes needed):
 *
 *   #review                         Reviewer queue (default)
 *   #review/queue                   Reviewer queue
 *   #review/queue/{id}              Queue with selected review
 *   #review/docs                    All documents tab
 *   #review/docs/{id}               All documents with selected doc
 *   #review/search                  Search tab
 *   #review/search?q={query}        Search tab with query
 *   #review/case/{personKey}        Case timeline
 *   #doc/{uuid}                     Direct document link (legacy alias)
 *   #rag-report                     RAG report page
 *   #submissions                    All submissions (legacy alias -> #review/docs)
 *   #dev                            Dev tools
 */

/** Sidebar tab in the reviewer dashboard. */
export type SidebarMode = 'queue' | 'documents' | 'search'

// ---------------------------------------------------------------------------
// Parsed route state
// ---------------------------------------------------------------------------

export type ReviewRoute = {
  mode: SidebarMode
  selectedId: string | null
  searchQuery: string
  casePersonKey: string | null
}

const DEFAULT_ROUTE: ReviewRoute = {
  mode: 'queue',
  selectedId: null,
  searchQuery: '',
  casePersonKey: null,
}

// ---------------------------------------------------------------------------
// Parsing
// ---------------------------------------------------------------------------

/**
 * Parse the current window.location.hash into a ReviewRoute.
 *
 * Handles both the new structured scheme and the legacy `#doc/{uuid}` and
 * `#submissions` formats for backwards compatibility.
 */
export function parseReviewHash(hash: string): ReviewRoute {
  // Normalize: strip leading "#" and any trailing whitespace
  const raw = hash.replace(/^#/, '').trim()

  // Legacy: #doc/{uuid} -- open documents tab with that doc selected
  const docMatch = raw.match(/^doc\/([0-9a-f-]{36})$/i)
  if (docMatch) {
    return { mode: 'documents', selectedId: docMatch[1], searchQuery: '', casePersonKey: null }
  }

  // Legacy: #submissions -- open documents tab
  if (raw === 'submissions') {
    return { mode: 'documents', selectedId: null, searchQuery: '', casePersonKey: null }
  }

  // New structured routes: #review/...
  if (raw === 'review' || raw.startsWith('review/') || raw.startsWith('review?')) {
    const afterReview = raw.replace(/^review\/?/, '')

    // #review/case/{personKey}
    const caseMatch = afterReview.match(/^case\/(.+)$/)
    if (caseMatch) {
      return { mode: 'queue', selectedId: null, searchQuery: '', casePersonKey: decodeURIComponent(caseMatch[1]) }
    }

    // #review/search or #review/search?q=...
    if (afterReview === 'search' || afterReview.startsWith('search?') || afterReview.startsWith('search/')) {
      const qMatch = afterReview.match(/[?&]q=([^&]*)/)
      return { mode: 'search', selectedId: null, searchQuery: qMatch ? decodeURIComponent(qMatch[1]) : '', casePersonKey: null }
    }

    // #review/docs or #review/docs/{id}
    if (afterReview === 'docs' || afterReview.startsWith('docs/')) {
      const id = afterReview.replace(/^docs\/?/, '') || null
      return { mode: 'documents', selectedId: id, searchQuery: '', casePersonKey: null }
    }

    // #review/queue or #review/queue/{id}
    if (afterReview === 'queue' || afterReview.startsWith('queue/') || afterReview === '') {
      const id = afterReview.replace(/^queue\/?/, '') || null
      return { mode: 'queue', selectedId: id, searchQuery: '', casePersonKey: null }
    }

    // #review/{id} -- bare ID after review/ defaults to queue
    if (/^[0-9a-f-]{36}$/i.test(afterReview)) {
      return { mode: 'queue', selectedId: afterReview, searchQuery: '', casePersonKey: null }
    }
  }

  return DEFAULT_ROUTE
}

// ---------------------------------------------------------------------------
// Building
// ---------------------------------------------------------------------------

/** Build a hash string (without the leading "#") from route state. */
export function buildReviewHash(route: ReviewRoute): string {
  if (route.casePersonKey) {
    return `review/case/${encodeURIComponent(route.casePersonKey)}`
  }

  const modeSegment = route.mode === 'queue' ? 'queue' : route.mode === 'documents' ? 'docs' : 'search'

  if (route.mode === 'search') {
    if (route.searchQuery) {
      return `review/search?q=${encodeURIComponent(route.searchQuery)}`
    }
    return 'review/search'
  }

  if (route.selectedId) {
    return `review/${modeSegment}/${route.selectedId}`
  }

  return `review/${modeSegment}`
}

// ---------------------------------------------------------------------------
// URL manipulation (thin wrappers around History API)
// ---------------------------------------------------------------------------

/** Replace the current hash without creating a history entry. */
export function replaceHash(hash: string): void {
  const newUrl = hash ? `#${hash}` : window.location.pathname + window.location.search
  window.history.replaceState(null, '', newUrl)
  // Dispatch a hashchange event so useHashRoute picks it up
  window.dispatchEvent(new HashChangeEvent('hashchange'))
}

/** Push a new hash, creating a history entry (Back button works). */
export function pushHash(hash: string): void {
  const newUrl = hash ? `#${hash}` : window.location.pathname + window.location.search
  window.history.pushState(null, '', newUrl)
  window.dispatchEvent(new HashChangeEvent('hashchange'))
}
