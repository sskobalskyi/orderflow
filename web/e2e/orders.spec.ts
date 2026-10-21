import { expect, test, type Browser, type Page } from '@playwright/test'

async function signIn(page: Page, user: string) {
  await page.goto('/')
  await page.getByRole('button', { name: /sign in|увійти/i }).click()
  await page.locator('#username').fill(user)
  await page.locator('#password').fill(user)
  await page.locator('#kc-login').click()
  await expect(page.getByRole('tab', { name: /shop|магазин/i })).toBeVisible()
}

async function selectLanguage(page: Page, option: string) {
  await page.getByRole('combobox', { name: /language|мова/i }).click()
  await page.getByRole('option', { name: option }).click()
}

async function placeOrder(page: Page, sku: string, quantity = 1) {
  await page.getByRole('tab', { name: /shop|магазин/i }).click()
  await page.locator(`[data-sku="${sku}"] input`).fill(String(quantity))
  await page.getByRole('button', { name: /place order|оформити замовлення/i }).click()
  await expect(page.getByTestId('timeline')).toBeVisible()
}

const latestBadge = (page: Page) => page.locator('tbody tr').first().locator('[data-status]')

test('a confirmed order shows the full saga timeline', async ({ page }) => {
  await signIn(page, 'demo')
  await expect(page.locator('[data-sku="LAMP-01"]')).toContainText('Desk lamp')
  await page.screenshot({ path: 'e2e/screenshots/shop-en.png', fullPage: true })

  await placeOrder(page, 'LAMP-01')

  await expect(latestBadge(page)).toHaveAttribute('data-status', 'Confirmed')
  await expect(page.locator('[data-testid=timeline] li strong')).toHaveText([
    'Order submitted',
    'Stock reserved',
    'Payment completed',
    'Order confirmed',
  ])
  await page.screenshot({ path: 'e2e/screenshots/orders-en.png', fullPage: true })
})

test('a declined payment rejects the order and shows the reason', async ({ page }) => {
  await signIn(page, 'demo')
  await placeOrder(page, 'DESK-01')

  await expect(latestBadge(page)).toHaveAttribute('data-status', 'Rejected')
  await expect(page.locator('[data-testid=timeline] li[data-failed=true] small').first()).toContainText('Card declined')
})

test('the UI switches to Ukrainian, including product names and reasons', async ({ page }) => {
  await signIn(page, 'demo')
  await selectLanguage(page, 'UK')

  await expect(page.getByRole('tab', { name: 'Магазин' })).toBeVisible()
  await expect(page.locator('[data-sku="LAMP-01"]')).toContainText('Настільна лампа')

  // A declined order in Ukrainian shows the compensation path in the timeline.
  await placeOrder(page, 'DESK-01')
  await expect(latestBadge(page)).toHaveAttribute('data-status', 'Rejected')
  await expect(page.locator('[data-testid=timeline] li[data-failed=true] small').first()).toContainText('Картку відхилено')
  await page.screenshot({ path: 'e2e/screenshots/orders-uk.png', fullPage: true })
})

test('users only see their own orders', async ({ page }) => {
  await signIn(page, 'alice')
  await page.getByRole('tab', { name: /my orders|мої замовлення/i }).click()
  await expect(page.getByText('No orders yet.')).toBeVisible()
  await expect(page.getByRole('tab', { name: /admin|адмін/i })).toHaveCount(0)
})

async function newPage(browser: Browser) {
  return (await browser.newContext()).newPage()
}

test('an order is shipped by an admin, returned by the customer and refunded', async ({ browser }) => {
  const customer = await newPage(browser)
  await signIn(customer, 'demo')
  await placeOrder(customer, 'MUG-01', 2)
  await expect(latestBadge(customer)).toHaveAttribute('data-status', 'Confirmed')

  const admin = await newPage(browser)
  await signIn(admin, 'admin')
  await admin.getByRole('tab', { name: /admin|адмін/i }).click()
  await admin.locator('tbody tr', { has: admin.locator('[data-status=Confirmed]') }).first().getByRole('button', { name: 'Mark shipped' }).click()
  await admin.screenshot({ path: 'e2e/screenshots/admin-en.png', fullPage: true })

  await expect(latestBadge(customer)).toHaveAttribute('data-status', 'Shipped', { timeout: 15_000 })
  await customer.getByRole('button', { name: 'Return' }).first().click()
  await expect(latestBadge(customer)).toHaveAttribute('data-status', 'Returned')

  await expect(admin.locator('[data-status=Returned]').first()).toBeVisible({ timeout: 15_000 })
  await admin.locator('tbody tr', { has: admin.locator('[data-status=Returned]') }).first().getByRole('button', { name: 'Refund' }).click()

  await expect(latestBadge(customer)).toHaveAttribute('data-status', 'Refunded', { timeout: 15_000 })
  await expect(customer.locator('[data-testid=timeline] li strong')).toContainText(['Order shipped', 'Return registered', 'Refund issued'])
})

test('a customer cannot reach the admin API', async ({ page }) => {
  await signIn(page, 'demo')
  const status = await page.evaluate(async () => {
    const user = JSON.parse(sessionStorage.getItem(Object.keys(sessionStorage).find((k) => k.startsWith('oidc.user:'))!)!)
    const res = await fetch('http://localhost:8080/api/admin/orders', { headers: { authorization: `Bearer ${user.access_token}` } })
    return res.status
  })
  expect(status).toBe(403)
})
