import react from '@vitejs/plugin-react'
import { defineConfig } from 'vitest/config'

export default defineConfig({
  plugins: [react()],
  server: {
    host: '127.0.0.1',
    port: Number(process.env.PORT ?? 5173),
    strictPort: true,
    proxy: {
      '/api': { target: process.env.BFF_URL ?? 'http://localhost:5181', changeOrigin: true, timeout: 0, proxyTimeout: 0 },
    },
  },
  test: { environment: 'jsdom', include: ['src/**/*.test.{ts,tsx}'], restoreMocks: true },
})
