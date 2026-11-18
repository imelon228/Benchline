import { defineConfig, devices } from '@playwright/test';

export default defineConfig({
  testDir: '.',
  fullyParallel: false,
  workers: 1,
  timeout: 45000,
  use: {
    baseURL: process.env.BENCHLINE_URL ?? 'http://localhost:8080',
    trace: 'retain-on-failure'
  },
  projects: [
    { name: 'chromium-desktop', use: { ...devices['Desktop Chrome'] } },
    { name: 'firefox-tablet', testMatch: /layout\.spec\.js/, use: { browserName: 'firefox', viewport: { width: 820, height: 1180 }, hasTouch: true } },
    { name: 'webkit-phone', testMatch: /(layout|accessibility)\.spec\.js/, use: { ...devices['iPhone 13'] } }
  ]
});
