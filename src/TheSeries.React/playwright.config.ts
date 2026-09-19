import { defineConfig } from '@playwright/test'

export default defineConfig({
  testDir: './tests',
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 1 : 0,
  workers: 2,
  use: { baseURL: 'http://127.0.0.1:5174', trace: 'retain-on-failure' },
  projects: [
    { name: 'desktop', use: { viewport: { width: 1440, height: 1000 } } },
    { name: 'tablet', use: { viewport: { width: 1024, height: 900 } } },
    { name: 'mobile', use: { viewport: { width: 390, height: 844 } } },
  ],
  webServer: [
    { command: 'node tests/fixture.mjs', url: 'http://127.0.0.1:5197/health', reuseExistingServer: false },
    { command: 'dotnet run --project ../TheSeries.Bff --no-launch-profile --no-build --urls http://127.0.0.1:5183',
      url: 'http://127.0.0.1:5183/health', env: { ASPNETCORE_ENVIRONMENT: 'Development', AiService__Url: 'http://127.0.0.1:5197' }, reuseExistingServer: false },
    { command: 'npm run dev -- --port 5174', url: 'http://127.0.0.1:5174', env: { BFF_URL: 'http://127.0.0.1:5183' }, reuseExistingServer: false },
  ],
})