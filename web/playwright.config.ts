import { defineConfig } from '@playwright/test'

// E2E runs against the full local stack: `docker compose up -d --build` from the repo root.
export default defineConfig({
  testDir: './e2e',
  timeout: 45_000,
  workers: 1,
  use: {
    baseURL: process.env.ORDERFLOW_WEB_URL ?? 'http://localhost:5173',
    screenshot: 'only-on-failure',
  },
  projects: [{ name: 'chromium', use: { browserName: 'chromium' } }],
})
