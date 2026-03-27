import type { LoginResponse } from '../types'

export const AUTH_STORAGE_KEY = 'northwoods:auth'

export function readStoredAuth(): LoginResponse | null {
  try {
    const raw = localStorage.getItem(AUTH_STORAGE_KEY)
    if (!raw) return null
    const parsed = JSON.parse(raw) as unknown
    if (
      parsed !== null &&
      typeof parsed === 'object' &&
      'accessToken' in parsed &&
      typeof (parsed as Record<string, unknown>).accessToken === 'string' &&
      (parsed as Record<string, unknown>).accessToken &&
      'tenantId' in parsed &&
      typeof (parsed as Record<string, unknown>).tenantId === 'string' &&
      (parsed as Record<string, unknown>).tenantId &&
      'role' in parsed &&
      [0, 1, 'IntakeWorker', 'Reviewer'].includes(
        (parsed as Record<string, unknown>).role as string | number,
      )
    ) {
      return parsed as LoginResponse
    }
    return null
  } catch {
    return null
  }
}

export function clearStoredAuth(): void {
  try {
    localStorage.removeItem(AUTH_STORAGE_KEY)
  } catch {
    // best-effort
  }
}

export function storeAuth(auth: LoginResponse): void {
  try {
    localStorage.setItem(AUTH_STORAGE_KEY, JSON.stringify(auth))
  } catch {
    // best-effort
  }
}
