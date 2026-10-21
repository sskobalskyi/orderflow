import { expect, test, type Page } from '@playwright/test'

async function signIn(page: Page, user: string) {
  await page.goto('/')
  await page.getByRole('button', { name: /sign in|увійти/i }).click()
  await page.locator('#username').fill(user)
  await page.locator('#password').fill(user)
  await page.locator('#kc-login').click()
  await expect(page.getByRole('heading', { name: /new order|нове замовлення/i })).toBeVisible()
}

async function placeOrder(page: Page, sku: string, unitPrice: number) {
  await page.getByLabel(/^SKU|^Артикул/).fill(sku)
  await page.getByLabel(/unit price|ціна за одиницю/i).fill(String(unitPrice))
  await page.getByRole('button', { name: /place order|оформити замовлення/i }).click()
}

test('a confirmed order shows the full saga timeline', async ({ page }) => {
  await signIn(page, 'demo')
  await placeOrder(page, 'A', 10)

  await expect(page.locator('.badge.Confirmed').first()).toBeVisible()
  const steps = page.locator('.timeline li strong')
  await expect(steps).toHaveText(['Order submitted', 'Stock reserved', 'Payment completed', 'Order confirmed'])
  await page.screenshot({ path: 'e2e/screenshots/orders-en.png', fullPage: true })
})

test('a declined payment rejects the order and shows the reason', async ({ page }) => {
  await signIn(page, 'demo')
  await placeOrder(page, 'B', 5000)

  await expect(page.locator('.badge.Rejected').first()).toBeVisible()
  await expect(page.locator('.timeline li.failed small').first()).toContainText('Card declined')
})

test('the UI switches to Ukrainian', async ({ page }) => {
  await signIn(page, 'demo')
  await page.getByRole('combobox').selectOption('uk')

  await expect(page.getByRole('heading', { name: 'Нове замовлення' })).toBeVisible()
  await expect(page.getByRole('heading', { name: 'Хронологія саги' })).toBeVisible()

  // Place a declined order in Ukrainian to show the compensation path in the timeline.
  await placeOrder(page, 'B', 5000)
  await expect(page.locator('.badge.Rejected').first()).toBeVisible()
  await expect(page.locator('.timeline li.failed small').first()).toBeVisible()
  await page.screenshot({ path: 'e2e/screenshots/orders-uk.png', fullPage: true })
})

test('users only see their own orders', async ({ page }) => {
  await signIn(page, 'alice')
  await expect(page.getByText('No orders yet.')).toBeVisible()
})
