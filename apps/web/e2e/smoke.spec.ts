import path from 'node:path'
import { fileURLToPath } from 'node:url'

import { expect, test } from '@playwright/test'

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

const __filename = fileURLToPath(import.meta.url)
const __dirname = path.dirname(__filename)

/** Fills in the login form and submits. Does NOT assert post-login state. */
async function login(page: import('@playwright/test').Page, email: string, password: string) {
  await page.goto('/')
  await page.getByLabel('Email').fill(email)
  await page.getByLabel('Password').fill(password)
  await page.getByRole('button', { name: 'Sign in' }).click()
}

// ---------------------------------------------------------------------------
// 0. Login page renders (no backend needed)
// ---------------------------------------------------------------------------

test.describe('Login page', () => {
  test('renders the sign-in form', async ({ page }) => {
    await page.goto('/')
    await expect(page.getByRole('heading', { name: 'Sign in' })).toBeVisible()
    await expect(page.getByLabel('Email')).toBeVisible()
    await expect(page.getByLabel('Password')).toBeVisible()
    await expect(page.getByRole('button', { name: 'Sign in' })).toBeVisible()
  })
})

// ---------------------------------------------------------------------------
// 1. Worker login -> dashboard loads
// ---------------------------------------------------------------------------

test.describe('Worker login', () => {
  test('shows the intake submission dashboard after sign-in @backend', async ({ page }) => {
    await login(page, 'worker@sunrise.example', 'password')
    await expect(page.getByRole('heading', { name: 'Submit an intake' })).toBeVisible()
    // Verify the sign-out button is present (proves we're authenticated)
    await expect(page.getByRole('button', { name: 'Sign out' })).toBeVisible()
  })
})

// ---------------------------------------------------------------------------
// 2. Reviewer login -> review queue loads
// ---------------------------------------------------------------------------

test.describe('Reviewer login', () => {
  test('shows the review queue after sign-in @backend', async ({ page }) => {
    await login(page, 'reviewer@sunrise.example', 'password')
    // The reviewer dashboard has a "Skip to review queue" skip-link and
    // a header with "Northwoods" branding. The queue section is always
    // rendered (with items or empty state).
    await expect(page.getByRole('button', { name: 'Sign out' })).toBeVisible()
    // The reviewer dashboard renders a queue sidebar or an empty-state message.
    // Verify we're NOT on the login page (heading "Sign in" is gone).
    await expect(page.getByRole('heading', { name: 'Sign in' })).not.toBeVisible()
    // Verify we're NOT on the worker dashboard either.
    await expect(page.getByRole('heading', { name: 'Submit an intake' })).not.toBeVisible()
  })
})

// ---------------------------------------------------------------------------
// 3. Bad credentials -> error shown
// ---------------------------------------------------------------------------

test.describe('Bad credentials', () => {
  test('shows an error message for wrong password @backend', async ({ page }) => {
    await login(page, 'worker@sunrise.example', 'wrongpassword')
    const alert = page.getByRole('alert')
    await expect(alert).toBeVisible()
    await expect(alert).toContainText(/incorrect|failed|unauthorized/i)
  })

  test('shows an error message for unknown email @backend', async ({ page }) => {
    await login(page, 'nobody@nowhere.example', 'password')
    const alert = page.getByRole('alert')
    await expect(alert).toBeVisible()
    await expect(alert).toContainText(/incorrect|failed|unauthorized/i)
  })
})

// ---------------------------------------------------------------------------
// 4. Worker upload -> select template, upload PDF, verify status badge
// ---------------------------------------------------------------------------

test.describe('Worker upload', () => {
  test('uploads a PDF and shows a status indicator @backend', async ({ page }) => {
    await login(page, 'worker@sunrise.example', 'password')
    await expect(page.getByRole('heading', { name: 'Submit an intake' })).toBeVisible()

    // Wait for templates to load (the select should have a non-disabled option).
    const templateSelect = page.locator('#template-select')
    await expect(templateSelect).toBeEnabled({ timeout: 10_000 })

    // Attach the sample PDF via the file input.
    const samplePdf = path.resolve(__dirname, '../../../samples/intakes/blank-general-assistance.pdf')
    const fileInput = page.locator('input[type="file"]').first()
    await fileInput.setInputFiles(samplePdf)

    // Submit the upload form.
    await page.getByRole('button', { name: /submit|upload/i }).click()

    // After upload, a status badge or record should appear.
    // The worker dashboard renders upload records with a StatusBadge component.
    // Wait for any status indicator to appear (Queued, Processing, etc.)
    await expect(
      page.getByText(/queued|processing|ready for review|finalized|failed/i).first(),
    ).toBeVisible({ timeout: 15_000 })
  })
})

// ---------------------------------------------------------------------------
// 5. Reviewer queue -> verify items or empty state
// ---------------------------------------------------------------------------

test.describe('Reviewer queue', () => {
  test('displays the review queue list or empty state @backend', async ({ page }) => {
    await login(page, 'reviewer@sunrise.example', 'password')

    // Wait for the queue to load. Either we see queue items or an empty state.
    // The reviewer dashboard always shows a sign-out button when loaded.
    await expect(page.getByRole('button', { name: 'Sign out' })).toBeVisible()

    // After the queue loads, we should see either:
    // - "pending" badge with count, or
    // - "No items" / empty state text, or
    // - queue item rows
    // We check that the page is past the loading state.
    await expect(page.locator('text=Loading\u2026').first()).not.toBeVisible({ timeout: 10_000 })
  })
})

// ---------------------------------------------------------------------------
// 6. Cross-tenant isolation -> tenant-a docs not visible to tenant-b
// ---------------------------------------------------------------------------

test.describe('Cross-tenant isolation', () => {
  test('tenant-b reviewer cannot see tenant-a review items @backend', async ({ page }) => {
    // First: login as tenant-a worker and upload a document to ensure tenant-a has data.
    await login(page, 'worker@sunrise.example', 'password')
    await expect(page.getByRole('heading', { name: 'Submit an intake' })).toBeVisible()

    const templateSelect = page.locator('#template-select')
    await expect(templateSelect).toBeEnabled({ timeout: 10_000 })

    const samplePdf = path.resolve(__dirname, '../../../samples/intakes/blank-general-assistance.pdf')
    const fileInput = page.locator('input[type="file"]').first()
    await fileInput.setInputFiles(samplePdf)
    await page.getByRole('button', { name: /submit|upload/i }).click()
    await expect(
      page.getByText(/queued|processing|ready for review|finalized|failed/i).first(),
    ).toBeVisible({ timeout: 15_000 })

    // Logout and login as tenant-b reviewer.
    await page.getByRole('button', { name: 'Sign out' }).click()
    await expect(page.getByRole('heading', { name: 'Sign in' })).toBeVisible()

    await login(page, 'reviewer@lakewood.example', 'password')
    await expect(page.getByRole('button', { name: 'Sign out' })).toBeVisible()

    // The tenant-b review queue must not contain references to tenant-a's data.
    // Wait for queue to load.
    await page.waitForTimeout(2000)

    // Verify tenant-a's tenant ID is not displayed on the page.
    const bodyText = await page.locator('body').innerText()
    expect(bodyText).not.toContain('tenant-a')
    expect(bodyText).not.toContain('sunrise')
  })
})

// ---------------------------------------------------------------------------
