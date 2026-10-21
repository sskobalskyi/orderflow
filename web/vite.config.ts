import tailwindcss from '@tailwindcss/vite'
import react from '@vitejs/plugin-react'
import path from 'node:path'
import { defineConfig } from 'vitest/config'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: { alias: { '@': path.resolve(__dirname, 'src') } },
  test: {
    // Only unit tests next to the source. `e2e/` belongs to Playwright and must not be picked up here.
    include: ['src/**/*.test.ts'],
  },
})
